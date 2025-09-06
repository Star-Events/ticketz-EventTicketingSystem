
-- Enable pgcrypto (for UUIDs)
CREATE EXTENSION IF NOT EXISTS pgcrypto;


-- 1) Users table


CREATE TABLE IF NOT EXISTS users (
  user_id        UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  full_name      VARCHAR(150) NOT NULL,
  email          VARCHAR(150) NOT NULL UNIQUE,
  phone_number   VARCHAR(30),
  password_hash  TEXT NOT NULL,       -- PBKDF2 hash (base64)
  password_salt  TEXT NOT NULL,       -- salt (base64)
  role           VARCHAR(20) NOT NULL CHECK (role IN ('Admin','Organizer','Customer')),
  status         VARCHAR(20) NOT NULL DEFAULT 'Active',
  created_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at     TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- 2) CustomerProfile table (only for customers)
CREATE TABLE IF NOT EXISTS customer_profile (
  customer_profile_id SERIAL PRIMARY KEY,
  user_id             UUID NOT NULL UNIQUE REFERENCES users(user_id) ON DELETE CASCADE,
  loyalty_points      INTEGER NOT NULL DEFAULT 0
);

