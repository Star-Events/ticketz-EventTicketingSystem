using EventTicketingSystem.Data;
using EventTicketingSystem.Models;
using Npgsql;

namespace EventTicketingSystem.Services
{
    public class EventReadService
    {
        private readonly DbHelper _db;
        public EventReadService(DbHelper db) { _db = db; }

        public (IEnumerable<EventListItemVm> Items, int Total) GetPaged(
            int page, int pageSize, string? q,
            DateTimeOffset? fromUtc,
            DateTimeOffset? toUtcExclusive,
            int? categoryId   // 👈 NEW
            )
        {
            using var conn = _db.GetConnection();
            conn.Open();

            var where = new List<string>();
            if (!string.IsNullOrWhiteSpace(q))
                where.Add("LOWER(e.title) LIKE LOWER(@q)");
            if (fromUtc.HasValue)
                where.Add("e.starts_at >= @from");
            if (toUtcExclusive.HasValue)
                where.Add("e.starts_at < @to"); // exclusive
            if (categoryId.HasValue && categoryId.Value > 0)
                where.Add("e.category_id = @cid"); // 👈 NEW

            var whereSql = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

            // COUNT
            using var count = new NpgsqlCommand($@"
        SELECT COUNT(*) FROM event e {whereSql};", conn);
            if (!string.IsNullOrWhiteSpace(q)) count.Parameters.AddWithValue("q", $"%{q}%");
            if (fromUtc.HasValue) count.Parameters.AddWithValue("from", fromUtc.Value);
            if (toUtcExclusive.HasValue) count.Parameters.AddWithValue("to", toUtcExclusive.Value);
            if (categoryId.HasValue && categoryId.Value > 0) count.Parameters.AddWithValue("cid", categoryId.Value);
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
            if (categoryId.HasValue && categoryId.Value > 0) cmd.Parameters.AddWithValue("cid", categoryId.Value);
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
                    When = r.GetFieldValue<DateTimeOffset>(2).ToLocalTime().ToString("ddd dd MMM yyyy, h:mm tt"),
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

            // Auto-complete past events
            using (var auto = new NpgsqlCommand(@"
                UPDATE event
                SET status='Completed', updated_at=now()
                WHERE status IN ('Upcoming','Live')
                  AND starts_at < now();", conn))
            {
                auto.ExecuteNonQuery();
            }

            using var cmd = new NpgsqlCommand(@"
                SELECT
                e.event_id, e.title, COALESCE(e.description,'') AS description,
                COALESCE(ec.name,'Uncategorized') AS category_name,
                e.starts_at, e.ticket_price, e.total_tickets, e.sold_count,
                v.name AS venue_name, u.full_name AS organizer_name,
                e.status, e.image_path,
                u.email AS organizer_email, u.phone_number AS organizer_phone
                FROM event e
                JOIN venue v ON v.venue_id = e.venue_id
                JOIN users u ON u.user_id = e.organizer_id
                LEFT JOIN event_category ec ON ec.category_id = e.category_id
                WHERE e.event_id = @id
                LIMIT 1;", conn);

            cmd.Parameters.AddWithValue("id", id);

            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;

            int ix(string name) => r.GetOrdinal(name);


            return new EventDetailsVm
            {
                EventId = r.GetInt32(ix("event_id")),
                Title = r.GetString(ix("title")),
                Description = r.GetString(ix("description")),
                Category = r.GetString(ix("category_name")),
                StartsAt = r.GetFieldValue<DateTimeOffset>(ix("starts_at")),
                TicketPrice = r.GetDecimal(ix("ticket_price")),
                TotalTickets = r.GetInt32(ix("total_tickets")),
                SoldCount = r.GetInt32(ix("sold_count")),
                Venue = r.GetString(ix("venue_name")),
                OrganizerName = r.GetString(ix("organizer_name")),
                Status = r.GetString(ix("status")),
                ImagePath = r.IsDBNull(ix("image_path")) ? null : r.GetString(ix("image_path")),
                OrganizerEmail = r.IsDBNull(ix("organizer_email")) ? null : r.GetString(ix("organizer_email")),
                OrganizerPhone = r.IsDBNull(ix("organizer_phone")) ? null : r.GetString(ix("organizer_phone"))
            };
        }

        public IEnumerable<EventCardVm> GetUpcomingCards(int count = 2)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            // Auto-complete past events
            using (var auto = new NpgsqlCommand(@"
                UPDATE event
                SET status='Completed', updated_at=now()
                WHERE status IN ('Upcoming','Live')
                  AND starts_at < now();", conn))
            {
                auto.ExecuteNonQuery();
            }

            using var cmd = new NpgsqlCommand(@"
                SELECT
                  e.event_id, e.title, e.starts_at, e.ticket_price, e.total_tickets, e.sold_count,
                  v.name AS venue_name, e.status, e.image_thumb_path
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
                var startsAtLocal = r.GetFieldValue<DateTimeOffset>(2).ToLocalTime();
                var total = r.GetInt32(4);
                var sold = r.GetInt32(5);

                list.Add(new EventCardVm
                {
                    EventId = r.GetInt32(0),
                    Title = r.GetString(1),
                    DateTime = startsAtLocal.ToString("ddd dd MMM yyyy, h:mm tt"),
                    Venue = r.GetString(6),
                    Price = $"LKR {r.GetDecimal(3):N0}",
                    Availability = $"{sold} / {total}",
                    Status = r.GetString(7),
                    Remaining = total - sold,
                    ImageThumbPath = r.IsDBNull(8) ? null : r.GetString(8)
                });
            }
            return list;
        }

        public IEnumerable<EventCardVm> GetLiveCards(int count = 2)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            // Auto-complete any past events (same behavior as your other methods)
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
                WHERE e.status = 'Live' AND e.starts_at >= now()
                ORDER BY e.starts_at ASC
                LIMIT @lim;", conn);

            cmd.Parameters.AddWithValue("lim", count);

            var list = new List<EventCardVm>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var startsAtLocal = r.GetFieldValue<DateTimeOffset>(2).ToLocalTime();
                var total = r.GetInt32(4);
                var sold = r.GetInt32(5);

                list.Add(new EventCardVm
                {
                    EventId = r.GetInt32(0),
                    Title = r.GetString(1),
                    DateTime = startsAtLocal.ToString("ddd dd MMM yyyy, h:mm tt"),
                    Venue = r.GetString(6),
                    Price = $"LKR {r.GetDecimal(3):N0}",
                    Availability = $"{sold} / {total}",
                    Status = r.GetString(7),
                    Remaining = total - sold
                });
            }
            return list;
        }
    }
}

