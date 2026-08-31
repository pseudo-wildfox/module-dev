namespace Workflow.Tests.Fakes;

public static class FakeSchema
{
    public static string WorkflowMap()
    {
        return """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "required": [
            "contract_version",
            "flow_name",
            "version",
            "start_step",
            "steps",
            "transitions"
          ],
          "properties": {
            "contract_version": {
              "const": "course-1"
            },
            "flow_name": {
              "type": "string"
            },
            "version": {
              "type": "integer"
            },
            "start_step": {
              "type": "string"
            },
            "steps": {
              "type": "array"
            },
            "transitions": {
              "type": "array"
            }
          }
        }
        """;
    }
}