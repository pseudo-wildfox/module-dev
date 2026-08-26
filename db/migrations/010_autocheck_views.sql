-- ============================================================
-- 010_autocheck_views.sql
--
-- Stable, read-only projections used by the black-box checker.
--
-- Physical tables remain internal implementation details.
-- The checker interacts only with this schema.
-- ============================================================


-- ============================================================
-- 1. contract_info
-- ============================================================

CREATE OR REPLACE VIEW autocheck.contract_info
AS
SELECT
    'course-1'::text AS contract_version,
    CURRENT_TIMESTAMP::timestamptz AS generated_at;


ALTER VIEW autocheck.contract_info
    OWNER TO course_owner;


REVOKE ALL
    ON autocheck.contract_info
    FROM PUBLIC;

GRANT SELECT
    ON autocheck.contract_info
    TO course_runtime;


-- ============================================================
-- 2. action_definitions
-- ============================================================

CREATE OR REPLACE VIEW autocheck.action_definitions
AS
SELECT
    module,
    action,
    version,
    http_method,
    target_schema,
    target_function,
    outcomes,
    enabled,
    is_default
FROM runtime.action_definitions;


ALTER VIEW autocheck.action_definitions
    OWNER TO course_owner;


REVOKE ALL
    ON autocheck.action_definitions
    FROM PUBLIC;

GRANT SELECT
    ON autocheck.action_definitions
    TO course_runtime;


-- ============================================================
-- 3. action_dispatches
-- ============================================================

CREATE OR REPLACE VIEW autocheck.action_dispatches
AS
SELECT
    correlation_id,
    request_id,
    module,
    action,
    version,
    principal,
    payload_hash,
    status,
    outcome,
    occurred_at
FROM runtime.action_dispatches;


ALTER VIEW autocheck.action_dispatches
    OWNER TO course_owner;


REVOKE ALL
    ON autocheck.action_dispatches
    FROM PUBLIC;

GRANT SELECT
    ON autocheck.action_dispatches
    TO course_runtime;


-- ============================================================
-- 4. operations
--
-- Recreating it here makes the complete autocheck contract
-- self-contained in this migration.
-- ============================================================

CREATE OR REPLACE VIEW autocheck.operations
AS
SELECT
    operation_id,
    request_id,
    operation_kind,
    amount,
    currency,
    status,
    process_id,
    created_at,
    updated_at
FROM payment.operations;


ALTER VIEW autocheck.operations
    OWNER TO course_owner;


REVOKE ALL
    ON autocheck.operations
    FROM PUBLIC;

GRANT SELECT
    ON autocheck.operations
    TO course_runtime;


-- ============================================================
-- 5. operation_events
-- ============================================================

CREATE OR REPLACE VIEW autocheck.operation_events
AS
SELECT
    event_id,
    operation_id,
    event_type,
    payload_hash,
    occurred_at
FROM payment.operation_events;


ALTER VIEW autocheck.operation_events
    OWNER TO course_owner;


REVOKE ALL
    ON autocheck.operation_events
    FROM PUBLIC;

GRANT SELECT
    ON autocheck.operation_events
    TO course_runtime;


-- ============================================================
-- 6. Explicitly deny DML through the projection views.
--
-- SELECT is the only supported operation.
-- ============================================================

REVOKE INSERT, UPDATE, DELETE, TRUNCATE
    ON autocheck.contract_info,
       autocheck.action_definitions,
       autocheck.action_dispatches,
       autocheck.operations,
       autocheck.operation_events
FROM course_runtime;