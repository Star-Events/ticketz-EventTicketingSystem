namespace EventTicketingSystem.Models
{
    public class OrgSalesSummaryVm
    {
        public int EventsTotal { get; set; }
        public int EventsLive { get; set; }
        public int EventsUpcoming { get; set; }
        public int TicketsSoldPeriod { get; set; }
        public decimal RevenuePeriod { get; set; }
        public int TicketsSoldLifetime { get; set; }
        public decimal RevenueLifetime { get; set; }
        public DateTimeOffset From { get; set; }
        public DateTimeOffset To { get; set; }
    }

    public class EventSalesRow
    {
        public int EventId { get; set; }
        public string Title { get; set; } = "";
        public string Status { get; set; } = "Upcoming";

        public int TotalTickets { get; set; }
        public int SoldLifetime { get; set; }
        public int Remaining => Math.Max(0, TotalTickets - SoldLifetime);
        public decimal Price { get; set; }

        // Period (date range) metrics
        public int SoldPeriod { get; set; }
        public int BookingsPeriod { get; set; }
        public decimal RevenuePeriod { get; set; }
        public DateTimeOffset? LastSaleAt { get; set; }
    }

    public class OrgReportsVm
    {
        public OrgSalesSummaryVm Summary { get; set; } = new();
        public List<EventSalesRow> Rows { get; set; } = new();
    }
}
