using Assembly = System.Reflection.Assembly;
using ArchUnitNET.Domain;
using ArchUnitNET.Loader;

namespace ArticleMonitor.UnitTests.Architecture;

/// <summary>
/// Source modules share one project, so the compiler does not keep them apart. Each module is a folder (and
/// namespace) under ArticleMonitor.Sources: it may use the common building blocks, never another module, and the
/// common blocks never use a module. Dependencies are read from the compiled code, method bodies included.
/// </summary>
public class SourceIsolationTests
{
    private const string ModulesNamespace = "ArticleMonitor.Sources.";
    private const string CommonFolder = "Common";

    private static readonly IReadOnlyList<IType> SourceTypes =
    [
        .. new ArchLoader().LoadAssemblies(Assembly.Load("ArticleMonitor.Sources")).Build().Types,
    ];

    [Fact]
    public void A_module_never_uses_another_module()
    {
        var violations = FolderDependencies()
            .Where(dependency => dependency.Target != CommonFolder && dependency.Target != dependency.Origin)
            .Select(dependency => dependency.Description)
            .Distinct();

        Assert.Empty(violations);
    }

    [Fact]
    public void The_check_sees_modules_using_the_common_blocks() =>
        Assert.Contains(
            FolderDependencies(),
            dependency => dependency.Origin != CommonFolder && dependency.Target == CommonFolder);

    private static IEnumerable<(string Origin, string Target, string Description)> FolderDependencies() =>
        from type in SourceTypes
        let origin = FolderOf(type)
        where origin is not null
        from dependency in type.Dependencies
        let target = FolderOf(dependency.Target)
        where target is not null
        select (origin, target, $"{type.FullName} -> {dependency.Target.FullName}");

    /// <summary>The module folder of a type (e.g. "G1", "Common"), or null outside the source modules.</summary>
    private static string? FolderOf(IType type)
    {
        var name = type.Namespace.FullName;
        if (!name.StartsWith(ModulesNamespace, StringComparison.Ordinal)) return null;

        return name[ModulesNamespace.Length..].Split('.')[0];
    }
}
