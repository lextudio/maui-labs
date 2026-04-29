using System.Reflection;

namespace Microsoft.Maui.Cli.Skills;

public static class MauiCliSkillResources
{
    public const string ResourceRoot = "devflow.skills";

    public static Assembly Assembly => typeof(MauiCliSkillResources).Assembly;

    public static IReadOnlyList<MauiCliSkillDefinition> BundledSkills { get; } =
    [
        new("maui-devflow-onboard", "MAUI DevFlow Onboard", "Guides first-time MAUI DevFlow project integration.", Recommended: true),
        new("maui-devflow-debug", "MAUI DevFlow Debug", "Guides build, deploy, connection recovery, inspect, and debug loops with MAUI DevFlow.", Recommended: true)
    ];
}

public sealed record MauiCliSkillDefinition(string Id, string DisplayName, string Description, bool Recommended);
