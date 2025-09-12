using EventTicketingSystem.Data;
using EventTicketingSystem.Models;
using Npgsql;

namespace EventTicketingSystem.Services
{
    public class EventReadService
    {
        private readonly DbHelper _db;
        public EventReadService(DbHelper db) { _db = db; }

        public (IEnumerable<EventListItemVm> Items, int Total) GetPaged(int page, int pageSize, string? q, DateTimeOffset? fromUtc, DateTimeOffset? toUtcExclusive)
        {
            using var conn = _db.GetConnection();
            conn.Open();


            var where = new List<string>();
            if (!string.IsNullOrWhiteSpace(q))
                where.Add("LOWER(e.title) LIKE LOWER(@q)");
            if (fromUtc.HasValue)
                where.Add("e.starts_at >= @from");
            if (toUtcExclusive.HasValue)
                where.Add("e.starts_at < @to"); // note: exclusive upper bound

            var whereSql = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

            // COUNT
            using var count = new NpgsqlCommand($@"SELECT COUNT(*)
                FROM event e {whereSql};", conn);
            if (!string.IsNullOrWhiteSpace(q)) count.Parameters.AddWithValue("q", $"%{q}%");
            if (fromUtc.HasValue) count.Parameters.AddWithValue("from", fromUtc.Value);
            if (toUtcExclusive.HasValue) count.Parameters.AddWithValue("to", toUtcExclusive.Value);
            var total = Convert.ToInt32(count.ExecuteScalar());

            // PAGE
            using var cmd = new NpgsqlCommand($@"
                SELECT
                e.event_id, e.title, e.starts_at, e.ticket_price, e.total_tickets, e.sold_count,
                v.name AS venue_name, COALESCE(ec.name,'Uncategorized') AS category_name
                FROM event e
                JOIN venue v ON v.venue_id = e.venue_id
                LEFT JOIN event_category ec ON ec.category_id = e.category_id
                {whereSql}
                ORDER BY e.starts_at ASC
                LIMIT @ps OFFSET @off;", conn);


            if (!string.IsNullOrWhiteSpace(q)) cmd.Parameters.AddWithValue("q", $"%{q}%");
            if (fromUtc.HasValue) cmd.Parameters.AddWithValue("from", fromUtc.Value);
            if (toUtcExclusive.HasValue) cmd.Parameters.AddWithValue("to", toUtcExclusive.Value);
            cmd.Parameters.AddWithValue("ps", pageSize);
            cmd.Parameters.AddWithValue("off", (page - 1) * pageSize);

            var items = new List<EventListItemVm>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var totalTickets = r.GetInt32(4);
                var sold = r.GetInt32(5);
                items.Add(new EventListItemVm
                {
                    EventId = r.GetInt32(0),
                    Title = r.GetString(1),
                    When = r.GetFieldValue<DateTimeOffset>(2).ToLocalTime()
                                .ToString("ddd dd MMM yyyy, h:mm tt"),
                    Price = $"LKR {r.GetDecimal(3):N0}",
                    Availability = $"{sold} / {totalTickets}",
                    Venue = r.GetString(6)
                });
            }
            return (items, total);

        }

        public EventDetailsVm? GetDetails(int id)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using (var auto = new NpgsqlCommand(@"
                UPDATE event
                SET status='Completed', updated_at=now()
                WHERE status IN ('Upcoming','Live')
                AND starts_at < now();", conn))
            {
                auto.ExecuteNonQuery();
            }


            using var cmd = new NpgsqlCommand(@"
                SELECT e.event_id, e.title, COALESCE(e.description,''), 
                    COALESCE(ec.name,'Uncategorized') AS category_name,
                    e.starts_at, e.ticket_price, e.total_tickets, e.sold_count,
                    v.name AS venue_name, u.full_name AS organizer_name,
                    e.status
                FROM event e
                JOIN venue v ON v.venue_id = e.venue_id
                JOIN users u ON u.user_id = e.organizer_id
                LEFT JOIN event_category ec ON ec.category_id = e.category_id
                WHERE e.event_id = @id
                LIMIT 1;", conn);

            cmd.Parameters.AddWithValue("id", id);

            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;

            return new EventDetailsVm
            {
                EventId = r.GetInt32(0),
                Title = r.GetString(1),
                Description = r.GetString(2),
                Category = r.GetString(3),
                StartsAt = r.GetFieldValue<DateTimeOffset>(4),
                TicketPrice = r.GetDecimal(5),
                TotalTickets = r.GetInt32(6),
                SoldCount = r.GetInt32(7),
                Venue = r.GetString(8),
                OrganizerName = r.GetString(9),
                Status = r.GetString(10)
            };
        }

        public IEnumerable<EventCardVm> GetUpcomingCards(int count = 2)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using (var auto = new NpgsqlCommand(@"
                UPDATE event
                SET status='Completed', updated_at=now()
                WHERE status IN ('Upcoming','Live')
                AND starts_at < now();", conn))
            {
                auto.ExecuteNonQuery();
            }

            using var cmd = new NpgsqlCommand(@"
                SELECT e.event_id, e.title, e.starts_at, e.ticket_price, e.total_tickets, e.sold_count,
                    v.name AS venue_name, e.status
                FROM event e
                JOIN venue v ON v.venue_id = e.venue_id
                WHERE e.status = 'Upcoming' AND e.starts_at >= now()
                ORDER BY e.starts_at ASC
                LIMIT @lim;", conn);

            cmd.Parameters.AddWithValue("lim", count);

            var list = new List<EventCardVm>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var startsAtLocal = r.GetFieldValue<DateTimeOffset>(2).ToLocalTime(); // show local time
                var total = r.GetInt32(4);
                var sold = r.GetInt32(5);

                list.Add(new EventCardVm
                {
                    EventId = r.GetInt32(0),
                    Title = r.GetString(1),
                    DateTime = startsAtLocal.ToString("ddd dd MMM yyyy, h:mm tt"),
                    Venue = r.GetString(6),
                    Price = $"LKR {r.GetDecimal(3):N0}",
                    Availability = $"{sold} / {total}", // or $"{total - sold} / {total}" if you prefer “Available”
                    Status = r.GetString(7),
                    Remaining = total - sold
                });
            }
            return list;
        }
    }
}
