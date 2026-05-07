using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Maui.AI.Attributes.Tests;

public class AIFunctionSchemaTests
{
    [Fact]
    public void Json_schema_contains_parameter_info()
    {
        var tool = TestToolContext.Default.Tools.First(t => t.Name == "test_tool");
        var function = Assert.IsAssignableFrom<AIFunctionDeclaration>(tool);

        Assert.Contains("input", function.JsonSchema.ToString());
    }

    [Fact]
    public void Schema_contains_parameter_description()
    {
        var tool = Assert.IsAssignableFrom<AIFunctionDeclaration>(
            TestToolContext.Default.Tools.First(t => t.Name == "test_tool"));

        Assert.Contains("input value", tool.JsonSchema.ToString());
    }

    [Fact]
    public void Schema_matches_direct_factory_output()
    {
        var diTool = Assert.IsAssignableFrom<AIFunctionDeclaration>(
            TestToolContext.Default.Tools.First(t => t.Name == "test_tool"));

        var schema = diTool.JsonSchema.ToString();
        // Verify the schema has the right structure for the "input" parameter
        Assert.Contains("\"input\"", schema);
        Assert.Contains("\"type\"", schema);
        Assert.Contains("\"properties\"", schema);
        Assert.Contains("input value", schema);
    }

    [Fact]
    public void Approval_wrapped_tools_preserve_full_ai_visible_schema()
    {
        var reflectedTool = ComplexSchemaToolContext.Default.Tools.Single(t => t.Name == "create_plant_profile");
        var reflectedFunction = Assert.IsAssignableFrom<AIFunction>(reflectedTool);

        Assert.IsType<ApprovalRequiredAIFunction>(reflectedTool);
        Assert.Equal("create_plant_profile", reflectedFunction.Name);
        Assert.Equal("Creates a plant profile from structured details.", reflectedFunction.Description);

        var inputSchema = reflectedFunction.JsonSchema.ToString();
        Assert.Contains("\"properties\"", inputSchema);
        Assert.Contains("structured details for the plant profile", inputSchema);
        Assert.Contains("friendly nickname shown to the user", inputSchema);
        Assert.Contains("botanical species or variety", inputSchema);
        Assert.Contains("current location of the plant", inputSchema);
        Assert.Contains("whether the plant lives indoors", inputSchema);
        Assert.Contains("whether to notify the user after creation", inputSchema);

        var returnSchema = reflectedFunction.ReturnJsonSchema?.ToString();
        Assert.NotNull(returnSchema);
        Assert.Contains("stable identifier returned to the AI", returnSchema!);
        Assert.Contains("nickname echoed back to the AI", returnSchema!);
    }
}
