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

        // GET: /OrgEvents
        public IActionResult Index()
        {
            // You can list organizer's events later.
            return View();
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
}
