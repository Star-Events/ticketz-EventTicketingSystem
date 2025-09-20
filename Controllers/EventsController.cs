using Microsoft.AspNetCore.Mvc;
using EventTicketingSystem.Services;
using EventTicketingSystem.Models;
using EventTicketingSystem.Data;
using Npgsql;

namespace EventTicketingSystem.Controllers
{
    public class EventsController : Controller
    {
        private readonly EventReadService _svc;
        private readonly DbHelper _db;

        public EventsController(EventReadService svc, DbHelper db)
        {
            _svc = svc;
            _db = db;
        }

        // helper to fetch active categories
        private List<(int Id, string Name)> GetCategories()
        {
            var list = new List<(int, string)>();
            using var conn = _db.GetConnection();
            conn.Open();
            using var cmd = new NpgsqlCommand(
                "SELECT category_id, name FROM event_category WHERE is_active = TRUE ORDER BY name;", conn);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add((r.GetInt32(0), r.GetString(1)));
            return list;
        }

        // /Events?page=1&pageSize=6&q=&categoryId=&from=&to=
        public IActionResult Index(
        int page = 1, int pageSize = 6, string? q = null,
        int? categoryId = null,
        DateTime? from = null, DateTime? to = null)
        {
            if (page < 1) page = 1;
            pageSize = Math.Clamp(pageSize, 1, 50);

            // date handling (optional)
            DateTimeOffset? fromUtc = null, toUtcExclusive = null;
            if (from.HasValue)
            {
                var f = from.Value.Date;
                fromUtc = new DateTimeOffset(f, TimeZoneInfo.Local.GetUtcOffset(f)).ToUniversalTime();
            }
            if (to.HasValue)
            {
                var t = to.Value.Date.AddDays(1); // exclusive upper bound
                toUtcExclusive = new DateTimeOffset(t, TimeZoneInfo.Local.GetUtcOffset(t)).ToUniversalTime();
            }

            var (items, total) = _svc.GetPaged(page, pageSize, q, fromUtc, toUtcExclusive, categoryId);
            var totalPages = (int)Math.Ceiling((double)total / pageSize);

            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalPages = totalPages;
            ViewBag.Q = q;
            ViewBag.CategoryId = categoryId;
            ViewBag.From = from?.ToString("yyyy-MM-dd");
            ViewBag.To = to?.ToString("yyyy-MM-dd");
            ViewBag.Categories = GetCategories(); // for dropdown

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
