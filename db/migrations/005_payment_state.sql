CREATE TABLE IF NOT EXISTS payment.operations
(
    operation_id     uuid PRIMARY KEY,

    request_id       text NOT NULL,

    operation_kind   text NOT NULL,

    amount           numeric(18,2) NOT NULL,

    currency         text NOT NULL,

    status           text NOT NULL,

    process_id       uuid NULL,

    created_at       timestamptz NOT NULL DEFAULT now(),
    updated_at       timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT ck_operation_kind
        CHECK (
            operation_kind IN (
                'PAYMENT_EXECUTION',
                'PAYMENT_APPROVAL'
            )
        ),

    CONSTRAINT ck_operation_currency
        CHECK (currency = 'RUB'),

    CONSTRAINT ck_operation_status
        CHECK (
            status IN (
                'CREATED',
                'PROCESSING',
                'COMPLETED',
                'REJECTED'
            )
        ),

    CONSTRAINT ck_operation_amount
        CHECK (amount > 0),

    CONSTRAINT ck_operation_request_id
        CHECK (length(request_id) BETWEEN 1 AND 128)

);

CREATE TABLE IF NOT EXISTS payment.operation_events
(
    event_id        uuid PRIMARY KEY,

    operation_id    uuid NOT NULL,

    event_type      text NOT NULL,

    payload_hash    text NOT NULL,

    occurred_at     timestamptz NOT NULL DEFAULT now(),


    CONSTRAINT ck_event_payload_hash
        CHECK (payload_hash ~ '^[0-9a-f]{64}$'),


    CONSTRAINT fk_operation_event_operation
        FOREIGN KEY (operation_id)
        REFERENCES payment.operations(operation_id)
);