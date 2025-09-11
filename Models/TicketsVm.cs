using System;
using System.Collections.Generic;

namespace EventTicketingSystem.Models
{
    public class MyTicketsVm
    {
        public List<BookingGroup> Groups { get; set; } = new();
    }

    public class BookingGroup
    {
        public Guid BookingId { get; set; }
        public DateTimeOffset BookedAt { get; set; }
        public string EventTitle { get; set; } = "";
        public int EventId { get; set; }
        public DateTimeOffset StartsAt { get; set; }
        public string Venue { get; set; } = "";
        public decimal Price { get; set; }           // price per ticket (for display)
        public int TicketCount { get; set; }
        public decimal TotalAmount { get; set; }
        public List<TicketRow> Tickets { get; set; } = new();
        public string Status { get; set; } = "Upcoming"; // event status
    }

    public class TicketRow
    {
        public Guid TicketId { get; set; }
        public string ShortId => TicketId.ToString("N")[..8].ToUpperInvariant();
    }
}
