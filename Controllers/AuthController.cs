using Microsoft.AspNetCore.Mvc;
using Npgsql;
using EventTicketingSystem.Data;
using EventTicketingSystem.Models;
using EventTicketingSystem.Security;

namespace EventTicketingSystem.Controllers
{
    public class AuthController : Controller
    {
        private readonly DbHelper _db;

        public AuthController(DbHelper db)
        {
            _db = db;
        }

        // GET: /Auth/Register
        [HttpGet]
        public IActionResult Register() => View(new RegisterVm());

        // POST: /Auth/Register
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Register(RegisterVm vm)
        {
            if (!ModelState.IsValid)
                return View(vm);

            if (vm.Role != "Customer" && vm.Role != "Organizer")
            {
                ModelState.AddModelError("Role", "Invalid role selection.");
                return View(vm);
            }

            using var conn = _db.GetConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();

            try
            {
                // 1) Duplicate email check
                using (var checkCmd = new NpgsqlCommand(
                    "SELECT 1 FROM users WHERE email = @em LIMIT 1;", conn, tx))
                {
                    checkCmd.Parameters.AddWithValue("em", vm.Email);
                    var exists = checkCmd.ExecuteScalar() != null;
                    if (exists)
                    {
                        ModelState.AddModelError("Email", "Email is already registered.");
                        tx.Rollback();
                        return View(vm);
                    }
                }

                // 2) Hash password
                var (hash, salt) = PasswordHasher.HashPassword(vm.Password);

                // 3) Insert user, capture generated user_id
                Guid newUserId;
                using (var insertCmd = new NpgsqlCommand(@"
                    INSERT INTO users (full_name, email, phone_number, password_hash, password_salt, role)
                    VALUES (@name, @em, @ph, @hash, @salt, @role)
                    RETURNING user_id;
                ", conn, tx))
                {
                    insertCmd.Parameters.AddWithValue("name", vm.FullName);
                    insertCmd.Parameters.AddWithValue("em", vm.Email);
                    insertCmd.Parameters.AddWithValue("ph", (object?)vm.PhoneNumber ?? DBNull.Value);
                    insertCmd.Parameters.AddWithValue("hash", hash);
                    insertCmd.Parameters.AddWithValue("salt", salt);
                    insertCmd.Parameters.AddWithValue("role", vm.Role);

                    newUserId = (Guid)insertCmd.ExecuteScalar()!;
                }

                // 4) If Customer → create CustomerProfile
                if (vm.Role == "Customer")
                {
                    using var profCmd = new NpgsqlCommand(@"
                        INSERT INTO customer_profile (user_id, loyalty_points)
                        VALUES (@uid, 0);
                    ", conn, tx);
                    profCmd.Parameters.AddWithValue("uid", newUserId);
                    profCmd.ExecuteNonQuery();
                }

                tx.Commit();

                TempData["RegisterMessage"] = $"Account created successfully as {vm.Role}.";
                return RedirectToAction("RegisterSuccess");
            }
            catch
            {
                tx.Rollback();
                ModelState.AddModelError(string.Empty, "An unexpected error occurred. Please try again.");
                return View(vm);
            }
        }

        // GET: /Auth/RegisterSuccess
        [HttpGet]
        public IActionResult RegisterSuccess() => View();
    }
}
