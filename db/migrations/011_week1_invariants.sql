-- ============================================================
-- 011_week1_invariants.sql
-- ============================================================

ALTER TABLE payment.operation_events
    ADD CONSTRAINT ck_event_type_uppercase
    CHECK (event_type ~ '^[A-Z][A-Z0-9_]*$');


ALTER TABLE payment.operations
    ADD CONSTRAINT ck_week1_process_id_null
    CHECK (process_id IS NULL);