using EventTicketingSystem.Models;

namespace EventTicketingSystem.Services
{
    public static class EventListService
    {
        // TEMP data source (replace with EF later)
        private static readonly List<EventCardVm> _events = new()
        {
            new() { Title="Rock Night Colombo", DateTime="Sun 12 Oct 2025, 7:00 PM", Venue="Nelum Pokuna", Price="LKR 5,000", Availability="120 / 800" },
            new() { Title="Classical Evening",   DateTime="Mon 20 Oct 2025, 6:30 PM", Venue="BMICH",        Price="LKR 3,500", Availability="34 / 500" },
            new() { Title="Jazz & Chill",        DateTime="Sat 18 Oct 2025, 7:30 PM", Venue="Nelum Pokuna", Price="LKR 4,000", Availability="210 / 900" },
            new() { Title="Kandyan Dance Gala",  DateTime="Wed 22 Oct 2025, 6:00 PM", Venue="BMICH",        Price="LKR 2,800", Availability="340 / 1200" },
            new() { Title="Drama Fest",          DateTime="Fri 24 Oct 2025, 7:00 PM", Venue="Elphinstone",  Price="LKR 2,500", Availability="95 / 500" },
            new() { Title="Folk Night",          DateTime="Sun 26 Oct 2025, 6:30 PM", Venue="Town Hall",    Price="LKR 2,000", Availability="410 / 1000" },
        };

        public static IEnumerable<EventCardVm> GetAll() => _events;

        public static (IEnumerable<EventCardVm> Items, int Total) GetPaged(
            int page, int pageSize, string? q = null, string? category = null, string? location = null)
        {
            // simple filtering demo (extend later)
            IEnumerable<EventCardVm> query = _events;
            if (!string.IsNullOrWhiteSpace(q))
                query = query.Where(e => e.Title.Contains(q, StringComparison.OrdinalIgnoreCase));

            // TODO: apply category/location when you add those fields to model

            var total = query.Count();
            var items = query.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return (items, total);
        }
    }
}
