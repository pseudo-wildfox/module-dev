-- ============================================================
-- 001-ownership-and-grants.sql
--
-- Applied by administrative bootstrap only.
--
-- Ownership model:
--
--   course_owner       NOLOGIN
--       owns application schemas/objects
--
--   course_migrator    LOGIN
--       may SET ROLE course_owner for migrations
--
--   course_publisher   LOGIN
--       may publish/activate action definitions
--
--   course_runtime     LOGIN
--       application runtime
--
-- Important:
--   course_runtime has NO direct DML access to payment tables.
--   Subject changes happen through trusted PostgreSQL functions.
-- ============================================================


-- ============================================================
-- 1. Schema ownership
-- ============================================================

ALTER SCHEMA api OWNER TO course_owner;
ALTER SCHEMA runtime OWNER TO course_owner;
ALTER SCHEMA payment OWNER TO course_owner;
ALTER SCHEMA autocheck OWNER TO course_owner;


-- ============================================================
-- 2. Existing table ownership
-- ============================================================

ALTER TABLE runtime.schema_migrations
    OWNER TO course_owner;

ALTER TABLE runtime.action_definitions
    OWNER TO course_owner;

ALTER TABLE runtime.idempotency
    OWNER TO course_owner;

ALTER TABLE runtime.action_dispatches
    OWNER TO course_owner;

ALTER TABLE payment.operations
    OWNER TO course_owner;

ALTER TABLE payment.operation_events
    OWNER TO course_owner;


-- ============================================================
-- 3. Remove default PUBLIC access
-- ============================================================

REVOKE ALL ON SCHEMA api FROM PUBLIC;
REVOKE ALL ON SCHEMA runtime FROM PUBLIC;
REVOKE ALL ON SCHEMA payment FROM PUBLIC;
REVOKE ALL ON SCHEMA autocheck FROM PUBLIC;

REVOKE ALL ON ALL TABLES IN SCHEMA api FROM PUBLIC;
REVOKE ALL ON ALL TABLES IN SCHEMA runtime FROM PUBLIC;
REVOKE ALL ON ALL TABLES IN SCHEMA payment FROM PUBLIC;
REVOKE ALL ON ALL TABLES IN SCHEMA autocheck FROM PUBLIC;

REVOKE ALL ON ALL SEQUENCES IN SCHEMA runtime FROM PUBLIC;
REVOKE ALL ON ALL SEQUENCES IN SCHEMA payment FROM PUBLIC;

REVOKE ALL ON ALL FUNCTIONS IN SCHEMA api FROM PUBLIC;


-- ============================================================
-- 4. course_runtime schema access
-- ============================================================

GRANT USAGE ON SCHEMA api
    TO course_runtime;

GRANT USAGE ON SCHEMA runtime
    TO course_runtime;

GRANT USAGE ON SCHEMA payment
    TO course_runtime;

GRANT USAGE ON SCHEMA autocheck
    TO course_runtime;


-- ============================================================
-- 5. course_runtime: action catalog is read-only
-- ============================================================

GRANT SELECT
    ON runtime.action_definitions
    TO course_runtime;


-- ============================================================
-- 6. course_runtime: idempotency storage
--
-- Runtime needs:
--   SELECT  -> inspect existing key/result
--   INSERT  -> reserve a new key
--   UPDATE  -> store final response
--
-- DELETE is deliberately forbidden.
-- ============================================================

GRANT SELECT, INSERT, UPDATE
    ON runtime.idempotency
    TO course_runtime;


-- ============================================================
-- 7. course_runtime: dispatch evidence
--
-- Dispatch records are written by api.invoke() under
-- SECURITY DEFINER / course_owner.
--
-- Runtime only needs read access for diagnostics.
-- ============================================================

GRANT SELECT
    ON runtime.action_dispatches
    TO course_runtime;


-- ============================================================
-- 8. course_runtime: absolutely no direct subject-table DML
-- ============================================================

REVOKE ALL
    ON payment.operations
    FROM course_runtime;

REVOKE ALL
    ON payment.operation_events
    FROM course_runtime;


-- ============================================================
-- 9. course_runtime: no direct catalog mutation
-- ============================================================

REVOKE INSERT, UPDATE, DELETE
    ON runtime.action_definitions
    FROM course_runtime;

REVOKE DELETE
    ON runtime.idempotency
    FROM course_runtime;

REVOKE INSERT, UPDATE, DELETE
    ON runtime.action_dispatches
    FROM course_runtime;


-- ============================================================
-- 10. course_publisher
-- ============================================================

GRANT USAGE
    ON SCHEMA runtime
    TO course_publisher;

GRANT SELECT
    ON runtime.action_definitions
    TO course_publisher;

-- Publication creates a new immutable version.
GRANT INSERT
    ON runtime.action_definitions
    TO course_publisher;

-- Operational state is mutable.
-- Only these two columns may be updated.
GRANT UPDATE (enabled, is_default)
    ON runtime.action_definitions
    TO course_publisher;

-- Explicitly forbid deletion.
REVOKE DELETE
    ON runtime.action_definitions
    FROM course_publisher;


-- ============================================================
-- 11. course_publisher: identity sequence
-- ============================================================

GRANT USAGE, SELECT
    ON SEQUENCE runtime.action_definitions_id_seq
    TO course_publisher;


-- ============================================================
-- 12. course_migrator
--
-- Migration runner connects as course_migrator and executes:
--
--     SET ROLE course_owner;
--
-- Therefore migration DDL is performed by course_owner.
--
-- No application runtime permissions are granted here.
-- ============================================================

GRANT USAGE
    ON SCHEMA api
    TO course_migrator;

GRANT USAGE
    ON SCHEMA runtime
    TO course_migrator;

GRANT USAGE
    ON SCHEMA payment
    TO course_migrator;

GRANT USAGE
    ON SCHEMA autocheck
    TO course_migrator;


-- ============================================================
-- 13. Explicitly protect subject tables from application roles
-- ============================================================

REVOKE ALL
    ON payment.operations
    FROM course_publisher, course_runtime;

REVOKE ALL
    ON payment.operation_events
    FROM course_publisher, course_runtime;


-- ============================================================
-- 14. autocheck
--
-- Views will be created later.
-- Runtime will receive SELECT on those views only.
-- ============================================================

GRANT USAGE
    ON SCHEMA autocheck
    TO course_runtime;


-- ============================================================
-- 15. Default privileges for future objects created by
--     course_owner
--
-- This matters because migrations and trusted functions will
-- create additional objects later.
-- ============================================================

ALTER DEFAULT PRIVILEGES FOR ROLE course_owner
    IN SCHEMA api
    REVOKE EXECUTE ON FUNCTIONS FROM PUBLIC;

ALTER DEFAULT PRIVILEGES FOR ROLE course_owner
    IN SCHEMA runtime
    REVOKE ALL ON TABLES FROM PUBLIC;

ALTER DEFAULT PRIVILEGES FOR ROLE course_owner
    IN SCHEMA payment
    REVOKE ALL ON TABLES FROM PUBLIC;

ALTER DEFAULT PRIVILEGES FOR ROLE course_owner
    IN SCHEMA autocheck
    REVOKE ALL ON TABLES FROM PUBLIC;

ALTER DEFAULT PRIVILEGES FOR ROLE course_owner
    IN SCHEMA runtime
    REVOKE ALL ON SEQUENCES FROM PUBLIC;

ALTER DEFAULT PRIVILEGES FOR ROLE course_owner
    IN SCHEMA payment
    REVOKE ALL ON SEQUENCES FROM PUBLIC;