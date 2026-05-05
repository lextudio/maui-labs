using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Attributes;

/// <summary>
/// Provides helpers to create and initialize metadata for AI tool contexts.
/// </summary>
/// <remarks>
/// This API is for use by the output of the AI.Attributes source generator
/// and should not be called directly.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class AIToolMetadataServices
{

    /// <summary>
    /// Reads a required argument from <see cref="AIFunctionArguments"/>, converting it to
    /// <typeparamref name="T"/> via a direct cast, JSON element conversion, or JSON round-trip.
    /// </summary>
    public static T GetRequiredArg<T>(AIFunctionArguments args, string name, JsonSerializerOptions? options = null)
    {
        if (!args.TryGetValue(name, out var value))
        {
            throw new ArgumentException($"Missing required argument '{name}'.", nameof(args));
        }
        return ConvertArg<T>(value, name, options);
    }

    /// <summary>
    /// Reads an optional argument. If the value is missing or <see langword="null"/>, returns
    /// <paramref name="defaultValue"/>.
    /// </summary>
    public static T? GetOptionalArg<T>(AIFunctionArguments args, string name, T? defaultValue, JsonSerializerOptions? options = null)
    {
        if (!args.TryGetValue(name, out var value) || value is null)
        {
            return defaultValue;
        }
        return ConvertArg<T>(value, name, options);
    }

    private static T ConvertArg<T>(object? value, string name, JsonSerializerOptions? options)
    {
        if (value is null)
        {
            if (default(T) is null)
            {
                return default!;
            }
            throw new ArgumentException($"Argument '{name}' is null but target type '{typeof(T)}' is non-nullable.", nameof(name));
        }

        if (value is T typed)
        {
            return typed;
        }

        var opts = options ?? AIJsonUtilities.DefaultOptions;

        if (value is JsonElement je)
        {
            return je.Deserialize<T>(opts)!;
        }

        if (value is JsonNode jn)
        {
            return jn.Deserialize<T>(opts)!;
        }

        // If the LLM supplied a raw JSON string for a non-string target, try to parse it.
        if (value is string s && typeof(T) != typeof(string))
        {
            try
            {
                return JsonSerializer.Deserialize<T>(s, opts)!;
            }
            catch
            {
                // Fall through to round-trip.
            }
        }

        // Fallback: JSON round-trip. This matches ReflectionAIFunction's behavior for
        // general object-to-T coercion.
        var json = JsonSerializer.Serialize(value, value.GetType(), opts);
        return JsonSerializer.Deserialize<T>(json, opts)!;
    }
}
