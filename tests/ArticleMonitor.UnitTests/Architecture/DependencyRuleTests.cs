using System.Reflection;

namespace ArticleMonitor.UnitTests.Architecture;

/// <summary>
/// Checks the references compiled into each assembly. Projects are discovered from the build output, so a new
/// source module is covered as soon as the Worker references it.
/// </summary>
public class DependencyRuleTests
{
    private const string ProjectPrefix = "ArticleMonitor.";
    private const string DomainProject = "ArticleMonitor.Domain";
    private const string ApplicationProject = "ArticleMonitor.Application";
    private const string InfrastructureProject = "ArticleMonitor.Infrastructure";
    private const string SourcesCommonProject = "ArticleMonitor.Sources.Common";
    private const string SourceModulePrefix = "ArticleMonitor.Sources.";
    private const string WorkerProject = "ArticleMonitor.Worker";
    private const string TestsProject = "ArticleMonitor.UnitTests";

    private static readonly string[] ApplicationLibraries =
    [
        "Microsoft.Extensions.Configuration.Abstractions",
        "Microsoft.Extensions.Configuration.Binder",
        "Microsoft.Extensions.DependencyInjection.Abstractions",
        "Microsoft.Extensions.Logging.Abstractions",
        "Microsoft.Extensions.Options",
        "Microsoft.Extensions.Options.ConfigurationExtensions",
        "Microsoft.Extensions.Options.DataAnnotations",
    ];

    public static TheoryData<string> LayersBelowTheWorker =>
        new(ProjectNames().Where(project => project != WorkerProject));

    [Fact]
    public void Every_layer_is_checked()
    {
        var projects = ProjectNames();

        Assert.Superset(
            new HashSet<string> { DomainProject, ApplicationProject, InfrastructureProject, SourcesCommonProject },
            projects.ToHashSet());
        Assert.Contains(projects, IsSourceModule);
    }

    [Theory]
    [MemberData(nameof(LayersBelowTheWorker))]
    public void Layers_only_reference_the_ones_they_may_depend_on(string project)
    {
        var references = ReferencesOf(project).Where(reference => reference.StartsWith(ProjectPrefix, Ordinal));

        Assert.Empty(references.Except(AllowedProjects(project)));
    }

    [Fact]
    public void Domain_depends_only_on_the_base_class_library() =>
        Assert.All(ReferencesOf(DomainProject), reference => Assert.StartsWith("System", reference, Ordinal));

    [Fact]
    public void Application_has_no_io_or_site_specific_library()
    {
        var external = ReferencesOf(ApplicationProject).Where(reference => reference != DomainProject).ToList();

        Assert.DoesNotContain(external, IsNetworkOrDatabaseLibrary);
        Assert.Empty(external.Where(reference => !reference.StartsWith("System", Ordinal))
            .Except(ApplicationLibraries));
    }

    private static string[] AllowedProjects(string project) => project switch
    {
        DomainProject => [],
        ApplicationProject => [DomainProject],
        InfrastructureProject or SourcesCommonProject => [ApplicationProject, DomainProject],
        _ when IsSourceModule(project) => [SourcesCommonProject, ApplicationProject, DomainProject],
        _ => throw new InvalidOperationException($"Define the dependency rule of {project}."),
    };

    private static bool IsSourceModule(string project) =>
        project.StartsWith(SourceModulePrefix, Ordinal) && project != SourcesCommonProject;

    private static bool IsNetworkOrDatabaseLibrary(string reference) =>
        reference.StartsWith("System.Net.", Ordinal) || reference.StartsWith("System.Data", Ordinal);

    private static List<string> ProjectNames() =>
    [
        .. Directory.GetFiles(AppContext.BaseDirectory, $"{ProjectPrefix}*.dll")
            .Select(file => Path.GetFileNameWithoutExtension(file))
            .Where(name => name != TestsProject)
            .Order(StringComparer.Ordinal),
    ];

    private static List<string> ReferencesOf(string project) =>
        [.. Assembly.Load(project).GetReferencedAssemblies().Select(reference => reference.Name!)];

    private const StringComparison Ordinal = StringComparison.Ordinal;
}
