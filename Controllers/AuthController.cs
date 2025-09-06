using Microsoft.AspNetCore.Mvc;
using EventTicketingSystem.Models;

namespace EventTicketingSystem.Controllers
{
    public class AuthController : Controller
    {
        // GET: /Auth/Register
        [HttpGet]
        public IActionResult Register()
        {
            // send an empty model to the view
            return View(new RegisterVm());
        }

        // POST: /Auth/Register  (UI-only in Phase A)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Register(RegisterVm vm)
        {
            // If there are any validation errors, redisplay the form
            if (!ModelState.IsValid)
                return View(vm);

            // Phase A: No database yet.
            // We'll just simulate success and show a success page.
            TempData["RegisterMessage"] =
                $"Registration form submitted (UI test). Selected role: {vm.Role}. " +
                "We will connect the database and create the real user in Phase B.";

            return RedirectToAction("RegisterSuccess");
        }

        // GET: /Auth/RegisterSuccess
        [HttpGet]
        public IActionResult RegisterSuccess()
        {
            return View();
        }
    }
}
