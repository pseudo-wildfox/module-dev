CREATE TABLE IF NOT EXISTS runtime.idempotency
(
    scope_key         text NOT NULL,
    idempotency_key   text NOT NULL,
    payload_hash      text NOT NULL,
    status            text NOT NULL,
    response_json     jsonb NOT NULL,
    created_at        timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_idempotency
        PRIMARY KEY (scope_key, idempotency_key)
);

CREATE TABLE IF NOT EXISTS runtime.action_dispatches
(
    id               bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,

    correlation_id   uuid NOT NULL,
    request_id       text,

    module           text NOT NULL,
    action           text NOT NULL,
    version          integer NOT NULL,

    principal        text NOT NULL,

    payload_hash     text NOT NULL,

    status           text NOT NULL,
    outcome          text,

    occurred_at      timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT ck_dispatch_status
        CHECK (status IN ('OK', 'ERROR')),

    CONSTRAINT ck_dispatch_version
        CHECK (version >= 1)
);