namespace EventTicketingSystem.Models
{
    public class EventCardVm
    {
        public int EventId { get; set; }
        public string Title { get; set; } = "";
        public string DateTime { get; set; } = "";
        public string Venue { get; set; } = "";
        public string Price { get; set; } = "";
        public string Availability { get; set; } = "";

        public string Status { get; set; } = "Upcoming";  // Upcoming | Live | Completed | Cancelled
        public bool CanBuy => string.Equals(Status, "Live", StringComparison.OrdinalIgnoreCase);

    }
}
