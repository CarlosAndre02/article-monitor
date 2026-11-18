using System.Diagnostics.CodeAnalysis;
using ArticleMonitor.Domain.Sources;

namespace ArticleMonitor.Application.Sources;

/// <summary>Source modules registered in the application, indexed by key.</summary>
public sealed class SourceModuleCatalog
{
    private readonly Dictionary<SourceKey, ISourceModule> _modules = [];

    public SourceModuleCatalog(IEnumerable<ISourceModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        foreach (var module in modules)
        {
            if (!_modules.TryAdd(module.Definition.Key, module))
            {
                throw new InvalidOperationException($"Two source modules use the key '{module.Definition.Key}'.");
            }
        }
    }

    public IReadOnlyCollection<SourceKey> Keys => _modules.Keys;

    public bool TryGet(SourceKey key, [NotNullWhen(true)] out ISourceModule? module) =>
        _modules.TryGetValue(key, out module);
}
