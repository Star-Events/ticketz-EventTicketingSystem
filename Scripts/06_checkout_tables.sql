-- Bookings (an order)
CREATE TABLE IF NOT EXISTS booking (
  booking_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id    UUID NOT NULL REFERENCES users(user_id),
  event_id   INT  NOT NULL REFERENCES event(event_id),
  ticket_count INT NOT NULL CHECK (ticket_count > 0),
  total_amount NUMERIC(12,2) NOT NULL CHECK (total_amount >= 0),
  booked_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Individual tickets under a booking (one per seat/code)
CREATE TABLE IF NOT EXISTS booking_ticket (
  ticket_id  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  booking_id UUID NOT NULL REFERENCES booking(booking_id),
  created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Helpful indexes
CREATE INDEX IF NOT EXISTS ix_booking_user ON booking(user_id);
CREATE INDEX IF NOT EXISTS ix_booking_event ON booking(event_id);
CREATE INDEX IF NOT EXISTS ix_ticket_booking ON booking_ticket(booking_id);
