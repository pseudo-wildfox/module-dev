-- ============================================================
-- Week 1 required course actions seed
-- ============================================================

INSERT INTO runtime.action_definitions
(
    contract_version,

    module,
    action,
    version,

    http_method,

    target_schema,
    target_function,

    request_schema,
    response_schema,

    outcomes,
    required_policy,

    idempotency_mode,
    idempotency_scope,

    timeout_ms,

    enabled,
    is_default,

    manifest_hash
)
VALUES
(
    'course-1',

    'payment',
    'request',
    1,

    'POST',

    'payment',
    'request_v1',

    '{
        "$schema": "https://json-schema.org/draft/2020-12/schema",
        "type": "object",
        "required": [
            "operationKind",
            "amount",
            "currency"
        ],
        "properties": {
            "operationKind": {
                "type": "string",
                "minLength": 1
            },
            "amount": {
                "type": "number"
            },
            "currency": {
                "type": "string",
                "minLength": 3,
                "maxLength": 3
            }
        },
        "additionalProperties": false
    }'::jsonb,


    '{
        "$schema": "https://json-schema.org/draft/2020-12/schema",
        "type": "object",
        "required": [
            "status",
            "outcome",
            "result"
        ],
        "properties": {
            "status": {
                "const": "ok"
            },
            "outcome": {
                "const": "CREATED"
            },
            "result": {
                "type": "object",
                "required": [
                    "operationId",
                    "requestId",
                    "operationKind",
                    "amount",
                    "currency",
                    "status"
                ]
            }
        }
    }'::jsonb,


    jsonb_build_array(
        'CREATED'
    ),

    jsonb_build_array(
        'payment:create'
    ),

    'required',
    'principal_action',

    5000,

    true,
    true,

    repeat('0', 64)
)

ON CONFLICT (module, action, version)
DO UPDATE SET

    enabled = EXCLUDED.enabled,
    is_default = EXCLUDED.is_default,

    target_schema = EXCLUDED.target_schema,
    target_function = EXCLUDED.target_function,

    request_schema = EXCLUDED.request_schema,
    response_schema = EXCLUDED.response_schema,

    outcomes = EXCLUDED.outcomes,
    required_policy = EXCLUDED.required_policy,

    idempotency_mode = EXCLUDED.idempotency_mode,
    idempotency_scope = EXCLUDED.idempotency_scope,

    timeout_ms = EXCLUDED.timeout_ms,
    contract_version = EXCLUDED.contract_version;



INSERT INTO runtime.action_definitions
(
    contract_version,

    module,
    action,
    version,

    http_method,

    target_schema,
    target_function,

    request_schema,
    response_schema,

    outcomes,
    required_policy,

    idempotency_mode,
    idempotency_scope,

    timeout_ms,

    enabled,
    is_default,

    manifest_hash
)
VALUES
(
    'course-1',

    'operation',
    'get',
    1,

    'POST',

    'payment',
    'operation_get_v1',


    '{
        "$schema": "https://json-schema.org/draft/2020-12/schema",
        "type": "object",
        "required": [
            "operationId"
        ],
        "properties": {
            "operationId": {
                "type": "string",
                "format": "uuid"
            }
        },
        "additionalProperties": false
    }'::jsonb,


    '{
        "$schema": "https://json-schema.org/draft/2020-12/schema",
        "type": "object",
        "required": [
            "status",
            "outcome",
            "result"
        ]
    }'::jsonb,


    jsonb_build_array(
        'FOUND'
    ),

    jsonb_build_array(
        'payment:read'
    ),


    'none',
    'none',

    5000,

    true,
    true,

    repeat('0', 64)
)

ON CONFLICT (module, action, version)
DO UPDATE SET

    enabled = EXCLUDED.enabled,
    is_default = EXCLUDED.is_default,

    target_schema = EXCLUDED.target_schema,
    target_function = EXCLUDED.target_function,

    request_schema = EXCLUDED.request_schema,
    response_schema = EXCLUDED.response_schema,

    outcomes = EXCLUDED.outcomes,
    required_policy = EXCLUDED.required_policy,

    idempotency_mode = EXCLUDED.idempotency_mode,
    idempotency_scope = EXCLUDED.idempotency_scope,

    timeout_ms = EXCLUDED.timeout_ms,
    contract_version = EXCLUDED.contract_version;