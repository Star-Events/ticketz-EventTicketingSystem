using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using EventTicketingSystem.Data;
using EventTicketingSystem.Models;

namespace EventTicketingSystem.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminEventsController : Controller
    {
        private readonly DbHelper _db;
        public AdminEventsController(DbHelper db) { _db = db; }

        // ---------- LIST ----------
        // GET: /AdminEvents?status=All&q=&page=1&pageSize=10
        public IActionResult Index(string status = "All", string? q = null, int page = 1, int pageSize = 10)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 50);

            using var conn = _db.GetConnection();
            conn.Open();

            // Auto-complete past events (Upcoming/Live -> Completed)
            using (var auto = new NpgsqlCommand(@"
                UPDATE event
                SET status='Completed', updated_at=now()
                WHERE status IN ('Upcoming','Live')
                  AND starts_at < now();", conn))
            {
                auto.ExecuteNonQuery();
            }

            // Filters
            var where = "WHERE 1=1";
            if (!string.IsNullOrWhiteSpace(q))
                where += " AND (LOWER(e.title) LIKE LOWER(@q) OR LOWER(v.name) LIKE LOWER(@q) OR LOWER(u.full_name) LIKE LOWER(@q))";
            if (!string.Equals(status, "All", StringComparison.OrdinalIgnoreCase))
                where += " AND e.status = @status";

            // total
            using var ccmd = new NpgsqlCommand($@"
                SELECT COUNT(*)
                FROM event e
                JOIN venue v ON v.venue_id = e.venue_id
                JOIN users u ON u.user_id = e.organizer_id
                {where};", conn);
            if (!string.IsNullOrWhiteSpace(q)) ccmd.Parameters.AddWithValue("q", $"%{q}%");
            if (!string.Equals(status, "All", StringComparison.OrdinalIgnoreCase)) ccmd.Parameters.AddWithValue("status", status);
            var total = Convert.ToInt32(ccmd.ExecuteScalar());
            var totalPages = (int)Math.Ceiling((double)total / pageSize);

            // page
            using var cmd = new NpgsqlCommand($@"
                SELECT e.event_id, e.title, e.starts_at, e.status, e.total_tickets, e.sold_count,
                       v.name AS venue_name, u.full_name AS organizer_name, e.organizer_id
                FROM event e
                JOIN venue v ON v.venue_id = e.venue_id
                JOIN users u ON u.user_id = e.organizer_id
                {where}
                ORDER BY e.starts_at DESC
                LIMIT @ps OFFSET @off;", conn);
            if (!string.IsNullOrWhiteSpace(q)) cmd.Parameters.AddWithValue("q", $"%{q}%");
            if (!string.Equals(status, "All", StringComparison.OrdinalIgnoreCase)) cmd.Parameters.AddWithValue("status", status);
            cmd.Parameters.AddWithValue("ps", pageSize);
            cmd.Parameters.AddWithValue("off", (page - 1) * pageSize);

            var list = new List<AdminEventRow>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new AdminEventRow
                {
                    Id = r.GetInt32(0),
                    Title = r.GetString(1),
                    StartsAtLocal = r.GetFieldValue<DateTimeOffset>(2).ToLocalTime(),
                    Status = r.GetString(3),
                    Total = r.GetInt32(4),
                    Sold = r.GetInt32(5),
                    Venue = r.GetString(6),
                    OrganizerName = r.GetString(7),
                    OrganizerId = r.GetGuid(8)
                });
            }

            ViewBag.Status = status;
            ViewBag.Q = q;
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalPages = totalPages;

            return View(list);
        }

        // ---------- EDIT (GET) ----------
        [HttpGet]
        public IActionResult Edit(int id)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = new NpgsqlCommand(@"
                SELECT e.event_id, e.title, COALESCE(e.description,''), e.category_id, e.starts_at,
                       e.venue_id, e.ticket_price, e.total_tickets, e.status
                FROM event e
                WHERE e.event_id = @id
                LIMIT 1;", conn);
            cmd.Parameters.AddWithValue("id", id);

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
            ViewBag.CurrentStatus = r.GetString(8);

            ViewBag.Venues = GetAllActiveVenues();     // admin sees all active venues
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
                ViewBag.Venues = GetAllActiveVenues();
                ViewBag.Categories = GetCategories();
                ViewBag.EventId = id;
                return View(vm);
            }

            using var conn = _db.GetConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();

            try
            {
                // fetch sold and sanity checks
                int sold;
                using (var chk = new NpgsqlCommand("SELECT sold_count FROM event WHERE event_id=@id;", conn, tx))
                {
                    chk.Parameters.AddWithValue("id", id);
                    var obj = chk.ExecuteScalar();
                    if (obj is null) { tx.Rollback(); return NotFound(); }
                    sold = Convert.ToInt32(obj);
                }
                if (vm.TotalTickets < sold)
                {
                    ModelState.AddModelError("TotalTickets", $"Total tickets cannot be less than already sold ({sold}).");
                    tx.Rollback();
                    ViewBag.Venues = GetAllActiveVenues();
                    ViewBag.Categories = GetCategories();
                    ViewBag.EventId = id;
                    return View(vm);
                }

                // capacity
                int capacity;
                using (var cap = new NpgsqlCommand("SELECT capacity FROM venue WHERE venue_id=@vid;", conn, tx))
                {
                    cap.Parameters.AddWithValue("vid", vm.VenueId);
                    var c = cap.ExecuteScalar();
                    if (c is null)
                    {
                        ModelState.AddModelError("VenueId", "Selected venue does not exist.");
                        tx.Rollback();
                        ViewBag.Venues = GetAllActiveVenues();
                        ViewBag.Categories = GetCategories();
                        ViewBag.EventId = id;
                        return View(vm);
                    }
                    capacity = Convert.ToInt32(c);
                }
                if (vm.TotalTickets > capacity)
                {
                    ModelState.AddModelError("TotalTickets", $"Total tickets cannot exceed venue capacity ({capacity}).");
                    tx.Rollback();
                    ViewBag.Venues = GetAllActiveVenues();
                    ViewBag.Categories = GetCategories();
                    ViewBag.EventId = id;
                    return View(vm);
                }

                if (vm.StartsAt <= DateTimeOffset.Now)
                {
                    ModelState.AddModelError("StartsAt", "Start date/time must be in the future.");
                    tx.Rollback();
                    ViewBag.Venues = GetAllActiveVenues();
                    ViewBag.Categories = GetCategories();
                    ViewBag.EventId = id;
                    return View(vm);
                }

                var utcStart = vm.StartsAt.ToUniversalTime();

                using var upd = new NpgsqlCommand(@"
                    UPDATE event
                    SET title=@title, description=@desc, category_id=@cat,
                        starts_at=@start, venue_id=@vid,
                        ticket_price=@price, total_tickets=@total,
                        updated_at=now()
                    WHERE event_id=@id;", conn, tx);

                upd.Parameters.AddWithValue("title", vm.Title);
                upd.Parameters.AddWithValue("desc", (object?)vm.Description ?? DBNull.Value);
                upd.Parameters.AddWithValue("cat", vm.CategoryId);
                upd.Parameters.AddWithValue("start", utcStart);
                upd.Parameters.AddWithValue("vid", vm.VenueId);
                upd.Parameters.AddWithValue("price", vm.TicketPrice);
                upd.Parameters.AddWithValue("total", vm.TotalTickets);
                upd.Parameters.AddWithValue("id", id);

                upd.ExecuteNonQuery();

                tx.Commit();
                TempData["AdminEventMsg"] = "Event updated.";
                return RedirectToAction("Index");
            }
            catch
            {
                try { tx.Rollback(); } catch { }
                ModelState.AddModelError(string.Empty, "Could not update event. Please try again.");
                ViewBag.Venues = GetAllActiveVenues();
                ViewBag.Categories = GetCategories();
                ViewBag.EventId = id;
                return View(vm);
            }
        }

        // ---------- STATUS ACTIONS ----------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Publish(int id) => MutateStatus(id, "Upcoming", "Live", "Event published (Live).");

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Unpublish(int id) => MutateStatus(id, "Live", "Upcoming", "Event unpublished (Upcoming).");

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Cancel(int id) => MutateStatusAny(id, "Cancelled", "Event cancelled.");

        private IActionResult MutateStatus(int id, string from, string to, string okMsg)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = new NpgsqlCommand(@"
                UPDATE event SET status=@to, updated_at=now()
                WHERE event_id=@id AND status=@from;", conn);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("from", from);
            cmd.Parameters.AddWithValue("to", to);

            var rows = cmd.ExecuteNonQuery();
            TempData["AdminEventMsg"] = rows > 0 ? okMsg : "Status change failed.";
            return RedirectToAction("Index");
        }

        private IActionResult MutateStatusAny(int id, string to, string okMsg)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = new NpgsqlCommand(@"
                UPDATE event SET status=@to, updated_at=now()
                WHERE event_id=@id AND status <> @to;", conn);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("to", to);

            var rows = cmd.ExecuteNonQuery();
            TempData["AdminEventMsg"] = rows > 0 ? okMsg : "Status change failed.";
            return RedirectToAction("Index");
        }

        // ---------- helpers ----------
        private List<CategoryItem> GetCategories()
        {
            var list = new List<CategoryItem>();
            using var conn = _db.GetConnection();
            conn.Open();
            using var cmd = new NpgsqlCommand("SELECT category_id, name FROM event_category WHERE is_active = TRUE ORDER BY name;", conn);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new CategoryItem { CategoryId = r.GetInt32(0), Name = r.GetString(1) });
            return list;
        }

        private List<VenueItem> GetAllActiveVenues()
        {
            var list = new List<VenueItem>();
            using var conn = _db.GetConnection();
            conn.Open();
            using var cmd = new NpgsqlCommand(@"
                SELECT venue_id, name
                FROM venue
                WHERE is_active = TRUE
                ORDER BY name;", conn);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new VenueItem { VenueId = r.GetInt32(0), Name = r.GetString(1) });
            return list;
        }
    }
}
