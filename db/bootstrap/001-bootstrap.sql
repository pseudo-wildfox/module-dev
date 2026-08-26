-- ============================================================
-- 1. Application roles
-- ============================================================

-- moved to 000-create-roles.sh

-- ============================================================
-- 2. Database privilege required by migrations
--
-- course_owner needs CREATE on database course in order to
-- create schemas such as api/payment/autocheck.
-- ============================================================

GRANT CREATE ON DATABASE course
    TO course_owner;


-- ============================================================
-- 3. Migration bookkeeping schema
--
-- runtime must exist before the migration runner starts because
-- runtime.schema_migrations is used by the migration runner.
-- ============================================================

CREATE SCHEMA IF NOT EXISTS runtime
    AUTHORIZATION course_owner;


-- ============================================================
-- 4. Migration bookkeeping table
-- ============================================================

CREATE TABLE IF NOT EXISTS runtime.schema_migrations
(
    filename   text PRIMARY KEY,
    checksum   text NOT NULL,
    applied_at timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE runtime.schema_migrations
    OWNER TO course_owner;


-- ============================================================
-- 5. Migration bookkeeping access
-- ============================================================

GRANT USAGE
    ON SCHEMA runtime
    TO course_migrator;

GRANT SELECT, INSERT
    ON runtime.schema_migrations
    TO course_migrator;