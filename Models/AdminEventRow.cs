// Models/AdminEventRow.cs
namespace EventTicketingSystem.Models
{
    public class AdminEventRow
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public DateTimeOffset StartsAtLocal { get; set; }
        public string Venue { get; set; } = "";
        public string OrganizerName { get; set; } = "";
        public Guid OrganizerId { get; set; }
        public string Status { get; set; } = "Upcoming";
        public int Total { get; set; }
        public int Sold { get; set; }
    }
}
