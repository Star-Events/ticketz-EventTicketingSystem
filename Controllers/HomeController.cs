using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using EventTicketingSystem.Models;
using EventTicketingSystem.Services;
using EventTicketingSystem.Data;
using Npgsql;

namespace EventTicketingSystem.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly DbHelper _db;
        private readonly EventReadService _events;

        public HomeController(DbHelper db, ILogger<HomeController> logger, EventReadService events)
        {
            _logger = logger;
            _db = db;
            _events = events;
        }

        public IActionResult TestDb()
        {
            using var conn = _db.GetConnection();
            conn.Open();
            using var cmd = new NpgsqlCommand("SELECT version();", conn);
            var version = cmd.ExecuteScalar()?.ToString();
            return Content($"Connected to PostgreSQL: {version}");
        }

        public IActionResult Index()
        {
            var top2 = _events.GetUpcomingCards(2);
            return View(top2);
        }

        public IActionResult Privacy() => View();
        public IActionResult About() => View();

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
            => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
