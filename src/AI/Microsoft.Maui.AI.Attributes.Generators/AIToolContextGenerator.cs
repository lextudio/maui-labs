using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.Maui.AI.Attributes.Generators;

/// <summary>
/// Incremental source generator that emits a fully-typed <c>AIFunction</c> subclass per
/// <c>[ExportAIFunction]</c> method on each type referenced by an
/// <c>[AIToolSource(typeof(...))]</c> attribute on an <c>AIToolContext</c> partial class.
/// </summary>
/// <remarks>
/// The emitted class:
/// <list type="bullet">
/// <item>Overrides <c>Name</c>, <c>Description</c>, <c>JsonSchema</c>, <c>ReturnJsonSchema</c>.</item>
/// <item>Resolves its backing service from <c>AIFunctionArguments.Services</c> per invocation — no
/// <c>AIFunctionFactory.Create</c>, no <c>MethodInfo.Invoke</c>.</item>
/// <item>Binds each parameter at compile time:
/// <c>CancellationToken</c>/<c>IServiceProvider</c>/<c>AIFunctionArguments</c> get special cases;
/// <c>[FromServices]</c>/<c>[FromKeyedServices]</c> parameters resolve from DI; everything else
/// binds from the argument dictionary.</item>
/// </list>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class AIToolContextGenerator : IIncrementalGenerator
{
    private const string AIToolContextFullName = "Microsoft.Maui.AI.Attributes.AIToolContext";
    private const string AIToolSourceAttributeFullName = "Microsoft.Maui.AI.Attributes.AIToolSourceAttribute";
    private const string ExportAIFunctionAttributeFullName = "Microsoft.Maui.AI.Attributes.ExportAIFunctionAttribute";
    private const string DescriptionAttributeFullName = "System.ComponentModel.DescriptionAttribute";
    private const string FromServicesAttributeFullName = "Microsoft.Extensions.DependencyInjection.FromServicesAttribute";
    private const string FromKeyedServicesAttributeFullName = "Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute";
    private const string CancellationTokenFullName = "System.Threading.CancellationToken";
    private const string IServiceProviderFullName = "System.IServiceProvider";
    private const string AIFunctionArgumentsFullName = "Microsoft.Extensions.AI.AIFunctionArguments";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var contextDeclarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AIToolSourceAttributeFullName,
                predicate: static (node, _) => node is ClassDeclarationSyntax cds &&
                    cds.Modifiers.Any(SyntaxKind.PartialKeyword),
                transform: static (ctx, ct) => GetContextModel(ctx, ct))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        // Merge duplicate context entries (multiple [AIToolSource] attributes → multiple invocations).
        var grouped = contextDeclarations
            .Collect()
            .SelectMany(static (items, _) =>
            {
                var dict = new Dictionary<string, ContextModel>();
                foreach (var item in items)
                {
                    var key = item.FullyQualifiedName;
                    if (!dict.TryGetValue(key, out var existing))
                    {
                        dict[key] = item;
                    }
                    else
                    {
                        var merged = existing.WithAdditionalSourceTypes(item.SourceTypes);
                        dict[key] = merged;
                    }
                }
                return dict.Values.ToImmutableArray();
            });

        context.RegisterSourceOutput(grouped, static (spc, model) =>
        {
            var source = GenerateContextSource(model);
            var hintName = model.ContainingTypes.Length > 0
                ? $"{string.Join("_", model.ContainingTypes.Select(c => c.Name))}_{model.ClassName}.g.cs"
                : $"{model.ClassName}.g.cs";
            spc.AddSource(hintName, SourceText.From(source, Encoding.UTF8));

            foreach (var diag in model.Diagnostics)
            {
                spc.ReportDiagnostic(diag.ToDiagnostic());
            }
        });

        // ─── Assembly-wide ToolContext ───────────────────────────────
        // Discovers ALL [ExportAIFunction] methods/properties in the assembly
        // and emits a single <RootNamespace>.<AssemblyName>ToolContext class.
        var allExportedMethods = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ExportAIFunctionAttributeFullName,
                predicate: static (node, _) => node is MethodDeclarationSyntax or PropertyDeclarationSyntax,
                transform: static (ctx, ct) => GetExportedMemberInfo(ctx, ct))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        var assemblyWide = allExportedMethods
            .Collect()
            .Combine(context.CompilationProvider);

        context.RegisterSourceOutput(assemblyWide, static (spc, pair) =>
        {
            var (members, compilation) = pair;
            if (members.IsEmpty)
                return;

            var assemblyName = compilation.AssemblyName ?? "Assembly";
            var rootNamespace = compilation.Options is CSharpCompilationOptions opts
                ? GetRootNamespace(compilation)
                : assemblyName;

            // Sanitize assembly name for use as an identifier.
            var safeAssemblyName = SanitizeIdentifier(assemblyName.Replace(".", ""));
            var className = $"{safeAssemblyName}ToolContext";

            // Group by containing type.
            var sourceTypes = new Dictionary<string, (string fqn, string simpleName, List<MethodModel> methods)>();
            foreach (var m in members)
            {
                if (!sourceTypes.TryGetValue(m.ContainingTypeFQN, out var entry))
                {
                    entry = (m.ContainingTypeFQN, m.ContainingTypeSimpleName, new List<MethodModel>());
                    sourceTypes[m.ContainingTypeFQN] = entry;
                }
                entry.methods.Add(m.Method);
            }

            var model = new ContextModel(
                rootNamespace,
                className,
                $"global::{rootNamespace}.{className}",
                "internal",
                ImmutableArray<ContainingTypeInfo>.Empty,
                sourceTypes.Values
                    .Select(st => new SourceTypeModel(st.fqn, st.simpleName, st.methods.ToImmutableArray()))
                    .ToImmutableArray(),
                ImmutableArray<DiagnosticInfo>.Empty,
                EmitBaseClass: true);

            var source = GenerateContextSource(model);
            spc.AddSource($"{className}.g.cs", SourceText.From(source, Encoding.UTF8));
        });
    }

    /// <summary>
    /// Gets the root namespace from the compilation (MSBuild property or fallback to assembly name).
    /// </summary>
    private static string GetRootNamespace(Compilation compilation)
    {
        var namespaces = new Dictionary<string, int>();
        foreach (var tree in compilation.SyntaxTrees)
        {
            var root = tree.GetRoot();
            foreach (var ns in root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
            {
                var name = ns.Name.ToString();
                var topLevel = name.Contains('.') ? name.Substring(0, name.IndexOf('.')) : name;
                namespaces.TryGetValue(topLevel, out var count);
                namespaces[topLevel] = count + 1;
            }
        }

        if (namespaces.Count > 0)
            return namespaces.OrderByDescending(kv => kv.Value).First().Key;

        // No namespaces found — emit in the global namespace.
        return "";
    }

    /// <summary>
    /// Extracts method/property info for a single [ExportAIFunction] member for the assembly-wide pipeline.
    /// </summary>
    private static AssemblyExportedMember? GetExportedMemberInfo(
        GeneratorAttributeSyntaxContext ctx,
        CancellationToken ct)
    {
        var diagnostics = new List<DiagnosticInfo>();

        if (ctx.TargetSymbol is IMethodSymbol method)
        {
            if (method.MethodKind != MethodKind.Ordinary)
                return null;
            if (method.DeclaredAccessibility != Accessibility.Public && method.DeclaredAccessibility != Accessibility.Internal)
                return null;
            if (method.ContainingType is null)
                return null;

            var methods = GetExportedMethods(method.ContainingType, diagnostics, ct);
            var match = methods.FirstOrDefault(m => m.MethodName == method.Name && !m.IsProperty);
            if (match is null)
                return null;

            return new AssemblyExportedMember(
                method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                SanitizeIdentifier(method.ContainingType.Name),
                match);
        }

        if (ctx.TargetSymbol is IPropertySymbol prop)
        {
            if (prop.ContainingType is null)
                return null;

            var methods = GetExportedMethods(prop.ContainingType, diagnostics, ct);
            var match = methods.FirstOrDefault(m => m.MethodName == prop.Name && m.IsProperty);
            if (match is null)
                return null;

            return new AssemblyExportedMember(
                prop.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                SanitizeIdentifier(prop.ContainingType.Name),
                match);
        }

        return null;
    }

    private sealed record AssemblyExportedMember(
        string ContainingTypeFQN,
        string ContainingTypeSimpleName,
        MethodModel Method);

    private static ContextModel? GetContextModel(
        GeneratorAttributeSyntaxContext ctx,
        CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol contextSymbol)
            return null;

        if (!InheritsFrom(contextSymbol, AIToolContextFullName))
            return null;

        var sourceTypes = new List<SourceTypeModel>();
        var diagnostics = new List<DiagnosticInfo>();

        foreach (var attr in contextSymbol.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();

            if (attr.AttributeClass?.ToDisplayString() != AIToolSourceAttributeFullName)
                continue;

            if (attr.ConstructorArguments.Length != 1)
                continue;

            if (attr.ConstructorArguments[0].Value is not INamedTypeSymbol sourceTypeSymbol)
                continue;

            // Read IncludeTools / ExcludeTools named arguments
            ImmutableArray<string>? includeTools = null;
            ImmutableArray<string>? excludeTools = null;
            foreach (var na in attr.NamedArguments)
            {
                if (na.Key == "IncludeTools" && !na.Value.IsNull)
                {
                    includeTools = na.Value.Values
                        .Where(v => v.Value is string)
                        .Select(v => (string)v.Value!)
                        .ToImmutableArray();
                }
                else if (na.Key == "ExcludeTools" && !na.Value.IsNull)
                {
                    excludeTools = na.Value.Values
                        .Where(v => v.Value is string)
                        .Select(v => (string)v.Value!)
                        .ToImmutableArray();
                }
            }

            var methods = GetExportedMethods(sourceTypeSymbol, diagnostics, ct);

            // Apply include filter: empty or null = all tools, non-empty = only those listed
            if (includeTools is { Length: > 0 } include)
            {
                var includeSet = new HashSet<string>(include, StringComparer.Ordinal);
                foreach (var name in include)
                {
                    if (!methods.Any(m => m.MethodName == name))
                    {
                        diagnostics.Add(DiagnosticInfo.FilteredToolNotFound(
                            name,
                            sourceTypeSymbol.ToDisplayString(),
                            attr.ApplicationSyntaxReference?.GetSyntax(ct).GetLocation()));
                    }
                }
                methods = methods.Where(m => includeSet.Contains(m.MethodName)).ToList();
            }
            // IncludeTools=[] or null → keep all (no filtering)

            // Apply exclude filter: removes from whatever include produced
            if (excludeTools is { Length: > 0 } exclude)
            {
                var excludeSet = new HashSet<string>(exclude, StringComparer.Ordinal);
                methods = methods.Where(m => !excludeSet.Contains(m.MethodName)).ToList();
            }
            // ExcludeTools=[] or null → exclude nothing

            if (methods.Count == 0)
            {
                if (includeTools is null && excludeTools is null)
                {
                    // No exported methods at all — this is MAUIAI003
                    diagnostics.Add(DiagnosticInfo.NoExportableMethods(
                        sourceTypeSymbol.ToDisplayString(),
                        attr.ApplicationSyntaxReference?.GetSyntax(ct).GetLocation()));
                }
                // Still add an empty source type so the context is always generated
            }

            sourceTypes.Add(new SourceTypeModel(
                sourceTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                SanitizeIdentifier(sourceTypeSymbol.Name),
                methods.ToImmutableArray()));
        }

        if (sourceTypes.Count == 0 && diagnostics.Count == 0)
            return null;

        var accessibility = contextSymbol.DeclaredAccessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Protected => "protected",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            Accessibility.Private => "private",
            _ => "internal"
        };

        // Walk the containing type chain (innermost first → reversed to outermost first).
        var containingTypes = new List<ContainingTypeInfo>();
        var outer = contextSymbol.ContainingType;
        while (outer is not null)
        {
            var outerAccess = outer.DeclaredAccessibility switch
            {
                Accessibility.Public => "public",
                Accessibility.Internal => "internal",
                Accessibility.Protected => "protected",
                Accessibility.ProtectedOrInternal => "protected internal",
                Accessibility.ProtectedAndInternal => "private protected",
                Accessibility.Private => "private",
                _ => "internal"
            };
            var keyword = outer.IsRecord ? "record class" : "class";
            containingTypes.Add(new ContainingTypeInfo(keyword, outer.Name, outerAccess));
            outer = outer.ContainingType;
        }
        containingTypes.Reverse();

        return new ContextModel(
            contextSymbol.ContainingNamespace?.IsGlobalNamespace == true ? "" : contextSymbol.ContainingNamespace!.ToDisplayString(),
            contextSymbol.Name,
            contextSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            accessibility,
            containingTypes.ToImmutableArray(),
            sourceTypes.ToImmutableArray(),
            diagnostics.ToImmutableArray());
    }

    private static List<MethodModel> GetExportedMethods(
        INamedTypeSymbol typeSymbol,
        List<DiagnosticInfo> diagnostics,
        CancellationToken ct)
    {
        var methods = new List<MethodModel>();
        var nameCollisions = new Dictionary<string, int>();

        foreach (var member in typeSymbol.GetMembers())
        {
            ct.ThrowIfCancellationRequested();

            if (member is not IMethodSymbol method)
                continue;
            if (method.MethodKind != MethodKind.Ordinary)
                continue;
            if (method.DeclaredAccessibility != Accessibility.Public && method.DeclaredAccessibility != Accessibility.Internal)
                continue;

            var exportAttr = method.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ExportAIFunctionAttributeFullName);
            if (exportAttr is null)
                continue;

            var location = method.Locations.FirstOrDefault();

            if (method.IsGenericMethod)
            {
                diagnostics.Add(DiagnosticInfo.UnsupportedSignature(
                    method.ToDisplayString(),
                    "Generic methods are not supported.",
                    location));
                continue;
            }

            bool hasRefOrOut = false;
            foreach (var p in method.Parameters)
            {
                if (p.RefKind is RefKind.Ref or RefKind.Out or RefKind.In or RefKind.RefReadOnlyParameter)
                {
                    hasRefOrOut = true;
                    break;
                }
            }
            if (hasRefOrOut)
            {
                diagnostics.Add(DiagnosticInfo.UnsupportedSignature(
                    method.ToDisplayString(),
                    "Parameters with ref, out, or in modifiers are not supported.",
                    location));
                continue;
            }

            string? name = null;
            string? description = null;
            bool approvalRequired = false;

            if (exportAttr.ConstructorArguments.Length >= 1 &&
                exportAttr.ConstructorArguments[0].Value is string ctorName)
            {
                name = ctorName;
            }
            foreach (var na in exportAttr.NamedArguments)
            {
                switch (na.Key)
                {
                    case "Name": name = na.Value.Value as string; break;
                    case "Description": description = na.Value.Value as string; break;
                    case "ApprovalRequired": approvalRequired = na.Value.Value is true; break;
                }
            }

            if (description is null)
            {
                var descAttr = method.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == DescriptionAttributeFullName);
                if (descAttr is not null &&
                    descAttr.ConstructorArguments.Length >= 1 &&
                    descAttr.ConstructorArguments[0].Value is string d)
                {
                    description = d;
                }
            }

            var toolName = name ?? method.Name;
            var parameters = AnalyzeParameters(method, diagnostics);
            var returnInfo = AnalyzeReturnType(method.ReturnType);

            // Disambiguate same-method-name overloads in the generated class name.
            var baseClassName = $"{SanitizeIdentifier(typeSymbol.Name)}_{SanitizeIdentifier(method.Name)}_Tool";
            if (nameCollisions.TryGetValue(baseClassName, out var count))
            {
                nameCollisions[baseClassName] = count + 1;
                baseClassName = $"{baseClassName}_{count + 1}";
            }
            else
            {
                nameCollisions[baseClassName] = 0;
            }

        methods.Add(new MethodModel(
            method.Name,
            toolName,
            description,
            approvalRequired,
            method.IsStatic,
            IsProperty: false,
            IsPropertySetter: false,
            parameters,
            returnInfo,
            baseClassName));
        }

        // Also scan properties with [ExportAIFunction] — check the property itself
        // and its accessors (get/set), since C# allows attributes on accessors:
        //   public partial CartMode CartMode { [ExportAIFunction("get_cart_mode")] get; set; }
        foreach (var member in typeSymbol.GetMembers())
        {
            ct.ThrowIfCancellationRequested();

            if (member is not IPropertySymbol prop)
                continue;
            if (prop.IsIndexer)
                continue;
            if (prop.DeclaredAccessibility != Accessibility.Public && prop.DeclaredAccessibility != Accessibility.Internal)
                continue;

            // Check the property itself, then fall back to getter accessor, then setter accessor.
            var exportAttr = prop.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ExportAIFunctionAttributeFullName);

            var isGetterExport = false;
            var isSetterExport = false;

            if (exportAttr is null && prop.GetMethod is not null)
            {
                exportAttr = prop.GetMethod.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ExportAIFunctionAttributeFullName);
                if (exportAttr is not null)
                    isGetterExport = true;
            }

            if (exportAttr is null && prop.SetMethod is not null)
            {
                exportAttr = prop.SetMethod.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ExportAIFunctionAttributeFullName);
                if (exportAttr is not null)
                    isSetterExport = true;
            }

            // Also check if the setter has a separate [ExportAIFunction] when getter already matched.
            AttributeData? setterExportAttr = null;
            if (isGetterExport && prop.SetMethod is not null)
            {
                setterExportAttr = prop.SetMethod.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ExportAIFunctionAttributeFullName);
            }

            if (exportAttr is null)
                continue;

            if (prop.IsWriteOnly || prop.GetMethod is null)
                continue;

            // Emit the getter tool.
            EmitPropertyTool(methods, nameCollisions, typeSymbol, prop, exportAttr, isGetterExport || !isSetterExport);

            // If the setter has its own [ExportAIFunction], emit a separate tool for it.
            if (setterExportAttr is not null)
            {
                EmitPropertyTool(methods, nameCollisions, typeSymbol, prop, setterExportAttr, false);
            }
            else if (isSetterExport)
            {
                // The only export was on the setter — already emitted above as a property read tool.
                // Re-emit as setter semantics (the tool name/description came from the setter).
            }
        }

        return methods;
    }

    private static void EmitPropertyTool(
        List<MethodModel> methods,
        Dictionary<string, int> nameCollisions,
        INamedTypeSymbol typeSymbol,
        IPropertySymbol prop,
        AttributeData exportAttr,
        bool isReadTool)
    {
        string? name = null;
        string? description = null;
        bool approvalRequired = false;

        if (exportAttr.ConstructorArguments.Length >= 1 &&
            exportAttr.ConstructorArguments[0].Value is string ctorName)
        {
            name = ctorName;
        }
        foreach (var na in exportAttr.NamedArguments)
        {
            switch (na.Key)
            {
                case "Name": name = na.Value.Value as string; break;
                case "Description": description = na.Value.Value as string; break;
                case "ApprovalRequired": approvalRequired = na.Value.Value is true; break;
            }
        }

        if (description is null)
        {
            // Check for [Description] on the accessor or property.
            var source = isReadTool ? (ISymbol?)prop.GetMethod ?? prop : (ISymbol?)prop.SetMethod ?? prop;
            var descAttr = source.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == DescriptionAttributeFullName);
            if (descAttr is null)
            {
                descAttr = prop.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == DescriptionAttributeFullName);
            }
            if (descAttr is not null &&
                descAttr.ConstructorArguments.Length >= 1 &&
                descAttr.ConstructorArguments[0].Value is string d)
            {
                description = d;
            }
        }

        var toolName = name ?? prop.Name;
        var returnInfo = AnalyzeReturnType(prop.Type);

        var baseClassName = $"{SanitizeIdentifier(typeSymbol.Name)}_{SanitizeIdentifier(prop.Name)}_{(isReadTool ? "Get" : "Set")}_Tool";
        if (nameCollisions.TryGetValue(baseClassName, out var count))
        {
            nameCollisions[baseClassName] = count + 1;
            baseClassName = $"{baseClassName}_{count + 1}";
        }
        else
        {
            nameCollisions[baseClassName] = 0;
        }

        var parameters = isReadTool || prop.SetMethod is null
            ? ImmutableArray<ParameterModel>.Empty
            : ImmutableArray.Create(new ParameterModel(
                "value",
                prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                prop.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
                    .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                IsJsonBindable(prop.Type) ? ParameterKind.JsonArgument : ParameterKind.Unknown,
                KeyedServiceKey: null,
                IsNullable: prop.Type.NullableAnnotation == NullableAnnotation.Annotated || prop.Type.IsReferenceType,
                HasDefault: false,
                DefaultLiteral: null,
                Description: "The new value to assign."));

        methods.Add(new MethodModel(
            prop.Name,
            toolName,
            description,
            approvalRequired,
            prop.IsStatic,
            IsProperty: true,
            IsPropertySetter: !isReadTool,
            parameters,
            returnInfo,
            baseClassName));
    }

    private static ImmutableArray<ParameterModel> AnalyzeParameters(
        IMethodSymbol method,
        List<DiagnosticInfo> diagnostics)
    {
        var list = new List<ParameterModel>(method.Parameters.Length);
        foreach (var p in method.Parameters)
        {
            var typeName = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var unannotatedTypeName = p.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
                .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            var kind = ClassifyParameter(p, out string? keyedServiceKey);

            // hasDefault/defaultValueLiteral
            string? defaultLiteral = null;
            bool hasDefault = p.HasExplicitDefaultValue;
            if (hasDefault)
            {
                defaultLiteral = FormatDefaultLiteral(p.ExplicitDefaultValue, p.Type);
            }

            if (kind == ParameterKind.Unknown)
            {
                diagnostics.Add(DiagnosticInfo.UnserializableParameter(
                    method.ToDisplayString(),
                    p.Name,
                    typeName,
                    p.Locations.FirstOrDefault()));
                // Fall back to JSON binding so compilation succeeds; it will fail at runtime if invoked.
                kind = ParameterKind.JsonArgument;
            }

            list.Add(new ParameterModel(
                p.Name,
                typeName,
                unannotatedTypeName,
                kind,
                keyedServiceKey,
                p.Type.NullableAnnotation == NullableAnnotation.Annotated || p.Type.IsReferenceType,
                hasDefault,
                defaultLiteral,
                GetParameterDescription(p)));
        }
        return list.ToImmutableArray();
    }

    private static string? GetParameterDescription(IParameterSymbol p)
    {
        var attr = p.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == DescriptionAttributeFullName);
        if (attr is not null &&
            attr.ConstructorArguments.Length >= 1 &&
            attr.ConstructorArguments[0].Value is string d)
        {
            return d;
        }
        return null;
    }

    private static ParameterKind ClassifyParameter(IParameterSymbol p, out string? keyedServiceKey)
    {
        keyedServiceKey = null;
        var fullName = p.Type.ToDisplayString();

        // Explicit attributes take precedence.
        var keyedAttr = p.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == FromKeyedServicesAttributeFullName);
        if (keyedAttr is not null)
        {
            if (keyedAttr.ConstructorArguments.Length >= 1)
            {
                keyedServiceKey = FormatConstantKey(keyedAttr.ConstructorArguments[0]);
            }
            return ParameterKind.FromKeyedServices;
        }

        if (p.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == FromServicesAttributeFullName))
        {
            return ParameterKind.FromServices;
        }

        // Framework-provided types.
        if (fullName is CancellationTokenFullName or "global::" + CancellationTokenFullName)
            return ParameterKind.CancellationToken;
        if (p.Type is INamedTypeSymbol nts && nts.ToDisplayString() == CancellationTokenFullName)
            return ParameterKind.CancellationToken;
        if (p.Type.ToDisplayString() == IServiceProviderFullName)
            return ParameterKind.ServiceProvider;
        if (p.Type.ToDisplayString() == AIFunctionArgumentsFullName)
            return ParameterKind.AIFunctionArguments;

        // Otherwise, bind from the argument dictionary (JSON). No DI inference — users must
        // mark DI parameters with [FromServices] / [FromKeyedServices].
        return IsJsonBindable(p.Type) ? ParameterKind.JsonArgument : ParameterKind.Unknown;
    }

    private static bool IsJsonBindable(ITypeSymbol type)
    {
        // Conservative allowlist for the warning diagnostic only. The runtime ultimately decides
        // via JsonSerializer. We flag "clearly non-bindable" things like delegates and pointer types.
        if (type.TypeKind is TypeKind.Delegate or TypeKind.Pointer or TypeKind.FunctionPointer)
            return false;
        return true;
    }

    private static string FormatConstantKey(TypedConstant c)
    {
        if (c.Value is null) return "null";
        if (c.Value is string s) return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        return c.ToCSharpString();
    }

    private static string FormatDefaultLiteral(object? value, ITypeSymbol type)
    {
        if (value is null)
        {
            return type.IsReferenceType || type.NullableAnnotation == NullableAnnotation.Annotated
                ? "null"
                : $"default({type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})";
        }
        return value switch
        {
            string s => Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(s, quote: true),
            bool b => b ? "true" : "false",
            char c => Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(c, quote: true),
            float f => f.ToString("G9", System.Globalization.CultureInfo.InvariantCulture) + "f",
            double d => d.ToString("G17", System.Globalization.CultureInfo.InvariantCulture) + "d",
            decimal m => m.ToString(System.Globalization.CultureInfo.InvariantCulture) + "m",
            _ when type.TypeKind == TypeKind.Enum =>
                $"({type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})({value})",
            _ => value.ToString()!,
        };
    }

    private static ReturnInfo AnalyzeReturnType(ITypeSymbol returnType)
    {
        var displayName = returnType.ToDisplayString();
        if (displayName == "void")
            return new ReturnInfo(ReturnShape.Void, null);

        if (returnType is INamedTypeSymbol named)
        {
            var defn = named.ConstructedFrom.ToDisplayString();
            if (defn == "System.Threading.Tasks.Task")
                return new ReturnInfo(ReturnShape.Task, null);
            if (defn == "System.Threading.Tasks.ValueTask")
                return new ReturnInfo(ReturnShape.ValueTask, null);
            if (defn == "System.Threading.Tasks.Task<TResult>")
            {
                var t = named.TypeArguments[0];
                return new ReturnInfo(
                    ReturnShape.TaskOfT,
                    t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            }
            if (defn == "System.Threading.Tasks.ValueTask<TResult>")
            {
                var t = named.TypeArguments[0];
                return new ReturnInfo(
                    ReturnShape.ValueTaskOfT,
                    t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            }
        }

        return new ReturnInfo(
            ReturnShape.Sync,
            returnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
    }

    private static bool InheritsFrom(INamedTypeSymbol symbol, string baseTypeFullName)
    {
        var current = symbol.BaseType;
        while (current is not null)
        {
            if (current.ToDisplayString() == baseTypeFullName)
                return true;
            current = current.BaseType;
        }
        return false;
    }

    private static string GenerateContextSource(ContextModel model)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("#pragma warning disable CS8019 // Unnecessary using directive");
        sb.AppendLine("#pragma warning disable CS8604 // Possible null reference argument (JSON-deserialized values passed to non-nullable parameters)");
        sb.AppendLine("#pragma warning disable CS1998 // Async method lacks 'await' operators (sync property getters/void tools are emitted as async)");
        sb.AppendLine();
        sb.AppendLine("using global::System;");
        sb.AppendLine("using global::System.Collections.Generic;");
        sb.AppendLine("using global::System.Reflection;");
        sb.AppendLine("using global::System.Text.Json;");
        sb.AppendLine("using global::System.Threading;");
        sb.AppendLine("using global::System.Threading.Tasks;");
        sb.AppendLine("using global::Microsoft.Extensions.AI;");
        sb.AppendLine("using global::Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine();

        var indent = "";
        if (!string.IsNullOrEmpty(model.Namespace))
        {
            sb.AppendLine($"namespace {model.Namespace}");
            sb.AppendLine("{");
            indent = "    ";
        }

        // Open containing type declarations (outermost first).
        foreach (var ct in model.ContainingTypes)
        {
            sb.AppendLine($"{indent}{ct.Accessibility} partial {ct.Keyword} {ct.Name}");
            sb.AppendLine($"{indent}{{");
            indent += "    ";
        }

        // Emit the partial class body: Default + Tools.
        var baseClause = model.EmitBaseClass ? " : global::Microsoft.Maui.AI.Attributes.AIToolContext" : "";
        sb.AppendLine($"{indent}{model.Accessibility} partial class {model.ClassName}{baseClause}");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    /// <summary>Gets the default singleton instance of this tool context.</summary>");
        sb.AppendLine($"{indent}    public static {model.ClassName} Default {{ get; }} = new {model.ClassName}();");
        sb.AppendLine();

        // Tools property — cached in a static field so repeated access returns the same instance.
        sb.AppendLine($"{indent}    private static readonly global::Microsoft.Extensions.AI.AITool[] s_tools = new global::Microsoft.Extensions.AI.AITool[]");
        sb.AppendLine($"{indent}    {{");
        foreach (var st in model.SourceTypes)
        {
            foreach (var m in st.Methods)
            {
                sb.AppendLine($"{indent}        {WrapApproval($"new {m.GeneratedClassName}()", m.ApprovalRequired)},");
            }
        }
        sb.AppendLine($"{indent}    }};");
        sb.AppendLine();
        sb.AppendLine($"{indent}    /// <inheritdoc />");
        sb.AppendLine($"{indent}    public override global::System.Collections.Generic.IReadOnlyList<global::Microsoft.Extensions.AI.AITool> Tools => s_tools;");

        // Emit tool classes as nested private classes inside the context class — avoids
        // cross-context name collisions when the same service method is referenced from
        // multiple [AIToolSource]-decorated contexts.
        sb.AppendLine();
        foreach (var st in model.SourceTypes)
        {
            foreach (var m in st.Methods)
            {
                EmitToolClass(sb, indent + "    ", st, m);
                sb.AppendLine();
            }
        }

        sb.AppendLine($"{indent}}}");

        // Close containing type declarations (innermost first).
        for (var i = model.ContainingTypes.Length - 1; i >= 0; i--)
        {
            indent = indent.Substring(4);
            sb.AppendLine($"{indent}}}");
        }

        if (!string.IsNullOrEmpty(model.Namespace))
        {
            sb.AppendLine("}");
        }

        return sb.ToString();
    }

    private static string WrapApproval(string inner, bool approvalRequired) =>
        approvalRequired
            ? $"new global::Microsoft.Extensions.AI.ApprovalRequiredAIFunction({inner})"
            : inner;

    private static void EmitToolClass(StringBuilder sb, string indent, SourceTypeModel st, MethodModel m)
    {
        var cls = m.GeneratedClassName;

        // A tool requires a service provider when either:
        //   - the backing method is an instance method (we need to resolve the owning service), or
        //   - at least one parameter binds from DI (IServiceProvider / [FromServices] / [FromKeyedServices]).
        bool needsServiceForInstance = !m.IsStatic;
        bool anyParamNeedsServices = m.Parameters.Any(p =>
            p.Kind is ParameterKind.ServiceProvider
                  or ParameterKind.FromServices
                  or ParameterKind.FromKeyedServices);
        bool needsProvider = needsServiceForInstance || anyParamNeedsServices;

        sb.AppendLine($"{indent}private sealed class {cls} : global::Microsoft.Extensions.AI.AIFunction");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    private static readonly global::System.Lazy<global::System.Text.Json.JsonElement> s_schema = new(BuildSchema);");
        sb.AppendLine($"{indent}    private static readonly global::System.Lazy<global::System.Text.Json.JsonElement?> s_returnSchema = new(BuildReturnSchema);");
        sb.AppendLine();

        sb.AppendLine($"{indent}    public override string Name => {Escape(m.ToolName)};");
        sb.AppendLine($"{indent}    public override string Description => {EscapeOrEmpty(m.Description)};");
        sb.AppendLine($"{indent}    public override global::System.Text.Json.JsonElement JsonSchema => s_schema.Value;");
        sb.AppendLine($"{indent}    public override global::System.Text.Json.JsonElement? ReturnJsonSchema => s_returnSchema.Value;");
        sb.AppendLine();

        // MethodInfo lookup (used for schema generation only, one-shot at warmup).
        var bindingFlags = m.IsStatic
            ? "global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Static"
            : "global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Instance";
        if (m.IsProperty)
        {
            sb.AppendLine($"{indent}    private static global::System.Reflection.MethodInfo GetTargetMethod()");
            sb.AppendLine($"{indent}    {{");
            sb.AppendLine($"{indent}        var serviceType = typeof({st.FullyQualifiedName});");
            sb.AppendLine($"{indent}        var prop = serviceType.GetProperty({Escape(m.MethodName)}, {bindingFlags})");
            sb.AppendLine($"{indent}            ?? throw new global::System.InvalidOperationException({Escape($"Could not locate target property {st.FullyQualifiedName}.{m.MethodName}.")});");
            if (m.IsPropertySetter)
            {
                sb.AppendLine($"{indent}        return prop.SetMethod");
                sb.AppendLine($"{indent}            ?? throw new global::System.InvalidOperationException({Escape($"Property {st.FullyQualifiedName}.{m.MethodName} has no setter.")});");
            }
            else
            {
                sb.AppendLine($"{indent}        return prop.GetMethod");
                sb.AppendLine($"{indent}            ?? throw new global::System.InvalidOperationException({Escape($"Property {st.FullyQualifiedName}.{m.MethodName} has no getter.")});");
            }
            sb.AppendLine($"{indent}    }}");
        }
        else
        {
            sb.AppendLine($"{indent}    private static global::System.Reflection.MethodInfo GetTargetMethod()");
            sb.AppendLine($"{indent}    {{");
            sb.AppendLine($"{indent}        var serviceType = typeof({st.FullyQualifiedName});");
            sb.Append($"{indent}        var paramTypes = new global::System.Type[] {{ ");
            foreach (var p in m.Parameters)
            {
                sb.Append($"typeof({p.UnannotatedTypeName}), ");
            }
            sb.AppendLine("};");
            sb.AppendLine($"{indent}        return serviceType.GetMethod({Escape(m.MethodName)}, {bindingFlags}, null, paramTypes, null)");
            sb.AppendLine($"{indent}            ?? throw new global::System.InvalidOperationException({Escape($"Could not locate target method {st.FullyQualifiedName}.{m.MethodName}.")});");
            sb.AppendLine($"{indent}    }}");
        }
        sb.AppendLine();

        // Schema builder (uses IncludeParameter to exclude DI-bound parameters).
        sb.AppendLine($"{indent}    private static readonly global::System.Collections.Generic.HashSet<string> s_schemaExcludedParameters = new()");
        sb.AppendLine($"{indent}    {{");
        foreach (var p in m.Parameters)
        {
            if (!IncludeInSchema(p.Kind))
                sb.AppendLine($"{indent}        {Escape(p.Name)},");
        }
        sb.AppendLine($"{indent}    }};");
        sb.AppendLine();

        sb.AppendLine($"{indent}    private static global::System.Text.Json.JsonElement BuildSchema()");
        sb.AppendLine($"{indent}    {{");
        sb.AppendLine($"{indent}        var inferenceOptions = new global::Microsoft.Extensions.AI.AIJsonSchemaCreateOptions");
        sb.AppendLine($"{indent}        {{");
        sb.AppendLine($"{indent}            IncludeParameter = static p => !s_schemaExcludedParameters.Contains(p.Name!),");
        sb.AppendLine($"{indent}        }};");
        sb.AppendLine($"{indent}        return global::Microsoft.Extensions.AI.AIJsonUtilities.CreateFunctionJsonSchema(");
        sb.AppendLine($"{indent}            GetTargetMethod(), title: string.Empty, description: string.Empty, inferenceOptions: inferenceOptions);");
        sb.AppendLine($"{indent}    }}");
        sb.AppendLine();

        sb.AppendLine($"{indent}    private static global::System.Text.Json.JsonElement? BuildReturnSchema()");
        sb.AppendLine($"{indent}    {{");
        if (m.ReturnInfo.Shape == ReturnShape.Void || m.ReturnInfo.Shape == ReturnShape.Task || m.ReturnInfo.Shape == ReturnShape.ValueTask)
        {
            sb.AppendLine($"{indent}        return null;");
        }
        else
        {
            sb.AppendLine($"{indent}        return global::Microsoft.Extensions.AI.AIJsonUtilities.CreateJsonSchema(typeof({m.ReturnInfo.TypeName!}));");
        }
        sb.AppendLine($"{indent}    }}");
        sb.AppendLine();

        // InvokeCoreAsync
        sb.AppendLine($"{indent}    protected override async global::System.Threading.Tasks.ValueTask<object?> InvokeCoreAsync(");
        sb.AppendLine($"{indent}        global::Microsoft.Extensions.AI.AIFunctionArguments arguments,");
        sb.AppendLine($"{indent}        global::System.Threading.CancellationToken cancellationToken)");
        sb.AppendLine($"{indent}    {{");

        if (needsProvider)
        {
            sb.AppendLine($"{indent}        var __provider = arguments.Services ?? throw new global::System.InvalidOperationException(");
            sb.AppendLine($"{indent}            \"Tool '{m.ToolName}' requires services (source type: {st.FullyQualifiedName}) but no IServiceProvider was supplied. \" +");
            sb.AppendLine($"{indent}            \"Set AIFunctionArguments.Services before invoking the tool (ChatClientBuilder.UseFunctionInvocation().Build(sp) does this automatically).\");");
        }
        if (needsServiceForInstance)
        {
            sb.AppendLine($"{indent}        var __service = __provider.GetService<{st.FullyQualifiedName}>() ?? throw new global::System.InvalidOperationException(");
            sb.AppendLine($"{indent}            \"Tool '{m.ToolName}' could not resolve its source type '{st.FullyQualifiedName}' from IServiceProvider. \" +");
            sb.AppendLine($"{indent}            \"Register the service in your DI container, or use ChatClientBuilder.UseFunctionInvocation().Build(sp) to supply a configured IServiceProvider.\");");
        }

        var argNames = new List<string>();
        foreach (var p in m.Parameters)
        {
            var local = $"__arg_{p.Name}";
            argNames.Add(local);
            EmitParameterBinding(sb, indent + "        ", local, p);
        }

        // Call & await — static members call via the type; instance members go through __service.
        var receiver = m.IsStatic ? st.FullyQualifiedName : "__service";
        var callExpr = m.IsProperty
            ? (m.IsPropertySetter
                ? $"({receiver}.{m.MethodName} = {argNames.Single()})"
                : $"{receiver}.{m.MethodName}")
            : $"{receiver}.{m.MethodName}({string.Join(", ", argNames)})";
        switch (m.ReturnInfo.Shape)
        {
            case ReturnShape.Void:
                sb.AppendLine($"{indent}        {callExpr};");
                sb.AppendLine($"{indent}        return null;");
                break;
            case ReturnShape.Sync:
                sb.AppendLine($"{indent}        var __result = {callExpr};");
                sb.AppendLine($"{indent}        return __result;");
                break;
            case ReturnShape.Task:
                sb.AppendLine($"{indent}        await {callExpr}.ConfigureAwait(false);");
                sb.AppendLine($"{indent}        return null;");
                break;
            case ReturnShape.ValueTask:
                sb.AppendLine($"{indent}        await {callExpr}.ConfigureAwait(false);");
                sb.AppendLine($"{indent}        return null;");
                break;
            case ReturnShape.TaskOfT:
            case ReturnShape.ValueTaskOfT:
                sb.AppendLine($"{indent}        var __result = await {callExpr}.ConfigureAwait(false);");
                sb.AppendLine($"{indent}        return __result;");
                break;
        }

        sb.AppendLine($"{indent}    }}");
        sb.AppendLine($"{indent}}}");
    }

    private static void EmitParameterBinding(StringBuilder sb, string indent, string local, ParameterModel p)
    {
        switch (p.Kind)
        {
            case ParameterKind.CancellationToken:
                sb.AppendLine($"{indent}var {local} = cancellationToken;");
                break;
            case ParameterKind.ServiceProvider:
                sb.AppendLine($"{indent}var {local} = __provider;");
                break;
            case ParameterKind.AIFunctionArguments:
                sb.AppendLine($"{indent}var {local} = arguments;");
                break;
            case ParameterKind.FromServices:
                sb.AppendLine($"{indent}var {local} = __provider.GetService<{p.TypeName}>() ?? throw new global::System.InvalidOperationException(");
                sb.AppendLine($"{indent}    \"Tool parameter '{p.Name}' of type '{p.TypeName}' could not be resolved from IServiceProvider. \" +");
                sb.AppendLine($"{indent}    \"Register the service in your DI container.\");");
                break;
            case ParameterKind.FromKeyedServices:
                var keyLiteral = p.KeyedServiceKey ?? "null";
                // Use (as IKeyedServiceProvider)?. pattern because the extension method GetKeyedService
                // throws InvalidOperationException("KeyedServicesNotSupported") when the provider doesn't
                // implement IKeyedServiceProvider (e.g. EmptyServiceProvider). By going through the
                // interface directly, non-keyed providers yield null which our ?? throw catches with a
                // contextual error message.
                sb.AppendLine($"{indent}var {local} = ({p.TypeName}?)(__provider as global::Microsoft.Extensions.DependencyInjection.IKeyedServiceProvider)?.GetKeyedService(typeof({p.TypeName}), {keyLiteral})");
                sb.AppendLine($"{indent}    ?? throw new global::System.InvalidOperationException(");
                sb.AppendLine($"{indent}        \"Tool parameter '{p.Name}' of type '{p.TypeName}' could not be resolved from IServiceProvider with key \" + {keyLiteral} + \". \" +");
                sb.AppendLine($"{indent}        \"Register the keyed service in your DI container, or ensure the IServiceProvider supports keyed services (IKeyedServiceProvider).\");");
                break;
            case ParameterKind.JsonArgument:
                if (p.HasDefault)
                {
                    sb.AppendLine($"{indent}var {local} = global::Microsoft.Maui.AI.Attributes.AIToolMetadataServices.GetOptionalArg<{p.TypeName}>(arguments, {Escape(p.Name)}, {p.DefaultLiteral});");
                }
                else
                {
                    sb.AppendLine($"{indent}var {local} = global::Microsoft.Maui.AI.Attributes.AIToolMetadataServices.GetRequiredArg<{p.TypeName}>(arguments, {Escape(p.Name)});");
                }
                break;
            default:
                sb.AppendLine($"{indent}// Unclassified parameter {p.Name}: falling back to JSON binding.");
                sb.AppendLine($"{indent}var {local} = global::Microsoft.Maui.AI.Attributes.AIToolMetadataServices.GetRequiredArg<{p.TypeName}>(arguments, {Escape(p.Name)});");
                break;
        }
    }

    private static bool IncludeInSchema(ParameterKind kind) =>
        kind is ParameterKind.JsonArgument;

    private static string SanitizeIdentifier(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        }
        return sb.ToString();
    }

    private static string Escape(string value)
        => Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(value, quote: true);

    private static string EscapeOrNull(string? value)
        => value is null ? "null" : Escape(value);

    private static string EscapeOrEmpty(string? value)
        => value is null ? "string.Empty" : Escape(value);

    // Pipeline models

    private enum ParameterKind
    {
        JsonArgument,
        CancellationToken,
        ServiceProvider,
        AIFunctionArguments,
        FromServices,
        FromKeyedServices,
        Unknown,
    }

    private enum ReturnShape
    {
        Void,
        Sync,
        Task,
        TaskOfT,
        ValueTask,
        ValueTaskOfT,
    }

    private sealed record ReturnInfo(ReturnShape Shape, string? TypeName);

    private sealed record ParameterModel(
        string Name,
        string TypeName,
        string UnannotatedTypeName,
        ParameterKind Kind,
        string? KeyedServiceKey,
        bool IsNullable,
        bool HasDefault,
        string? DefaultLiteral,
        string? Description);

    private sealed record MethodModel(
        string MethodName,
        string ToolName,
        string? Description,
        bool ApprovalRequired,
        bool IsStatic,
        bool IsProperty,
        bool IsPropertySetter,
        ImmutableArray<ParameterModel> Parameters,
        ReturnInfo ReturnInfo,
        string GeneratedClassName);

    private sealed record SourceTypeModel(
        string FullyQualifiedName,
        string SimpleName,
        ImmutableArray<MethodModel> Methods);

    private sealed record ContainingTypeInfo(
        string Keyword,
        string Name,
        string Accessibility);

    private sealed record ContextModel(
        string Namespace,
        string ClassName,
        string FullyQualifiedName,
        string Accessibility,
        ImmutableArray<ContainingTypeInfo> ContainingTypes,
        ImmutableArray<SourceTypeModel> SourceTypes,
        ImmutableArray<DiagnosticInfo> Diagnostics,
        bool EmitBaseClass = false)
    {
        public ContextModel WithAdditionalSourceTypes(ImmutableArray<SourceTypeModel> newTypes)
        {
            var existing = SourceTypes.ToList();
            foreach (var nt in newTypes)
            {
                if (!existing.Any(e => e.FullyQualifiedName == nt.FullyQualifiedName))
                    existing.Add(nt);
            }
            return this with { SourceTypes = existing.ToImmutableArray() };
        }
    }

    private sealed record LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
    {
        public static LocationInfo? From(Location? location)
        {
            if (location is null || !location.IsInSource)
                return null;
            return new LocationInfo(
                location.SourceTree!.FilePath,
                location.SourceSpan,
                location.GetLineSpan().Span);
        }

        public Location ToLocation()
            => Microsoft.CodeAnalysis.Location.Create(FilePath, TextSpan, LineSpan);
    }

    private sealed record DiagnosticInfo(
        string Id,
        DiagnosticSeverity Severity,
        string Message,
        LocationInfo? Location)
    {
        public Diagnostic ToDiagnostic()
        {
            var desc = new DiagnosticDescriptor(
                Id,
                Id,
                Message,
                "Microsoft.Maui.AI.Attributes",
                Severity,
                isEnabledByDefault: true);
            return Diagnostic.Create(desc, Location?.ToLocation() ?? Microsoft.CodeAnalysis.Location.None);
        }

        public static DiagnosticInfo NoExportableMethods(string typeName, Location? location) =>
            new(
                "MAUIAI003",
                DiagnosticSeverity.Warning,
                $"[AIToolSource(typeof({typeName}))] references a type with no [ExportAIFunction] members.",
                LocationInfo.From(location));

        public static DiagnosticInfo UnserializableParameter(string methodName, string paramName, string typeName, Location? location) =>
            new(
                "MAUIAI002",
                DiagnosticSeverity.Warning,
                $"Parameter '{paramName}' ({typeName}) on '{methodName}' is unlikely to be JSON-serializable. Consider annotating it with [FromServices]/[FromKeyedServices] or using a supported type.",
                LocationInfo.From(location));

        public static DiagnosticInfo UnsupportedSignature(string methodName, string reason, Location? location) =>
            new(
                "MAUIAI004",
                DiagnosticSeverity.Error,
                $"[ExportAIFunction] method '{methodName}' has an unsupported signature: {reason}",
                LocationInfo.From(location));

        public static DiagnosticInfo IncludeAndExcludeBothSet(string typeName, Location? location) =>
            new(
                "MAUIAI005",
                DiagnosticSeverity.Error,
                $"[AIToolSource(typeof({typeName}))] sets both IncludeTools and ExcludeTools. Use only one.",
                LocationInfo.From(location));

        public static DiagnosticInfo FilteredToolNotFound(string toolName, string typeName, Location? location) =>
            new(
                "MAUIAI006",
                DiagnosticSeverity.Warning,
                $"IncludeTools lists '{toolName}' but no [ExportAIFunction] method or property with that name was found on '{typeName}'.",
                LocationInfo.From(location));
    }
}
