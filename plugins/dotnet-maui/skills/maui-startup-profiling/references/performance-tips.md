# MAUI Startup Performance Tips

Curated tips from [MS Learn: Improve app performance](https://learn.microsoft.com/dotnet/maui/deployment/performance) relevant to startup time. Read this file when the trace analysis shows bottlenecks in a specific category.

## Decision Matrix

| Bottleneck category | Top suspects | Remediation |
|---|---|---|
| **MAUI Framework** | DI registration, XAML parsing, handler mapping | See §DI, §XAML, §Shell below |
| **App Code** | Constructor work, sync I/O, heavy first page | See §Lazy Init, §Async, §Page Complexity |
| **.NET Bindings** | Excessive managed↔native transitions | Batch platform calls, cache results |
| **.NET Runtime** | JIT compilation, assembly loading, reflection | See §R2R/MIBC (in mibc-r2r-guide.md), §Trimming |
| **Platform / OS** | OS-level rendering, native library loading | Usually not actionable from app code |

## DI Container Overhead

DI containers use reflection to construct types. Deep dependency graphs at startup are expensive.

**Symptoms in trace:** `Microsoft.Extensions.DependencyInjection` frames consuming >5% of startup CPU.

**Fixes:**
- Register services as `AddSingleton` or `AddTransient` instead of `AddScoped` when appropriate — scoped services create per-scope overhead.
- Use factory registrations (`AddSingleton<IService>(sp => new Service())`) instead of `AddSingleton<IService, Service>()` to avoid reflection-based construction.
- Defer construction with `Lazy<T>` wrappers for services not needed until after first page render.
- Avoid deep dependency chains — flatten constructor parameters or use initialization methods.

**Reference:** https://learn.microsoft.com/dotnet/maui/deployment/performance#choose-a-dependency-injection-container-carefully

## Shell vs TabbedPage

Shell creates pages on demand during navigation. TabbedPage creates all tab pages at startup.

**Symptoms in trace:** Multiple page constructors and `OnAppearing` calls visible during startup.

**Fixes:**
- Use `Shell` instead of `TabbedPage` — pages load lazily on navigation.
- Never set `ShellContent.Content` to a `ContentPage` instance directly (this forces eager creation).
- Use `DataTemplate` for shell content: `<ShellContent ContentTemplate="{DataTemplate local:MyPage}" />`.

**Reference:** https://learn.microsoft.com/dotnet/maui/deployment/performance#create-shell-apps

## XAML Compilation

Uncompiled XAML is parsed and inflated at runtime, which is significantly slower.

**Symptoms in trace:** `Microsoft.Maui.Controls.Xaml` or XAML-related parsing frames.

**Fixes:**
- Ensure `XamlCompilationAttribute` is set to `XamlCompilationOptions.Compile` (this is the default in .NET MAUI but can be disabled per-page).
- Use compiled bindings (`x:DataType`) instead of reflection-based bindings.

**Reference:** https://learn.microsoft.com/dotnet/maui/deployment/performance#use-compiled-bindings

## Compiled Bindings

Reflection bindings resolve property paths at runtime. Compiled bindings resolve at compile time.

**Symptoms in trace:** `BindingExpression` or `System.Reflection` frames during page inflation.

**Fixes:**
- Add `x:DataType="vm:MyViewModel"` to XAML pages and use compiled bindings.
- Avoid `string`-based `SetBinding()` calls in code-behind — use the lambda overload or XAML.

**Reference:** https://learn.microsoft.com/dotnet/maui/deployment/performance#use-compiled-bindings

## Lazy Initialization

Expensive objects created eagerly at startup delay first-frame rendering.

**Symptoms in trace:** App namespace constructors or `OnAppearing` doing heavy work before first visual frame.

**Fixes:**
- Use `Lazy<T>` for expensive-to-construct services.
- Defer non-critical initialization to `Dispatcher.DispatchAsync()` after the first frame.
- Move network calls and database initialization out of `MauiProgram.CreateMauiApp()`.

**Reference:** https://learn.microsoft.com/dotnet/framework/performance/lazy-initialization

## Async Patterns

Blocking synchronous calls on the UI thread during startup freeze the first render.

**Symptoms in trace:** Large self time in app code methods on the main thread.

**Fixes:**
- Never call `.Result` or `.Wait()` on startup tasks — use `await`.
- Use lifecycle events (`OnAppearing`) with `async` rather than synchronous constructors.
- Use `Task.Run` for CPU-bound initialization that doesn't need UI thread access.

**Reference:** https://learn.microsoft.com/dotnet/maui/deployment/performance#use-asynchronous-programming

## First Page Complexity

Complex first pages with many elements take longer to measure, layout, and render.

**Symptoms in trace:** MAUI handler creation and layout frames dominating startup.

**Fixes:**
- Minimize elements on the first visible page.
- Use `CollectionView` with virtualization instead of `StackLayout` with many children.
- Defer non-visible content (below the fold) to load after first render.
- Avoid complex nested layouts — prefer `Grid` over deeply nested `StackLayout`.

**Reference:** https://learn.microsoft.com/dotnet/maui/deployment/performance#reduce-the-number-of-elements-on-a-page

## Trimming and Linking

Untrimmed apps load more assemblies and types than necessary.

**Symptoms in trace:** Assembly loading and type resolution frames in .NET Runtime category.

**Fixes:**
- Use `PublishTrimmed=true` for Release builds.
- Set `TrimMode=full` for aggressive trimming (test thoroughly).
- The MAUI SDK enables trimming by default for Release Android/iOS builds.

**Reference:** https://learn.microsoft.com/dotnet/maui/deployment/performance#reduce-the-size-of-the-app

## Image and Resource Loading

Loading large images or many resources at startup blocks rendering.

**Symptoms in trace:** Image decoding or resource resolution frames.

**Fixes:**
- Use appropriately sized images (don't load 4K images for thumbnails).
- Use `CacheMode` and lazy image loading.
- Avoid loading all app resources in `App.xaml` — split into page-level resources.

**Reference:** https://learn.microsoft.com/dotnet/maui/deployment/performance#optimize-image-resources
