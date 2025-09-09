using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using EventTicketingSystem.Data;
using EventTicketingSystem.Models;
using NpgsqlTypes;


namespace EventTicketingSystem.Controllers
{
    [Authorize(Roles = "Organizer")]
    public class OrgEventsController : Controller
    {
        private readonly DbHelper _db;
        public OrgEventsController(DbHelper db) { _db = db; }

        // ---------- EDIT (GET) ----------
        [HttpGet]
        public IActionResult Edit(int id)
        {
            var organizerId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = new NpgsqlCommand(@"
                SELECT e.event_id, e.title, COALESCE(e.description,''), e.category_id, e.starts_at,
                       e.venue_id, e.ticket_price, e.total_tickets
                FROM event e
                WHERE e.event_id = @id AND e.organizer_id = @org
                LIMIT 1;", conn);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("org", organizerId);

            using var r = cmd.ExecuteReader();
            if (!r.Read()) return NotFound();

            var vm = new CreateEventVm
            {
                Title = r.GetString(1),
                Description = r.GetString(2),
                CategoryId = r.IsDBNull(3) ? 0 : r.GetInt32(3),
                StartsAt = r.GetFieldValue<DateTimeOffset>(4).ToLocalTime(),
                VenueId = r.GetInt32(5),
                TicketPrice = r.GetDecimal(6),
                TotalTickets = r.GetInt32(7)
            };

            ViewBag.Venues = GetVenues();
            ViewBag.Categories = GetCategories();
            ViewBag.EventId = id;
            return View(vm);

        }

        // ---------- EDIT (POST) ----------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(int id, CreateEventVm vm)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Venues = GetVenues();
                ViewBag.Categories = GetCategories();
                ViewBag.EventId = id;
                return View(vm);
            }

            var organizerId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            using var conn = _db.GetConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();

            try
            {
                // 1) Ensure event belongs to organizer and get sold_count
                int sold;
                using (var check = new NpgsqlCommand(@"
                    SELECT sold_count FROM event
                    WHERE event_id=@id AND organizer_id=@org;", conn, tx))
                {
                    check.Parameters.AddWithValue("id", id);
                    check.Parameters.AddWithValue("org", organizerId);
                    var obj = check.ExecuteScalar();
                    if (obj is null) { try { tx.Rollback(); } catch { } return NotFound(); }
                    sold = Convert.ToInt32(obj);
                }

                // 2) Validate TotalTickets vs sold
                if (vm.TotalTickets < sold)
                {
                    ModelState.AddModelError("TotalTickets", $"Total tickets cannot be lower than already sold ({sold}).");
                    try { tx.Rollback(); } catch { }
                    ViewBag.Venues = GetVenues();
                    ViewBag.Categories = GetCategories();
                    ViewBag.EventId = id;
                    return View(vm);
                }

                // 3) Validate venue capacity
                int capacity;
                using (var cap = new NpgsqlCommand("SELECT capacity FROM venue WHERE venue_id=@vid;", conn, tx))
                {
                    cap.Parameters.AddWithValue("vid", vm.VenueId);
                    var capObj = cap.ExecuteScalar();
                    if (capObj is null)
                    {
                        ModelState.AddModelError("VenueId", "Selected venue does not exist.");
                        try { tx.Rollback(); } catch { }
                        ViewBag.Venues = GetVenues();
                        ViewBag.Categories = GetCategories();
                        ViewBag.EventId = id;
                        return View(vm);
                    }
                    capacity = Convert.ToInt32(capObj);
                }
                if (vm.TotalTickets > capacity)
                {
                    ModelState.AddModelError("TotalTickets", $"Total tickets cannot exceed venue capacity ({capacity}).");
                    try { tx.Rollback(); } catch { }
                    ViewBag.Venues = GetVenues();
                    ViewBag.Categories = GetCategories();
                    ViewBag.EventId = id;
                    return View(vm);
                }

                // 4) Validate date in future (allow small slack if you prefer)
                if (vm.StartsAt <= DateTimeOffset.Now)
                {
                    ModelState.AddModelError("StartsAt", "Start date/time must be in the future.");
                    try { tx.Rollback(); } catch { }
                    ViewBag.Venues = GetVenues();
                    ViewBag.Categories = GetCategories();
                    ViewBag.EventId = id;
                    return View(vm);
                }

                var utcStart = vm.StartsAt.ToUniversalTime();

                // 5) Update
                using var upd = new NpgsqlCommand(@"
                    UPDATE event
                    SET title=@title, description=@desc, category_id=@cat,
                        starts_at=@start, venue_id=@vid,
                        ticket_price=@price, total_tickets=@total,
                        updated_at=now()
                    WHERE event_id=@id AND organizer_id=@org;", conn, tx);

                upd.Parameters.AddWithValue("title", vm.Title);
                upd.Parameters.AddWithValue("desc", (object?)vm.Description ?? DBNull.Value);
                upd.Parameters.AddWithValue("cat", vm.CategoryId);
                upd.Parameters.AddWithValue("start", utcStart);
                upd.Parameters.AddWithValue("vid", vm.VenueId);
                upd.Parameters.AddWithValue("price", vm.TicketPrice);
                upd.Parameters.AddWithValue("total", vm.TotalTickets);
                upd.Parameters.AddWithValue("id", id);
                upd.Parameters.AddWithValue("org", organizerId);

                var rows = upd.ExecuteNonQuery();
                if (rows == 0) { try { tx.Rollback(); } catch { } return NotFound(); }

                tx.Commit();
                TempData["OrgEventMessage"] = "Event updated.";
                return RedirectToAction("Index");
            }
            catch
            {
                try { tx.Rollback(); } catch { }
                ModelState.AddModelError(string.Empty, "Could not update event. Please try again.");
                ViewBag.Venues = GetVenues();
                ViewBag.Categories = GetCategories();
                ViewBag.EventId = id;
                return View(vm);
            }
        }


        // GET: /OrgEvents?page=1&pageSize=6&q=&status=All
        public IActionResult Index(int page = 1, int pageSize = 6, string? q = null, string status = "All")
        {
            var organizerId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            using var conn = _db.GetConnection();
            conn.Open();

            // ✅ Auto-complete past events for this organizer
            using (var auto = new NpgsqlCommand(@"
                UPDATE event
                SET status='Completed', updated_at=now()
                WHERE organizer_id=@org
                AND status IN ('Upcoming','Live')
                AND starts_at < now();", conn))
            {
                auto.Parameters.AddWithValue("org", organizerId);
                auto.ExecuteNonQuery();
            }

            var where = "WHERE e.organizer_id = @org";

            if (!string.IsNullOrWhiteSpace(q))
                where += " AND (LOWER(e.title) LIKE LOWER(@q) OR LOWER(v.name) LIKE LOWER(@q))";
            if (!string.IsNullOrWhiteSpace(status) && !status.Equals("All", StringComparison.OrdinalIgnoreCase))
                where += " AND e.status = @status";

            // total
            using var countCmd = new NpgsqlCommand($@"
                SELECT COUNT(*)
                FROM event e
                JOIN venue v ON v.venue_id = e.venue_id
                {where};", conn);
            countCmd.Parameters.AddWithValue("org", organizerId);
            if (!string.IsNullOrWhiteSpace(q)) countCmd.Parameters.AddWithValue("q", $"%{q}%");
            if (!string.Equals(status, "All", StringComparison.OrdinalIgnoreCase)) countCmd.Parameters.AddWithValue("status", status);
            var total = Convert.ToInt32(countCmd.ExecuteScalar());

            // page
            using var cmd = new NpgsqlCommand($@"
                SELECT e.event_id, e.title, e.starts_at, e.status, e.total_tickets, e.sold_count, v.name
                FROM event e
                JOIN venue v ON v.venue_id = e.venue_id
                {where}
                ORDER BY e.starts_at DESC
                LIMIT @ps OFFSET @off;", conn);
            cmd.Parameters.AddWithValue("org", organizerId);
            if (!string.IsNullOrWhiteSpace(q)) cmd.Parameters.AddWithValue("q", $"%{q}%");
            if (!string.Equals(status, "All", StringComparison.OrdinalIgnoreCase)) cmd.Parameters.AddWithValue("status", status);
            cmd.Parameters.AddWithValue("ps", Math.Clamp(pageSize, 1, 50));
            cmd.Parameters.AddWithValue("off", (Math.Max(page, 1) - 1) * Math.Clamp(pageSize, 1, 50));

            var list = new List<MyEventRow>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new MyEventRow
                {
                    Id = r.GetInt32(0),
                    Title = r.GetString(1),
                    StartsAtLocal = r.GetFieldValue<DateTimeOffset>(2).ToLocalTime(),
                    Status = r.GetString(3),
                    Total = r.GetInt32(4),
                    Sold = r.GetInt32(5),
                    Venue = r.GetString(6)
                });
            }

            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalPages = (int)Math.Ceiling((double)total / Math.Clamp(pageSize, 1, 50));
            ViewBag.Q = q;
            ViewBag.Status = status;

            return View(list);
        }

        // POST: /OrgEvents/Publish/123  (Upcoming -> Live)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Publish(int id)
        {
            var orgId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = new NpgsqlCommand(@"
                UPDATE event SET status='Live', updated_at=now()
                WHERE event_id=@id AND organizer_id=@org AND status='Upcoming';", conn);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("org", orgId);

            var rows = cmd.ExecuteNonQuery();
            TempData["OrgEventMessage"] = rows > 0 ? "Event published (Live)." : "Publish failed.";
            return RedirectToAction("Index");
        }

        // POST: /OrgEvents/Unpublish/123  (Live -> Upcoming)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Unpublish(int id)
        {
            var orgId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = new NpgsqlCommand(@"
                UPDATE event SET status='Upcoming', updated_at=now()
                WHERE event_id=@id AND organizer_id=@org AND status='Live';", conn);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("org", orgId);

            var rows = cmd.ExecuteNonQuery();
            TempData["OrgEventMessage"] = rows > 0 ? "Event unpublished (Upcoming)." : "Unpublish failed.";
            return RedirectToAction("Index");
        }

        // POST: /OrgEvents/Cancel/123  (-> Cancelled)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Cancel(int id)
        {
            var orgId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = new NpgsqlCommand(@"
                UPDATE event SET status='Cancelled', updated_at=now()
                WHERE event_id=@id AND organizer_id=@org AND status <> 'Cancelled';", conn);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("org", orgId);

            var rows = cmd.ExecuteNonQuery();
            TempData["OrgEventMessage"] = rows > 0 ? "Event cancelled." : "Cancel failed.";
            return RedirectToAction("Index");
        }




        // GET: /OrgEvents/Create
        [HttpGet]
        public IActionResult Create()
        {
            ViewBag.Venues = GetVenues();
            ViewBag.Categories = GetCategories();
            return View(new CreateEventVm());
        }

        // POST: /OrgEvents/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(CreateEventVm vm)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Venues = GetVenues();
                ViewBag.Categories = GetCategories();
                return View(vm);
            }

            var organizerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(organizerId))
                return Forbid();

            using var conn = _db.GetConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();

            try
            {
                // 1) Validate venue capacity against TotalTickets
                int? capacity = null;
                using (var capCmd = new NpgsqlCommand(
                    "SELECT capacity FROM venue WHERE venue_id = @vid;", conn, tx))
                {
                    capCmd.Parameters.AddWithValue("vid", vm.VenueId);
                    var capObj = capCmd.ExecuteScalar();
                    if (capObj == null)
                    {
                        ModelState.AddModelError("VenueId", "Selected venue does not exist.");
                        tx.Rollback(); ViewBag.Venues = GetVenues(); return View(vm);
                    }
                    capacity = Convert.ToInt32(capObj);
                }

                if (vm.TotalTickets > capacity)
                {
                    ModelState.AddModelError("TotalTickets", $"Total tickets cannot exceed venue capacity ({capacity}).");
                    tx.Rollback(); ViewBag.Venues = GetVenues(); return View(vm);
                }

                // // 2) Insert the event (sold_count defaults to 0; status defaults to 'Upcoming')
                // using var cmd = new NpgsqlCommand(@"
                //     INSERT INTO event
                //       (organizer_id, venue_id, title, description, category, starts_at, ticket_price, total_tickets)
                //     VALUES
                //       (@org, @vid, @title, @desc, @cat, @start, @price, @total);
                // ", conn, tx);

                var utcStart = vm.StartsAt.ToUniversalTime();

                using var cmd = new NpgsqlCommand(@"
                    INSERT INTO event
                        (organizer_id, venue_id, title, description, category_id, starts_at, ticket_price, total_tickets)
                    VALUES
                        (@org, @vid, @title, @desc, @catId, @start, @price, @total);
                ", conn, tx);

                cmd.Parameters.AddWithValue("org", Guid.Parse(organizerId));
                cmd.Parameters.AddWithValue("vid", vm.VenueId);
                cmd.Parameters.AddWithValue("title", vm.Title);
                cmd.Parameters.AddWithValue("desc", (object?)vm.Description ?? DBNull.Value);
                cmd.Parameters.AddWithValue("catId", vm.CategoryId);
                cmd.Parameters.Add("start", NpgsqlDbType.TimestampTz).Value = utcStart;
                cmd.Parameters.AddWithValue("price", vm.TicketPrice);
                cmd.Parameters.AddWithValue("total", vm.TotalTickets);

                cmd.ExecuteNonQuery();
                tx.Commit();

                TempData["OrgEventMessage"] = "Event created successfully.";
                return RedirectToAction("Index");
            }
            catch
            {
                try { tx.Rollback(); } catch { /* ignore */ }
                ModelState.AddModelError(string.Empty, "Could not create event. Please try again.");
                ViewBag.Venues = GetVenues();
                ViewBag.Categories = GetCategories();
                return View(vm);
            }
        }

        // NEW: category dropdown items
        private List<CategoryItem> GetCategories()
        {
            var list = new List<CategoryItem>();
            using var conn = _db.GetConnection();
            conn.Open();
            using var cmd = new NpgsqlCommand(
                "SELECT category_id, name FROM event_category WHERE is_active = TRUE ORDER BY name;", conn);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new CategoryItem { CategoryId = r.GetInt32(0), Name = r.GetString(1) });
            return list;
        }



        // Helper: simple venue item for dropdown
        private List<VenueItem> GetVenues()
        {
            var list = new List<VenueItem>();
            using var conn = _db.GetConnection();
            conn.Open();
            using var cmd = new NpgsqlCommand("SELECT venue_id, name FROM venue ORDER BY name;", conn);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new VenueItem { VenueId = r.GetInt32(0), Name = r.GetString(1) });
            return list;
        }
    }

    public class CategoryItem
    {
        public int CategoryId { get; set; }
        public string Name { get; set; } = "";
    }

    public class VenueItem
    {
        public int VenueId { get; set; }
        public string Name { get; set; } = "";
    }

    public class MyEventRow
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public DateTimeOffset StartsAtLocal { get; set; }
        public string Venue { get; set; } = "";
        public string Status { get; set; } = "Upcoming";
        public int Total { get; set; }
        public int Sold { get; set; }
    }
}
