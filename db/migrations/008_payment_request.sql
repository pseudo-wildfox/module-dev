-- ============================================================
-- Week 1 — payment.request v1
-- ============================================================

CREATE OR REPLACE FUNCTION payment.request_v1(
    p_context jsonb,
    p_payload jsonb
)
RETURNS jsonb
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = payment, runtime, pg_catalog
AS $$
DECLARE
    v_operation_id uuid;
    v_request_id text;
    v_operation_kind text;
    v_amount numeric(18,2);
    v_currency text;
    v_payload_hash text;
BEGIN
    /*
     * The HTTP/runtime layer already validated the request
     * against the published request schema.
     *
     * These fields are therefore expected to exist.
     */
    v_request_id :=
        p_context ->> 'requestId';

    v_operation_kind :=
        p_payload ->> 'operationKind';

    v_currency :=
        p_payload ->> 'currency';

    v_amount :=
        (p_payload ->> 'amount')::numeric(18,2);


    /*
     * payment.request requires idempotency.
     *
     * The idempotency reservation has already been created
     * by ActionExecutor in the same PostgreSQL transaction.
     *
     * Reuse its SHA-256 hash so the event hash is exactly the
     * same hash that runtime uses for idempotency.
     */
    SELECT payload_hash
    INTO v_payload_hash
    FROM runtime.idempotency
    WHERE idempotency_key = v_request_id
      AND scope_key = (
          (p_context ->> 'principal')
          || '|payment.request'
      );

    IF v_payload_hash IS NULL THEN
        RAISE EXCEPTION
            'payment.request idempotency record was not found';
    END IF;


    v_operation_id :=
        gen_random_uuid();


    INSERT INTO payment.operations
    (
        operation_id,
        request_id,
        operation_kind,
        amount,
        currency,
        status,
        process_id
    )
    VALUES
    (
        v_operation_id,
        v_request_id,
        v_operation_kind,
        v_amount,
        v_currency,
        'CREATED',
        NULL
    );


    INSERT INTO payment.operation_events
    (
        event_id,
        operation_id,
        event_type,
        payload_hash
    )
    VALUES
    (
        gen_random_uuid(),
        v_operation_id,
        'OPERATION_CREATED',
        v_payload_hash
    );


    RETURN jsonb_build_object(
        'status', 'ok',
        'outcome', 'CREATED',
        'result', jsonb_build_object(
            'operationId', v_operation_id,
            'requestId', v_request_id,
            'operationKind', v_operation_kind,
            'amount', to_char(
                v_amount,
                'FM9999999999999990.00'
            ),
            'currency', v_currency,
            'status', 'CREATED'
        )
    );
END;
$$;


ALTER FUNCTION payment.request_v1(jsonb, jsonb)
    OWNER TO course_owner;

REVOKE ALL
    ON FUNCTION payment.request_v1(jsonb, jsonb)
    FROM PUBLIC;