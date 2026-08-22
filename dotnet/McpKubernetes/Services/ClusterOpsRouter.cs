using k8s.Autorest;

namespace McpKubernetes.Services;

public sealed class ClusterOpsRouter(
    McpConfig config,
    KubernetesReader reader,
    NativeClusterOps native,
    Dashboard.DashboardClusterOps dashboard) : IClusterOps
{
    private IClusterOps? _current;
    private bool _fellBackToDashboard;

    public string ApiKind => _current?.ApiKind ?? "auto";

    public Task<string> DescribeContextAsync() =>
        InvokeAsync(ops => ops.DescribeContextAsync());

    public Task<string> DiagnoseAsync(string? @namespace) =>
        InvokeAsync(ops => ops.DiagnoseAsync(@namespace));

    public Task<string> ListNamespacesAsync() =>
        InvokeAsync(ops => ops.ListNamespacesAsync());

    public Task<string> ListPodsAsync(string @namespace, string? labelSelector) =>
        InvokeAsync(ops => ops.ListPodsAsync(@namespace, labelSelector));

    public Task<string> GetPodAsync(string name, string @namespace) =>
        InvokeAsync(ops => ops.GetPodAsync(name, @namespace));

    public Task<string> LogsPodAsync(
        string name,
        string @namespace,
        string? container,
        int tail,
        int sinceSeconds,
        bool previous) =>
        InvokeAsync(ops => ops.LogsPodAsync(name, @namespace, container, tail, sinceSeconds, previous));

    public Task<string> ListEventsAsync(string @namespace, string? involvedObject) =>
        InvokeAsync(ops => ops.ListEventsAsync(@namespace, involvedObject));

    public Task<string> ListDeploymentsAsync(string @namespace, string? labelSelector) =>
        InvokeAsync(ops => ops.ListDeploymentsAsync(@namespace, labelSelector));

    public Task<string> ListReplicaSetsAsync(string @namespace, string? labelSelector) =>
        InvokeAsync(ops => ops.ListReplicaSetsAsync(@namespace, labelSelector));

    public Task<string> ListServicesAsync(string @namespace) =>
        InvokeAsync(ops => ops.ListServicesAsync(@namespace));

    public Task<string> GetResourceAsync(string kind, string name, string @namespace) =>
        InvokeAsync(ops => ops.GetResourceAsync(kind, name, @namespace));

    public Task<string> ListResourcesAsync(string kind, string @namespace, string? labelSelector) =>
        InvokeAsync(ops => ops.ListResourcesAsync(kind, @namespace, labelSelector));

    public Task<string> UsoRecursosPodsAsync(string @namespace, string? name) =>
        InvokeAsync(ops => ops.UsoRecursosPodsAsync(@namespace, name));

    private async Task<string> InvokeAsync(Func<IClusterOps, Task<string>> action)
    {
        var ops = ResolvePreferred();
        try
        {
            var result = await action(ops).ConfigureAwait(false);
            _current = ops;
            return result;
        }
        catch (Exception ex) when (ShouldFallbackToDashboard(ops, ex))
        {
            _fellBackToDashboard = true;
            _current = dashboard;
            return await action(dashboard).ConfigureAwait(false);
        }
    }

    private IClusterOps ResolvePreferred()
    {
        if (_current is not null)
        {
            return _current;
        }

        return config.ApiMode switch
        {
            ApiMode.Dashboard => dashboard,
            ApiMode.Native => native,
            _ => InferFromHost(),
        };
    }

    private IClusterOps InferFromHost()
    {
        var host = McpConfig.NormalizeServer(config.Server);
        if (reader.TryGetKubeConfig(out var cfg) && cfg is not null)
        {
            host ??= McpConfig.NormalizeServer(cfg.Host);
        }

        if (McpConfig.LooksLikeDashboardHost(host))
        {
            return dashboard;
        }

        if (McpConfig.LooksLikeRancherHost(host))
        {
            return native;
        }

        if (!string.IsNullOrWhiteSpace(config.Server) && config.HasToken)
        {
            return dashboard;
        }

        return native;
    }

    private bool ShouldFallbackToDashboard(IClusterOps attempted, Exception ex)
    {
        if (_fellBackToDashboard || attempted.ApiKind == "dashboard" || config.ApiMode == ApiMode.Native)
        {
            return false;
        }

        if (ex is not HttpOperationException http)
        {
            return false;
        }

        var body = http.Response?.Content ?? "";
        return body.Contains("404 page not found", StringComparison.OrdinalIgnoreCase)
               || body.Contains("<html", StringComparison.OrdinalIgnoreCase);
    }
}
