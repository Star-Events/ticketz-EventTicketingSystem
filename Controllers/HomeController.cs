using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using EventTicketingSystem.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using EventTicketingSystem.Services;
using EventTicketingSystem.Data;
using Npgsql;


namespace EventTicketingSystem.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly DbHelper _db;

    public HomeController(DbHelper db, ILogger<HomeController> logger)
    {
        _logger = logger;
        _db = db;
    }

    // GET: /Home/TestDb
    public IActionResult TestDb()
    {
        using var conn = _db.GetConnection();
        conn.Open();

        using var cmd = new NpgsqlCommand("SELECT version();", conn);
        var version = cmd.ExecuteScalar()?.ToString();

        return Content($"Connected to PostgreSQL: {version}");
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
