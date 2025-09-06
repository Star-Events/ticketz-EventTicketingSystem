using Microsoft.AspNetCore.Mvc;
using EventTicketingSystem.Models;
using EventTicketingSystem.Services;

namespace EventTicketingSystem.Controllers
{
    public class EventsController : Controller
    {
        // GET: /Events?page=1&pageSize=6&q=rock
        public IActionResult Index(int page = 1, int pageSize = 6, string? q = null)
        {
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 50) pageSize = 6;

            var (items, total) = EventListService.GetPaged(page, pageSize, q);
            var vm = new PagedResult<EventCardVm>
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                Total = total,
                Q = q
            };
            return View(vm);
        }

        // GET: /Events/Details/{id}
        // For now we don’t have IDs in the in-memory VM; wire this after EF.
        public IActionResult Details(int id) => NotFound(); // placeholder
    }
}
