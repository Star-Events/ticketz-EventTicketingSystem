CREATE TABLE IF NOT EXISTS venue (
  venue_id     SERIAL PRIMARY KEY,
  name         VARCHAR(150) NOT NULL,
  location     VARCHAR(200) NOT NULL,
  capacity     INTEGER NOT NULL CHECK (capacity >= 0),
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);

