-- ============================================================
-- 013_action_dispatch_evidence.sql
--
-- Runtime dispatch evidence.
--
-- api.invoke() is SECURITY DEFINER and records dispatch
-- evidence inside the same transaction as the target action.
--
-- Target functions are resolved only through the immutable
-- action catalog and must have the exact signature:
--
--   (jsonb, jsonb) RETURNS jsonb
--
-- course_runtime can EXECUTE api.invoke() but cannot directly
-- INSERT into runtime.action_dispatches.
-- ============================================================


-- ------------------------------------------------------------
-- pgcrypto is required for digest(..., 'sha256').
-- ------------------------------------------------------------

CREATE EXTENSION IF NOT EXISTS pgcrypto;


-- ------------------------------------------------------------
-- api.invoke
-- ------------------------------------------------------------

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

    v_target_oid oid;

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
    -- For an idempotent action, the authoritative hash is the
    -- value reserved by the C# runtime in runtime.idempotency.
    --
    -- For a non-idempotent action, use a deterministic fallback
    -- hash of the payload.
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
                public.digest(
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
    -- Validate exact target function signature.
    --
    -- The registered target must be:
    --
    --   <target_schema>.<target_function>(
    --       jsonb,
    --       jsonb
    --   ) RETURNS jsonb
    --
    -- The function must be an ordinary PostgreSQL function
    -- (not a procedure, aggregate, window function, etc.).
    --
    -- IMPORTANT:
    --   The target is still resolved exclusively from the
    --   immutable action catalog.
    -- --------------------------------------------------------

    SELECT p.oid
    INTO v_target_oid
    FROM pg_proc p
    JOIN pg_namespace n
      ON n.oid = p.pronamespace
    WHERE n.nspname = v_target_schema
      AND p.proname = v_target_function
      AND p.prokind = 'f'
      AND p.pronargs = 2
      AND p.proargtypes =
          ARRAY[
              'jsonb'::regtype,
              'jsonb'::regtype
          ]::oidvector
      AND p.prorettype = 'jsonb'::regtype;

    IF v_target_oid IS NULL THEN
        RETURN jsonb_build_object(
            'status', 'error',
            'code', 'internal.error',
            'message', 'Action target is invalid.',
            'retryable', false,
            'details', '{}'::jsonb
        );
    END IF;


    -- --------------------------------------------------------
    -- Call only the target registered in the immutable catalog.
    --
    -- Identifiers are quoted with %I and originate exclusively
    -- from the trusted catalog.
    -- --------------------------------------------------------

    EXECUTE format(
        'SELECT %I.%I($1, $2)',
        v_target_schema,
        v_target_function
    )
    INTO v_result
    USING p_context, p_payload;


    -- --------------------------------------------------------
    -- Record runtime dispatch evidence.
    --
    -- This INSERT is part of the same transaction as the target
    -- function. If the caller later rolls the transaction back,
    -- this evidence row is rolled back as well.
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


-- ------------------------------------------------------------
-- Ownership and privileges
-- ------------------------------------------------------------

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