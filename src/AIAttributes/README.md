# AI Attributes

Source-generated AI tool discovery for .NET, built on [`Microsoft.Extensions.AI`](https://learn.microsoft.com/dotnet/ai/ai-extensions) abstractions. Decorate methods or property accessors with `[ExportAIFunction]` to create AI-callable tools — no runtime reflection, AOT-friendly.

```csharp
[ExportAIFunction("search_plants")]
public List<PlantInfo> SearchPlants([Description("Filter text")] string? query = null)
{
    // ...
}
```

| Package | Description |
|---------|-------------|
| [`Microsoft.Maui.AI.Attributes`](Microsoft.Maui.AI.Attributes/) | Source-generated AI tool contexts for `Microsoft.Extensions.AI` |

- [NuGet README](Microsoft.Maui.AI.Attributes/README.md) — full API documentation, samples, and equivalence rules
- CI: `ci-ai.yml` / Solution filter: `AIAttributes.slnf`

## Samples

| Sample | Demonstrates |
|--------|-------------|
| [`AIAttributes.Sample.Hello`](../../samples/AIAttributes.Sample.Hello/) | Minimal end-to-end AI Attributes usage |
| [`AIAttributes.Sample.DIParameters`](../../samples/AIAttributes.Sample.DIParameters/) | DI parameter binding with `[FromServices]` |
| [`AIAttributes.Sample.Garden`](../../samples/AIAttributes.Sample.Garden/) | Full MAUI chat app with navigation, cart, approval flow |

## Requirements

- .NET 10

> ⚠️ **This package is experimental.** APIs may change between releases.
