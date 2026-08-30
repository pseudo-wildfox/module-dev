-- ============================================================
-- 017_workflow_definition_permissions.sql
--
-- Workflow definitions are immutable after publication.
-- Publication/activation is performed through controlled
-- workflow functions/CLI operations, not direct UPDATE of
-- definition data.
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