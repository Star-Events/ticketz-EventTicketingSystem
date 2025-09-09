namespace EventTicketingSystem.Models
{
    public class EventListItemVm
    {
        public int EventId { get; set; }
        public string Title { get; set; } = "";
        public string When { get; set; } = "";          // formatted datetime
        public string Venue { get; set; } = "";
        public string Price { get; set; } = "";         // formatted price
        public string Availability { get; set; } = "";  // e.g., "120 / 800"
        public string Status { get; set; } = "Upcoming";

        public int Remaining { get; set; }
    }
}