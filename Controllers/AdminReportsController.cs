using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using EventTicketingSystem.Data;
using EventTicketingSystem.Models;
using System.Text;

namespace EventTicketingSystem.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminReportsController : Controller
    {
        private readonly DbHelper _db;
        public AdminReportsController(DbHelper db) { _db = db; }

        // GET: /AdminReports?from=2025-08-01&to=2025-09-01
        [HttpGet]
        public IActionResult Index(DateTime? from = null, DateTime? to = null)
        {
            // Default: last 30 days (to = today + 1 as exclusive bound)
            var nowLocal = DateTime.Now;
            var fromLocal = from ?? nowLocal.Date.AddDays(-30);
            var toLocalExclusive = (to ?? nowLocal.Date).AddDays(1);

            var vm = new AdminReportsVm
            {
                FromDate = fromLocal,
                ToDateExclusive = toLocalExclusive
            };

            using var conn = _db.GetConnection();
            conn.Open();

            // KPIs (bookings within range)
            using (var cmd = new NpgsqlCommand(@"
                SELECT
                    COUNT(*)                                                AS total_bookings,
                    COALESCE(SUM(b.ticket_count),0)                         AS tickets_sold,
                    COALESCE(SUM(b.total_amount),0)                         AS revenue,
                    COUNT(DISTINCT b.user_id)                                AS unique_customers
                FROM booking b
                WHERE b.booked_at >= @from AND b.booked_at < @to;", conn))
            {
                var fromUtc = new DateTimeOffset(fromLocal, TimeZoneInfo.Local.GetUtcOffset(fromLocal)).ToUniversalTime();
                var toUtc = new DateTimeOffset(toLocalExclusive, TimeZoneInfo.Local.GetUtcOffset(toLocalExclusive)).ToUniversalTime();

                cmd.Parameters.AddWithValue("from", fromUtc);
                cmd.Parameters.AddWithValue("to", toUtc);

                using var r = cmd.ExecuteReader();
                if (r.Read())
                {
                    vm.Kpi.TotalBookings = r.GetInt32(0);
                    vm.Kpi.TicketsSold = r.GetInt32(1);
                    vm.Kpi.Revenue = r.GetDecimal(2);
                    vm.Kpi.UniqueCustomers = r.GetInt32(3);
                }
            }

            // By organizer
            using (var cmd = new NpgsqlCommand(@"
                SELECT
                    e.organizer_id,
                    u.full_name       AS organizer_name,
                    COALESCE(SUM(b.ticket_count),0) AS tickets,
                    COALESCE(SUM(b.total_amount),0) AS revenue
                FROM booking b
                JOIN event e ON e.event_id = b.event_id
                JOIN users u ON u.user_id = e.organizer_id
                WHERE b.booked_at >= @from AND b.booked_at < @to
                GROUP BY e.organizer_id, u.full_name
                ORDER BY revenue DESC, tickets DESC;", conn))
            {
                cmd.Parameters.AddWithValue("from", new DateTimeOffset(fromLocal, TimeSpan.Zero));
                cmd.Parameters.AddWithValue("to", new DateTimeOffset(toLocalExclusive, TimeSpan.Zero));

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    vm.ByOrganizer.Add(new AdminReportOrganizerRow
                    {
                        OrganizerId = r.GetGuid(0),
                        OrganizerName = r.GetString(1),
                        Tickets = r.GetInt32(2),
                        Revenue = r.GetDecimal(3)
                    });
                }
            }

            // By event
            using (var cmd = new NpgsqlCommand(@"
                SELECT
                    e.event_id,
                    e.title,
                    u.full_name AS organizer_name,
                    e.status,
                    e.ticket_price,
                    e.starts_at,
                    COALESCE(SUM(b.ticket_count),0) AS tickets,
                    COALESCE(SUM(b.total_amount),0) AS revenue
                FROM event e
                JOIN users u ON u.user_id = e.organizer_id
                LEFT JOIN booking b ON b.event_id = e.event_id
                    AND b.booked_at >= @from AND b.booked_at < @to
                GROUP BY e.event_id, e.title, u.full_name, e.status, e.ticket_price, e.starts_at
                ORDER BY revenue DESC, tickets DESC, e.starts_at DESC;", conn))
            {
                cmd.Parameters.AddWithValue("from", new DateTimeOffset(fromLocal, TimeSpan.Zero));
                cmd.Parameters.AddWithValue("to", new DateTimeOffset(toLocalExclusive, TimeSpan.Zero));

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    vm.ByEvent.Add(new AdminReportEventRow
                    {
                        EventId = r.GetInt32(0),
                        Title = r.GetString(1),
                        OrganizerName = r.GetString(2),
                        Status = r.GetString(3),
                        Price = r.GetDecimal(4),
                        StartsAt = r.GetFieldValue<DateTimeOffset>(5).ToLocalTime(),
                        Tickets = r.GetInt32(6),
                        Revenue = r.GetDecimal(7)
                    });
                }
            }

            return View(vm);
        }

        // GET: /AdminReports/ExportEventsCsv?from=YYYY-MM-DD&to=YYYY-MM-DD
        [HttpGet]
        public IActionResult ExportEventsCsv(DateTime? from = null, DateTime? to = null)
        {
            var nowLocal = DateTime.Now;
            var fromLocal = from ?? nowLocal.Date.AddDays(-30);
            var toLocalExclusive = (to ?? nowLocal.Date).AddDays(1);

            var rows = new List<AdminReportEventRow>();
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = new NpgsqlCommand(@"
                SELECT
                    e.event_id, e.title, u.full_name AS organizer_name,
                    e.status, e.ticket_price, e.starts_at,
                    COALESCE(SUM(b.ticket_count),0) AS tickets,
                    COALESCE(SUM(b.total_amount),0) AS revenue
                FROM event e
                JOIN users u ON u.user_id = e.organizer_id
                LEFT JOIN booking b ON b.event_id = e.event_id
                    AND b.booked_at >= @from AND b.booked_at < @to
                GROUP BY e.event_id, e.title, u.full_name, e.status, e.ticket_price, e.starts_at
                ORDER BY revenue DESC, tickets DESC, e.starts_at DESC;", conn);

            cmd.Parameters.AddWithValue("from", new DateTimeOffset(fromLocal, TimeSpan.Zero));
            cmd.Parameters.AddWithValue("to", new DateTimeOffset(toLocalExclusive, TimeSpan.Zero));

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                rows.Add(new AdminReportEventRow
                {
                    EventId = r.GetInt32(0),
                    Title = r.GetString(1),
                    OrganizerName = r.GetString(2),
                    Status = r.GetString(3),
                    Price = r.GetDecimal(4),
                    StartsAt = r.GetFieldValue<DateTimeOffset>(5).ToLocalTime(),
                    Tickets = r.GetInt32(6),
                    Revenue = r.GetDecimal(7)
                });
            }

            var sb = new StringBuilder();
            sb.AppendLine("EventId,Title,Organizer,Status,Price,Tickets,Revenue,StartsAt");
            foreach (var x in rows)
            {
                // Basic CSV with quotes for safety
                sb.AppendLine($"{x.EventId},\"{x.Title.Replace("\"", "\"\"")}\",\"{x.OrganizerName.Replace("\"", "\"\"")}\",{x.Status},{x.Price},{x.Tickets},{x.Revenue},\"{x.StartsAt:yyyy-MM-dd HH:mm}\"");
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var fileName = $"events_{fromLocal:yyyyMMdd}_{toLocalExclusive.AddDays(-1):yyyyMMdd}.csv";
            return File(bytes, "text/csv", fileName);
        }
    }
}
