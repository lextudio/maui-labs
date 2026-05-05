# .NET MAUI AI

AI capabilities for .NET MAUI, built on [`Microsoft.Extensions.AI`](https://learn.microsoft.com/dotnet/ai/ai-extensions) abstractions.

This directory contains two packages:

## AI Attributes

Source-generated AI tool discovery for .NET. Decorate methods or property accessors with `[ExportAIFunction]` to create AI-callable tools — no runtime reflection, AOT-friendly.

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
- CI: `ci-ai.yml` / Solution filter: `AI.slnf`

## Essentials.AI

On-device AI for .NET MAUI apps using platform-native models — no cloud required. On Apple platforms, wraps Apple Intelligence (Foundation Models) for chat completion and NaturalLanguage APIs for embeddings.

```csharp
builder.Services.AddSingleton<IChatClient>(new AppleIntelligenceChatClient());
```

### Platform Support

| Platform | Chat (IChatClient) | Embeddings (IEmbeddingGenerator) |
|----------|-------------------|----------------------------------|
| iOS 26+ | ✅ Apple Intelligence (Foundation Models) | ✅ NL Embeddings |
| Mac Catalyst 26+ | ✅ Apple Intelligence | ✅ NL Embeddings |
| macOS 26+ | ✅ Apple Intelligence | ✅ NL Embeddings |
| Android | 🔜 Coming soon | 🔜 Coming soon |
| Windows | 🔜 Coming soon | 🔜 Coming soon |

### Features

- **`IChatClient`** — backed by Apple Intelligence (Foundation Models) on iOS, macOS, and Mac Catalyst
- **Streaming** — progressive JSON deserialization of LLM responses via `JsonStreamChunker` and `PlainTextStreamChunker`
- **Tool calling** — function-calling support for on-device models
- **NL embeddings** — on-device semantic search via Apple's NaturalLanguage framework (`NLEmbeddingGenerator`)

### Architecture

- **Native Swift bindings** (`AppleNative/EssentialsAI/`) — compiled via Xcode, producing `.xcframework` bundles
- **`AppleBindings.targets`** — MSBuild targets for cross-platform native artifact flow (macOS builds Swift, Windows downloads pre-built artifacts)
- **Streaming infrastructure** — `JsonStreamChunker`, `PlainTextStreamChunker`, `StreamingResponseHandler` for progressive deserialization
- **Android native** (`AndroidNative/`) — placeholder for future Android on-device AI

### Building

```bash
# macOS (builds Swift bindings + .NET library)
dotnet build src/AI/EssentialsAI.slnf

# Windows (CI only — the Azure DevOps pipeline downloads macOS-built
# native artifacts automatically. Local Windows builds require CI=true
# or TF_BUILD=true for the pre-built artifact path to activate.)
```

| Package | Description |
|---------|-------------|
| [`Microsoft.Maui.Essentials.AI`](Microsoft.Maui.Essentials.AI/) | On-device AI APIs for MAUI |

- [NuGet README](Microsoft.Maui.Essentials.AI/README.md) — install instructions, streaming, embeddings
- [Design doc](../../docs/ai/json-stream-chunker-design.md) — JSON stream chunker architecture
- CI: `ci-essentialsai.yml` / Solution filter: `EssentialsAI.slnf`

## Samples

| Sample | Demonstrates |
|--------|-------------|
| [`AIAttributes.Sample.Hello`](../../samples/AIAttributes.Sample.Hello/) | Minimal end-to-end AI Attributes usage |
| [`AIAttributes.Sample.DIParameters`](../../samples/AIAttributes.Sample.DIParameters/) | DI parameter binding with `[FromServices]` |
| [`AIAttributes.Sample.Garden`](../../samples/AIAttributes.Sample.Garden/) | Full MAUI chat app with navigation, cart, approval flow |
| [`EssentialsAISample`](../../samples/EssentialsAISample/) | On-device trip planner with streaming and embeddings |

## Requirements

- .NET 10
- MAUI workload (`dotnet workload install maui`)
- Apple Intelligence features require iOS 26+, Mac Catalyst 26+, or macOS 26+

> ⚠️ **These packages are experimental.** APIs may change between releases.
