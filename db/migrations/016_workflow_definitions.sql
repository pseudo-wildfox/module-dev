-- ============================================================
-- 016_workflow_definitions.sql
--
-- Workflow definition layer.
--
-- Contains:
--   flow_definition
--   flow_version
--   step_definition
--   transition_definition
--   task_definition
--
-- Published workflow definitions are immutable at the
-- application level. Operational state such as is_active is
-- changed separately by atomic publication/activation commands.
--
-- Semantic validation which depends on the complete workflow
-- graph or action catalog is intentionally performed by the
-- workflow validator/publisher, not by these tables alone.
-- ============================================================


-- ============================================================
-- 1. Stable flow identity
-- ============================================================

CREATE TABLE IF NOT EXISTS workflow.flow_definition
(
    id              bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,

    flow_name       text NOT NULL,

    created_at      timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT uq_flow_definition_name
        UNIQUE (flow_name),

    CONSTRAINT ck_flow_definition_name
        CHECK (
            flow_name ~ '^[a-z][a-z0-9_-]{0,62}$'
        )
);


ALTER TABLE workflow.flow_definition
    OWNER TO course_owner;


-- ============================================================
-- 2. Published flow version
--
-- One row represents one immutable version of a workflow map.
--
-- A version is published first and may later become active for
-- NEW process instances.
--
-- Existing processes will store their own pinned flow_version
-- and therefore are not affected by later activation.
-- ============================================================

CREATE TABLE IF NOT EXISTS workflow.flow_version
(
    id                  bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,

    flow_definition_id  bigint NOT NULL,

    version             integer NOT NULL,

    contract_version    text NOT NULL,

    start_step          text NOT NULL,

    status              text NOT NULL DEFAULT 'PUBLISHED',

    map_json            jsonb NOT NULL,

    published_at        timestamptz NOT NULL DEFAULT now(),

    manifest_hash       text NOT NULL,

    is_active           boolean NOT NULL DEFAULT false,


    CONSTRAINT fk_flow_version_definition
        FOREIGN KEY (flow_definition_id)
        REFERENCES workflow.flow_definition(id)
        ON DELETE RESTRICT,


    CONSTRAINT uq_flow_version
        UNIQUE (flow_definition_id, version),


    CONSTRAINT ck_flow_version_number
        CHECK (version >= 1),


    CONSTRAINT ck_flow_version_contract
        CHECK (contract_version = 'course-1'),


    CONSTRAINT ck_flow_version_start_step
        CHECK (
            start_step ~ '^[a-z][a-z0-9_-]{0,62}$'
        ),


    CONSTRAINT ck_flow_version_status
        CHECK (
            status = 'PUBLISHED'
        ),


    CONSTRAINT ck_flow_version_manifest_hash
        CHECK (
            manifest_hash ~ '^[0-9a-f]{64}$'
        ),


    CONSTRAINT ck_flow_version_active_status
        CHECK (
            NOT is_active
            OR status = 'PUBLISHED'
        )
);


-- At most one active version may exist for a flow.
CREATE UNIQUE INDEX IF NOT EXISTS ux_flow_one_active_version
    ON workflow.flow_version (flow_definition_id)
    WHERE is_active = true;


ALTER TABLE workflow.flow_version
    OWNER TO course_owner;


-- ============================================================
-- 3. Step definitions
--
-- One row represents one node of one particular published
-- workflow version.
--
-- Type-specific fields:
--
--   AUTOMATIC
--       task_definition contains action execution details.
--
--   WAIT_SIGNAL
--       signal_type + step_outcome.
--
--   MANUAL
--       allowed_outcomes.
--
--   END
--       step_outcome.
--
-- Cross-field semantic consistency is checked by the workflow
-- publisher.
-- ============================================================

CREATE TABLE IF NOT EXISTS workflow.step_definition
(
    id                  bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,

    flow_version_id     bigint NOT NULL,

    step_key            text NOT NULL,

    step_type           text NOT NULL,

    signal_type         text,

    step_outcome        text,

    allowed_outcomes    jsonb,


    CONSTRAINT fk_step_definition_version
        FOREIGN KEY (flow_version_id)
        REFERENCES workflow.flow_version(id)
        ON DELETE RESTRICT,


    CONSTRAINT uq_step_definition_key
        UNIQUE (flow_version_id, step_key),


    CONSTRAINT ck_step_definition_key
        CHECK (
            step_key ~ '^[a-z][a-z0-9_-]{0,62}$'
        ),


    CONSTRAINT ck_step_definition_type
        CHECK (
            step_type IN (
                'AUTOMATIC',
                'WAIT_SIGNAL',
                'MANUAL',
                'END'
            )
        ),


    CONSTRAINT ck_step_definition_signal_type
        CHECK (
            signal_type IS NULL
            OR signal_type ~
                '^[a-z][a-z0-9_-]*(\.[a-z][a-z0-9_-]*)*$'
        ),


    CONSTRAINT ck_step_definition_outcome
        CHECK (
            step_outcome IS NULL
            OR step_outcome ~
                '^[A-Z][A-Z0-9_]{0,62}$'
        ),


    CONSTRAINT ck_step_definition_allowed_outcomes
        CHECK (
            allowed_outcomes IS NULL
            OR jsonb_typeof(allowed_outcomes) = 'array'
        )
);


ALTER TABLE workflow.step_definition
    OWNER TO course_owner;


-- ============================================================
-- 4. Task definitions
--
-- One task belongs to exactly one step definition.
--
-- In course-1 task definitions are used by AUTOMATIC steps.
-- The publisher verifies that the referenced step has the
-- correct type.
--
-- input_mapping:
--   target action payload JSON Pointer -> process data pointer
--
-- input_constants:
--   initial JSON object merged into the constructed payload
--
-- Retry:
--   max_attempts includes the first execution.
-- ============================================================

CREATE TABLE IF NOT EXISTS workflow.task_definition
(
    step_definition_id    bigint PRIMARY KEY,

    service               text NOT NULL,

    module                text NOT NULL,

    action                text NOT NULL,

    action_version        integer NOT NULL,

    required_policy       jsonb NOT NULL,

    timeout_ms            integer NOT NULL,

    retry_max_attempts    integer NOT NULL,

    retry_delays_ms       jsonb NOT NULL,

    input_mapping         jsonb NOT NULL,

    input_constants       jsonb NOT NULL,


    CONSTRAINT fk_task_definition_step
        FOREIGN KEY (step_definition_id)
        REFERENCES workflow.step_definition(id)
        ON DELETE RESTRICT,


    CONSTRAINT ck_task_service
        CHECK (
            service = 'postgres'
        ),


    CONSTRAINT ck_task_module
        CHECK (
            module ~ '^[a-z][a-z0-9_]{0,62}$'
        ),


    CONSTRAINT ck_task_action
        CHECK (
            action ~ '^[a-z][a-z0-9_]{0,62}$'
        ),


    CONSTRAINT ck_task_action_version
        CHECK (
            action_version >= 1
        ),


    CONSTRAINT ck_task_timeout
        CHECK (
            timeout_ms BETWEEN 1 AND 30000
        ),


    CONSTRAINT ck_task_required_policy
        CHECK (
            jsonb_typeof(required_policy) = 'array'
        ),


    CONSTRAINT ck_task_retry_attempts
        CHECK (
            retry_max_attempts BETWEEN 1 AND 10
        ),


    CONSTRAINT ck_task_retry_delays
        CHECK (
            jsonb_typeof(retry_delays_ms) = 'array'
        ),


    CONSTRAINT ck_task_retry_delays_length
        CHECK (
            jsonb_array_length(retry_delays_ms)
            = retry_max_attempts - 1
        ),


    CONSTRAINT ck_task_input_mapping
        CHECK (
            jsonb_typeof(input_mapping) = 'object'
        ),


    CONSTRAINT ck_task_input_constants
        CHECK (
            jsonb_typeof(input_constants) = 'object'
        )
);


ALTER TABLE workflow.task_definition
    OWNER TO course_owner;


-- ============================================================
-- 5. Transitions
--
-- A transition is unique by:
--
--   flow version + source step + outcome
--
-- Composite foreign keys ensure both source and destination
-- steps belong to the SAME workflow version.
--
-- The publisher additionally proves that:
--
--   * source step exists;
--   * target step exists;
--   * outcome is valid for the source step;
--   * every required outcome is covered exactly once;
--   * END has no outgoing transitions.
-- ============================================================

CREATE TABLE IF NOT EXISTS workflow.transition_definition
(
    id                  bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,

    flow_version_id     bigint NOT NULL,

    from_step_key       text NOT NULL,

    outcome             text NOT NULL,

    to_step_key         text NOT NULL,


    CONSTRAINT fk_transition_version
        FOREIGN KEY (flow_version_id)
        REFERENCES workflow.flow_version(id)
        ON DELETE RESTRICT,


    CONSTRAINT fk_transition_from_step
        FOREIGN KEY (flow_version_id, from_step_key)
        REFERENCES workflow.step_definition(flow_version_id, step_key)
        ON DELETE RESTRICT,


    CONSTRAINT fk_transition_to_step
        FOREIGN KEY (flow_version_id, to_step_key)
        REFERENCES workflow.step_definition(flow_version_id, step_key)
        ON DELETE RESTRICT,


    CONSTRAINT uq_transition
        UNIQUE (
            flow_version_id,
            from_step_key,
            outcome
        ),


    CONSTRAINT ck_transition_from_step
        CHECK (
            from_step_key ~ '^[a-z][a-z0-9_-]{0,62}$'
        ),


    CONSTRAINT ck_transition_outcome
        CHECK (
            outcome ~ '^[A-Z][A-Z0-9_]{0,62}$'
        ),


    CONSTRAINT ck_transition_to_step
        CHECK (
            to_step_key ~ '^[a-z][a-z0-9_-]{0,62}$'
        )
);


ALTER TABLE workflow.transition_definition
    OWNER TO course_owner;