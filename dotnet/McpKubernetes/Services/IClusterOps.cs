namespace McpKubernetes.Services;

public interface IClusterOps
{
    string ApiKind { get; }

    Task<string> DescribeContextAsync();

    Task<string> DiagnoseAsync(string? @namespace);

    Task<string> ListNamespacesAsync();

    Task<string> ListPodsAsync(string @namespace, string? labelSelector);

    Task<string> GetPodAsync(string name, string @namespace);

    Task<string> LogsPodAsync(
        string name,
        string @namespace,
        string? container,
        int tail,
        int sinceSeconds,
        bool previous);

    Task<string> ListEventsAsync(string @namespace, string? involvedObject);

    Task<string> ListDeploymentsAsync(string @namespace, string? labelSelector);

    Task<string> ListReplicaSetsAsync(string @namespace, string? labelSelector);

    Task<string> ListServicesAsync(string @namespace);

    Task<string> GetResourceAsync(string kind, string name, string @namespace);

    Task<string> ListResourcesAsync(string kind, string @namespace, string? labelSelector);

    Task<string> UsoRecursosPodsAsync(string @namespace, string? name);
}
