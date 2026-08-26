CREATE OR REPLACE VIEW autocheck.operations
AS
SELECT
    operation_id,
    request_id,
    operation_kind,
    amount,
    currency,
    status,
    process_id,
    created_at,
    updated_at
FROM payment.operations;


CREATE OR REPLACE VIEW autocheck.operation_events
AS
SELECT
    event_id,
    operation_id,
    event_type,
    payload_hash,
    occurred_at
FROM payment.operation_events;



GRANT SELECT
    ON autocheck.operations
    TO course_runtime;


GRANT SELECT
    ON autocheck.operation_events
    TO course_runtime;