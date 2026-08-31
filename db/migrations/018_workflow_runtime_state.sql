-- ============================================================
-- 018_workflow_runtime_state.sql
--
-- Persistent runtime state of workflow execution.
--
-- Definitions:
--   workflow.flow_definition
--   workflow.flow_version
--   workflow.step_definition
--   workflow.task_definition
--   workflow.transition_definition
--
-- Runtime state:
--   workflow.process_instance
--   workflow.step_instance
--   workflow.workflow_job
--   workflow.task_attempt
--   workflow.workflow_signal
--   workflow.workflow_event
--
-- Runtime state survives worker recreation.
-- ============================================================


-- ============================================================
-- 1. Process instance
-- ============================================================

CREATE TABLE IF NOT EXISTS workflow.process_instance
(
    process_id          uuid PRIMARY KEY,

    flow_definition_id  bigint NOT NULL,
    flow_version_id     bigint NOT NULL,

    -- Denormalized evidence fields.
    -- Their consistency is protected by foreign keys below.
    flow_name           text NOT NULL,
    flow_version        integer NOT NULL,

    business_key        text NOT NULL,

    data                jsonb NOT NULL DEFAULT '{}'::jsonb,

    state               text NOT NULL,

    current_step_key    text,

    created_at          timestamptz NOT NULL DEFAULT now(),
    updated_at          timestamptz NOT NULL DEFAULT now(),


    CONSTRAINT fk_process_flow_definition
        FOREIGN KEY (flow_definition_id)
        REFERENCES workflow.flow_definition(id)
        ON DELETE RESTRICT,


    CONSTRAINT fk_process_flow_version
        FOREIGN KEY (flow_version_id)
        REFERENCES workflow.flow_version(id)
        ON DELETE RESTRICT,


    CONSTRAINT ck_process_data_object
        CHECK (
            jsonb_typeof(data) = 'object'
        ),


    CONSTRAINT ck_process_state
        CHECK (
            state IN (
                'CREATED',
                'RUNNING',
                'WAITING_SIGNAL',
                'WAITING_MANUAL',
                'COMPLETED',
                'FAILED'
            )
        ),


    CONSTRAINT ck_process_business_key
        CHECK (
            length(business_key) > 0
        ),


    CONSTRAINT uq_process_business_key
        UNIQUE (flow_name, business_key)
);


CREATE INDEX IF NOT EXISTS ix_process_flow_version
    ON workflow.process_instance (flow_version_id);


CREATE INDEX IF NOT EXISTS ix_process_state
    ON workflow.process_instance (state);


ALTER TABLE workflow.process_instance
    OWNER TO course_owner;


-- ============================================================
-- 2. Step instance
-- ============================================================

CREATE TABLE IF NOT EXISTS workflow.step_instance
(
    step_instance_id   uuid PRIMARY KEY,

    process_id         uuid NOT NULL,

    step_key            text NOT NULL,

    step_type           text NOT NULL,

    state               text NOT NULL,

    outcome             text,

    entered_at          timestamptz NOT NULL DEFAULT now(),
    completed_at        timestamptz,


    CONSTRAINT fk_step_instance_process
        FOREIGN KEY (process_id)
        REFERENCES workflow.process_instance(process_id)
        ON DELETE RESTRICT,


    CONSTRAINT uq_step_instance_process_key
        UNIQUE (process_id, step_key),


    -- Required as the referenced key for the composite FK from
    -- workflow_job. This guarantees that a job's step belongs
    -- to the same process stored in the job.
    CONSTRAINT uq_step_instance_process_id
        UNIQUE (process_id, step_instance_id),


    CONSTRAINT ck_step_instance_key
        CHECK (
            step_key ~ '^[a-z][a-z0-9_-]{0,62}$'
        ),


    CONSTRAINT ck_step_instance_type
        CHECK (
            step_type IN (
                'AUTOMATIC',
                'WAIT_SIGNAL',
                'MANUAL',
                'END'
            )
        ),


    CONSTRAINT ck_step_instance_state
        CHECK (
            state IN (
                'PENDING',
                'READY',
                'RUNNING',
                'WAITING',
                'COMPLETED',
                'FAILED'
            )
        ),


    CONSTRAINT ck_step_instance_outcome
        CHECK (
            outcome IS NULL
            OR outcome ~ '^[A-Z][A-Z0-9_]{0,62}$'
        ),


    CONSTRAINT ck_step_instance_completed_at
        CHECK (
            (
                state IN ('COMPLETED', 'FAILED')
                AND completed_at IS NOT NULL
            )
            OR
            (
                state NOT IN ('COMPLETED', 'FAILED')
                AND completed_at IS NULL
            )
        )
);


CREATE INDEX IF NOT EXISTS ix_step_instance_process
    ON workflow.step_instance (process_id);


CREATE INDEX IF NOT EXISTS ix_step_instance_process_state
    ON workflow.step_instance (process_id, state);


ALTER TABLE workflow.step_instance
    OWNER TO course_owner;


-- ============================================================
-- 3. Workflow job
--
-- One row is one logical executable job.
--
-- execution_id:
--   stable across retries and lease reclaim.
--
-- lease_version:
--   monotonically increasing fencing token.
--
-- attempt_count:
--   number of concrete attempts/reclaims represented by this job.
--
-- It is intentionally separate from attempt_number semantics.
-- ============================================================

CREATE TABLE IF NOT EXISTS workflow.workflow_job
(
    job_id             uuid PRIMARY KEY,

    process_id         uuid NOT NULL,

    step_instance_id   uuid NOT NULL,

    execution_id       uuid NOT NULL,

    state              text NOT NULL,

    lease_owner        text,

    lease_version      bigint NOT NULL DEFAULT 0,

    lease_until        timestamptz,

    attempt_count      integer NOT NULL DEFAULT 0,

    next_attempt_at    timestamptz,

    created_at         timestamptz NOT NULL DEFAULT now(),

    updated_at         timestamptz NOT NULL DEFAULT now(),


    CONSTRAINT fk_job_process
        FOREIGN KEY (process_id)
        REFERENCES workflow.process_instance(process_id)
        ON DELETE RESTRICT,


    -- The step must belong to this exact process.
    CONSTRAINT fk_job_step
        FOREIGN KEY (process_id, step_instance_id)
        REFERENCES workflow.step_instance (
            process_id,
            step_instance_id
        )
        ON DELETE RESTRICT,


    -- Exactly one logical job exists for one step instance.
    CONSTRAINT uq_job_step_instance
        UNIQUE (step_instance_id),


    -- executionId identifies the logical execution globally.
    CONSTRAINT uq_job_execution_id
        UNIQUE (execution_id),


    -- Required as the referenced key for the composite FK from
    -- task_attempt. This guarantees that an attempt cannot
    -- carry an execution_id different from its job.
    CONSTRAINT uq_job_id_execution_id
        UNIQUE (job_id, execution_id),


    CONSTRAINT ck_job_state
        CHECK (
            state IN (
                'READY',
                'LEASED',
                'RETRY_WAIT',
                'SUCCEEDED',
                'DEAD'
            )
        ),


    CONSTRAINT ck_job_lease_version
        CHECK (
            lease_version >= 0
        ),


    CONSTRAINT ck_job_attempt_count
        CHECK (
            attempt_count >= 0
        ),


    CONSTRAINT ck_job_leased_fields
        CHECK (
            (
                state = 'LEASED'
                AND lease_owner IS NOT NULL
                AND lease_until IS NOT NULL
            )
            OR
            state <> 'LEASED'
        ),


    CONSTRAINT ck_job_retry_wait_fields
        CHECK (
            (
                state = 'RETRY_WAIT'
                AND next_attempt_at IS NOT NULL
            )
            OR
            state <> 'RETRY_WAIT'
        )
);


CREATE INDEX IF NOT EXISTS ix_job_ready
    ON workflow.workflow_job (
        state,
        next_attempt_at,
        created_at
    )
    WHERE state IN ('READY', 'RETRY_WAIT');


CREATE INDEX IF NOT EXISTS ix_job_lease_expiry
    ON workflow.workflow_job (lease_until)
    WHERE state = 'LEASED';


CREATE INDEX IF NOT EXISTS ix_job_process
    ON workflow.workflow_job (process_id);


ALTER TABLE workflow.workflow_job
    OWNER TO course_owner;


-- ============================================================
-- 4. Task attempt
--
-- One row = one concrete lease/execution attempt.
--
-- Retry:
--   same job_id
--   same execution_id
--   new attempt_id
--   new attempt_number
--   new lease_version
--
-- Reclaim:
--   previous attempt becomes STALE
--   new attempt is created.
-- ============================================================

CREATE TABLE IF NOT EXISTS workflow.task_attempt
(
    attempt_id         uuid PRIMARY KEY,

    job_id             uuid NOT NULL,

    execution_id       uuid NOT NULL,

    lease_version      bigint NOT NULL,

    attempt_number     integer NOT NULL,

    status              text NOT NULL,

    outcome             text,

    error_code          text,

    started_at         timestamptz NOT NULL DEFAULT now(),

    finished_at        timestamptz,


    -- execution_id must be the execution_id of this exact job.
    CONSTRAINT fk_attempt_job_execution
        FOREIGN KEY (job_id, execution_id)
        REFERENCES workflow.workflow_job (
            job_id,
            execution_id
        )
        ON DELETE RESTRICT,


    CONSTRAINT ck_attempt_number
        CHECK (
            attempt_number >= 1
        ),


    CONSTRAINT ck_attempt_lease_version
        CHECK (
            lease_version >= 1
        ),


    CONSTRAINT ck_attempt_status
        CHECK (
            status IN (
                'RUNNING',
                'SUCCEEDED',
                'FAILED',
                'STALE'
            )
        ),


    CONSTRAINT ck_attempt_outcome
        CHECK (
            outcome IS NULL
            OR outcome ~ '^[A-Z][A-Z0-9_]{0,62}$'
        ),


    CONSTRAINT ck_attempt_finished_at
        CHECK (
            (
                status IN (
                    'SUCCEEDED',
                    'FAILED',
                    'STALE'
                )
                AND finished_at IS NOT NULL
            )
            OR
            (
                status = 'RUNNING'
                AND finished_at IS NULL
            )
        ),


    CONSTRAINT uq_attempt_job_number
        UNIQUE (job_id, attempt_number)
);


CREATE INDEX IF NOT EXISTS ix_attempt_job
    ON workflow.task_attempt (job_id);


CREATE INDEX IF NOT EXISTS ix_attempt_execution
    ON workflow.task_attempt (execution_id);


ALTER TABLE workflow.task_attempt
    OWNER TO course_owner;


-- ============================================================
-- 5. Workflow signal
--
-- message_id is globally unique.
--
-- Same message_id:
--   same process + same type + same body -> duplicate
--   anything else -> conflict
--
-- body is intentionally not restricted to object JSON.
-- The course contract does not impose an object-only shape.
-- ============================================================

CREATE TABLE IF NOT EXISTS workflow.workflow_signal
(
    message_id        text PRIMARY KEY,

    process_id        uuid NOT NULL,

    signal_type       text NOT NULL,

    body_hash         text NOT NULL,

    body              jsonb NOT NULL,

    status            text NOT NULL,

    received_at       timestamptz NOT NULL DEFAULT now(),


    CONSTRAINT fk_signal_process
        FOREIGN KEY (process_id)
        REFERENCES workflow.process_instance(process_id)
        ON DELETE RESTRICT,


    CONSTRAINT ck_signal_type
        CHECK (
            signal_type ~
                '^[a-z][a-z0-9_-]*(\.[a-z][a-z0-9_-]*)*$'
        ),


    CONSTRAINT ck_signal_body_hash
        CHECK (
            body_hash ~ '^[0-9a-f]{64}$'
        ),


    CONSTRAINT ck_signal_status
        CHECK (
            status IN (
                'ACCEPTED',
                'APPLIED'
            )
        )
);


CREATE INDEX IF NOT EXISTS ix_signal_process
    ON workflow.workflow_signal (process_id);


CREATE INDEX IF NOT EXISTS ix_signal_process_type
    ON workflow.workflow_signal (
        process_id,
        signal_type
    );


ALTER TABLE workflow.workflow_signal
    OWNER TO course_owner;


-- ============================================================
-- 6. Workflow event
--
-- Append-only history.
--
-- There is intentionally no UPDATE/DELETE API for this table.
-- Permissions are enforced separately.
--
-- payload is not restricted to object JSON because the runtime
-- contract does not require that restriction.
-- ============================================================

CREATE TABLE IF NOT EXISTS workflow.workflow_event
(
    event_id           uuid PRIMARY KEY,

    process_id         uuid NOT NULL,

    step_instance_id   uuid,

    event_type         text NOT NULL,

    occurred_at        timestamptz NOT NULL DEFAULT now(),

    payload            jsonb NOT NULL DEFAULT '{}'::jsonb,


    CONSTRAINT fk_event_process
        FOREIGN KEY (process_id)
        REFERENCES workflow.process_instance(process_id)
        ON DELETE RESTRICT,


    CONSTRAINT fk_event_step
        FOREIGN KEY (step_instance_id)
        REFERENCES workflow.step_instance(step_instance_id)
        ON DELETE RESTRICT,


    CONSTRAINT ck_event_type
        CHECK (
            event_type ~ '^[A-Z][A-Za-z0-9]*$'
        )
);


CREATE INDEX IF NOT EXISTS ix_event_process
    ON workflow.workflow_event (
        process_id,
        occurred_at
    );


CREATE INDEX IF NOT EXISTS ix_event_step
    ON workflow.workflow_event (
        step_instance_id,
        occurred_at
    );


ALTER TABLE workflow.workflow_event
    OWNER TO course_owner;