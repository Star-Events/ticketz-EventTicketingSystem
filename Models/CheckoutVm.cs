using System;
using System.ComponentModel.DataAnnotations;

namespace EventTicketingSystem.Models
{
    public class CheckoutVm
    {
        public int EventId { get; set; }
        public string Title { get; set; } = "";
        public string Venue { get; set; } = "";
        public DateTimeOffset StartsAt { get; set; }
        public decimal Price { get; set; }
        public int Remaining { get; set; }
        public string Status { get; set; } = "Upcoming";

        [Range(1, 20)]
        public int Quantity { get; set; } = 1;
    }

    public class CheckoutSuccessVm
    {
        public Guid BookingId { get; set; }
        public DateTimeOffset BookedAt { get; set; }
        public int TicketCount { get; set; }
        public decimal TotalAmount { get; set; }

        public int EventId { get; set; }
        public string Title { get; set; } = "";
        public string Venue { get; set; } = "";
        public DateTimeOffset StartsAt { get; set; }
    }
}
