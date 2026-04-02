using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Maui.DevFlow.Agent.SourceGen;

namespace Microsoft.Maui.DevFlow.Tests;

public class DevFlowAutoInitGeneratorTests
{
    // Minimal stubs so the semantic model resolves MauiAppBuilder.Build() → MauiApp
    private const string MauiStubs = """
        namespace Microsoft.Maui.Hosting
        {
            public class MauiApp { }
            public class MauiAppBuilder
            {
                public MauiApp Build() => new MauiApp();
            }
        }
        """;

    [Fact]
    public void DoesNotGenerate_WhenInitPropertyNotSet()
    {
        var source = """
            using Microsoft.Maui.Hosting;
            class Program
            {
                static MauiApp Create()
                {
                    var builder = new MauiAppBuilder();
                    return builder.Build();
                }
            }
            """;

        var generated = RunGenerator(source, properties: new());
        Assert.Empty(generated);
    }

    [Fact]
    public void DoesNotGenerate_WhenInitPropertyFalse()
    {
        var source = """
            using Microsoft.Maui.Hosting;
            class Program
            {
                static MauiApp Create()
                {
                    var builder = new MauiAppBuilder();
                    return builder.Build();
                }
            }
            """;

        var generated = RunGenerator(source, properties: new()
        {
            ["build_property.InitMauiDevFlowAgent"] = "false",
        });
        Assert.Empty(generated);
    }

    [Fact]
    public void GeneratesInterceptor_WhenInitPropertyTrue()
    {
        var source = """
            using Microsoft.Maui.Hosting;
            class Program
            {
                static MauiApp Create()
                {
                    var builder = new MauiAppBuilder();
                    return builder.Build();
                }
            }
            """;

        var generated = RunGenerator(source, properties: new()
        {
            ["build_property.InitMauiDevFlowAgent"] = "true",
        });

        var text = Assert.Single(generated);
        Assert.Contains("DevFlowBuild", text);
        Assert.Contains("InterceptsLocation", text);
        Assert.Contains("builder.Build()", text);
    }

    [Fact]
    public void DoesNotGenerate_WhenNoBuildCalls()
    {
        var source = """
            class Program
            {
                static void Main() { }
            }
            """;

        var generated = RunGenerator(source, properties: new()
        {
            ["build_property.InitMauiDevFlowAgent"] = "true",
        });
        Assert.Empty(generated);
    }

    [Fact]
    public void SkipsNonMauiAppBuilderBuildCalls()
    {
        var source = """
            class Other
            {
                public Other Build() => this;
            }
            class Program
            {
                static void Main()
                {
                    new Other().Build();
                }
            }
            """;

        var generated = RunGenerator(source, properties: new()
        {
            ["build_property.InitMauiDevFlowAgent"] = "true",
        });
        Assert.Empty(generated);
    }

    [Fact]
    public void IncludesPortOption_WhenMauiDevFlowPortSet()
    {
        var source = """
            using Microsoft.Maui.Hosting;
            class Program
            {
                static MauiApp Create()
                {
                    var builder = new MauiAppBuilder();
                    return builder.Build();
                }
            }
            """;

        var generated = RunGenerator(source, properties: new()
        {
            ["build_property.InitMauiDevFlowAgent"] = "true",
            ["build_property.MauiDevFlowPort"] = "9225",
        });

        var text = Assert.Single(generated);
        Assert.Contains("options.Port = 9225;", text);
    }

    [Fact]
    public void IncludesProfilerOption_WhenSet()
    {
        var source = """
            using Microsoft.Maui.Hosting;
            class Program
            {
                static MauiApp Create()
                {
                    var builder = new MauiAppBuilder();
                    return builder.Build();
                }
            }
            """;

        var generated = RunGenerator(source, properties: new()
        {
            ["build_property.InitMauiDevFlowAgent"] = "true",
            ["build_property.MauiDevFlowProfiler"] = "true",
        });

        var text = Assert.Single(generated);
        Assert.Contains("options.EnableProfiler = true;", text);
    }

    [Fact]
    public void IncludesAllOptions_WhenAllSet()
    {
        var source = """
            using Microsoft.Maui.Hosting;
            class Program
            {
                static MauiApp Create()
                {
                    var builder = new MauiAppBuilder();
                    return builder.Build();
                }
            }
            """;

        var generated = RunGenerator(source, properties: new()
        {
            ["build_property.InitMauiDevFlowAgent"] = "true",
            ["build_property.MauiDevFlowPort"] = "8080",
            ["build_property.MauiDevFlowFileLogging"] = "false",
            ["build_property.MauiDevFlowNetworkMonitoring"] = "true",
            ["build_property.MauiDevFlowProfiler"] = "false",
        });

        var text = Assert.Single(generated);
        Assert.Contains("options.Port = 8080;", text);
        Assert.Contains("options.EnableFileLogging = false;", text);
        Assert.Contains("options.EnableNetworkMonitoring = true;", text);
        Assert.Contains("options.EnableProfiler = false;", text);
    }

    [Fact]
    public void CallsWithoutOptions_WhenNoPropertiesSet()
    {
        var source = """
            using Microsoft.Maui.Hosting;
            class Program
            {
                static MauiApp Create()
                {
                    var builder = new MauiAppBuilder();
                    return builder.Build();
                }
            }
            """;

        var generated = RunGenerator(source, properties: new()
        {
            ["build_property.InitMauiDevFlowAgent"] = "true",
        });

        var text = Assert.Single(generated);
        Assert.Contains("AddMauiDevFlowAgent(builder);", text);
        Assert.DoesNotContain("options =>", text);
    }

    [Fact]
    public void GeneratesMultipleInterceptsLocations_ForMultipleBuildCalls()
    {
        var source = """
            using Microsoft.Maui.Hosting;
            class Program
            {
                static MauiApp Create1()
                {
                    var builder = new MauiAppBuilder();
                    return builder.Build();
                }
                static MauiApp Create2()
                {
                    var b = new MauiAppBuilder();
                    return b.Build();
                }
            }
            """;

        var generated = RunGenerator(source, properties: new()
        {
            ["build_property.InitMauiDevFlowAgent"] = "true",
        });

        var text = Assert.Single(generated);
        // Count [InterceptsLocation(...)] attribute usages (not the class definition)
        var count = text.Split("[global::System.Runtime.CompilerServices.InterceptsLocation(@").Length - 1;
        Assert.Equal(2, count);
    }

    [Fact]
    public void IncludesDoubleRegistrationGuard()
    {
        var source = """
            using Microsoft.Maui.Hosting;
            class Program
            {
                static MauiApp Create()
                {
                    var builder = new MauiAppBuilder();
                    return builder.Build();
                }
            }
            """;

        var generated = RunGenerator(source, properties: new()
        {
            ["build_property.InitMauiDevFlowAgent"] = "true",
        });

        var text = Assert.Single(generated);
        Assert.Contains("alreadyRegistered", text);
        Assert.Contains("DevFlowAgentService", text);
    }

    [Fact]
    public void IgnoresInvalidPortValue()
    {
        var source = """
            using Microsoft.Maui.Hosting;
            class Program
            {
                static MauiApp Create()
                {
                    var builder = new MauiAppBuilder();
                    return builder.Build();
                }
            }
            """;

        var generated = RunGenerator(source, properties: new()
        {
            ["build_property.InitMauiDevFlowAgent"] = "true",
            ["build_property.MauiDevFlowPort"] = "not-a-number",
        });

        var text = Assert.Single(generated);
        Assert.DoesNotContain("options.Port", text);
    }

    #region Helpers

    /// <summary>
    /// Runs the <see cref="DevFlowAutoInitGenerator"/> against the given source and returns
    /// the list of generated source texts (excluding the input).
    /// </summary>
    private static List<string> RunGenerator(
        string source,
        Dictionary<string, string> properties)
    {
        var syntaxTrees = new[]
        {
            CSharpSyntaxTree.ParseText(MauiStubs, path: "MauiStubs.cs"),
            CSharpSyntaxTree.ParseText(source, path: "TestSource.cs"),
        };

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        };

        // Add core runtime references needed for compilation
        var trustedAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?.Split(Path.PathSeparator) ?? [];
        foreach (var path in trustedAssemblies)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (name is "System.Runtime" or "System.Collections" or "netstandard")
                references.Add(MetadataReference.CreateFromFile(path));
        }

        var compilation = CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: syntaxTrees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new DevFlowAutoInitGenerator();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            optionsProvider: new TestOptionsProvider(properties));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        var results = driver.GetRunResult();
        return results.GeneratedTrees
            .Select(t => t.GetText().ToString())
            .ToList();
    }

    /// <summary>
    /// Provides MSBuild build properties to the source generator under test.
    /// </summary>
    private sealed class TestOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly TestOptions _globalOptions;

        public TestOptionsProvider(Dictionary<string, string> globalOptions)
            => _globalOptions = new TestOptions(globalOptions);

        public override AnalyzerConfigOptions GlobalOptions => _globalOptions;
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => TestOptions.Empty;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => TestOptions.Empty;

        private sealed class TestOptions : AnalyzerConfigOptions
        {
            public static readonly TestOptions Empty = new(new Dictionary<string, string>());
            private readonly Dictionary<string, string> _values;

            public TestOptions(Dictionary<string, string> values) => _values = values;

            public override bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value)
                => _values.TryGetValue(key, out value);
        }
    }

    #endregion
}
