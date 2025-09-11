ALTER TABLE venue
  ADD COLUMN IF NOT EXISTS created_by UUID NULL REFERENCES users(user_id),
  ADD COLUMN IF NOT EXISTS is_active BOOLEAN NOT NULL DEFAULT TRUE;

-- Optional index for faster organizer-scoped queries
CREATE INDEX IF NOT EXISTS idx_venue_created_by ON venue(created_by);
