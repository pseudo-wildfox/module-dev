-- ============================================================
-- Week 1 — rollback probe
-- ============================================================

CREATE TABLE IF NOT EXISTS test.rollback_canary
(
    id          bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    marker      text NOT NULL,
    created_at  timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE test.rollback_canary
    OWNER TO course_owner;

REVOKE ALL
    ON test.rollback_canary
    FROM PUBLIC;


CREATE OR REPLACE FUNCTION test.echo_rollback_v1(
    p_context jsonb,
    p_payload jsonb
)
RETURNS jsonb
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = test, pg_catalog
AS $$
BEGIN
    INSERT INTO test.rollback_canary(marker)
    VALUES (
        COALESCE(
            p_payload ->> 'marker',
            'rollback-probe'
        )
    );

    RETURN jsonb_build_object(
        'status', 'ok',
        'outcome', 'NOT_ALLOWED',
        'result', jsonb_build_object(
            'marker', p_payload ->> 'marker'
        )
    );
END;
$$;

ALTER FUNCTION test.echo_rollback_v1(jsonb, jsonb)
    OWNER TO course_owner;

REVOKE ALL
    ON FUNCTION test.echo_rollback_v1(jsonb, jsonb)
    FROM PUBLIC;