-- ============================================================
-- 020_week2_required_actions.sql
--
-- Week 2 development action:
-- training.canary v1
-- ============================================================

CREATE SCHEMA IF NOT EXISTS training
    AUTHORIZATION course_owner;

REVOKE ALL
    ON SCHEMA training
    FROM PUBLIC;


CREATE TABLE IF NOT EXISTS training.canary_effect
(
    execution_id uuid PRIMARY KEY,
    value        text NOT NULL,
    created_at   timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE training.canary_effect
    OWNER TO course_owner;

REVOKE ALL
    ON TABLE training.canary_effect
    FROM PUBLIC;


CREATE OR REPLACE FUNCTION training.canary_v1(
    p_context jsonb,
    p_payload jsonb
)
RETURNS jsonb
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = training, pg_catalog
AS $$
DECLARE
    v_execution_id uuid;
BEGIN
    v_execution_id :=
        (p_context ->> 'executionId')::uuid;

    INSERT INTO training.canary_effect
    (
        execution_id,
        value
    )
    VALUES
    (
        v_execution_id,
        p_payload ->> 'value'
    )
    ON CONFLICT (execution_id)
    DO NOTHING;

    RETURN jsonb_build_object(
        'status', 'ok',
        'outcome', 'APPLIED',
        'result', jsonb_build_object(
            'stored', true
        )
    );
END;
$$;

ALTER FUNCTION training.canary_v1(jsonb, jsonb)
    OWNER TO course_owner;

REVOKE ALL
    ON FUNCTION training.canary_v1(jsonb, jsonb)
    FROM PUBLIC;


INSERT INTO runtime.action_definitions
(
    contract_version,
    module,
    action,
    version,
    http_method,
    target_schema,
    target_function,
    request_schema,
    response_schema,
    outcomes,
    required_policy,
    idempotency_mode,
    idempotency_scope,
    timeout_ms,
    enabled,
    is_default,
    manifest_hash
)
VALUES
(
    'course-1',
    'training',
    'canary',
    1,
    'POST',
    'training',
    'canary_v1',

    '{
        "$schema": "https://json-schema.org/draft/2020-12/schema",
        "type": "object",
        "additionalProperties": false,
        "required": ["value"],
        "properties": {
            "value": {
                "type": "string"
            }
        }
    }'::jsonb,

    '{
        "$schema": "https://json-schema.org/draft/2020-12/schema",
        "type": "object",
        "additionalProperties": false,
        "required": ["stored"],
        "properties": {
            "stored": {
                "type": "boolean"
            }
        }
    }'::jsonb,

    jsonb_build_array('APPLIED'),
    jsonb_build_array('workflow:execute'),

    'required',
    'principal_action',

    2000,

    true,
    true,

    repeat('0', 64)
)
ON CONFLICT (module, action, version)
DO NOTHING;