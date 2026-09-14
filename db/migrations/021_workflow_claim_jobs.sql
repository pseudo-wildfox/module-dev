-- ============================================================
-- 021_workflow_claim_jobs.sql
--
-- Atomic job claim boundary.
--
-- Responsibilities:
--   - select READY / due RETRY_WAIT jobs;
--   - serialize competing workers with SKIP LOCKED;
--   - acquire lease;
--   - increment lease_version;
--   - increment attempt_count;
--   - create one RUNNING task_attempt;
--   - return everything required by the shared action executor.
--
-- The action itself is NOT executed here.
-- ============================================================

CREATE OR REPLACE FUNCTION workflow.claim_jobs(
    p_owner text,
    p_limit integer DEFAULT 1,
    p_lease_duration interval DEFAULT interval '2 seconds'
)
RETURNS TABLE
(
    job_id              uuid,
    process_id          uuid,
    step_instance_id    uuid,
    execution_id        uuid,

    attempt_id          uuid,
    attempt_number      integer,
    lease_version       bigint,

    flow_name           text,
    flow_version        integer,
    step_key            text,

    service             text,
    module              text,
    action              text,
    action_version      integer,

    request_schema      jsonb,
    response_schema     jsonb,
    outcomes            jsonb,
    required_policy     jsonb,

    timeout_ms          integer,
    max_attempts        integer,
    delays_ms           jsonb,

    input_mapping       jsonb,
    input_constants     jsonb,
    process_data        jsonb
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = workflow, pg_catalog
AS $$
BEGIN
    IF p_owner IS NULL OR btrim(p_owner) = '' THEN
        RAISE EXCEPTION
            'workflow claim owner is required';
    END IF;

    IF p_limit < 1 OR p_limit > 100 THEN
        RAISE EXCEPTION
            'workflow claim limit must be between 1 and 100';
    END IF;

    IF p_lease_duration <= interval '0 seconds' THEN
        RAISE EXCEPTION
            'workflow lease duration must be positive';
    END IF;

    RETURN QUERY
    WITH candidates AS
    (
        SELECT
            j.job_id
        FROM workflow_job AS j
        WHERE
            j.state = 'READY'

            OR

            (
                j.state = 'RETRY_WAIT'
                AND j.next_attempt_at <= clock_timestamp()
            )

        ORDER BY
            j.next_attempt_at NULLS FIRST,
            j.created_at,
            j.job_id

        FOR UPDATE SKIP LOCKED

        LIMIT p_limit
    ),

    claimed AS
    (
        UPDATE workflow_job AS j
        SET
            state = 'LEASED',
            lease_owner = p_owner,
            lease_version = j.lease_version + 1,
            lease_until = clock_timestamp() + p_lease_duration,
            attempt_count = j.attempt_count + 1,
            next_attempt_at = NULL,
            updated_at = clock_timestamp()
        FROM candidates AS c
        WHERE j.job_id = c.job_id
        RETURNING
            j.job_id,
            j.process_id,
            j.step_instance_id,
            j.execution_id,
            j.lease_version,
            j.attempt_count
    ),

    inserted_attempts AS
    (
        INSERT INTO task_attempt AS ta
        (
            attempt_id,
            job_id,
            execution_id,
            lease_version,
            attempt_number,
            status,
            started_at
        )
        SELECT
            gen_random_uuid(),
            c.job_id,
            c.execution_id,
            c.lease_version,
            c.attempt_count,
            'RUNNING',
            clock_timestamp()
        FROM claimed AS c
        RETURNING
            ta.attempt_id,
            ta.job_id,
            ta.execution_id,
            ta.lease_version,
            ta.attempt_number
    )

    SELECT
        c.job_id,
        c.process_id,
        c.step_instance_id,
        c.execution_id,

        a.attempt_id,
        a.attempt_number,
        c.lease_version,

        p.flow_name,
        p.flow_version,
        si.step_key,

        td.service,
        td.module,
        td.action,
        td.action_version,

        ad.request_schema,
        ad.response_schema,
        ad.outcomes,
        ad.required_policy,

        td.timeout_ms,
        td.retry_max_attempts,
        td.retry_delays_ms,

        td.input_mapping,
        td.input_constants,
        p.data

     FROM claimed AS c

    JOIN inserted_attempts AS a
      ON a.job_id = c.job_id
     AND a.execution_id = c.execution_id
     AND a.lease_version = c.lease_version

    JOIN process_instance AS p
      ON p.process_id = c.process_id

    JOIN step_instance AS si
      ON si.step_instance_id = c.step_instance_id
     AND si.process_id = c.process_id

    JOIN flow_version AS fv
      ON fv.id = p.flow_version_id

    JOIN step_definition AS sd
      ON sd.flow_version_id = fv.id
     AND sd.step_key = si.step_key

    JOIN task_definition AS td
      ON td.step_definition_id = sd.id

    JOIN runtime.action_definitions AS ad
      ON ad.module = td.module
     AND ad.action = td.action
     AND ad.version = td.action_version

    WHERE si.step_type = 'AUTOMATIC';
END;
$$;

-- ============================================================
-- Workflow worker execution boundaries
-- ============================================================

GRANT EXECUTE
    ON FUNCTION workflow.claim_jobs(
        text,
        integer,
        interval
    )
    TO workflow_worker;