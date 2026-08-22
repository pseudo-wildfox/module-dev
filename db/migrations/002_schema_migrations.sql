CREATE TABLE IF NOT EXISTS runtime.schema_migrations
(
    filename   text PRIMARY KEY,
    checksum   text NOT NULL,
    applied_at timestamptz NOT NULL DEFAULT now()
);