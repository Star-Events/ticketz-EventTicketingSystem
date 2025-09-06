using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using EventTicketingSystem.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;

namespace EventTicketingSystem.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    // GET: /
    public IActionResult Index()
    {
        var events = new List<EventCardVm>
        {
            new EventCardVm { Title = "Rock Night Colombo", DateTime = "Sun 12 Oct 2025, 7:00 PM", Venue = "Nelum Pokuna", Price = "LKR 5,000", Availability = "120 / 800" },
            new EventCardVm { Title = "Classical Evening",   DateTime = "Mon 20 Oct 2025, 6:30 PM", Venue = "BMICH",        Price = "LKR 3,500", Availability = "34 / 500" }
        };

        return View(events);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    public IActionResult About()
    {
    return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
