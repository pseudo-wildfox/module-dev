-- ============================================================
-- 013_action_dispatch_evidence.sql
--
-- Record runtime dispatch evidence inside the same transaction
-- as api.invoke().
--
-- Important:
--   course_runtime must not INSERT directly into
--   runtime.action_dispatches.
--
-- api.invoke() is SECURITY DEFINER and writes the evidence
-- using the trusted course_owner privileges.
-- ============================================================


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

    v_payload_hash text;
    v_request_id text;
    v_principal text;
    v_correlation_id uuid;
    v_outcome text;
BEGIN

    -- --------------------------------------------------------
    -- Resolve enabled action from immutable catalog.
    -- --------------------------------------------------------

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
        RETURN jsonb_build_object(
            'status', 'error',
            'code', 'action.not_found',
            'message', 'Action is not available.',
            'retryable', false,
            'details', '{}'::jsonb
        );
    END IF;


    -- --------------------------------------------------------
    -- Validate required policy against trusted server context.
    -- --------------------------------------------------------

    SELECT required_scope
    INTO v_missing_policy
    FROM jsonb_array_elements_text(
        COALESCE(v_required_policy, '[]'::jsonb)
    ) AS required_scope
    WHERE NOT EXISTS (
        SELECT 1
        FROM jsonb_array_elements_text(
            COALESCE(
                p_context -> 'scopes',
                '[]'::jsonb
            )
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
            'details', '{}'::jsonb
        );
    END IF;


    -- --------------------------------------------------------
    -- Read trusted context fields.
    -- --------------------------------------------------------

    v_request_id :=
        p_context ->> 'requestId';

    v_principal :=
        p_context ->> 'principal';

    v_correlation_id :=
        (p_context ->> 'correlationId')::uuid;


    -- --------------------------------------------------------
    -- Obtain the exact payload hash used by the C# runtime
    -- for idempotent actions.
    --
    -- For payment.request this is the authoritative hash:
    --
    --   C# JsonElement.GetRawText()
    --          ->
    --   SHA-256
    --          ->
    --   runtime.idempotency.payload_hash
    --
    -- If there is no idempotency record (for example a
    -- non-idempotent action), use a deterministic PostgreSQL
    -- fallback hash.
    -- --------------------------------------------------------

    IF v_request_id IS NOT NULL THEN

        SELECT payload_hash
        INTO v_payload_hash
        FROM runtime.idempotency
        WHERE idempotency_key = v_request_id
          AND scope_key = (
              CASE
                  WHEN EXISTS (
                      SELECT 1
                      FROM runtime.action_definitions ad
                      WHERE ad.module = p_module
                        AND ad.action = p_action
                        AND ad.version = p_version
                        AND ad.idempotency_scope = 'principal_action'
                  )
                  THEN v_principal
                       || '|'
                       || p_module
                       || '.'
                       || p_action

                  WHEN EXISTS (
                      SELECT 1
                      FROM runtime.action_definitions ad
                      WHERE ad.module = p_module
                        AND ad.action = p_action
                        AND ad.version = p_version
                        AND ad.idempotency_scope = 'consumer_action'
                  )
                  THEN (p_context ->> 'consumer')
                       || '|'
                       || p_module
                       || '.'
                       || p_action

                  WHEN EXISTS (
                      SELECT 1
                      FROM runtime.action_definitions ad
                      WHERE ad.module = p_module
                        AND ad.action = p_action
                        AND ad.version = p_version
                        AND ad.idempotency_scope = 'global_action'
                  )
                  THEN p_module || '.' || p_action

                  ELSE NULL
              END
          );

    END IF;


    IF v_payload_hash IS NULL THEN
        v_payload_hash :=
            encode(
                digest(
                    convert_to(
                        p_payload::text,
                        'UTF8'
                    ),
                    'sha256'
                ),
                'hex'
            );
    END IF;


    -- --------------------------------------------------------
    -- Call only the target registered in the immutable catalog.
    -- --------------------------------------------------------

    EXECUTE format(
        'SELECT %I.%I($1, $2)',
        v_target_schema,
        v_target_function
    )
    INTO v_result
    USING p_context, p_payload;


    -- --------------------------------------------------------
    -- Record successful runtime dispatch evidence.
    --
    -- This INSERT executes inside the same transaction as the
    -- target function.
    --
    -- If C# later rejects the result and rolls the transaction
    -- back, the dispatch record is rolled back as well.
    -- --------------------------------------------------------

    v_outcome :=
        CASE
            WHEN v_result ->> 'status' = 'ok'
                THEN v_result ->> 'outcome'
            ELSE NULL
        END;

    INSERT INTO runtime.action_dispatches
    (
        correlation_id,
        request_id,
        module,
        action,
        version,
        principal,
        payload_hash,
        status,
        outcome
    )
    VALUES
    (
        v_correlation_id,
        v_request_id,
        p_module,
        p_action,
        p_version,
        v_principal,
        v_payload_hash,
        CASE
            WHEN v_result ->> 'status' = 'ok'
                THEN 'OK'
            ELSE 'ERROR'
        END,
        v_outcome
    );


    RETURN v_result;
END;
$$;


ALTER FUNCTION api.invoke(
    text,
    text,
    integer,
    jsonb,
    jsonb
)
    OWNER TO course_owner;


REVOKE ALL
    ON FUNCTION api.invoke(
        text,
        text,
        integer,
        jsonb,
        jsonb
    )
    FROM PUBLIC;


GRANT EXECUTE
    ON FUNCTION api.invoke(
        text,
        text,
        integer,
        jsonb,
        jsonb
    )
    TO course_runtime;