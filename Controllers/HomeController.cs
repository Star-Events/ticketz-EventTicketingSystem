using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using EventTicketingSystem.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using EventTicketingSystem.Services;


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
        var top2 = EventListService.GetAll().Take(2).ToList();
        return View(top2);

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
