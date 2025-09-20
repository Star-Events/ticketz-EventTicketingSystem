namespace EventTicketingSystem.Models
{
    public class HomeVm
    {
        public List<EventCardVm> Upcoming { get; set; } = new();
        public List<EventCardVm> Live { get; set; } = new();
    }
}
