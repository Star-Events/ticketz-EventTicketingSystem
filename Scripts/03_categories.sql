-- 1) Category lookup
CREATE TABLE IF NOT EXISTS event_category (
  category_id SERIAL PRIMARY KEY,
  name        VARCHAR(50) NOT NULL UNIQUE,
  is_active   BOOLEAN NOT NULL DEFAULT TRUE
);

-- Seed
INSERT INTO event_category (name) VALUES
('Concert'), ('Theatre'), ('Comedy'), ('Festival'),
('Workshop'), ('Conference'), ('Sports'), ('Other')
ON CONFLICT (name) DO NOTHING;

-- 2) Event → Category FK
-- If your 'event' table currently has a text column named "category",
-- we’ll move to category_id while keeping the old column temporarily.
ALTER TABLE event
  ADD COLUMN IF NOT EXISTS category_id INTEGER REFERENCES event_category(category_id);

-- Optional: if you already have data in event.category (text), you can map a few common ones:
-- UPDATE event e SET category_id = ec.category_id
-- FROM event_category ec
-- WHERE LOWER(e.category) = LOWER(ec.name) AND e.category_id IS NULL;

-- (Later, once you're happy, you can drop the old text column:)
-- ALTER TABLE event DROP COLUMN category;
