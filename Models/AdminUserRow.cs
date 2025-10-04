namespace EventTicketingSystem.Models
{
    public class AdminUserRow
    {
        public Guid UserId { get; set; }
        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";
        public string? Phone { get; set; }
        public string Role { get; set; } = "";       // Customer | Organizer | Admin
        public string Status { get; set; } = "";     // Active | Inactive | Suspended
        public DateTimeOffset CreatedAt { get; set; }
    }
}
