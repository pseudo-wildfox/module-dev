-- ============================================================
-- 005-ownership-and-grants.sql
--
-- Security configuration for application objects.
--
-- Executed after all migrations.
--
-- Roles:
--
--   course_owner
--       NOLOGIN owner of application schemas and objects.
--
--   course_migrator
--       migration runner.
--
--   course_publisher
--       action publication CLI.
--
--   course_runtime
--       API runtime.
--
-- The PostgreSQL superuser is outside the application threat
-- model.
-- ============================================================


-- ============================================================
-- 1. Schema ownership
-- ============================================================

ALTER SCHEMA api
    OWNER TO course_owner;

ALTER SCHEMA runtime
    OWNER TO course_owner;

ALTER SCHEMA payment
    OWNER TO course_owner;

ALTER SCHEMA autocheck
    OWNER TO course_owner;


-- ============================================================
-- 2. Application object ownership
-- ============================================================

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
-- 3. Remove PUBLIC access
-- ============================================================

REVOKE ALL
    ON SCHEMA api
    FROM PUBLIC;

REVOKE ALL
    ON SCHEMA runtime
    FROM PUBLIC;

REVOKE ALL
    ON SCHEMA payment
    FROM PUBLIC;

REVOKE ALL
    ON SCHEMA autocheck
    FROM PUBLIC;

REVOKE ALL
    ON SCHEMA test
    FROM PUBLIC;

REVOKE ALL
    ON ALL TABLES IN SCHEMA api
    FROM PUBLIC;

REVOKE ALL
    ON ALL TABLES IN SCHEMA runtime
    FROM PUBLIC;

REVOKE ALL
    ON ALL TABLES IN SCHEMA payment
    FROM PUBLIC;

REVOKE ALL
    ON ALL TABLES IN SCHEMA autocheck
    FROM PUBLIC;


REVOKE ALL
    ON ALL SEQUENCES IN SCHEMA runtime
    FROM PUBLIC;

REVOKE ALL
    ON ALL SEQUENCES IN SCHEMA payment
    FROM PUBLIC;


REVOKE ALL
    ON ALL FUNCTIONS IN SCHEMA api
    FROM PUBLIC;


-- ============================================================
-- 4. Runtime schema access
-- ============================================================

GRANT USAGE
    ON SCHEMA api
    TO course_runtime;

GRANT USAGE
    ON SCHEMA runtime
    TO course_runtime;

GRANT USAGE
    ON SCHEMA payment
    TO course_runtime;

GRANT USAGE
    ON SCHEMA autocheck
    TO course_runtime;


-- ============================================================
-- 5. Runtime: action catalog
--
-- Runtime can inspect the catalog but cannot modify it.
-- ============================================================

GRANT SELECT
    ON runtime.action_definitions
    TO course_runtime;


-- ============================================================
-- 6. Runtime: idempotency
--
-- Runtime needs:
--
--   SELECT  existing record
--   INSERT  new key
--   UPDATE  final response
--
-- DELETE is forbidden.
-- ============================================================

GRANT SELECT, INSERT, UPDATE
    ON runtime.idempotency
    TO course_runtime;

REVOKE DELETE
    ON runtime.idempotency
    FROM course_runtime;


-- ============================================================
-- 7. Runtime: dispatches
--
-- Dispatch evidence is written by trusted database code.
-- Runtime only reads it.
-- ============================================================

GRANT SELECT
    ON runtime.action_dispatches
    TO course_runtime;


REVOKE INSERT, UPDATE, DELETE
    ON runtime.action_dispatches
    FROM course_runtime;


-- ============================================================
-- 8. Runtime: subject state
--
-- Direct access is forbidden.
--
-- Subject changes must happen through registered PostgreSQL
-- functions and the api.invoke() transaction boundary.
-- ============================================================

REVOKE ALL
    ON payment.operations
    FROM course_runtime;

REVOKE ALL
    ON payment.operation_events
    FROM course_runtime;


-- ============================================================
-- 9. Runtime: catalog mutation
-- ============================================================

REVOKE INSERT, UPDATE, DELETE
    ON runtime.action_definitions
    FROM course_runtime;


-- ============================================================
-- 10. Publisher
--
-- Publisher can:
--
--   SELECT published definitions
--   INSERT a new immutable version
--   UPDATE only enabled/is_default
--
-- Publisher cannot:
--
--   DELETE definitions
--   modify target
--   modify schemas
--   modify policy
--   modify idempotency configuration
-- ============================================================

GRANT USAGE
    ON SCHEMA runtime
    TO course_publisher;

GRANT SELECT
    ON runtime.action_definitions
    TO course_publisher;

GRANT INSERT
    ON runtime.action_definitions
    TO course_publisher;

GRANT UPDATE (enabled, is_default)
    ON runtime.action_definitions
    TO course_publisher;

REVOKE DELETE
    ON runtime.action_definitions
    FROM course_publisher;


-- ============================================================
-- 11. Publisher identity sequence
-- ============================================================

GRANT USAGE, SELECT
    ON SEQUENCE runtime.action_definitions_id_seq
    TO course_publisher;


-- ============================================================
-- 12. Publisher must not touch subject state
-- ============================================================

REVOKE ALL
    ON payment.operations
    FROM course_publisher;

REVOKE ALL
    ON payment.operation_events
    FROM course_publisher;


-- ============================================================
-- 13. Migration runner
--
-- course_migrator normally executes:
--
--     SET ROLE course_owner;
--
-- Therefore no application DML permissions are required here.
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
-- 14. autocheck
--
-- Views will be created by later migrations.
-- Runtime receives SELECT on the views themselves.
-- ============================================================

GRANT USAGE
    ON SCHEMA autocheck
    TO course_runtime;


-- ============================================================
-- 15. Default privileges for objects created by course_owner
-- ============================================================

ALTER DEFAULT PRIVILEGES
    FOR ROLE course_owner
    IN SCHEMA api
    REVOKE EXECUTE ON FUNCTIONS FROM PUBLIC;

ALTER DEFAULT PRIVILEGES
    FOR ROLE course_owner
    IN SCHEMA runtime
    REVOKE ALL ON TABLES FROM PUBLIC;

ALTER DEFAULT PRIVILEGES
    FOR ROLE course_owner
    IN SCHEMA payment
    REVOKE ALL ON TABLES FROM PUBLIC;

ALTER DEFAULT PRIVILEGES
    FOR ROLE course_owner
    IN SCHEMA autocheck
    REVOKE ALL ON TABLES FROM PUBLIC;

ALTER DEFAULT PRIVILEGES
    FOR ROLE course_owner
    IN SCHEMA runtime
    REVOKE ALL ON SEQUENCES FROM PUBLIC;

ALTER DEFAULT PRIVILEGES
    FOR ROLE course_owner
    IN SCHEMA payment
    REVOKE ALL ON SEQUENCES FROM PUBLIC;

    
-- ============================================================
-- 16. Test infrastructure
-- ============================================================


ALTER DEFAULT PRIVILEGES
    FOR ROLE course_owner
    IN SCHEMA test
    REVOKE ALL ON TABLES FROM PUBLIC;

ALTER DEFAULT PRIVILEGES
    FOR ROLE course_owner
    IN SCHEMA test
    REVOKE EXECUTE ON FUNCTIONS FROM PUBLIC;
