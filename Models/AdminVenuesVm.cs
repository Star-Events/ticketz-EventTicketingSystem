namespace EventTicketingSystem.Models
{
    public class AdminVenueRow
    {
        public int VenueId { get; set; }
        public string Name { get; set; } = "";
        public string? Address { get; set; }
        public int Capacity { get; set; }
        public bool IsActive { get; set; }
        public string? OwnerName { get; set; }   // organizer who created it (if any)
    }

    public class EditVenueVm
    {
        public int VenueId { get; set; }
        public string Name { get; set; } = "";
        public string? Address { get; set; }
        public int Capacity { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
