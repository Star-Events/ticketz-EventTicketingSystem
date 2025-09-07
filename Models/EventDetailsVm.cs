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
    }
}