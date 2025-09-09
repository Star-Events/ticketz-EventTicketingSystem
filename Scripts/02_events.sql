-- VENUE
CREATE TABLE IF NOT EXISTS venue (
  venue_id     SERIAL PRIMARY KEY,
  name         VARCHAR(150) NOT NULL,
  location     VARCHAR(200) NOT NULL,
  capacity     INTEGER NOT NULL CHECK (capacity >= 0),
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- EVENT
CREATE TABLE IF NOT EXISTS event (
  event_id      SERIAL PRIMARY KEY,
  organizer_id  UUID NOT NULL REFERENCES users(user_id) ON DELETE RESTRICT,
  venue_id      INTEGER NOT NULL REFERENCES venue(venue_id) ON DELETE RESTRICT,
  title         VARCHAR(200) NOT NULL,
  description   TEXT,
  category      VARCHAR(50),
  starts_at     TIMESTAMPTZ NOT NULL,
  ticket_price  NUMERIC(10,2) NOT NULL,
  total_tickets INTEGER NOT NULL CHECK (total_tickets >= 0),
  sold_count    INTEGER NOT NULL DEFAULT 0 CHECK (sold_count >= 0),
  status        VARCHAR(20) NOT NULL DEFAULT 'Upcoming', -- Upcoming/Live/Completed/cancelled
  created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at    TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_event_starts_at ON event(starts_at);
