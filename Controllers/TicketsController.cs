using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using EventTicketingSystem.Data;
using EventTicketingSystem.Models;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using System.IO;
using QuestPDF.Helpers;   // gives Colors.*


namespace EventTicketingSystem.Controllers
{
    [Authorize(Roles = "Customer")]
    public class TicketsController : Controller
    {
        private readonly DbHelper _db;
        public TicketsController(DbHelper db) { _db = db; }

        // GET: /Tickets/My
        [HttpGet]
        public IActionResult My()
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            using var conn = _db.GetConnection();
            conn.Open();

            // Get bookings for this user with event info
            var groups = new List<BookingGroup>();
            using (var cmd = new NpgsqlCommand(@"
                SELECT b.booking_id, b.booked_at, b.ticket_count, b.total_amount,
                       e.event_id, e.title, e.starts_at, e.status, v.name AS venue, e.ticket_price
                FROM booking b
                JOIN event e ON e.event_id = b.event_id
                JOIN venue v ON v.venue_id = e.venue_id
                WHERE b.user_id = @u
                ORDER BY b.booked_at DESC;", conn))
            {
                cmd.Parameters.AddWithValue("u", userId);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    groups.Add(new BookingGroup
                    {
                        BookingId = r.GetGuid(0),
                        BookedAt = r.GetFieldValue<DateTimeOffset>(1).ToLocalTime(),
                        TicketCount = r.GetInt32(2),
                        TotalAmount = r.GetDecimal(3),
                        EventId = r.GetInt32(4),
                        EventTitle = r.GetString(5),
                        StartsAt = r.GetFieldValue<DateTimeOffset>(6).ToLocalTime(),
                        Status = r.GetString(7),
                        Venue = r.GetString(8),
                        Price = r.GetDecimal(9)
                    });
                }
            }

            // For each booking, load its tickets
            foreach (var g in groups)
            {
                using var tcmd = new NpgsqlCommand(@"
                    SELECT ticket_id
                    FROM booking_ticket
                    WHERE booking_id = @b
                    ORDER BY ticket_id;", conn);
                tcmd.Parameters.AddWithValue("b", g.BookingId);
                using var tr = tcmd.ExecuteReader();
                while (tr.Read())
                    g.Tickets.Add(new TicketRow { TicketId = tr.GetGuid(0) });
            }

            return View(new MyTicketsVm { Groups = groups });
        }

        // GET: /Tickets/Qr/{ticketId}
        [HttpGet]
        public IActionResult Qr(Guid ticketId)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            using var conn = _db.GetConnection();
            conn.Open();

            // Ownership check + fetch minimal info
            using var cmd = new NpgsqlCommand(@"
                SELECT 1
                FROM booking_ticket t
                JOIN booking b ON b.booking_id = t.booking_id
                WHERE t.ticket_id = @tid AND b.user_id = @u;", conn);
            cmd.Parameters.AddWithValue("tid", ticketId);
            cmd.Parameters.AddWithValue("u", userId);
            var ok = cmd.ExecuteScalar() != null;
            if (!ok) return NotFound();

            // Generate QR PNG for the ticketId (payload can be just the GUID)
            using var gen = new QRCodeGenerator();
            using var data = gen.CreateQrCode(ticketId.ToString(), QRCodeGenerator.ECCLevel.Q);
            var png = new PngByteQRCode(data).GetGraphic(20); // byte[]

            return File(png, "image/png");
        }

        // GET: /Tickets/Pdf/{ticketId}
        [HttpGet]
        public IActionResult Pdf(Guid ticketId)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            using var conn = _db.GetConnection();
            conn.Open();

            // Fetch info and enforce ownership
            using var cmd = new NpgsqlCommand(@"
                SELECT t.ticket_id, b.booking_id, b.booked_at,
                       e.event_id, e.title, e.starts_at, e.status,
                       v.name AS venue, u.full_name, u.email
                FROM booking_ticket t
                JOIN booking b ON b.booking_id = t.booking_id
                JOIN event e ON e.event_id = b.event_id
                JOIN venue v ON v.venue_id = e.venue_id
                JOIN users u ON u.user_id = b.user_id
                WHERE t.ticket_id = @tid AND b.user_id = @u
                LIMIT 1;", conn);
            cmd.Parameters.AddWithValue("tid", ticketId);
            cmd.Parameters.AddWithValue("u", userId);

            using var r = cmd.ExecuteReader();
            if (!r.Read()) return NotFound();

            var ticketGuid = r.GetGuid(0);
            var bookingId = r.GetGuid(1);
            var bookedAt = r.GetFieldValue<DateTimeOffset>(2).ToLocalTime();
            var eventId = r.GetInt32(3);
            var title = r.GetString(4);
            var starts = r.GetFieldValue<DateTimeOffset>(5).ToLocalTime();
            var status = r.GetString(6);
            var venue = r.GetString(7);
            var fullName = r.GetString(8);
            var email = r.GetString(9);

            // Build QR image bytes
            using var gen = new QRCodeGenerator();
            using var data = gen.CreateQrCode(ticketGuid.ToString(), QRCodeGenerator.ECCLevel.Q);
            var qrPng = new PngByteQRCode(data).GetGraphic(20);

            // Render PDF
            var pdfBytes = RenderTicketPdf(title, starts, venue, fullName, email, ticketGuid, bookingId, status, bookedAt, qrPng);

            var fileName = $"Ticket-{title}-{ticketGuid.ToString("N")[..8].ToUpper()}.pdf";
            return File(pdfBytes, "application/pdf", fileName);
        }

        private static byte[] RenderTicketPdf(
            string title, DateTimeOffset starts, string venue,
            string fullName, string email,
            Guid ticketId, Guid bookingId, string status,
            DateTimeOffset bookedAt, byte[] qrPng)
        {
            using var ms = new MemoryStream();
            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(36);
                    page.Size(PageSizes.A5);

                    page.Header().Row(row =>
                    {
                        row.ConstantItem(24).Text("🎟️");
                        row.RelativeItem().Text($"Ticket • {title}")
                           .Bold().FontSize(16);
                        row.ConstantItem(120).AlignRight().Text($"#{ticketId.ToString("N")[..8].ToUpper()}").FontSize(10);
                    });

                    page.Content().Column(col =>
                    {
                        col.Item().Text($"{starts:ddd dd MMM yyyy, h:mm tt} — {venue}")
                                  .FontSize(11).FontColor(Colors.Grey.Darken2);
                        col.Item().Text($"Status: {status}").FontSize(11);

                        col.Item().PaddingTop(12).BorderTop(1).PaddingTop(12);

                        col.Item().Row(r =>
                        {
                            r.RelativeItem().Column(info =>
                            {
                                info.Item().Text("Attendee").Bold();
                                info.Item().Text(fullName);
                                info.Item().Text(email).FontColor(Colors.Grey.Darken2).FontSize(10);

                                info.Item().PaddingTop(8).Text("Booking Ref").Bold();
                                info.Item().Text(bookingId.ToString());
                                info.Item().PaddingTop(8).Text("Booked At").Bold();
                                info.Item().Text($"{bookedAt:ddd dd MMM yyyy, h:mm tt}");
                            });

                            r.ConstantItem(220).Column(q =>
                            {
                                q.Item().Text("Scan at entry").Bold();
                                q.Item().PaddingTop(6).Width(200).Height(200).Image(qrPng);
                            });
                        });

                        col.Item().PaddingTop(12).BorderTop(1).PaddingTop(8)
                           .Text("Important: This QR admits one person. Do not share publicly. Organizer may request ID.")
                           .FontSize(9).FontColor(Colors.Grey.Darken2);
                    });
                });
            })
            .GeneratePdf(ms);

            return ms.ToArray();
        }
    }
}
