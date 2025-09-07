using Microsoft.AspNetCore.Mvc;
using EventTicketingSystem.Services;
using EventTicketingSystem.Models;

namespace EventTicketingSystem.Controllers
{
    public class EventsController : Controller
    {
        private readonly EventReadService _svc;
        public EventsController(EventReadService svc) { _svc = svc; }

        // /Events?page=1&pageSize=6&q=rock
        public IActionResult Index(int page = 1, int pageSize = 6, string? q = null)
        {
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 50) pageSize = 6;

            var (items, total) = _svc.GetPaged(page, pageSize, q);
            var totalPages = (int)Math.Ceiling((double)total / pageSize);

            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalPages = totalPages;
            ViewBag.Q = q;

            return View(items);
        }

        // /Events/Details/123
        public IActionResult Details(int id)
        {
            var vm = _svc.GetDetails(id);
            if (vm == null) return NotFound();
            return View(vm);
        }
    }
}
