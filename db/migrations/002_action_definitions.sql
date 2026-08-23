CREATE TABLE IF NOT EXISTS runtime.action_definitions
(
    id                  bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,

    contract_version    text NOT NULL,

    module              text NOT NULL,
    action              text NOT NULL,
    version             integer NOT NULL,

    http_method         text NOT NULL,

    target_schema       text NOT NULL,
    target_function     text NOT NULL,

    request_schema      jsonb NOT NULL,
    response_schema     jsonb NOT NULL,

    outcomes            jsonb NOT NULL,
    required_policy     jsonb NOT NULL,

    idempotency_mode    text NOT NULL,
    idempotency_scope   text NOT NULL,

    timeout_ms          integer NOT NULL,

    enabled             boolean NOT NULL DEFAULT false,
    is_default          boolean NOT NULL DEFAULT false,

    manifest_hash       text NOT NULL,

    created_at          timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT uq_action_definition_version
        UNIQUE (module, action, version),

    CONSTRAINT ck_action_http_method
        CHECK (http_method = 'POST'),

    CONSTRAINT ck_action_version
        CHECK (version >= 1),

    CONSTRAINT ck_action_timeout
        CHECK (timeout_ms BETWEEN 1 AND 30000),

    CONSTRAINT ck_action_idempotency_mode
        CHECK (
            idempotency_mode IN (
                'none',
                'optional',
                'required'
            )
        ),

    CONSTRAINT ck_action_module_identifier
        CHECK (module ~ '^[a-z][a-z0-9_]{0,62}$'),

    CONSTRAINT ck_action_name_identifier
        CHECK (action ~ '^[a-z][a-z0-9_]{0,62}$'),

    CONSTRAINT ck_target_schema_identifier
        CHECK (target_schema ~ '^[a-z][a-z0-9_]{0,62}$'),

    CONSTRAINT ck_target_function_identifier
        CHECK (target_function ~ '^[a-z][a-z0-9_]{0,62}$'),

    CONSTRAINT ck_action_idempotency_scope
        CHECK (
            idempotency_scope IN (
                'none',
                'principal_action',
                'consumer_action',
                'global_action'
            )
        ),

    CONSTRAINT ck_action_idempotency_pair
        CHECK (
            (idempotency_mode = 'none'
                AND idempotency_scope = 'none')
            OR
            (idempotency_mode IN ('optional', 'required')
                AND idempotency_scope IN (
                    'principal_action',
                    'consumer_action',
                    'global_action'
                ))
        ),

    CONSTRAINT ck_default_requires_enabled
        CHECK (NOT is_default OR enabled),

    CONSTRAINT ck_manifest_hash_sha256
        CHECK (
            manifest_hash ~ '^[0-9a-f]{64}$'
        )

);


CREATE UNIQUE INDEX IF NOT EXISTS ux_action_one_default
    ON runtime.action_definitions (module, action)
    WHERE enabled = true
      AND is_default = true;