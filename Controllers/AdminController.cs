using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using EventTicketingSystem.Data;
using EventTicketingSystem.Models;

namespace EventTicketingSystem.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly DbHelper _db;
        public AdminController(DbHelper db) { _db = db; }

        // GET: /Admin
        [HttpGet]
        public IActionResult Index()
        {
            var vm = new AdminDashboardVm();

            using var conn = _db.GetConnection();
            conn.Open();

            // --- KPIs: Users by role & totals ---
            using (var cmd = new NpgsqlCommand(@"
                SELECT
                    COUNT(*)                                                            AS users_total,
                    COUNT(*) FILTER (WHERE role='Customer')                             AS users_customers,
                    COUNT(*) FILTER (WHERE role='Organizer')                            AS users_organizers,
                    COUNT(*) FILTER (WHERE role='Admin')                                AS users_admins
                FROM users;", conn))
            using (var r = cmd.ExecuteReader())
            {
                if (r.Read())
                {
                    vm.Kpi.UsersTotal = r.GetInt32(0);
                    vm.Kpi.UsersCustomers = r.GetInt32(1);
                    vm.Kpi.UsersOrganizers = r.GetInt32(2);
                    vm.Kpi.UsersAdmins = r.GetInt32(3);
                }
            }

            // --- KPIs: Events by status & totals ---
            using (var cmd = new NpgsqlCommand(@"
                SELECT
                    COUNT(*)                                                           AS events_total,
                    COUNT(*) FILTER (WHERE status='Live')                              AS events_live,
                    COUNT(*) FILTER (WHERE status='Upcoming')                          AS events_upcoming,
                    COUNT(*) FILTER (WHERE status='Completed')                         AS events_completed,
                    COUNT(*) FILTER (WHERE status='Cancelled')                         AS events_cancelled
                FROM event;", conn))
            using (var r = cmd.ExecuteReader())
            {
                if (r.Read())
                {
                    vm.Kpi.EventsTotal = r.GetInt32(0);
                    vm.Kpi.EventsLive = r.GetInt32(1);
                    vm.Kpi.EventsUpcoming = r.GetInt32(2);
                    vm.Kpi.EventsCompleted = r.GetInt32(3);
                    vm.Kpi.EventsCancelled = r.GetInt32(4);
                }
            }

            // --- KPIs: Venues ---
            using (var cmd = new NpgsqlCommand(@"
                SELECT
                    COUNT(*)                                AS venues_total,
                    COUNT(*) FILTER (WHERE is_active)       AS venues_active
                FROM venue;", conn))
            using (var r = cmd.ExecuteReader())
            {
                if (r.Read())
                {
                    vm.Kpi.VenuesTotal = r.GetInt32(0);
                    vm.Kpi.VenuesActive = r.GetInt32(1);
                }
            }

            // --- KPIs: Lifetime tickets & revenue from bookings ---
            using (var cmd = new NpgsqlCommand(@"
                SELECT
                    COALESCE(SUM(ticket_count),0)  AS tickets_sold_life,
                    COALESCE(SUM(total_amount),0) AS revenue_life
                FROM booking;", conn))
            using (var r = cmd.ExecuteReader())
            {
                if (r.Read())
                {
                    vm.Kpi.TicketsSoldLifetime = r.GetInt32(0);
                    vm.Kpi.RevenueLifetime = r.GetDecimal(1);
                }
            }

            // --- Top events by revenue (lifetime) ---
            using (var cmd = new NpgsqlCommand(@"
                SELECT
                    e.event_id, e.title, u.full_name AS organizer_name,
                    e.status, e.total_tickets, e.sold_count, e.ticket_price
                FROM event e
                JOIN users u ON u.user_id = e.organizer_id
                ORDER BY (e.sold_count * e.ticket_price) DESC, e.event_id
                LIMIT 10;", conn))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    vm.TopEvents.Add(new AdminEventStatRow
                    {
                        EventId = r.GetInt32(0),
                        Title = r.GetString(1),
                        OrganizerName = r.GetString(2),
                        Status = r.GetString(3),
                        Total = r.GetInt32(4),
                        Sold = r.GetInt32(5),
                        Price = r.GetDecimal(6)
                    });
                }
            }

            // --- Recent bookings (system-wide) ---
            using (var cmd = new NpgsqlCommand(@"
                SELECT
                    b.booking_id, b.booked_at, u.full_name AS customer_name,
                    e.title AS event_title, b.ticket_count, b.total_amount
                FROM booking b
                JOIN users u ON u.user_id = b.user_id
                JOIN event e ON e.event_id = b.event_id
                ORDER BY b.booked_at DESC
                LIMIT 12;", conn))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    vm.RecentBookings.Add(new AdminRecentBookingRow
                    {
                        BookingId = r.GetGuid(0),
                        BookedAt = r.GetFieldValue<DateTimeOffset>(1).ToLocalTime(),
                        CustomerName = r.GetString(2),
                        EventTitle = r.GetString(3),
                        TicketCount = r.GetInt32(4),
                        TotalAmount = r.GetDecimal(5)
                    });
                }
            }

            return View(vm);
        }
    }
}
