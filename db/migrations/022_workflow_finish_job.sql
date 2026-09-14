-- ============================================================
-- 022_workflow_finish_job.sql
--
-- Finish boundary for one leased automatic job.
--
-- The caller owns the transaction.
--
-- Responsibilities:
--   - validate job fencing;
--   - validate current attempt;
--   - validate outcome;
--   - complete the attempt;
--   - complete the current job;
--   - complete the current step;
--   - resolve the transition;
--   - create/enter the next step;
--   - create the next job when required;
--   - update process state;
--   - append workflow history.
--
-- No COMMIT / ROLLBACK is performed here.
-- ============================================================

CREATE OR REPLACE FUNCTION workflow.finish_job(
    p_job_id uuid,
    p_owner text,
    p_lease_version bigint,
    p_outcome text,
    p_result jsonb
)
RETURNS jsonb
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = workflow, pg_catalog
AS $$
DECLARE
    v_job workflow_job%ROWTYPE;
    v_attempt task_attempt%ROWTYPE;

    v_process process_instance%ROWTYPE;
    v_step step_instance%ROWTYPE;
    v_step_definition step_definition%ROWTYPE;

    v_transition transition_definition%ROWTYPE;
    v_next_step_definition step_definition%ROWTYPE;

    v_next_step_instance_id uuid;
    v_next_job_id uuid;
    v_next_execution_id uuid;

    v_now timestamptz := clock_timestamp();
BEGIN
    IF p_job_id IS NULL THEN
        RAISE EXCEPTION
            USING
                MESSAGE = 'workflow job id is required';
    END IF;

    IF p_owner IS NULL OR btrim(p_owner) = '' THEN
        RAISE EXCEPTION
            USING
                MESSAGE = 'workflow finish owner is required';
    END IF;

    IF p_lease_version < 1 THEN
        RAISE EXCEPTION
            USING
                MESSAGE = 'workflow lease version must be positive';
    END IF;

    IF p_outcome IS NULL
       OR btrim(p_outcome) = '' THEN
        RAISE EXCEPTION
            USING
                MESSAGE = 'workflow outcome is required';
    END IF;

    -- ========================================================
    -- 1. Fencing check.
    --
    -- Only the currently leased owner/version may finish.
    -- ========================================================

    SELECT *
    INTO v_job
    FROM workflow_job
    WHERE job_id = p_job_id
    FOR UPDATE;

    IF NOT FOUND
       OR v_job.state <> 'LEASED'
       OR v_job.lease_owner IS DISTINCT FROM p_owner
       OR v_job.lease_version <> p_lease_version
       OR v_job.lease_until IS NULL
       OR v_job.lease_until <= v_now
    THEN
        RAISE EXCEPTION
            USING
                ERRCODE = 'P0001',
                MESSAGE = 'workflow.lease_stale';
    END IF;


    -- ========================================================
    -- 2. Load the current process and step.
    -- ========================================================

    SELECT *
    INTO v_process
    FROM process_instance
    WHERE process_id = v_job.process_id
    FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION
            USING
                MESSAGE = 'workflow process does not exist';
    END IF;


    SELECT *
    INTO v_step
    FROM step_instance
    WHERE step_instance_id = v_job.step_instance_id
      AND process_id = v_job.process_id
    FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION
            USING
                MESSAGE = 'workflow step does not exist';
    END IF;

    IF v_step.step_type <> 'AUTOMATIC'
       OR v_step.state <> 'READY'
       AND v_step.state <> 'RUNNING'
    THEN
        RAISE EXCEPTION
            USING
                MESSAGE = 'workflow step is not executable';
    END IF;


    -- ========================================================
    -- 3. Find the immutable step definition.
    -- ========================================================

    SELECT *
    INTO v_step_definition
    FROM step_definition
    WHERE flow_version_id = v_process.flow_version_id
      AND step_key = v_step.step_key;

    IF NOT FOUND THEN
        RAISE EXCEPTION
            USING
                MESSAGE = 'workflow step definition does not exist';
    END IF;


    -- ========================================================
    -- 4. Validate that the supplied outcome is declared by
    --    the action contract of the task.
    --
    -- The action executor already validates the action response,
    -- but finish_job is another trust boundary.
    -- ========================================================

    IF NOT EXISTS
    (
        SELECT 1
        FROM task_definition AS td
        JOIN runtime.action_definitions AS ad
          ON ad.module = td.module
         AND ad.action = td.action
         AND ad.version = td.action_version
        WHERE td.step_definition_id = v_step_definition.id
          AND ad.outcomes ? p_outcome
    )
    THEN
        RAISE EXCEPTION
            USING
                MESSAGE =
                    'workflow outcome is not declared by the action';
    END IF;


    -- ========================================================
    -- 5. Find the current RUNNING attempt.
    -- ========================================================

    SELECT *
    INTO v_attempt
    FROM task_attempt
    WHERE job_id = v_job.job_id
      AND execution_id = v_job.execution_id
      AND lease_version = v_job.lease_version
      AND status = 'RUNNING'
    FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION
            USING
                MESSAGE = 'workflow running attempt does not exist';
    END IF;


    -- ========================================================
    -- 6. Complete attempt.
    -- ========================================================

    UPDATE task_attempt
    SET
        status = 'SUCCEEDED',
        outcome = p_outcome,
        finished_at = v_now
    WHERE attempt_id = v_attempt.attempt_id;


    -- ========================================================
    -- 7. Complete job.
    -- ========================================================

    UPDATE workflow_job
    SET
        state = 'SUCCEEDED',
        lease_owner = NULL,
        lease_until = NULL,
        updated_at = v_now
    WHERE job_id = v_job.job_id;


    -- ========================================================
    -- 8. Complete current step.
    -- ========================================================

    UPDATE step_instance
    SET
        state = 'COMPLETED',
        outcome = p_outcome,
        completed_at = v_now
    WHERE step_instance_id = v_step.step_instance_id
      AND process_id = v_process.process_id;


    -- ========================================================
    -- 9. Record completion history.
    -- ========================================================

    INSERT INTO workflow_event
    (
        event_id,
        process_id,
        step_instance_id,
        event_type,
        occurred_at,
        payload
    )
    VALUES
    (
        gen_random_uuid(),
        v_process.process_id,
        v_step.step_instance_id,
        'StepCompleted',
        v_now,
        jsonb_build_object(
            'outcome', p_outcome
        )
    );


    -- ========================================================
    -- 10. Resolve exactly one transition.
    -- ========================================================

    SELECT *
    INTO v_transition
    FROM transition_definition
    WHERE flow_version_id = v_process.flow_version_id
      AND from_step_key = v_step.step_key
      AND outcome = p_outcome;

    IF NOT FOUND THEN
        RAISE EXCEPTION
            USING
                MESSAGE = 'workflow transition does not exist';
    END IF;


    -- ========================================================
    -- 11. Resolve next step definition.
    -- ========================================================

    SELECT *
    INTO v_next_step_definition
    FROM step_definition
    WHERE flow_version_id = v_process.flow_version_id
      AND step_key = v_transition.to_step_key;

    IF NOT FOUND THEN
        RAISE EXCEPTION
            USING
                MESSAGE = 'workflow next step definition does not exist';
    END IF;


    -- ========================================================
    -- 12. Create the next step instance.
    -- ========================================================

    v_next_step_instance_id := gen_random_uuid();

    INSERT INTO step_instance
    (
        step_instance_id,
        process_id,
        step_key,
        step_type,
        state,
        outcome,
        entered_at,
        completed_at
    )
    VALUES
    (
        v_next_step_instance_id,
        v_process.process_id,
        v_next_step_definition.step_key,
        v_next_step_definition.step_type,

        CASE v_next_step_definition.step_type
            WHEN 'AUTOMATIC' THEN 'READY'
            WHEN 'WAIT_SIGNAL' THEN 'WAITING'
            WHEN 'MANUAL' THEN 'WAITING'
            WHEN 'END' THEN 'COMPLETED'
        END,

        CASE
            WHEN v_next_step_definition.step_type = 'END'
                THEN v_next_step_definition.step_outcome
            ELSE NULL
        END,

        v_now,

        CASE
            WHEN v_next_step_definition.step_type = 'END'
                THEN v_now
            ELSE NULL
        END
    );


    -- ========================================================
    -- 13. Handle next step.
    -- ========================================================

    IF v_next_step_definition.step_type = 'AUTOMATIC' THEN

        v_next_job_id := gen_random_uuid();
        v_next_execution_id := gen_random_uuid();

        INSERT INTO workflow_job
        (
            job_id,
            process_id,
            step_instance_id,
            execution_id,
            state,
            lease_owner,
            lease_version,
            lease_until,
            attempt_count,
            next_attempt_at,
            created_at,
            updated_at
        )
        VALUES
        (
            v_next_job_id,
            v_process.process_id,
            v_next_step_instance_id,
            v_next_execution_id,
            'READY',
            NULL,
            0,
            NULL,
            0,
            NULL,
            v_now,
            v_now
        );

        UPDATE process_instance
        SET
            state = 'RUNNING',
            current_step_key = v_next_step_definition.step_key,
            updated_at = v_now
        WHERE process_id = v_process.process_id;

        INSERT INTO workflow_event
        (
            event_id,
            process_id,
            step_instance_id,
            event_type,
            occurred_at,
            payload
        )
        VALUES
        (
            gen_random_uuid(),
            v_process.process_id,
            v_next_step_instance_id,
            'StepReady',
            v_now,
            '{}'::jsonb
        );

    ELSIF v_next_step_definition.step_type = 'WAIT_SIGNAL' THEN

        UPDATE process_instance
        SET
            state = 'WAITING_SIGNAL',
            current_step_key = v_next_step_definition.step_key,
            updated_at = v_now
        WHERE process_id = v_process.process_id;

    ELSIF v_next_step_definition.step_type = 'MANUAL' THEN

        UPDATE process_instance
        SET
            state = 'WAITING_MANUAL',
            current_step_key = v_next_step_definition.step_key,
            updated_at = v_now
        WHERE process_id = v_process.process_id;

    ELSIF v_next_step_definition.step_type = 'END' THEN

        UPDATE process_instance
        SET
            state = 'COMPLETED',
            current_step_key = v_next_step_definition.step_key,
            updated_at = v_now
        WHERE process_id = v_process.process_id;

        INSERT INTO workflow_event
        (
            event_id,
            process_id,
            step_instance_id,
            event_type,
            occurred_at,
            payload
        )
        VALUES
        (
            gen_random_uuid(),
            v_process.process_id,
            v_next_step_instance_id,
            'StepCompleted',
            v_now,
            jsonb_build_object(
                'outcome',
                v_next_step_definition.step_outcome
            )
        );

    ELSE

        RAISE EXCEPTION
            USING
                MESSAGE = 'unsupported workflow step type';

    END IF;


    -- ========================================================
    -- 14. Return compact technical result.
    --
    -- The worker does not use this as the business result;
    -- the workflow state is authoritative.
    -- ========================================================

    RETURN jsonb_build_object(
        'status', 'ok',
        'jobId', v_job.job_id,
        'executionId', v_job.execution_id,
        'attemptId', v_attempt.attempt_id,
        'outcome', p_outcome,
        'nextStepKey', v_next_step_definition.step_key,
        'processState',
            CASE v_next_step_definition.step_type
                WHEN 'AUTOMATIC' THEN 'RUNNING'
                WHEN 'WAIT_SIGNAL' THEN 'WAITING_SIGNAL'
                WHEN 'MANUAL' THEN 'WAITING_MANUAL'
                WHEN 'END' THEN 'COMPLETED'
            END
    );
END;
$$;


ALTER FUNCTION workflow.finish_job(
    uuid,
    text,
    bigint,
    text,
    jsonb
)
OWNER TO course_owner;

-- ============================================================
-- Workflow worker execution boundaries
-- ============================================================

GRANT EXECUTE
    ON FUNCTION workflow.finish_job(
        uuid,
        text,
        bigint,
        text,
        jsonb
    )
    TO workflow_worker;