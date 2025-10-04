using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using EventTicketingSystem.Data;
using EventTicketingSystem.Models;

namespace EventTicketingSystem.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminUsersController : Controller
    {
        private readonly DbHelper _db;
        public AdminUsersController(DbHelper db) { _db = db; }

        // GET: /AdminUsers?role=All&q=&page=1&pageSize=12
        [HttpGet]
        public IActionResult Index(string role = "All", string? q = null, int page = 1, int pageSize = 12)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 50);

            using var conn = _db.GetConnection();
            conn.Open();

            var where = new List<string>();
            if (!string.IsNullOrWhiteSpace(q))
                where.Add("(LOWER(u.full_name) LIKE LOWER(@q) OR LOWER(u.email) LIKE LOWER(@q))");
            if (!string.IsNullOrWhiteSpace(role) && !role.Equals("All", StringComparison.OrdinalIgnoreCase))
                where.Add("u.role = @role");

            var whereSql = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

            // total
            using var countCmd = new NpgsqlCommand($@"SELECT COUNT(*) FROM users u {whereSql};", conn);
            if (!string.IsNullOrWhiteSpace(q)) countCmd.Parameters.AddWithValue("q", $"%{q}%");
            if (!string.IsNullOrWhiteSpace(role) && !role.Equals("All", StringComparison.OrdinalIgnoreCase))
                countCmd.Parameters.AddWithValue("role", role);
            var total = Convert.ToInt32(countCmd.ExecuteScalar());

            // page
            using var cmd = new NpgsqlCommand($@"
                SELECT u.user_id, u.full_name, u.email, u.phone_number, u.role, u.status, u.created_at
                FROM users u
                {whereSql}
                ORDER BY u.created_at DESC
                LIMIT @ps OFFSET @off;", conn);
            if (!string.IsNullOrWhiteSpace(q)) cmd.Parameters.AddWithValue("q", $"%{q}%");
            if (!string.IsNullOrWhiteSpace(role) && !role.Equals("All", StringComparison.OrdinalIgnoreCase))
                cmd.Parameters.AddWithValue("role", role);
            cmd.Parameters.AddWithValue("ps", pageSize);
            cmd.Parameters.AddWithValue("off", (page - 1) * pageSize);

            var rows = new List<AdminUserRow>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                rows.Add(new AdminUserRow
                {
                    UserId = r.GetGuid(0),
                    FullName = r.GetString(1),
                    Email = r.GetString(2),
                    Phone = r.IsDBNull(3) ? null : r.GetString(3),
                    Role = r.GetString(4),
                    Status = r.GetString(5),
                    CreatedAt = r.GetFieldValue<DateTimeOffset>(6)
                });
            }

            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalPages = (int)Math.Ceiling((double)total / pageSize);
            ViewBag.Q = q;
            ViewBag.Role = role;

            return View(rows);
        }

        // POST: /AdminUsers/Suspend/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Suspend(Guid id)
        {
            var myId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            using var conn = _db.GetConnection();
            conn.Open();

            // Don’t allow suspending Admins or yourself
            using var check = new NpgsqlCommand(
                "SELECT role FROM users WHERE user_id=@id;", conn);
            check.Parameters.AddWithValue("id", id);
            var role = (string?)check.ExecuteScalar();
            if (role is null) return NotFound();
            if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) || id == myId)
            {
                TempData["UsersMessage"] = "You cannot suspend this account.";
                return RedirectToAction(nameof(Index));
            }

            using var upd = new NpgsqlCommand(
                "UPDATE users SET status='Suspended', updated_at=now() WHERE user_id=@id;", conn);
            upd.Parameters.AddWithValue("id", id);
            upd.ExecuteNonQuery();

            TempData["UsersMessage"] = "User suspended.";
            return RedirectToAction(nameof(Index));
        }

        // POST: /AdminUsers/Activate/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Activate(Guid id)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var upd = new NpgsqlCommand(
                "UPDATE users SET status='Active', updated_at=now() WHERE user_id=@id;", conn);
            upd.Parameters.AddWithValue("id", id);
            upd.ExecuteNonQuery();

            TempData["UsersMessage"] = "User activated.";
            return RedirectToAction(nameof(Index));
        }
    }
}
