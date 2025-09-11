using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using EventTicketingSystem.Data;
using EventTicketingSystem.Models;

namespace EventTicketingSystem.Controllers
{
    [Authorize(Roles = "Customer")]
    public class CheckoutController : Controller
    {
        private readonly DbHelper _db;
        public CheckoutController(DbHelper db) { _db = db; }

        // GET /Checkout/Start/123
        [HttpGet]
        public IActionResult Start(int id, int qty = 1)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            // Read basic event info (no lock; just to show UI)
            using var cmd = new NpgsqlCommand(@"
                SELECT e.event_id, e.title, e.starts_at, e.ticket_price,
                       e.total_tickets, e.sold_count, e.status, v.name AS venue
                FROM event e
                JOIN venue v ON v.venue_id = e.venue_id
                WHERE e.event_id = @id;", conn);
            cmd.Parameters.AddWithValue("id", id);

            using var r = cmd.ExecuteReader();
            if (!r.Read()) return NotFound();

            var total = r.GetInt32(4);
            var sold = r.GetInt32(5);
            var remaining = total - sold;

            var vm = new CheckoutVm
            {
                EventId = r.GetInt32(0),
                Title = r.GetString(1),
                StartsAt = r.GetFieldValue<DateTimeOffset>(2).ToLocalTime(),
                Price = r.GetDecimal(3),
                Venue = r.GetString(7),
                Remaining = remaining,
                Status = r.GetString(6),
                Quantity = Math.Clamp(qty, 1, Math.Max(1, remaining))
            };

            // guard: only Live and remaining > 0
            if (!string.Equals(vm.Status, "Live", StringComparison.OrdinalIgnoreCase) || vm.Remaining <= 0)
                return RedirectToAction("Details", "Events", new { id });

            return View(vm);
        }

        // POST /Checkout/Confirm
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Confirm(CheckoutVm vm)
        {
            if (vm.Quantity <= 0)
            {
                ModelState.AddModelError("Quantity", "Select at least 1 ticket.");
                return ReShowStart(vm.EventId, vm.Quantity);
            }

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdStr)) return Forbid();
            var userId = Guid.Parse(userIdStr);

            using var conn = _db.GetConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();

            try
            {
                int total, sold;
                decimal price;
                string status;

                using (var lockCmd = new NpgsqlCommand(@"
            SELECT total_tickets, sold_count, ticket_price, status
            FROM event
            WHERE event_id = @id
            FOR UPDATE;", conn, tx))
                {
                    lockCmd.Parameters.AddWithValue("id", vm.EventId);
                    using var r = lockCmd.ExecuteReader();
                    if (!r.Read())
                    {
                        ModelState.AddModelError(string.Empty, "Event not found.");
                        tx.Rollback();
                        return ReShowStart(vm.EventId, vm.Quantity);
                    }

                    total = r.GetInt32(0);
                    sold = r.GetInt32(1);
                    price = r.GetDecimal(2);
                    status = r.GetString(3);
                }

                var remaining = total - sold;

                if (!string.Equals(status, "Live", StringComparison.OrdinalIgnoreCase))
                {
                    ModelState.AddModelError(string.Empty, "This event is not open for sales.");
                    tx.Rollback();
                    return ReShowStart(vm.EventId, vm.Quantity);
                }
                if (remaining <= 0)
                {
                    ModelState.AddModelError(string.Empty, "This event is sold out.");
                    tx.Rollback();
                    return ReShowStart(vm.EventId, vm.Quantity);
                }
                if (vm.Quantity > remaining)
                {
                    ModelState.AddModelError("Quantity", $"Only {remaining} tickets remaining.");
                    tx.Rollback();
                    return ReShowStart(vm.EventId, remaining);
                }

                var totalAmount = price * vm.Quantity;

                Guid bookingId;
                using (var b = new NpgsqlCommand(@"
            INSERT INTO booking (user_id, event_id, ticket_count, total_amount)
            VALUES (@u, @e, @c, @amt)
            RETURNING booking_id;", conn, tx))
                {
                    b.Parameters.AddWithValue("u", userId);
                    b.Parameters.AddWithValue("e", vm.EventId);
                    b.Parameters.AddWithValue("c", vm.Quantity);
                    b.Parameters.AddWithValue("amt", totalAmount);
                    bookingId = (Guid)b.ExecuteScalar()!;
                }

                using (var t = new NpgsqlCommand(@"INSERT INTO booking_ticket (booking_id) VALUES (@b);", conn, tx))
                {
                    t.Parameters.AddWithValue("b", bookingId);
                    for (int i = 0; i < vm.Quantity; i++) t.ExecuteNonQuery();
                }

                using (var u = new NpgsqlCommand(@"
            UPDATE event
            SET sold_count = sold_count + @c, updated_at = now()
            WHERE event_id = @id;", conn, tx))
                {
                    u.Parameters.AddWithValue("c", vm.Quantity);
                    u.Parameters.AddWithValue("id", vm.EventId);
                    u.ExecuteNonQuery();
                }

                tx.Commit();
                return RedirectToAction("Success", new { id = bookingId });
            }
            catch (Exception ex)
            {
                try { tx.Rollback(); } catch { }
                // TEMP: show detailed error so we can fix it
                ModelState.AddModelError(string.Empty, $"Error: {ex.Message}");
                return ReShowStart(vm.EventId, vm.Quantity);
            }

            // Local function to re-render Start with the latest event info and ModelState errors.
            IActionResult ReShowStart(int eventId, int qty)
            {
                using var conn2 = _db.GetConnection();
                conn2.Open();

                using var cmd = new NpgsqlCommand(@"
            SELECT e.event_id, e.title, e.starts_at, e.ticket_price,
                   e.total_tickets, e.sold_count, e.status, v.name AS venue
            FROM event e
            JOIN venue v ON v.venue_id = e.venue_id
            WHERE e.event_id = @id;", conn2);
                cmd.Parameters.AddWithValue("id", eventId);

                using var r = cmd.ExecuteReader();
                if (!r.Read()) return NotFound();

                var total = r.GetInt32(4);
                var sold = r.GetInt32(5);
                var remaining = total - sold;

                var vm2 = new CheckoutVm
                {
                    EventId = r.GetInt32(0),
                    Title = r.GetString(1),
                    StartsAt = r.GetFieldValue<DateTimeOffset>(2).ToLocalTime(),
                    Price = r.GetDecimal(3),
                    Venue = r.GetString(7),
                    Remaining = remaining,
                    Status = r.GetString(6),
                    Quantity = Math.Clamp(qty, 1, Math.Max(1, remaining))
                };

                // Re-render the same Start view so the validation errors show on the page.
                return View("Start", vm2);
            }
        }


        // GET /Checkout/Success?Id=<bookingId>
        [HttpGet]
        public IActionResult Success(Guid id)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = new NpgsqlCommand(@"
                SELECT b.booking_id, b.booked_at, b.ticket_count, b.total_amount,
                       e.event_id, e.title, e.starts_at, v.name AS venue
                FROM booking b
                JOIN event e ON e.event_id = b.event_id
                JOIN venue v ON v.venue_id = e.venue_id
                WHERE b.booking_id = @id;", conn);
            cmd.Parameters.AddWithValue("id", id);

            using var r = cmd.ExecuteReader();
            if (!r.Read()) return NotFound();

            var vm = new CheckoutSuccessVm
            {
                BookingId = r.GetGuid(0),
                BookedAt = r.GetFieldValue<DateTimeOffset>(1).ToLocalTime(),
                TicketCount = r.GetInt32(2),
                TotalAmount = r.GetDecimal(3),
                EventId = r.GetInt32(4),
                Title = r.GetString(5),
                StartsAt = r.GetFieldValue<DateTimeOffset>(6).ToLocalTime(),
                Venue = r.GetString(7)
            };

            return View(vm);
        }
    }
}
