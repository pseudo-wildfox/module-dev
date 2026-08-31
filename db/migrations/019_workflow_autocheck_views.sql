-- ============================================================
-- 019_workflow_autocheck_views.sql
--
-- Stable read-only workflow projections for the black-box
-- checker.
--
-- Physical workflow tables remain implementation details.
-- ============================================================


-- ============================================================
-- 1. flow_versions
-- ============================================================

CREATE OR REPLACE VIEW autocheck.flow_versions
AS
SELECT
    fd.flow_name,
    fv.version AS flow_version,
    fv.status,
    fv.is_active,
    fv.published_at
FROM workflow.flow_version fv
JOIN workflow.flow_definition fd
    ON fd.id = fv.flow_definition_id;


ALTER VIEW autocheck.flow_versions
    OWNER TO course_owner;


REVOKE ALL
    ON autocheck.flow_versions
    FROM PUBLIC;

GRANT SELECT
    ON autocheck.flow_versions
    TO course_runtime;


-- ============================================================
-- 2. processes
-- ============================================================

CREATE OR REPLACE VIEW autocheck.processes
AS
SELECT
    process_id,
    business_key,
    flow_name,
    flow_version,
    state,
    current_step_key,
    created_at,
    updated_at
FROM workflow.process_instance;


ALTER VIEW autocheck.processes
    OWNER TO course_owner;


REVOKE ALL
    ON autocheck.processes
    FROM PUBLIC;

GRANT SELECT
    ON autocheck.processes
    TO course_runtime;


-- ============================================================
-- 3. steps
-- ============================================================

CREATE OR REPLACE VIEW autocheck.steps
AS
SELECT
    step_instance_id,
    process_id,
    step_key,
    step_type,
    state,
    outcome,
    entered_at,
    completed_at
FROM workflow.step_instance;


ALTER VIEW autocheck.steps
    OWNER TO course_owner;


REVOKE ALL
    ON autocheck.steps
    FROM PUBLIC;

GRANT SELECT
    ON autocheck.steps
    TO course_runtime;


-- ============================================================
-- 4. jobs
-- ============================================================

CREATE OR REPLACE VIEW autocheck.jobs
AS
SELECT
    job_id,
    process_id,
    step_instance_id,
    execution_id,
    state,
    lease_owner,
    lease_version,
    lease_until,
    attempt_count,
    next_attempt_at
FROM workflow.workflow_job;


ALTER VIEW autocheck.jobs
    OWNER TO course_owner;


REVOKE ALL
    ON autocheck.jobs
    FROM PUBLIC;

GRANT SELECT
    ON autocheck.jobs
    TO course_runtime;


-- ============================================================
-- 5. attempts
-- ============================================================

CREATE OR REPLACE VIEW autocheck.attempts
AS
SELECT
    attempt_id,
    job_id,
    execution_id,
    lease_version,
    attempt_number,
    status,
    outcome,
    error_code,
    started_at,
    finished_at
FROM workflow.task_attempt;


ALTER VIEW autocheck.attempts
    OWNER TO course_owner;


REVOKE ALL
    ON autocheck.attempts
    FROM PUBLIC;

GRANT SELECT
    ON autocheck.attempts
    TO course_runtime;


-- ============================================================
-- 6. signals
-- ============================================================

CREATE OR REPLACE VIEW autocheck.signals
AS
SELECT
    message_id,
    process_id,
    signal_type,
    body_hash,
    status,
    received_at
FROM workflow.workflow_signal;


ALTER VIEW autocheck.signals
    OWNER TO course_owner;


REVOKE ALL
    ON autocheck.signals
    FROM PUBLIC;

GRANT SELECT
    ON autocheck.signals
    TO course_runtime;


-- ============================================================
-- 7. workflow_events
-- ============================================================

CREATE OR REPLACE VIEW autocheck.workflow_events
AS
SELECT
    event_id,
    process_id,
    step_instance_id,
    event_type,
    occurred_at
FROM workflow.workflow_event;


ALTER VIEW autocheck.workflow_events
    OWNER TO course_owner;


REVOKE ALL
    ON autocheck.workflow_events
    FROM PUBLIC;

GRANT SELECT
    ON autocheck.workflow_events
    TO course_runtime;


-- ============================================================
-- 8. Explicitly deny DML through workflow projection views.
-- ============================================================

REVOKE INSERT, UPDATE, DELETE, TRUNCATE
    ON autocheck.flow_versions,
       autocheck.processes,
       autocheck.steps,
       autocheck.jobs,
       autocheck.attempts,
       autocheck.signals,
       autocheck.workflow_events
FROM course_runtime;