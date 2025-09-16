namespace EventTicketingSystem.Models
{
    public class AdminDashboardVm
    {
        public AdminKpiVm Kpi { get; set; } = new();
        public List<AdminEventStatRow> TopEvents { get; set; } = new();
        public List<AdminRecentBookingRow> RecentBookings { get; set; } = new();
    }

    public class AdminKpiVm
    {
        public int UsersTotal { get; set; }
        public int UsersCustomers { get; set; }
        public int UsersOrganizers { get; set; }
        public int UsersAdmins { get; set; }

        public int EventsTotal { get; set; }
        public int EventsLive { get; set; }
        public int EventsUpcoming { get; set; }
        public int EventsCompleted { get; set; }
        public int EventsCancelled { get; set; }

        public int VenuesTotal { get; set; }        // all venues
        public int VenuesActive { get; set; }       // is_active = true

        public int TicketsSoldLifetime { get; set; }  // SUM(b.ticket_count)
        public decimal RevenueLifetime { get; set; }  // SUM(b.total_amount)
    }

    public class AdminEventStatRow
    {
        public int EventId { get; set; }
        public string Title { get; set; } = "";
        public string OrganizerName { get; set; } = "";
        public string Status { get; set; } = "Upcoming";
        public int Total { get; set; }
        public int Sold { get; set; }
        public decimal Price { get; set; }
        public decimal Revenue => Sold * Price;
    }

    public class AdminRecentBookingRow
    {
        public Guid BookingId { get; set; }
        public DateTimeOffset BookedAt { get; set; }
        public string CustomerName { get; set; } = "";
        public string EventTitle { get; set; } = "";
        public int TicketCount { get; set; }
        public decimal TotalAmount { get; set; }
    }
}
