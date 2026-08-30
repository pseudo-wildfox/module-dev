-- ============================================================
-- 014_workflow_schema.sql
--
-- Workflow engine schema.
--
-- Contains only workflow-specific persistent state and
-- workflow runtime functions.
-- ============================================================

CREATE SCHEMA IF NOT EXISTS workflow
    AUTHORIZATION course_owner;