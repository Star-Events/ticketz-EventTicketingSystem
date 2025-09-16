namespace EventTicketingSystem.Models
{
    public class AdminReportsVm
    {
        public DateTime FromDate { get; set; }
        public DateTime ToDateExclusive { get; set; } // exclusive upper bound
        public AdminReportKpi Kpi { get; set; } = new();
        public List<AdminReportOrganizerRow> ByOrganizer { get; set; } = new();
        public List<AdminReportEventRow> ByEvent { get; set; } = new();
    }

    public class AdminReportKpi
    {
        public int TotalBookings { get; set; }
        public int TicketsSold { get; set; }
        public int UniqueCustomers { get; set; }
        public decimal Revenue { get; set; }
    }

    public class AdminReportOrganizerRow
    {
        public Guid OrganizerId { get; set; }
        public string OrganizerName { get; set; } = "";
        public int Tickets { get; set; }
        public decimal Revenue { get; set; }
    }

    public class AdminReportEventRow
    {
        public int EventId { get; set; }
        public string Title { get; set; } = "";
        public string OrganizerName { get; set; } = "";
        public string Status { get; set; } = "Upcoming";
        public int Tickets { get; set; }
        public decimal Price { get; set; }   // current ticket price (for info)
        public decimal Revenue { get; set; }
        public DateTimeOffset StartsAt { get; set; }
    }
}
