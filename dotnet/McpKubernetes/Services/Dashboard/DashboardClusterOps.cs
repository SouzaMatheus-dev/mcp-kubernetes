using System.Text.Json;

namespace McpKubernetes.Services.Dashboard;

public sealed class DashboardClusterOps : IClusterOps, IDisposable
{
    private readonly McpConfig _config;
    private readonly KubernetesReader _reader;
    private readonly Lazy<DashboardApiClient> _client;

    public DashboardClusterOps(McpConfig config, KubernetesReader reader)
    {
        _config = config;
        _reader = reader;
        _client = new Lazy<DashboardApiClient>(Connect);
    }

    public string ApiKind => "dashboard";

    public Task<string> DescribeContextAsync()
    {
        var client = _client.Value;
        return Task.FromResult(KubernetesReader.JoinLines(
            "api=dashboard",
            $"host={client.Server}",
            $"namespace_padrao={_config.DefaultNamespace}",
            "usuario=(token)",
            "token=configurado (valor omitido)",
            "modo=somente_leitura",
            "dica=rotas no padrão do Kubernetes Dashboard (singular), não a API nativa do control plane"));
    }

    public async Task<string> DiagnoseAsync(string? @namespace)
    {
        var ns = string.IsNullOrWhiteSpace(@namespace) ? _reader.ResolveNamespace(null) : @namespace;
        var lines = new List<string>
        {
            "=== Diagnóstico Dashboard (somente leitura) ===",
            await DescribeContextAsync().ConfigureAwait(false),
            "",
            "=== Probes ===",
            await ProbeAsync("GET /api/v1/namespace", () => _client.Value.GetJsonAsync("api/v1/namespace")).ConfigureAwait(false),
            await ProbeAsync($"GET /api/v1/pod/{ns}", () => _client.Value.GetJsonAsync($"api/v1/pod/{Uri.EscapeDataString(ns)}?itemsPerPage=1")).ConfigureAwait(false),
            await ProbeAsync($"GET /api/v1/deployment/{ns}", () => _client.Value.GetJsonAsync($"api/v1/deployment/{Uri.EscapeDataString(ns)}?itemsPerPage=1")).ConfigureAwait(false),
            await ProbeAsync($"GET /api/v1/event/{ns}", () => _client.Value.GetJsonAsync($"api/v1/event/{Uri.EscapeDataString(ns)}?itemsPerPage=1")).ConfigureAwait(false),
            "",
            "Como ler: ok = o portal Dashboard aceitou o token. 401 = token expirado (renove no portal, não no pacote). 403 = SA sem leitura. 404 = host/path errado.",
        };
        return string.Join(Environment.NewLine, lines);
    }

    public async Task<string> ListNamespacesAsync()
    {
        var root = await _client.Value.GetJsonAsync("api/v1/namespace").ConfigureAwait(false);
        var rows = DashboardJson.Array(root, "namespaces", "namespace").Select(item => (IReadOnlyList<string>)
        [
            DashboardJson.ObjectName(item),
            DashboardJson.FirstText(item, ["phase"], ["status", "phase"]),
            KubernetesReader.Age(DashboardJson.Timestamp(item, "objectMeta", "creationTimestamp")),
        ]);
        return KubernetesReader.Table(["NAMESPACE", "STATUS", "AGE"], rows);
    }

    public async Task<string> ListPodsAsync(string @namespace, string? labelSelector)
    {
        var items = await ListAsync("pod", "pods", @namespace).ConfigureAwait(false);
        var rows = items
            .Where(p => DashboardJson.MatchesLabels(p, labelSelector))
            .Take(_config.MaxRows)
            .Select(pod => (IReadOnlyList<string>)
            [
                DashboardJson.ObjectName(pod),
                Ready(pod),
                DashboardJson.FirstText(pod, ["podStatus", "status"], ["podStatus", "podPhase"], ["status", "phase"]),
                DashboardJson.FirstText(pod, ["restartCount"], ["podStatus", "restartCount"]),
                KubernetesReader.Age(DashboardJson.Timestamp(pod, "objectMeta", "creationTimestamp")),
                DashboardJson.FirstText(pod, ["nodeName"], ["objectMeta", "nodeName"]),
            ]);
        return KubernetesReader.Table(["NAME", "READY", "STATUS", "RESTARTS", "AGE", "NODE"], rows);
    }

    public async Task<string> GetPodAsync(string name, string @namespace)
    {
        var pod = await GetAsync("pod", @namespace, name).ConfigureAwait(false);
        return DashboardJson.PrettyRedacted(pod, "Pod");
    }

    public async Task<string> LogsPodAsync(
        string name,
        string @namespace,
        string? container,
        int tail,
        int sinceSeconds,
        bool previous)
    {
        var containerName = container;
        if (string.IsNullOrWhiteSpace(containerName))
        {
            var pod = await GetAsync("pod", @namespace, name).ConfigureAwait(false);
            containerName = DashboardJson.FirstContainer(pod);
            if (containerName == "-")
            {
                return "Bloqueado: informe o container (o Dashboard exige /log/{ns}/{pod}/{container}).";
            }
        }

        var url =
            $"api/v1/log/{Uri.EscapeDataString(@namespace)}/{Uri.EscapeDataString(name)}/{Uri.EscapeDataString(containerName)}" +
            $"?previous={previous.ToString().ToLowerInvariant()}&referenceTimestamp=newest&offsetFrom={Math.Max(0, 2000000000 - tail)}&offsetTo=2000000000";
        _ = sinceSeconds;

        var raw = await _client.Value.GetRawAsync(url).ConfigureAwait(false);
        return ExtractLogs(raw);
    }

    public async Task<string> ListEventsAsync(string @namespace, string? involvedObject)
    {
        var items = await ListAsync("event", "events", @namespace).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(involvedObject))
        {
            items = items.Where(ev =>
                DashboardJson.FirstText(ev, ["involvedObject", "name"], ["objectMeta", "name"])
                    .Contains(involvedObject, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var rows = items
            .OrderByDescending(ev => DashboardJson.Timestamp(ev, "lastSeen")
                ?? DashboardJson.Timestamp(ev, "objectMeta", "creationTimestamp"))
            .Take(_config.MaxRows)
            .Select(ev => (IReadOnlyList<string>)
            [
                KubernetesReader.Age(
                    DashboardJson.Timestamp(ev, "lastSeen")
                    ?? DashboardJson.Timestamp(ev, "objectMeta", "creationTimestamp")),
                DashboardJson.FirstText(ev, ["type"]),
                DashboardJson.FirstText(ev, ["reason"]),
                DashboardJson.FirstText(ev, ["involvedObject", "kind"]),
                DashboardJson.FirstText(ev, ["involvedObject", "name"]),
                DashboardJson.FirstText(ev, ["message"]),
            ]);
        return KubernetesReader.Table(["AGE", "TYPE", "REASON", "KIND", "OBJECT", "MESSAGE"], rows);
    }

    public async Task<string> ListDeploymentsAsync(string @namespace, string? labelSelector)
    {
        var items = await ListAsync("deployment", "deployments", @namespace).ConfigureAwait(false);
        var rows = items
            .Where(d => DashboardJson.MatchesLabels(d, labelSelector))
            .Take(_config.MaxRows)
            .Select(d => (IReadOnlyList<string>)
            [
                DashboardJson.ObjectName(d),
                $"{DashboardJson.Text(d, "pods", "running")}/{DashboardJson.FirstText(d, ["pods", "desired"], ["pods", "wanted"])}",
                DashboardJson.FirstText(d, ["pods", "failed"], ["pods", "pending"]),
                KubernetesReader.Age(DashboardJson.Timestamp(d, "objectMeta", "creationTimestamp")),
            ]);
        return KubernetesReader.Table(["NAME", "READY", "UNAVAILABLE", "AGE"], rows);
    }

    public async Task<string> ListReplicaSetsAsync(string @namespace, string? labelSelector)
    {
        var items = await ListAsync("replicaset", "replicaSets", "replicasets", @namespace).ConfigureAwait(false);
        var rows = items
            .Where(rs => DashboardJson.MatchesLabels(rs, labelSelector))
            .Take(_config.MaxRows)
            .Select(rs => (IReadOnlyList<string>)
            [
                DashboardJson.ObjectName(rs),
                $"{DashboardJson.Text(rs, "pods", "running")}/{DashboardJson.FirstText(rs, ["pods", "desired"], ["pods", "wanted"])}",
                KubernetesReader.Age(DashboardJson.Timestamp(rs, "objectMeta", "creationTimestamp")),
            ]);
        return KubernetesReader.Table(["NAME", "READY", "AGE"], rows);
    }

    public async Task<string> ListServicesAsync(string @namespace)
    {
        var items = await ListAsync("service", "services", @namespace).ConfigureAwait(false);
        var rows = items.Take(_config.MaxRows).Select(svc => (IReadOnlyList<string>)
        [
            DashboardJson.ObjectName(svc),
            DashboardJson.FirstText(svc, ["type"], ["internalEndpoint", "type"]),
            DashboardJson.FirstText(svc, ["clusterIP"], ["internalEndpoint", "host"]),
            Ports(svc),
        ]);
        return KubernetesReader.Table(["NAME", "TYPE", "CLUSTER-IP", "PORTS"], rows);
    }

    public async Task<string> GetResourceAsync(string kind, string name, string @namespace)
    {
        var (route, _) = MapKind(kind);
        if (route is null)
        {
            return $"Kind '{kind}' não suportado no Dashboard. Use Pod, Deployment, ReplicaSet, Service, ConfigMap, HPA, StatefulSet, DaemonSet, Job, Ingress ou Secret (metadados).";
        }

        if (kind.Equals("Namespace", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("namespaces", StringComparison.OrdinalIgnoreCase))
        {
            var ns = await GetAsync("namespace", name, null).ConfigureAwait(false);
            return DashboardJson.PrettyRedacted(ns, kind);
        }

        var resource = await GetAsync(route, @namespace, name).ConfigureAwait(false);
        return DashboardJson.PrettyRedacted(resource, kind);
    }

    public async Task<string> ListResourcesAsync(string kind, string @namespace, string? labelSelector)
    {
        return kind.ToLowerInvariant() switch
        {
            "pod" or "pods" => await ListPodsAsync(@namespace, labelSelector).ConfigureAwait(false),
            "deployment" or "deployments" => await ListDeploymentsAsync(@namespace, labelSelector).ConfigureAwait(false),
            "replicaset" or "replicasets" => await ListReplicaSetsAsync(@namespace, labelSelector).ConfigureAwait(false),
            "service" or "services" => await ListServicesAsync(@namespace).ConfigureAwait(false),
            "event" or "events" => await ListEventsAsync(@namespace, null).ConfigureAwait(false),
            "namespace" or "namespaces" => await ListNamespacesAsync().ConfigureAwait(false),
            _ => await ListGenericAsync(kind, @namespace, labelSelector).ConfigureAwait(false),
        };
    }

    public async Task<string> UsoRecursosPodsAsync(string @namespace, string? name)
    {
        var items = await ListAsync("pod", "pods", @namespace).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(name))
        {
            items = items.Where(p => DashboardJson.ObjectName(p).Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var rows = items.Take(_config.MaxRows).Select(pod => (IReadOnlyList<string>)
        [
            DashboardJson.ObjectName(pod),
            DashboardJson.FirstContainer(pod),
            DashboardJson.FirstText(pod, ["metrics", "cpuUsage"], ["metrics", "cpu"]),
            DashboardJson.FirstText(pod, ["metrics", "memoryUsage"], ["metrics", "memory"]),
        ]);

        var table = KubernetesReader.Table(["POD", "CONTAINER", "CPU", "MEMORY"], rows);
        return table.Contains("(0 linha", StringComparison.Ordinal)
            ? "Métricas indisponíveis neste Dashboard (sem metrics-server ou o portal não as expõe). Use events/logs."
            : table;
    }

    public void Dispose()
    {
        if (_client.IsValueCreated)
        {
            _client.Value.Dispose();
        }
    }

    private async Task<string> ListGenericAsync(string kind, string @namespace, string? labelSelector)
    {
        var (route, listKey) = MapKind(kind);
        if (route is null)
        {
            return $"Kind '{kind}' não suportado. Use Pod, Deployment, ReplicaSet, Service, ConfigMap, HPA, StatefulSet, DaemonSet, Event, Ingress ou Secret (metadados).";
        }

        var items = await ListAsync(route, listKey ?? route + "s", @namespace).ConfigureAwait(false);
        var rows = items
            .Where(i => DashboardJson.MatchesLabels(i, labelSelector))
            .Take(_config.MaxRows)
            .Select(item => (IReadOnlyList<string>)
            [
                DashboardJson.ObjectName(item),
                KubernetesReader.IsSecretKind(kind)
                    ? DashboardJson.FirstText(item, ["type"], ["secretType"])
                    : DashboardJson.FirstText(item, ["type"], ["status"]),
                KubernetesReader.Age(DashboardJson.Timestamp(item, "objectMeta", "creationTimestamp")),
            ]);
        return KubernetesReader.Table(["NAME", "TYPE", "AGE"], rows);
    }

    private async Task<IReadOnlyList<JsonElement>> ListAsync(string route, string listKey, string @namespace) =>
        await ListAsync(route, listKey, listKey, @namespace).ConfigureAwait(false);

    private async Task<IReadOnlyList<JsonElement>> ListAsync(string route, string listKey, string altKey, string @namespace)
    {
        var collected = new List<JsonElement>();
        var page = 1;
        var pageSize = Math.Clamp(_config.MaxRows, 1, 100);
        var escaped = Uri.EscapeDataString(@namespace);

        while (collected.Count < _config.MaxRows)
        {
            var url = route.Equals("namespace", StringComparison.OrdinalIgnoreCase)
                ? $"api/v1/namespace?itemsPerPage={pageSize}&page={page}"
                : $"api/v1/{route}/{escaped}?itemsPerPage={pageSize}&page={page}";

            var root = await _client.Value.GetJsonAsync(url).ConfigureAwait(false);
            var batch = DashboardJson.Array(root, listKey, altKey, route);
            if (batch.Count == 0)
            {
                break;
            }

            collected.AddRange(batch);
            var total = DashboardJson.Text(root, "listMeta", "totalItems");
            if (!int.TryParse(total, out var totalItems) || collected.Count >= totalItems)
            {
                break;
            }

            page++;
        }

        return collected;
    }

    private Task<JsonElement> GetAsync(string route, string @namespace, string? name)
    {
        var url = name is null
            ? $"api/v1/{route}/{Uri.EscapeDataString(@namespace)}"
            : route.Equals("namespace", StringComparison.OrdinalIgnoreCase)
                ? $"api/v1/namespace/{Uri.EscapeDataString(@namespace)}"
                : $"api/v1/{route}/{Uri.EscapeDataString(@namespace)}/{Uri.EscapeDataString(name)}";
        return _client.Value.GetJsonAsync(url);
    }

    private static (string? Route, string? ListKey) MapKind(string kind) =>
        kind.ToLowerInvariant() switch
        {
            "pod" or "pods" => ("pod", "pods"),
            "deployment" or "deployments" => ("deployment", "deployments"),
            "replicaset" or "replicasets" => ("replicaset", "replicaSets"),
            "service" or "services" => ("service", "services"),
            "event" or "events" => ("event", "events"),
            "configmap" or "configmaps" => ("configmap", "configMaps"),
            "secret" or "secrets" => ("secret", "secrets"),
            "statefulset" or "statefulsets" => ("statefulset", "statefulSets"),
            "daemonset" or "daemonsets" => ("daemonset", "daemonSets"),
            "job" or "jobs" => ("job", "jobs"),
            "ingress" or "ingresses" => ("ingress", "ingresses"),
            "hpa" or "horizontalpodautoscaler" or "horizontalpodautoscalers" =>
                ("horizontalpodautoscaler", "horizontalpodautoscalers"),
            "namespace" or "namespaces" => ("namespace", "namespaces"),
            _ => (null, null),
        };

    private static string Ready(JsonElement pod)
    {
        var ready = DashboardJson.FirstText(pod, ["podStatus", "ready"], ["ready"]);
        if (ready != "-")
        {
            return ready;
        }

        return DashboardJson.FirstText(pod, ["podStatus", "status"]) == "Running" ? "1/1" : "0/1";
    }

    private static string Ports(JsonElement service)
    {
        if (DashboardJson.TryGet(service, "internalEndpoint", out var endpoint) &&
            DashboardJson.TryGet(endpoint, "ports", out var ports) &&
            ports.ValueKind == JsonValueKind.Array)
        {
            return string.Join(",", ports.EnumerateArray().Select(p =>
                $"{DashboardJson.FirstText(p, ["port"], ["Port"])}/{DashboardJson.FirstText(p, ["protocol"], ["Protocol"])}"));
        }

        return "-";
    }

    private static string ExtractLogs(string raw)
    {
        try
        {
            var root = DashboardJson.Parse(raw);
            if (DashboardJson.TryGet(root, "logs", out var logs))
            {
                if (logs.ValueKind == JsonValueKind.String)
                {
                    return string.IsNullOrWhiteSpace(logs.GetString()) ? "(log vazio)" : logs.GetString()!.TrimEnd();
                }

                if (logs.ValueKind == JsonValueKind.Array)
                {
                    var text = string.Join(Environment.NewLine, logs.EnumerateArray().Select(l => l.GetString()).Where(s => s is not null));
                    return string.IsNullOrWhiteSpace(text) ? "(log vazio)" : text;
                }
            }
        }
        catch (JsonException)
        {
            // corpo em texto puro
        }

        return string.IsNullOrWhiteSpace(raw) ? "(log vazio)" : raw.TrimEnd();
    }

    private async Task<string> ProbeAsync(string label, Func<Task<JsonElement>> action)
    {
        try
        {
            await action().ConfigureAwait(false);
            return $"[ok] {label}";
        }
        catch (Exception ex)
        {
            return $"[falha] {label}: {ex.Message}";
        }
    }

    private DashboardApiClient Connect()
    {
        var server = McpConfig.NormalizeServer(_config.Server);
        var token = _config.Token;
        var skipTls = _config.SkipTlsVerify;

        if (_reader.TryGetKubeConfig(out var cfg) && cfg is not null)
        {
            server ??= McpConfig.NormalizeServer(cfg.Host);
            token ??= string.IsNullOrWhiteSpace(cfg.AccessToken) ? null : cfg.AccessToken;
            skipTls = skipTls || cfg.SkipTlsVerify;
        }

        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                "Dashboard sem credencial no ambiente. Defina K8S_SERVER e K8S_TOKEN no env do cliente MCP " +
                "(settings.local.json). O pacote McpKubernetes não embute token, senha nem URL interna.");
        }

        return DashboardApiClient.Create(server, token, skipTls);
    }
}
