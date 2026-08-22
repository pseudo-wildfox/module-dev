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
        )
);

CREATE TABLE IF NOT EXISTS payment.operation_events
(
    event_id        uuid PRIMARY KEY,

    operation_id    uuid NOT NULL,

    event_type      text NOT NULL,

    payload_hash    text NOT NULL,

    occurred_at     timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT fk_operation_event_operation
        FOREIGN KEY (operation_id)
        REFERENCES payment.operations(operation_id)
);