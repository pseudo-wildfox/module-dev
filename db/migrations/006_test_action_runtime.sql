-- ============================================================
-- Week 1 — first generic action runtime target
-- ============================================================

CREATE SCHEMA IF NOT EXISTS test
    AUTHORIZATION course_owner;

REVOKE ALL
    ON SCHEMA test
    FROM PUBLIC;


CREATE OR REPLACE FUNCTION test.echo_v1(
    p_context jsonb,
    p_payload jsonb
)
RETURNS jsonb
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = test, pg_catalog
AS $$
BEGIN
    RETURN jsonb_build_object(
        'status', 'ok',
        'outcome', 'OK',
        'result', jsonb_build_object(
            'echo', p_payload,
            'principal', p_context ->> 'principal'
        )
    );
END;
$$;

ALTER FUNCTION test.echo_v1(jsonb, jsonb)
    OWNER TO course_owner;

REVOKE ALL
    ON FUNCTION test.echo_v1(jsonb, jsonb)
    FROM PUBLIC;


CREATE OR REPLACE FUNCTION api.invoke(
    p_module text,
    p_action text,
    p_version integer,
    p_context jsonb,
    p_payload jsonb
)
RETURNS jsonb
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = api, runtime, pg_catalog
AS $$
DECLARE
    v_target_schema text;
    v_target_function text;
    v_required_policy jsonb;
    v_result jsonb;
    v_missing_policy text;
BEGIN

    SELECT
        target_schema,
        target_function,
        required_policy
    INTO
        v_target_schema,
        v_target_function,
        v_required_policy
    FROM runtime.action_definitions
    WHERE module = p_module
      AND action = p_action
      AND version = p_version
      AND enabled = true;

    IF NOT FOUND THEN
        RAISE EXCEPTION
            'action is not available';
    END IF;


    SELECT required_scope
    INTO v_missing_policy
    FROM jsonb_array_elements_text(v_required_policy)
         AS required_scope
    WHERE NOT EXISTS (
        SELECT 1
        FROM jsonb_array_elements_text(
            COALESCE(p_context -> 'scopes', '[]'::jsonb)
        ) AS granted_scope
        WHERE granted_scope = required_scope
    )
    LIMIT 1;


    IF v_missing_policy IS NOT NULL THEN
        RETURN jsonb_build_object(
            'status', 'error',
            'code', 'access.denied',
            'message', 'Required policy is missing.',
            'retryable', false,
            'details', jsonb_build_object()
        );
    END IF;


    EXECUTE format(
        'SELECT %I.%I($1, $2)',
        v_target_schema,
        v_target_function
    )
    INTO v_result
    USING p_context, p_payload;


    RETURN v_result;
END;
$$;

ALTER FUNCTION api.invoke(text, text, integer, jsonb, jsonb)
    OWNER TO course_owner;

REVOKE ALL
    ON FUNCTION api.invoke(text, text, integer, jsonb, jsonb)
    FROM PUBLIC;

GRANT EXECUTE
    ON FUNCTION api.invoke(text, text, integer, jsonb, jsonb)
    TO course_runtime;