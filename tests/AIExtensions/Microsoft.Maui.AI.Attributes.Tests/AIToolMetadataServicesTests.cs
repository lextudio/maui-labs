using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Attributes;

namespace Microsoft.Maui.AI.Attributes.Tests;

/// <summary>
/// Tests the runtime helper methods in <see cref="AIToolMetadataServices"/>:
/// <c>GetRequiredArg</c>, <c>GetOptionalArg</c>, and the internal <c>ConvertArg</c> paths.
/// </summary>
public class AIToolMetadataServicesTests
{
    // ── GetRequiredArg ──────────────────────────────────────────────────

    [Fact]
    public void GetRequiredArg_DirectCast_ReturnsValue()
    {
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["x"] = "hello" });
        var result = AIToolMetadataServices.GetRequiredArg<string>(args, "x");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void GetRequiredArg_MissingKey_Throws()
    {
        var args = new AIFunctionArguments(new Dictionary<string, object?>());
        var ex = Assert.Throws<ArgumentException>(
            () => AIToolMetadataServices.GetRequiredArg<string>(args, "missing"));
        Assert.Contains("missing", ex.Message);
        Assert.Contains("Missing required argument", ex.Message);
    }

    [Fact]
    public void GetRequiredArg_NullValueForNullableType_ReturnsNull()
    {
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["x"] = null });
        var result = AIToolMetadataServices.GetRequiredArg<string?>(args, "x");
        Assert.Null(result);
    }

    [Fact]
    public void GetRequiredArg_NullValueForNonNullableType_Throws()
    {
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["x"] = null });
        var ex = Assert.Throws<ArgumentException>(
            () => AIToolMetadataServices.GetRequiredArg<int>(args, "x"));
        Assert.Contains("null", ex.Message);
        Assert.Contains("non-nullable", ex.Message);
    }

    // ── GetOptionalArg ──────────────────────────────────────────────────

    [Fact]
    public void GetOptionalArg_MissingKey_ReturnsDefault()
    {
        var args = new AIFunctionArguments(new Dictionary<string, object?>());
        var result = AIToolMetadataServices.GetOptionalArg<string>(args, "missing", "fallback");
        Assert.Equal("fallback", result);
    }

    [Fact]
    public void GetOptionalArg_NullValue_ReturnsDefault()
    {
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["x"] = null });
        var result = AIToolMetadataServices.GetOptionalArg<int>(args, "x", 42);
        Assert.Equal(42, result);
    }

    [Fact]
    public void GetOptionalArg_PresentValue_ReturnsConverted()
    {
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["x"] = "hello" });
        var result = AIToolMetadataServices.GetOptionalArg<string>(args, "x", "fallback");
        Assert.Equal("hello", result);
    }

    // ── ConvertArg paths (via GetRequiredArg) ───────────────────────────

    [Fact]
    public void ConvertArg_JsonElement_Deserializes()
    {
        var je = JsonDocument.Parse("42").RootElement;
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["x"] = je });
        var result = AIToolMetadataServices.GetRequiredArg<int>(args, "x");
        Assert.Equal(42, result);
    }

    [Fact]
    public void ConvertArg_JsonNode_Deserializes()
    {
        var jn = JsonNode.Parse("\"world\"")!;
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["x"] = jn });
        var result = AIToolMetadataServices.GetRequiredArg<string>(args, "x");
        Assert.Equal("world", result);
    }

    [Fact]
    public void ConvertArg_RawJsonString_DeserializesToNonStringType()
    {
        // LLM sometimes sends a JSON string representation for non-string targets
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["x"] = "42" });
        var result = AIToolMetadataServices.GetRequiredArg<int>(args, "x");
        Assert.Equal(42, result);
    }

    [Fact]
    public void ConvertArg_ObjectValue_FallsBackToJsonRoundTrip()
    {
        // A boxed int passed where a long is expected — not a direct cast match
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["x"] = 42 });
        var result = AIToolMetadataServices.GetRequiredArg<long>(args, "x");
        Assert.Equal(42L, result);
    }

    [Fact]
    public void ConvertArg_ComplexObject_DeserializesViaRoundTrip()
    {
        var obj = new Dictionary<string, string> { ["key"] = "val" };
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["x"] = obj });
        var result = AIToolMetadataServices.GetRequiredArg<Dictionary<string, string>>(args, "x");
        Assert.Equal("val", result["key"]);
    }

    [Fact]
    public void ConvertArg_JsonElement_StringValue_DeserializesCorrectly()
    {
        var je = JsonDocument.Parse("\"hello\"").RootElement;
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["x"] = je });
        var result = AIToolMetadataServices.GetRequiredArg<string>(args, "x");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void ConvertArg_JsonElement_ComplexObject_Deserializes()
    {
        var je = JsonDocument.Parse("{\"Name\":\"test\"}").RootElement;
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["x"] = je });
        var result = AIToolMetadataServices.GetRequiredArg<SimpleDto>(args, "x");
        Assert.Equal("test", result.Name);
    }

    [Fact]
    public void ConvertArg_JsonNode_NumberValue_Deserializes()
    {
        var jn = JsonNode.Parse("3.14")!;
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["x"] = jn });
        var result = AIToolMetadataServices.GetRequiredArg<double>(args, "x");
        Assert.Equal(3.14, result);
    }

    [Fact]
    public void ConvertArg_InvalidJsonString_FallsBackToRoundTrip()
    {
        // A string value that is NOT valid JSON for int — falls through to round-trip
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["x"] = "not-a-number" });
        // Round-trip: JsonSerializer.Serialize("not-a-number") → "\"not-a-number\"" → Deserialize<int> → fail
        Assert.ThrowsAny<Exception>(
            () => AIToolMetadataServices.GetRequiredArg<int>(args, "x"));
    }

    // ── Test DTO ────────────────────────────────────────────────────────

    public class SimpleDto
    {
        public string Name { get; set; } = "";
    }
}
