-- ============================================================
-- 017_workflow_definition_permissions.sql
--
-- Workflow definitions are immutable after publication.
--
-- Publication is performed by the trusted CLI through INSERT.
-- Activation changes only operational state (is_active) through
-- a controlled CLI operation.
--
-- Published definition data is never updated or deleted by
-- course_publisher.
-- ============================================================

REVOKE ALL
    ON workflow.flow_definition
    FROM PUBLIC;

REVOKE ALL
    ON workflow.flow_version
    FROM PUBLIC;

REVOKE ALL
    ON workflow.step_definition
    FROM PUBLIC;

REVOKE ALL
    ON workflow.task_definition
    FROM PUBLIC;

REVOKE ALL
    ON workflow.transition_definition
    FROM PUBLIC;


GRANT USAGE
    ON SCHEMA workflow
    TO course_publisher;

GRANT SELECT, INSERT
    ON workflow.flow_definition,
       workflow.flow_version,
       workflow.step_definition,
       workflow.task_definition,
       workflow.transition_definition
    TO course_publisher;

GRANT UPDATE (is_active)
    ON workflow.flow_version
    TO course_publisher;
