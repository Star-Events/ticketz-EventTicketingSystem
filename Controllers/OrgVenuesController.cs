using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using EventTicketingSystem.Data;
using System.ComponentModel.DataAnnotations;

namespace EventTicketingSystem.Controllers
{
    [Authorize(Roles = "Organizer")]
    public class OrgVenuesController : Controller
    {
        private readonly DbHelper _db;
        public OrgVenuesController(DbHelper db) { _db = db; }

        // GET: /OrgVenues
        public IActionResult Index(int page = 1, int pageSize = 10, string? q = null)
        {
            var orgId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            using var conn = _db.GetConnection();
            conn.Open();

            // List: Organizer’s own active venues, with optional search
            var where = "WHERE is_active = TRUE AND created_by = @org";
            if (!string.IsNullOrWhiteSpace(q))
                where += " AND (LOWER(name) LIKE LOWER(@q) OR LOWER(location) LIKE LOWER(@q))";

            using var count = new NpgsqlCommand($@"SELECT COUNT(*) FROM venue {where};", conn);
            count.Parameters.AddWithValue("org", orgId);
            if (!string.IsNullOrWhiteSpace(q)) count.Parameters.AddWithValue("q", $"%{q}%");
            var total = Convert.ToInt32(count.ExecuteScalar());

            using var cmd = new NpgsqlCommand($@"
                SELECT venue_id, name, location, capacity
                FROM venue
                {where}
                ORDER BY name ASC
                LIMIT @ps OFFSET @off;", conn);
            cmd.Parameters.AddWithValue("org", orgId);
            if (!string.IsNullOrWhiteSpace(q)) cmd.Parameters.AddWithValue("q", $"%{q}%");
            cmd.Parameters.AddWithValue("ps", Math.Clamp(pageSize, 1, 50));
            cmd.Parameters.AddWithValue("off", (Math.Max(page, 1) - 1) * Math.Clamp(pageSize, 1, 50));

            var rows = new List<VenueRow>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                rows.Add(new VenueRow
                {
                    VenueId = r.GetInt32(0),
                    Name = r.GetString(1),
                    Location = r.IsDBNull(2) ? "" : r.GetString(2),
                    Capacity = r.GetInt32(3)
                });
            }

            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalPages = (int)Math.Ceiling((double)total / Math.Clamp(pageSize, 1, 50));
            ViewBag.Q = q;
            return View(rows);
        }

        // GET: /OrgVenues/Create
        [HttpGet]
        public IActionResult Create() => View(new VenueForm());

        // POST: /OrgVenues/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(VenueForm vm)
        {
            if (!ModelState.IsValid) return View(vm);

            var orgId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = new NpgsqlCommand(@"
                INSERT INTO venue (name, location, capacity, created_by, is_active)
                VALUES (@n, @loc, @cap, @org, TRUE);", conn);
            cmd.Parameters.AddWithValue("n", vm.Name);
            cmd.Parameters.AddWithValue("loc", (object?)vm.Location ?? DBNull.Value);
            cmd.Parameters.AddWithValue("cap", vm.Capacity);
            cmd.Parameters.AddWithValue("org", orgId);

            cmd.ExecuteNonQuery();
            TempData["VenueMsg"] = "Venue created.";
            return RedirectToAction("Index");
        }

        // GET: /OrgVenues/Edit/5
        [HttpGet]
        public IActionResult Edit(int id)
        {
            var orgId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = new NpgsqlCommand(@"
                SELECT name, location, capacity
                FROM venue
                WHERE venue_id=@id AND created_by=@org AND is_active=TRUE;", conn);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("org", orgId);

            using var r = cmd.ExecuteReader();
            if (!r.Read()) return NotFound();

            var vm = new VenueForm
            {
                Name = r.GetString(0),
                Location = r.IsDBNull(1) ? "" : r.GetString(1),
                Capacity = r.GetInt32(2)
            };
            ViewBag.VenueId = id;
            return View(vm);
        }

        // POST: /OrgVenues/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(int id, VenueForm vm)
        {
            if (!ModelState.IsValid) { ViewBag.VenueId = id; return View(vm); }

            var orgId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            using var conn = _db.GetConnection();
            conn.Open();

            // Optional safety: prevent reducing capacity below any existing event’s total_tickets
            using (var check = new NpgsqlCommand(@"
                SELECT COALESCE(MAX(e.total_tickets),0)
                FROM event e
                WHERE e.venue_id=@id;", conn))
            {
                check.Parameters.AddWithValue("id", id);
                var maxTotal = Convert.ToInt32(check.ExecuteScalar());
                if (vm.Capacity < maxTotal)
                {
                    ModelState.AddModelError("Capacity", $"Capacity cannot be lower than the largest event’s total tickets ({maxTotal}).");
                    ViewBag.VenueId = id;
                    return View(vm);
                }
            }

            using var cmd = new NpgsqlCommand(@"
                UPDATE venue
                SET name=@n, location=@loc, capacity=@cap
                WHERE venue_id=@id AND created_by=@org AND is_active=TRUE;", conn);
            cmd.Parameters.AddWithValue("n", vm.Name);
            cmd.Parameters.AddWithValue("loc", (object?)vm.Location ?? DBNull.Value);
            cmd.Parameters.AddWithValue("cap", vm.Capacity);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("org", orgId);

            var rows = cmd.ExecuteNonQuery();
            if (rows == 0) return NotFound();

            TempData["VenueMsg"] = "Venue updated.";
            return RedirectToAction("Index");
        }

        // POST: /OrgVenues/Delete/5  (soft delete)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(int id)
        {
            var orgId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            using var conn = _db.GetConnection();
            conn.Open();

            // Optional guard: don’t delete if venue referenced by any event
            using (var used = new NpgsqlCommand(@"SELECT EXISTS(SELECT 1 FROM event WHERE venue_id=@id);", conn))
            {
                used.Parameters.AddWithValue("id", id);
                var inUse = (bool)used.ExecuteScalar()!;
                if (inUse)
                {
                    TempData["VenueMsg"] = "Cannot delete: Venue is used by events.";
                    return RedirectToAction("Index");
                }
            }

            using var cmd = new NpgsqlCommand(@"
                UPDATE venue SET is_active=FALSE
                WHERE venue_id=@id AND created_by=@org;", conn);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("org", orgId);

            var rows = cmd.ExecuteNonQuery();
            TempData["VenueMsg"] = rows > 0 ? "Venue deleted." : "Delete failed.";
            return RedirectToAction("Index");
        }
    }

    // Small models for the view
    public class VenueRow
    {
        public int VenueId { get; set; }
        public string Name { get; set; } = "";
        public string Location { get; set; } = "";
        public int Capacity { get; set; }
    }

    public class VenueForm
    {
        [Required, StringLength(150)]
        public string Name { get; set; } = "";
        [StringLength(200)]
        public string? Location { get; set; }
        [Range(1, int.MaxValue)]
        public int Capacity { get; set; }
    }
}
