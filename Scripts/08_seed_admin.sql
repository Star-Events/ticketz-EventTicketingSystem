-- Seed an initial admin user (change email & password as needed)
DO $$
DECLARE
    salt BYTEA;
    hash BYTEA;
BEGIN
    -- Example password hashing (bcrypt or SHA256 done in C# usually)
    -- Here we just use a placeholder hash/salt so you can replace later.
    salt := decode('73616c745f73616d706c65', 'hex'); -- "salt_sample"
    hash := decode('686173685f73616d706c65', 'hex'); -- "hash_sample"

    INSERT INTO users (user_id, full_name, email, phone_number, password_hash, password_salt, role)
    VALUES (gen_random_uuid(), 'System Administrator', 'admin@system.local', NULL, hash, salt, 'Admin')
    ON CONFLICT (email) DO NOTHING;
END $$;
