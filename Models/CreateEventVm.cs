using System.ComponentModel.DataAnnotations;

namespace EventTicketingSystem.Models
{
    public class CreateEventVm
    {
        [Required, StringLength(200)]
        public string Title { get; set; } = "";

        [StringLength(2000)]
        public string? Description { get; set; }

        [Display(Name = "Date & Time"), Required]
        public DateTimeOffset StartsAt { get; set; } = DateTimeOffset.Now.AddDays(7);

        [Display(Name = "Venue"), Required]
        public int VenueId { get; set; }

        [Display(Name = "Category"), Required]          
        public int CategoryId { get; set; }            

        [Display(Name = "Ticket price (LKR)"), Range(0, 9_999_999)]
        public decimal TicketPrice { get; set; } = 0;

        [Display(Name = "Total tickets"), Range(0, int.MaxValue)]
        public int TotalTickets { get; set; } = 0;
    }
}
