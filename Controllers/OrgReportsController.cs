using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using EventTicketingSystem.Data;
using EventTicketingSystem.Models;

namespace EventTicketingSystem.Controllers
{
    [Authorize(Roles = "Organizer")]
    public class OrgReportsController : Controller
    {
        private readonly DbHelper _db;
        public OrgReportsController(DbHelper db) { _db = db; }

        // GET /OrgReports?from=2025-01-01&to=2025-01-31
        [HttpGet]
        public IActionResult Index(DateTime? from = null, DateTime? to = null)
        {
            var organizerId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            // Default period: last 30 days (inclusive)
            var periodTo = to?.Date.AddDays(1).AddTicks(-1) ?? DateTime.UtcNow;
            var periodFrom = from?.Date.ToUniversalTime() ?? DateTime.UtcNow.AddDays(-30);

            using var conn = _db.GetConnection();
            conn.Open();

            // --- Summary: event counts (lifetime by status) ---
            int eventsTotal = 0, eventsLive = 0, eventsUpcoming = 0;
            using (var cmd = new NpgsqlCommand(@"
                SELECT COUNT(*) FILTER (WHERE status IS NOT NULL)                           AS total,
                       COUNT(*) FILTER (WHERE status = 'Live')                             AS live,
                       COUNT(*) FILTER (WHERE status = 'Upcoming')                         AS upcoming
                FROM event
                WHERE organizer_id = @org;", conn))
            {
                cmd.Parameters.AddWithValue("org", organizerId);
                using var r = cmd.ExecuteReader();
                if (r.Read())
                {
                    eventsTotal = r.IsDBNull(0) ? 0 : r.GetInt32(0);
                    eventsLive = r.IsDBNull(1) ? 0 : r.GetInt32(1);
                    eventsUpcoming = r.IsDBNull(2) ? 0 : r.GetInt32(2);
                }
            }

            // --- Summary: lifetime sold & revenue (simple: sold_count * price) ---
            int ticketsLifetime = 0;
            decimal revenueLifetime = 0m;
            using (var cmd = new NpgsqlCommand(@"
                SELECT COALESCE(SUM(e.sold_count),0)                                       AS sold_life,
                       COALESCE(SUM(e.sold_count * e.ticket_price),0)                      AS revenue_life
                FROM event e
                WHERE e.organizer_id = @org;", conn))
            {
                cmd.Parameters.AddWithValue("org", organizerId);
                using var r = cmd.ExecuteReader();
                if (r.Read())
                {
                    ticketsLifetime = r.GetInt32(0);
                    revenueLifetime = r.GetDecimal(1);
                }
            }

            // --- Summary: period sold & revenue from bookings ---
            int ticketsPeriod = 0;
            decimal revenuePeriod = 0m;
            using (var cmd = new NpgsqlCommand(@"
                SELECT COALESCE(SUM(b.ticket_count),0)    AS sold_period,
                       COALESCE(SUM(b.total_amount),0)    AS revenue_period
                FROM booking b
                JOIN event e ON e.event_id = b.event_id
                WHERE e.organizer_id = @org
                  AND b.booked_at >= @from AND b.booked_at <= @to;", conn))
            {
                cmd.Parameters.AddWithValue("org", organizerId);
                cmd.Parameters.AddWithValue("from", periodFrom);
                cmd.Parameters.AddWithValue("to", periodTo);
                using var r = cmd.ExecuteReader();
                if (r.Read())
                {
                    ticketsPeriod = r.GetInt32(0);
                    revenuePeriod = r.GetDecimal(1);
                }
            }

            var vm = new OrgReportsVm
            {
                Summary = new OrgSalesSummaryVm
                {
                    EventsTotal = eventsTotal,
                    EventsLive = eventsLive,
                    EventsUpcoming = eventsUpcoming,
                    TicketsSoldLifetime = ticketsLifetime,
                    RevenueLifetime = revenueLifetime,
                    TicketsSoldPeriod = ticketsPeriod,
                    RevenuePeriod = revenuePeriod,
                    From = periodFrom,
                    To = periodTo
                }
            };

            // --- Per-event rows: lifetime + period ---
            using (var cmd = new NpgsqlCommand(@"
                SELECT e.event_id, e.title, e.status, e.total_tickets,
                       e.sold_count, e.ticket_price, v.name AS venue
                FROM event e
                JOIN venue v ON v.venue_id = e.venue_id
                WHERE e.organizer_id = @org
                ORDER BY e.starts_at DESC;", conn))
            {
                cmd.Parameters.AddWithValue("org", organizerId);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    vm.Rows.Add(new EventSalesRow
                    {
                        EventId = r.GetInt32(0),
                        Title = r.GetString(1),
                        Status = r.GetString(2),
                        TotalTickets = r.GetInt32(3),
                        SoldLifetime = r.GetInt32(4),
                        Price = r.GetDecimal(5)
                    });
                }
            }

            // Attach period metrics per event
            foreach (var row in vm.Rows)
            {
                using var per = new NpgsqlCommand(@"
                    SELECT COALESCE(SUM(b.ticket_count),0) AS sold_period,
                           COALESCE(SUM(b.total_amount),0) AS revenue_period,
                           COALESCE(COUNT(b.booking_id),0) AS bookings_period,
                           MAX(b.booked_at)                AS last_sale
                    FROM booking b
                    WHERE b.event_id = @eid
                      AND b.booked_at >= @from AND b.booked_at <= @to;", conn);
                per.Parameters.AddWithValue("eid", row.EventId);
                per.Parameters.AddWithValue("from", periodFrom);
                per.Parameters.AddWithValue("to", periodTo);

                using var rr = per.ExecuteReader();
                if (rr.Read())
                {
                    row.SoldPeriod = rr.GetInt32(0);
                    row.RevenuePeriod = rr.GetDecimal(1);
                    row.BookingsPeriod = rr.GetInt32(2);
                    row.LastSaleAt = rr.IsDBNull(3) ? null : rr.GetFieldValue<DateTimeOffset>(3).ToLocalTime();
                }
            }

            ViewBag.From = periodFrom.ToLocalTime().Date.ToString("yyyy-MM-dd");
            ViewBag.To = periodTo.ToLocalTime().Date.ToString("yyyy-MM-dd");
            return View(vm);
        }
    }
}
