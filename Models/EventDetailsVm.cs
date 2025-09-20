namespace EventTicketingSystem.Models
{
    public class EventDetailsVm
    {
        public int EventId { get; set; }
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Category { get; set; } = "";
        public DateTimeOffset StartsAt { get; set; }
        public string Venue { get; set; } = "";
        public decimal TicketPrice { get; set; }
        public int TotalTickets { get; set; }
        public int SoldCount { get; set; }
        public string OrganizerName { get; set; } = "";
        public string Status { get; set; } = "Upcoming"; // Upcoming | Live | Completed | Cancelled



        public int Remaining => TotalTickets - SoldCount;
        public bool CanBuy => string.Equals(Status, "Live", StringComparison.OrdinalIgnoreCase) && Remaining > 0;

        public string? ImagePath { get; set; }

        public string? OrganizerEmail { get; set; }
        public string? OrganizerPhone { get; set; }

    }
}
