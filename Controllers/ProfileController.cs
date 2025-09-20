using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using EventTicketingSystem.Data;
using EventTicketingSystem.Models;
using EventTicketingSystem.Security; // for PasswordHasher

namespace EventTicketingSystem.Controllers
{
    [Authorize] // any signed-in user
    public class ProfileController : Controller
    {
        private readonly DbHelper _db;
        public ProfileController(DbHelper db) { _db = db; }

        // GET: /Profile
        [HttpGet]
        public IActionResult Index()
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = new NpgsqlCommand(
                "SELECT email, full_name FROM users WHERE user_id=@id LIMIT 1;", conn);
            cmd.Parameters.AddWithValue("id", userId);

            using var r = cmd.ExecuteReader();
            if (!r.Read()) return NotFound();

            var vm = new ProfileVm
            {
                Email = r.GetString(0),
                FullName = r.GetString(1)
            };
            return View(vm);
        }

        // POST: /Profile
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(ProfileVm vm)
        {
            if (!ModelState.IsValid)
                return View(vm);

            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            using var conn = _db.GetConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();

            try
            {
                // 1) Update name (always)
                using (var upName = new NpgsqlCommand(
                    "UPDATE users SET full_name=@n WHERE user_id=@id;", conn, tx))
                {
                    upName.Parameters.AddWithValue("n", vm.FullName);
                    upName.Parameters.AddWithValue("id", userId);
                    upName.ExecuteNonQuery();
                }

                // 2) If new password provided → verify current, then set new hash/salt
                if (!string.IsNullOrWhiteSpace(vm.NewPassword))
                {
                    // must provide current password to change
                    if (string.IsNullOrWhiteSpace(vm.CurrentPassword))
                    {
                        ModelState.AddModelError("CurrentPassword", "Please enter your current password.");
                        tx.Rollback();
                        return View(vm);
                    }

                    string hashStr, saltStr;

                    // byte[]? storedHash = null, storedSalt = null;
                    using (var readPwd = new NpgsqlCommand(
                        "SELECT password_hash, password_salt FROM users WHERE user_id=@id;", conn, tx))
                    {
                        readPwd.Parameters.AddWithValue("id", userId);
                        using var r = readPwd.ExecuteReader();
                        if (!r.Read())
                        {
                            tx.Rollback();
                            return NotFound();
                        }
                        object hObj = r["password_hash"];
                        object sObj = r["password_salt"];

                        hashStr = hObj is byte[] hb ? Convert.ToBase64String(hb) : (string)hObj;
                        saltStr = sObj is byte[] sb ? Convert.ToBase64String(sb) : (string)sObj;
                    }

                    var ok = PasswordHasher.Verify(vm.CurrentPassword!, hashStr, saltStr);
                    if (!ok)
                    {
                        ModelState.AddModelError("CurrentPassword", "Current password is incorrect.");
                        tx.Rollback();
                        return View(vm);
                    }

                    var (newHash, newSalt) = PasswordHasher.HashPassword(vm.NewPassword!);
                    using var updPwd = new NpgsqlCommand(
                        "UPDATE users SET password_hash=@h, password_salt=@s WHERE user_id=@id;", conn, tx);
                    updPwd.Parameters.AddWithValue("h", newHash);
                    updPwd.Parameters.AddWithValue("s", newSalt);
                    updPwd.Parameters.AddWithValue("id", userId);
                    updPwd.ExecuteNonQuery();
                }

                tx.Commit();

                // 3) Refresh the auth cookie so the displayed Name updates immediately
                await RefreshCookieName(vm.FullName);

                TempData["ProfileMessage"] = "Profile updated successfully.";
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                try { tx.Rollback(); } catch { }
                ModelState.AddModelError(string.Empty, "Could not update profile. Please try again.");
                return View(vm);
            }
        }

        private async Task RefreshCookieName(string fullName)
        {
            // rebuild claims, preserving Id + Role + Email, but updating Name
            var identity = (ClaimsIdentity)User.Identity!;
            var claims = identity.Claims.ToList();

            // replace or add ClaimTypes.Name
            claims.RemoveAll(c => c.Type == ClaimTypes.Name);
            claims.Add(new Claim(ClaimTypes.Name, fullName));

            var newIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(newIdentity);

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
        }
    }
}
