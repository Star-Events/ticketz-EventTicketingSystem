using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using EventTicketingSystem.Data;
using EventTicketingSystem.Models;

namespace EventTicketingSystem.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminVenuesController : Controller
    {
        private readonly DbHelper _db;
        public AdminVenuesController(DbHelper db) { _db = db; }

        // GET: /AdminVenues?search=&page=1&pageSize=10
        public IActionResult Index(string? search = null, int page = 1, int pageSize = 10)
        {
            if (page < 1) page = 1;
            pageSize = Math.Clamp(pageSize, 1, 50);

            using var conn = _db.GetConnection();
            conn.Open();

            var where = "";
            if (!string.IsNullOrWhiteSpace(search))
                where = "WHERE LOWER(v.name) LIKE LOWER(@q) OR LOWER(COALESCE(v.address,'')) LIKE LOWER(@q)";

            // total
            using var ccmd = new NpgsqlCommand($@"SELECT COUNT(*)
                FROM venue v {where};", conn);
            if (!string.IsNullOrWhiteSpace(search)) ccmd.Parameters.AddWithValue("q", $"%{search}%");
            var total = Convert.ToInt32(ccmd.ExecuteScalar());
            var totalPages = (int)Math.Ceiling((double)total / pageSize);

            // page
            using var cmd = new NpgsqlCommand($@"
                SELECT v.venue_id, v.name, v.address, v.capacity, v.is_active,
                       u.full_name AS owner_name
                FROM venue v
                LEFT JOIN users u ON u.user_id = v.created_by
                {where}
                ORDER BY v.name ASC
                LIMIT @ps OFFSET @off;", conn);
            if (!string.IsNullOrWhiteSpace(search)) cmd.Parameters.AddWithValue("q", $"%{search}%");
            cmd.Parameters.AddWithValue("ps", pageSize);
            cmd.Parameters.AddWithValue("off", (page - 1) * pageSize);

            var list = new List<AdminVenueRow>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new AdminVenueRow
                {
                    VenueId = r.GetInt32(0),
                    Name = r.GetString(1),
                    Address = r.IsDBNull(2) ? null : r.GetString(2),
                    Capacity = r.GetInt32(3),
                    IsActive = r.GetBoolean(4),
                    OwnerName = r.IsDBNull(5) ? null : r.GetString(5)
                });
            }

            ViewBag.Search = search;
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalPages = totalPages;

            return View(list);
        }

        // GET: /AdminVenues/Edit/5
        [HttpGet]
        public IActionResult Edit(int id)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = new NpgsqlCommand(@"
                SELECT venue_id, name, address, capacity, is_active
                FROM venue WHERE venue_id=@id LIMIT 1;", conn);
            cmd.Parameters.AddWithValue("id", id);

            using var r = cmd.ExecuteReader();
            if (!r.Read()) return NotFound();

            var vm = new EditVenueVm
            {
                VenueId = r.GetInt32(0),
                Name = r.GetString(1),
                Address = r.IsDBNull(2) ? null : r.GetString(2),
                Capacity = r.GetInt32(3),
                IsActive = r.GetBoolean(4)
            };
            return View(vm);
        }

        // POST: /AdminVenues/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(int id, EditVenueVm vm)
        {
            if (id != vm.VenueId) return BadRequest();
            if (string.IsNullOrWhiteSpace(vm.Name))
                ModelState.AddModelError("Name", "Name is required.");
            if (vm.Capacity < 1)
                ModelState.AddModelError("Capacity", "Capacity must be at least 1.");

            if (!ModelState.IsValid) return View(vm);

            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = new NpgsqlCommand(@"
                UPDATE venue
                SET name=@name, address=@addr, capacity=@cap, is_active=@act, updated_at=now()
                WHERE venue_id=@id;", conn);
            cmd.Parameters.AddWithValue("name", vm.Name);
            cmd.Parameters.AddWithValue("addr", (object?)vm.Address ?? DBNull.Value);
            cmd.Parameters.AddWithValue("cap", vm.Capacity);
            cmd.Parameters.AddWithValue("act", vm.IsActive);
            cmd.Parameters.AddWithValue("id", id);

            var rows = cmd.ExecuteNonQuery();
            TempData["AdminVenueMsg"] = rows > 0 ? "Venue updated." : "No changes were made.";
            return RedirectToAction("Index");
        }

        // GET: /AdminVenues/Delete/5
        [HttpGet]
        public IActionResult Delete(int id)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = new NpgsqlCommand(@"
                SELECT v.venue_id, v.name, v.address, v.capacity, v.is_active,
                       (SELECT COUNT(*) FROM event e WHERE e.venue_id = v.venue_id) AS event_ref_count
                FROM venue v WHERE v.venue_id=@id;", conn);
            cmd.Parameters.AddWithValue("id", id);

            using var r = cmd.ExecuteReader();
            if (!r.Read()) return NotFound();

            ViewBag.RefCount = r.GetInt32(5);
            var vm = new EditVenueVm
            {
                VenueId = r.GetInt32(0),
                Name = r.GetString(1),
                Address = r.IsDBNull(2) ? null : r.GetString(2),
                Capacity = r.GetInt32(3),
                IsActive = r.GetBoolean(4)
            };
            return View(vm);
        }

        // POST: /AdminVenues/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteConfirmed(int id)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            // prevent delete if referenced by events
            using (var chk = new NpgsqlCommand("SELECT COUNT(*) FROM event WHERE venue_id=@id;", conn))
            {
                chk.Parameters.AddWithValue("id", id);
                var refsCnt = Convert.ToInt32(chk.ExecuteScalar());
                if (refsCnt > 0)
                {
                    TempData["AdminVenueMsg"] = "Cannot delete: venue is used by one or more events. Consider deactivating it.";
                    return RedirectToAction("Index");
                }
            }

            using (var del = new NpgsqlCommand("DELETE FROM venue WHERE venue_id=@id;", conn))
            {
                del.Parameters.AddWithValue("id", id);
                del.ExecuteNonQuery();
            }

            TempData["AdminVenueMsg"] = "Venue deleted.";
            return RedirectToAction("Index");
        }


        // GET: /AdminVenues/Create
        [HttpGet]
        public IActionResult Create()
        {
            return View(new EditVenueVm { IsActive = true });
        }

        // POST: /AdminVenues/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(EditVenueVm vm)
        {
            if (string.IsNullOrWhiteSpace(vm.Name))
                ModelState.AddModelError("Name", "Name is required.");
            if (vm.Capacity < 1)
                ModelState.AddModelError("Capacity", "Capacity must be at least 1.");

            if (!ModelState.IsValid) return View(vm);

            using var conn = _db.GetConnection();
            conn.Open();

            // Admin-created venues use created_by = NULL so all organizers can see them
            using var cmd = new NpgsqlCommand(@"
                INSERT INTO venue (name, address, capacity, is_active, created_by, updated_at)
                VALUES (@name, @addr, @cap, @act, NULL, now());
            ", conn);

            cmd.Parameters.AddWithValue("name", vm.Name);
            cmd.Parameters.AddWithValue("addr", (object?)vm.Address ?? DBNull.Value);
            cmd.Parameters.AddWithValue("cap", vm.Capacity);
            cmd.Parameters.AddWithValue("act", vm.IsActive);

            cmd.ExecuteNonQuery();

            TempData["AdminVenueMsg"] = "Venue created.";
            return RedirectToAction("Index");
        }
    }
}
