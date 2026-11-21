using SpriteLoom.Abstractions;
using SpriteLoom.Foundation;
using SpriteLoom.Pipeline.Caching;

namespace SpriteLoom.Pipeline;

public sealed class PipelineRunner
{
    public Task<IReadOnlyDictionary<string, Artifact>> RunAsync(
        IReadOnlyList<PassNode> nodes,
        string baseDirectory,
        IDiagnosticsSink diagnostics,
        CancellationToken cancellation = default) =>
        RunAsync(nodes, baseDirectory, diagnostics, new PipelineRunOptions(), cancellation);

    public async Task<IReadOnlyDictionary<string, Artifact>> RunAsync(
        IReadOnlyList<PassNode> nodes,
        string baseDirectory,
        IDiagnosticsSink diagnostics,
        PipelineRunOptions runOptions,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(baseDirectory);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(runOptions);

        var byId = new Dictionary<string, PassNode>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            if (!byId.TryAdd(node.Id, node))
            {
                throw new InvalidOperationException($"Duplicate pass id '{node.Id}'.");
            }
        }

        var ordered = TopologicalOrder(nodes, byId);
        var artifacts = new Dictionary<string, Artifact>(StringComparer.Ordinal);

        foreach (var node in ordered)
        {
            cancellation.ThrowIfCancellationRequested();

            var inputs = ResolveInputs(node, artifacts);
            var context = new PassContext
            {
                BaseDirectory = baseDirectory,
                Parameters = node.Parameters,
                Diagnostics = diagnostics,
                Cancellation = cancellation
            };

            var outputs = default(IReadOnlyDictionary<string, Artifact>);
            var descriptor = node.Pass.Descriptor;
            var useCache = runOptions.Cache is not null &&
                           descriptor.Cacheable &&
                           descriptor.Determinism == PassDeterminism.Deterministic;
            string? cacheKey = null;
            var cacheHit = false;

            if (useCache)
            {
                cacheKey = ArtifactKey.Compute(node, inputs);

                if (!runOptions.Refresh &&
                    runOptions.Cache!.TryLoad(node.Id, cacheKey, out var cached))
                {
                    outputs = cached;
                    cacheHit = true;
                    if (runOptions.Stats is { } stats)
                    {
                        stats.Hits++;
                    }
                    diagnostics.Report(Diagnostic.Info(
                        "SL3100",
                        $"Pass '{node.Id}' restored from cache."));
                }
            }

            outputs ??= await node.Pass.ExecuteAsync(context, inputs);
            ValidateOutputs(node, outputs);

            if (useCache && !cacheHit && cacheKey is not null)
            {
                if (runOptions.Stats is { } stats)
                {
                    stats.Misses++;
                }
                runOptions.Cache!.Store(node.Id, cacheKey, outputs);
            }

            foreach (var (port, artifact) in outputs)
            {
                artifacts[$"{node.Id}.{port}"] = artifact;
            }
        }

        return artifacts;
    }

    private static IReadOnlyDictionary<string, Artifact> ResolveInputs(
        PassNode node,
        IReadOnlyDictionary<string, Artifact> artifacts)
    {
        var inputs = new Dictionary<string, Artifact>(StringComparer.Ordinal);

        foreach (var port in node.Pass.Descriptor.Inputs)
        {
            if (!node.Inputs.TryGetValue(port.Name, out var reference))
            {
                if (port.Required)
                {
                    throw new InvalidOperationException(
                        $"Pass '{node.Id}' is missing required input '{port.Name}'.");
                }

                continue;
            }

            if (!artifacts.TryGetValue(reference, out var artifact))
            {
                throw new InvalidOperationException(
                    $"Pass '{node.Id}' input '{port.Name}' references unknown artifact '{reference}'.");
            }

            if (artifact.Kind != port.Kind)
            {
                throw new InvalidOperationException(
                    $"Pass '{node.Id}' input '{port.Name}' expects {port.Kind} but got {artifact.Kind}.");
            }

            inputs[port.Name] = artifact;
        }

        return inputs;
    }

    private static void ValidateOutputs(
        PassNode node,
        IReadOnlyDictionary<string, Artifact> outputs)
    {
        foreach (var port in node.Pass.Descriptor.Outputs)
        {
            if (!outputs.TryGetValue(port.Name, out var artifact))
            {
                throw new InvalidOperationException(
                    $"Pass '{node.Id}' did not produce declared output '{port.Name}'.");
            }

            if (artifact.Kind != port.Kind)
            {
                throw new InvalidOperationException(
                    $"Pass '{node.Id}' output '{port.Name}' expects {port.Kind} but got {artifact.Kind}.");
            }
        }
    }

    private static IReadOnlyList<PassNode> TopologicalOrder(
        IReadOnlyList<PassNode> nodes,
        IReadOnlyDictionary<string, PassNode> byId)
    {
        var ordered = new List<PassNode>(nodes.Count);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        void Visit(PassNode node)
        {
            if (visited.Contains(node.Id))
            {
                return;
            }

            if (!visiting.Add(node.Id))
            {
                throw new InvalidOperationException($"Pipeline contains a cycle at pass '{node.Id}'.");
            }

            foreach (var reference in node.Inputs.Values)
            {
                var separator = reference.LastIndexOf('.');
                if (separator <= 0)
                {
                    throw new InvalidOperationException(
                        $"Pass '{node.Id}' input reference '{reference}' must use 'pass.port'.");
                }

                var dependencyId = reference[..separator];
                if (!byId.TryGetValue(dependencyId, out var dependency))
                {
                    throw new InvalidOperationException(
                        $"Pass '{node.Id}' references unknown pass '{dependencyId}'.");
                }

                Visit(dependency);
            }

            visiting.Remove(node.Id);
            visited.Add(node.Id);
            ordered.Add(node);
        }

        foreach (var node in nodes)
        {
            Visit(node);
        }

        return ordered;
    }
}
