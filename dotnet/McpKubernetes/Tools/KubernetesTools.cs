using System.ComponentModel;
using ModelContextProtocol.Server;
using McpKubernetes.Services;

namespace McpKubernetes.Tools;

[McpServerToolType]
public sealed class KubernetesTools(McpConfig config, KubernetesReader reader, IClusterOps cluster)
{
    [McpServerTool, Description("Mostra o contexto atual (API nativa/Rancher ou Dashboard), host e modo somente leitura.")]
    public Task<string> ContextoAtual() => reader.SafeAsync(cluster.DescribeContextAsync);

    [McpServerTool, Description("Diagnostica acesso ao cluster (API nativa ou Dashboard). Use quando as outras tools derem 404.")]
    public Task<string> DiagnosticarAcesso(string @namespace = "") =>
        reader.SafeAsync(() => cluster.DiagnoseAsync(@namespace));

    [McpServerTool, Description("Lista namespaces visíveis para o token/kubeconfig conectado.")]
    public Task<string> ListarNamespaces() =>
        reader.SafeAsync(cluster.ListNamespacesAsync);

    [McpServerTool, Description("Lista pods de um namespace. Prefira informar o namespace.")]
    public Task<string> ListarPods(string @namespace = "", string labelSelector = "") =>
        reader.SafeAsync(async () =>
        {
            var ns = RequireNamespace(@namespace);
            return ns.StartsWith("Bloqueado:", StringComparison.Ordinal)
                ? ns
                : await cluster.ListPodsAsync(ns, EmptyToNull(labelSelector)).ConfigureAwait(false);
        });

    [McpServerTool, Description("Obtém um pod (status, probes, restarts, imagem, limites). Não retorna Secret.")]
    public Task<string> ObterPod(string name, string @namespace = "") =>
        reader.SafeAsync(async () =>
        {
            var ns = RequireNamespace(@namespace);
            return ns.StartsWith("Bloqueado:", StringComparison.Ordinal)
                ? ns
                : await cluster.GetPodAsync(name, ns).ConfigureAwait(false);
        });

    [McpServerTool, Description("Lê logs de um pod com recorte (tail/since). Use previous=true se o container reiniciou.")]
    public Task<string> LogsPod(
        string name,
        string @namespace = "",
        string container = "",
        int tail = 100,
        int sinceSeconds = 0,
        bool previous = false) =>
        reader.SafeAsync(async () =>
        {
            var ns = RequireNamespace(@namespace);
            if (ns.StartsWith("Bloqueado:", StringComparison.Ordinal))
            {
                return ns;
            }

            if (tail < 1 || tail > config.MaxLogLines)
            {
                return $"Bloqueado: tail deve estar entre 1 e {config.MaxLogLines}.";
            }

            return await cluster.LogsPodAsync(
                name,
                ns,
                EmptyToNull(container),
                tail,
                sinceSeconds,
                previous).ConfigureAwait(false);
        });

    [McpServerTool, Description("Lista events de um namespace. Opcionalmente filtra pelo nome do objeto envolvido.")]
    public Task<string> ListarEventos(string @namespace = "", string involvedObject = "") =>
        reader.SafeAsync(async () =>
        {
            var ns = RequireNamespace(@namespace);
            return ns.StartsWith("Bloqueado:", StringComparison.Ordinal)
                ? ns
                : await cluster.ListEventsAsync(ns, EmptyToNull(involvedObject)).ConfigureAwait(false);
        });

    [McpServerTool, Description("Lista Deployments de um namespace.")]
    public Task<string> ListarDeployments(string @namespace = "", string labelSelector = "") =>
        reader.SafeAsync(async () =>
        {
            var ns = RequireNamespace(@namespace);
            return ns.StartsWith("Bloqueado:", StringComparison.Ordinal)
                ? ns
                : await cluster.ListDeploymentsAsync(ns, EmptyToNull(labelSelector)).ConfigureAwait(false);
        });

    [McpServerTool, Description("Obtém um Deployment (replicas, conditions, imagem, probes).")]
    public Task<string> ObterDeployment(string name, string @namespace = "") =>
        GetTyped("Deployment", name, @namespace);

    [McpServerTool, Description("Lista ReplicaSets de um namespace.")]
    public Task<string> ListarReplicaSets(string @namespace = "", string labelSelector = "") =>
        reader.SafeAsync(async () =>
        {
            var ns = RequireNamespace(@namespace);
            return ns.StartsWith("Bloqueado:", StringComparison.Ordinal)
                ? ns
                : await cluster.ListReplicaSetsAsync(ns, EmptyToNull(labelSelector)).ConfigureAwait(false);
        });

    [McpServerTool, Description("Lista Services de um namespace.")]
    public Task<string> ListarServices(string @namespace = "") =>
        reader.SafeAsync(async () =>
        {
            var ns = RequireNamespace(@namespace);
            return ns.StartsWith("Bloqueado:", StringComparison.Ordinal)
                ? ns
                : await cluster.ListServicesAsync(ns).ConfigureAwait(false);
        });

    [McpServerTool, Description("Obtém um Service.")]
    public Task<string> ObterService(string name, string @namespace = "") =>
        GetTyped("Service", name, @namespace);

    [McpServerTool, Description("Obtém um recurso por kind/name. Secret só retorna metadados.")]
    public Task<string> ObterRecurso(string kind, string name, string @namespace = "") =>
        GetTyped(kind, name, @namespace);

    [McpServerTool, Description("Lista recursos por kind em um namespace.")]
    public Task<string> ListarRecursos(string kind, string @namespace = "", string labelSelector = "") =>
        kind.ToLowerInvariant() switch
        {
            "namespace" or "namespaces" => ListarNamespaces(),
            _ => reader.SafeAsync(async () =>
            {
                var ns = RequireNamespace(@namespace);
                return ns.StartsWith("Bloqueado:", StringComparison.Ordinal)
                    ? ns
                    : await cluster.ListResourcesAsync(kind, ns, EmptyToNull(labelSelector)).ConfigureAwait(false);
            }),
        };

    [McpServerTool, Description("Uso de CPU/memória dos pods (metrics-server ou métricas do Dashboard, se existirem).")]
    public Task<string> UsoRecursosPods(string @namespace = "", string name = "") =>
        reader.SafeAsync(async () =>
        {
            var ns = RequireNamespace(@namespace);
            return ns.StartsWith("Bloqueado:", StringComparison.Ordinal)
                ? ns
                : await cluster.UsoRecursosPodsAsync(ns, EmptyToNull(name)).ConfigureAwait(false);
        });

    private Task<string> GetTyped(string kind, string name, string @namespace) =>
        reader.SafeAsync(async () =>
        {
            var ns = kind.Equals("Namespace", StringComparison.OrdinalIgnoreCase)
                ? name
                : RequireNamespace(@namespace);
            return ns.StartsWith("Bloqueado:", StringComparison.Ordinal)
                ? ns
                : await cluster.GetResourceAsync(kind, name, ns).ConfigureAwait(false);
        });

    private string RequireNamespace(string requested)
    {
        var ns = reader.ResolveNamespace(requested);
        return string.IsNullOrWhiteSpace(ns)
            ? "Bloqueado: informe namespace (ou defina K8S_NAMESPACE)."
            : ns;
    }

    private static string? EmptyToNull(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
