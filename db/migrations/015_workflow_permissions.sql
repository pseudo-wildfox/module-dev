-- ============================================================
-- 015_workflow_permissions.sql
--
-- Security boundary for the workflow subsystem.
--
-- Roles:
--
--   course_owner
--       NOLOGIN owner of workflow schema and objects.
--
--   workflow_worker
--       Worker runtime role.
--       Receives USAGE on the schema and EXECUTE only on
--       explicitly granted workflow functions.
--
-- Direct DML to workflow tables is forbidden for the worker.
-- ============================================================


-- ============================================================
-- 1. Schema ownership
-- ============================================================

ALTER SCHEMA workflow
    OWNER TO course_owner;


-- ============================================================
-- 2. Remove PUBLIC access
--
-- No workflow schema or workflow object should become
-- implicitly accessible through PUBLIC privileges.
-- ============================================================

REVOKE ALL
    ON SCHEMA workflow
    FROM PUBLIC;

REVOKE ALL
    ON ALL TABLES IN SCHEMA workflow
    FROM PUBLIC;

REVOKE ALL
    ON ALL SEQUENCES IN SCHEMA workflow
    FROM PUBLIC;

REVOKE ALL
    ON ALL FUNCTIONS IN SCHEMA workflow
    FROM PUBLIC;


-- ============================================================
-- 3. Worker schema access
--
-- The worker may resolve and execute explicitly granted
-- workflow functions, but this does not grant table DML.
-- ============================================================

GRANT USAGE
    ON SCHEMA workflow
    TO workflow_worker;


-- ============================================================
-- 4. Explicitly deny direct table mutation/read access
--
-- Workflow state must be accessed through trusted PostgreSQL
-- function boundaries.
--
-- Future workflow tables are handled here through the current
-- object set; default privileges below protect new objects.
-- ============================================================

REVOKE ALL
    ON ALL TABLES IN SCHEMA workflow
    FROM workflow_worker;

REVOKE ALL
    ON ALL SEQUENCES IN SCHEMA workflow
    FROM workflow_worker;


-- ============================================================
-- 5. Worker must not receive broad function execution
--
-- Only explicitly named workflow functions are granted later,
-- for example:
--
--   workflow.claim_jobs(...)
--   workflow.finish_job(...)
--   workflow.fail_job(...)
--
-- Therefore do not grant EXECUTE on ALL FUNCTIONS here.
-- ============================================================

REVOKE ALL
    ON ALL FUNCTIONS IN SCHEMA workflow
    FROM workflow_worker;


-- ============================================================
-- 6. Default privileges for future workflow objects
--
-- These are important because workflow objects will be created
-- by course_owner in later migrations.
--
-- New tables/sequences/functions must not accidentally become
-- accessible to PUBLIC or directly writable by worker.
-- ============================================================

ALTER DEFAULT PRIVILEGES
    FOR ROLE course_owner
    IN SCHEMA workflow
    REVOKE ALL ON TABLES FROM PUBLIC;

ALTER DEFAULT PRIVILEGES
    FOR ROLE course_owner
    IN SCHEMA workflow
    REVOKE ALL ON SEQUENCES FROM PUBLIC;

ALTER DEFAULT PRIVILEGES
    FOR ROLE course_owner
    IN SCHEMA workflow
    REVOKE EXECUTE ON FUNCTIONS FROM PUBLIC;


-- ============================================================
-- 7. Worker default privileges
--
-- Explicitly keep the worker without direct DML on future
-- workflow tables/sequences.
--
-- Function execution is granted individually by subsequent
-- migrations.
-- ============================================================

ALTER DEFAULT PRIVILEGES
    FOR ROLE course_owner
    IN SCHEMA workflow
    REVOKE ALL ON TABLES FROM workflow_worker;

ALTER DEFAULT PRIVILEGES
    FOR ROLE course_owner
    IN SCHEMA workflow
    REVOKE ALL ON SEQUENCES FROM workflow_worker;

ALTER DEFAULT PRIVILEGES
    FOR ROLE course_owner
    IN SCHEMA workflow
    REVOKE EXECUTE ON FUNCTIONS FROM workflow_worker;


-- ============================================================
-- 8. Shared action runtime boundary
--
-- The workflow worker uses the same generic action executor
-- as the HTTP API. It may execute api.invoke(), but still
-- cannot access action target tables/functions directly.
-- ============================================================

REVOKE ALL
    ON FUNCTION api.invoke(
        text,
        text,
        integer,
        jsonb,
        jsonb
    )
    FROM workflow_worker;

GRANT EXECUTE
    ON FUNCTION api.invoke(
        text,
        text,
        integer,
        jsonb,
        jsonb
    )
    TO workflow_worker;