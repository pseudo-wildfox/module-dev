CREATE OR REPLACE FUNCTION payment.operation_get_v1(
    p_context jsonb,
    p_payload jsonb
)
RETURNS jsonb
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = payment, pg_catalog
AS $$
DECLARE
    v_operation_id uuid;
    v_operation payment.operations%ROWTYPE;
BEGIN
    v_operation_id :=
        (p_payload ->> 'operationId')::uuid;

    SELECT *
    INTO v_operation
    FROM payment.operations
    WHERE operation_id = v_operation_id;

    IF NOT FOUND THEN
        RETURN jsonb_build_object(
            'status', 'error',
            'code', 'operation.not_found',
            'message', 'operation was not found',
            'retryable', false,
            'details', jsonb_build_object(
                'operationId', v_operation_id
            )
        );
    END IF;

    RETURN jsonb_build_object(
        'status', 'ok',
        'outcome', 'FOUND',
        'result', jsonb_build_object(
            'operationId', v_operation.operation_id,
            'requestId', v_operation.request_id,
            'operationKind', v_operation.operation_kind,
            'amount', to_char(
                v_operation.amount,
                'FM9999999999999990.00'
            ),
            'currency', v_operation.currency,
            'status', v_operation.status
        )
    );
END;
$$;

ALTER FUNCTION payment.operation_get_v1(jsonb, jsonb)
    OWNER TO course_owner;

REVOKE ALL
    ON FUNCTION payment.operation_get_v1(jsonb, jsonb)
    FROM PUBLIC;