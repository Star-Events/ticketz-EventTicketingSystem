using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
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


        // GET: /Auth/Login
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View(new LoginVm());
        }

        // POST: /Auth/Login
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginVm vm, string? returnUrl = null)
        {
            if (!ModelState.IsValid) return View(vm);

            using var conn = _db.GetConnection();
            await conn.OpenAsync();

            const string sql = @"
                SELECT user_id, full_name, email, password_hash, password_salt, role, status
                FROM users
                WHERE email = @em
                LIMIT 1;";

            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("em", vm.Email);

            using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                ModelState.AddModelError(string.Empty, "Invalid email or password.");
                return View(vm);
            }

            var userId = reader.GetGuid(0);
            var fullName = reader.GetString(1);
            var email = reader.GetString(2);
            var hash = reader.GetString(3);
            var salt = reader.GetString(4);
            var role = reader.GetString(5);
            var status = reader.GetString(6);

            if (!PasswordHasher.Verify(vm.Password, hash, salt))
            {
                ModelState.AddModelError(string.Empty, "Invalid email or password.");
                return View(vm);
            }

            if (!string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(string.Empty, "Your account is not active.");
                return View(vm);
            }

            // Build claims identity (includes Role so User.IsInRole(...) works)
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Name, fullName),
                new Claim(ClaimTypes.Email, email),
                new Claim(ClaimTypes.Role, role)
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            var props = new AuthenticationProperties { IsPersistent = vm.RememberMe };

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, props);

            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction("Index", "Home");
        }

        // GET: /Auth/RegisterSuccess
        [HttpGet]
        public IActionResult RegisterSuccess() => View();


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login", "Auth"); // 👈 redirect to Login page
        }

        // Optional: /Auth/AccessDenied
        [HttpGet]
        public IActionResult AccessDenied() => Content("Access denied");

    }
}
