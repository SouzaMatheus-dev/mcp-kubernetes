using System.ComponentModel;
using k8s;
using k8s.Models;
using ModelContextProtocol.Server;
using McpKubernetes.Services;

namespace McpKubernetes.Tools;

[McpServerToolType]
public sealed class KubernetesTools(McpConfig config, KubernetesReader reader)
{
    [McpServerTool, Description("Mostra o contexto kube atual, host da API e modo somente leitura.")]
    public string ContextoAtual() => reader.DescribeContext();

    [McpServerTool, Description("Lista namespaces visíveis para o kubeconfig conectado.")]
    public Task<string> ListarNamespaces() =>
        reader.SafeAsync(async () =>
        {
            var list = await reader.Client.CoreV1.ListNamespaceAsync().ConfigureAwait(false);
            var rows = list.Items.Select(ns => (IReadOnlyList<string>)
            [
                ns.Name(),
                ns.Status?.Phase ?? "-",
                KubernetesReader.Age(ns.CreationTimestamp()),
            ]);
            return KubernetesReader.Table(["NAMESPACE", "STATUS", "AGE"], rows);
        });

    [McpServerTool, Description("Lista pods de um namespace. Prefira informar o namespace.")]
    public Task<string> ListarPods(string @namespace = "", string labelSelector = "") =>
        reader.SafeAsync(async () =>
        {
            var ns = RequireNamespace(@namespace);
            if (ns.StartsWith("Bloqueado:", StringComparison.Ordinal))
            {
                return ns;
            }

            var list = await reader.Client.CoreV1.ListNamespacedPodAsync(
                ns,
                labelSelector: EmptyToNull(labelSelector)).ConfigureAwait(false);

            var rows = list.Items.Take(config.MaxRows).Select(pod => (IReadOnlyList<string>)
            [
                pod.Name(),
                Ready(pod),
                pod.Status?.Phase ?? "-",
                RestartCount(pod).ToString(),
                KubernetesReader.Age(pod.CreationTimestamp()),
                pod.Spec?.NodeName ?? "-",
            ]);
            return KubernetesReader.Table(["NAME", "READY", "STATUS", "RESTARTS", "AGE", "NODE"], rows);
        });

    [McpServerTool, Description("Obtém um pod (status, probes, restarts, imagem, limites). Não retorna Secret.")]
    public Task<string> ObterPod(string name, string @namespace = "") =>
        reader.SafeAsync(async () =>
        {
            var ns = RequireNamespace(@namespace);
            if (ns.StartsWith("Bloqueado:", StringComparison.Ordinal))
            {
                return ns;
            }

            var pod = await reader.Client.CoreV1.ReadNamespacedPodAsync(name, ns).ConfigureAwait(false);
            return KubernetesReader.YamlOrRedacted(pod, "Pod");
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

            await using var stream = await reader.Client.CoreV1.ReadNamespacedPodLogAsync(
                name,
                ns,
                container: EmptyToNull(container),
                tailLines: tail,
                sinceSeconds: sinceSeconds > 0 ? sinceSeconds : null,
                previous: previous).ConfigureAwait(false);

            using var readerStream = new StreamReader(stream);
            var text = await readerStream.ReadToEndAsync().ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(text)
                ? "(log vazio)"
                : text.TrimEnd();
        });

    [McpServerTool, Description("Lista events de um namespace. Opcionalmente filtra pelo nome do objeto envolvido.")]
    public Task<string> ListarEventos(string @namespace = "", string involvedObject = "") =>
        reader.SafeAsync(async () =>
        {
            var ns = RequireNamespace(@namespace);
            if (ns.StartsWith("Bloqueado:", StringComparison.Ordinal))
            {
                return ns;
            }

            var fieldSelector = string.IsNullOrWhiteSpace(involvedObject)
                ? null
                : $"involvedObject.name={involvedObject}";

            var list = await reader.Client.CoreV1.ListNamespacedEventAsync(
                ns,
                fieldSelector: fieldSelector).ConfigureAwait(false);

            var ordered = list.Items
                .OrderByDescending(e => e.LastTimestamp ?? e.EventTime ?? e.CreationTimestamp())
                .Take(config.MaxRows);

            var rows = ordered.Select(ev => (IReadOnlyList<string>)
            [
                KubernetesReader.Age(ev.LastTimestamp ?? ev.EventTime ?? ev.CreationTimestamp()),
                ev.Type ?? "-",
                ev.Reason ?? "-",
                ev.InvolvedObject?.Kind ?? "-",
                ev.InvolvedObject?.Name ?? "-",
                ev.Message ?? "-",
            ]);
            return KubernetesReader.Table(["AGE", "TYPE", "REASON", "KIND", "OBJECT", "MESSAGE"], rows);
        });

    [McpServerTool, Description("Lista Deployments de um namespace.")]
    public Task<string> ListarDeployments(string @namespace = "", string labelSelector = "") =>
        reader.SafeAsync(async () =>
        {
            var ns = RequireNamespace(@namespace);
            if (ns.StartsWith("Bloqueado:", StringComparison.Ordinal))
            {
                return ns;
            }

            var list = await reader.Client.AppsV1.ListNamespacedDeploymentAsync(
                ns,
                labelSelector: EmptyToNull(labelSelector)).ConfigureAwait(false);

            var rows = list.Items.Take(config.MaxRows).Select(d => (IReadOnlyList<string>)
            [
                d.Name(),
                $"{d.Status?.ReadyReplicas ?? 0}/{d.Spec?.Replicas ?? 0}",
                (d.Status?.UnavailableReplicas ?? 0).ToString(),
                KubernetesReader.Age(d.CreationTimestamp()),
            ]);
            return KubernetesReader.Table(["NAME", "READY", "UNAVAILABLE", "AGE"], rows);
        });

    [McpServerTool, Description("Obtém um Deployment (replicas, conditions, imagem, probes).")]
    public Task<string> ObterDeployment(string name, string @namespace = "") =>
        GetTyped("Deployment", name, @namespace);

    [McpServerTool, Description("Lista ReplicaSets de um namespace.")]
    public Task<string> ListarReplicaSets(string @namespace = "", string labelSelector = "") =>
        reader.SafeAsync(async () =>
        {
            var ns = RequireNamespace(@namespace);
            if (ns.StartsWith("Bloqueado:", StringComparison.Ordinal))
            {
                return ns;
            }

            var list = await reader.Client.AppsV1.ListNamespacedReplicaSetAsync(
                ns,
                labelSelector: EmptyToNull(labelSelector)).ConfigureAwait(false);

            var rows = list.Items.Take(config.MaxRows).Select(rs => (IReadOnlyList<string>)
            [
                rs.Name(),
                $"{rs.Status?.ReadyReplicas ?? 0}/{rs.Spec?.Replicas ?? 0}",
                KubernetesReader.Age(rs.CreationTimestamp()),
            ]);
            return KubernetesReader.Table(["NAME", "READY", "AGE"], rows);
        });

    [McpServerTool, Description("Lista Services de um namespace.")]
    public Task<string> ListarServices(string @namespace = "") =>
        reader.SafeAsync(async () =>
        {
            var ns = RequireNamespace(@namespace);
            if (ns.StartsWith("Bloqueado:", StringComparison.Ordinal))
            {
                return ns;
            }

            var list = await reader.Client.CoreV1.ListNamespacedServiceAsync(ns).ConfigureAwait(false);
            var rows = list.Items.Take(config.MaxRows).Select(svc => (IReadOnlyList<string>)
            [
                svc.Name(),
                svc.Spec?.Type ?? "-",
                svc.Spec?.ClusterIP ?? "-",
                string.Join(",", svc.Spec?.Ports?.Select(p => $"{p.Port}/{p.Protocol}") ?? []),
            ]);
            return KubernetesReader.Table(["NAME", "TYPE", "CLUSTER-IP", "PORTS"], rows);
        });

    [McpServerTool, Description("Obtém um Service.")]
    public Task<string> ObterService(string name, string @namespace = "") =>
        GetTyped("Service", name, @namespace);

    [McpServerTool, Description("Obtém um recurso por kind/name (Pod, Deployment, ReplicaSet, Service, ConfigMap, HPA, Ingress, StatefulSet, DaemonSet, Job). Secret só retorna metadados.")]
    public Task<string> ObterRecurso(string kind, string name, string @namespace = "") =>
        GetTyped(kind, name, @namespace);

    [McpServerTool, Description("Lista recursos por kind em um namespace.")]
    public Task<string> ListarRecursos(string kind, string @namespace = "", string labelSelector = "") =>
        kind.ToLowerInvariant() switch
        {
            "pod" or "pods" => ListarPods(@namespace, labelSelector),
            "deployment" or "deployments" => ListarDeployments(@namespace, labelSelector),
            "replicaset" or "replicasets" => ListarReplicaSets(@namespace, labelSelector),
            "service" or "services" => ListarServices(@namespace),
            "event" or "events" => ListarEventos(@namespace),
            "namespace" or "namespaces" => ListarNamespaces(),
            _ => reader.SafeAsync(async () =>
            {
                var ns = RequireNamespace(@namespace);
                if (ns.StartsWith("Bloqueado:", StringComparison.Ordinal))
                {
                    return ns;
                }

                return kind.ToLowerInvariant() switch
                {
                    "configmap" or "configmaps" => FormatConfigMaps(
                        await reader.Client.CoreV1.ListNamespacedConfigMapAsync(
                            ns,
                            labelSelector: EmptyToNull(labelSelector)).ConfigureAwait(false)),
                    "hpa" or "horizontalpodautoscaler" or "horizontalpodautoscalers" => FormatHpas(
                        await reader.Client.AutoscalingV2.ListNamespacedHorizontalPodAutoscalerAsync(
                            ns,
                            labelSelector: EmptyToNull(labelSelector)).ConfigureAwait(false)),
                    "statefulset" or "statefulsets" => FormatStatefulSets(
                        await reader.Client.AppsV1.ListNamespacedStatefulSetAsync(
                            ns,
                            labelSelector: EmptyToNull(labelSelector)).ConfigureAwait(false)),
                    "daemonset" or "daemonsets" => FormatDaemonSets(
                        await reader.Client.AppsV1.ListNamespacedDaemonSetAsync(
                            ns,
                            labelSelector: EmptyToNull(labelSelector)).ConfigureAwait(false)),
                    "secret" or "secrets" => FormatSecrets(
                        await reader.Client.CoreV1.ListNamespacedSecretAsync(
                            ns,
                            labelSelector: EmptyToNull(labelSelector)).ConfigureAwait(false)),
                    _ => $"Kind '{kind}' não suportado. Use Pod, Deployment, ReplicaSet, Service, ConfigMap, HPA, StatefulSet, DaemonSet, Event ou Secret (metadados).",
                };
            }),
        };

    [McpServerTool, Description("Uso de CPU/memória dos pods (metrics-server). Se indisponível, informa a lacuna.")]
    public Task<string> UsoRecursosPods(string @namespace = "", string name = "") =>
        reader.SafeAsync(async () =>
        {
            var ns = RequireNamespace(@namespace);
            if (ns.StartsWith("Bloqueado:", StringComparison.Ordinal))
            {
                return ns;
            }

            var metrics = await reader.Client.GetKubernetesPodsMetricsByNamespaceAsync(ns).ConfigureAwait(false);
            var items = metrics.Items.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(name))
            {
                items = items.Where(m => string.Equals(m.Metadata?.Name, name, StringComparison.OrdinalIgnoreCase));
            }

            var rows = items.Take(config.MaxRows).SelectMany(pod =>
                (pod.Containers ?? []).Select(c => (IReadOnlyList<string>)
                [
                    pod.Metadata?.Name ?? "-",
                    c.Name,
                    c.Usage?["cpu"]?.ToString() ?? "-",
                    c.Usage?["memory"]?.ToString() ?? "-",
                ]));

            return KubernetesReader.Table(["POD", "CONTAINER", "CPU", "MEMORY"], rows);
        });

    private Task<string> GetTyped(string kind, string name, string @namespace) =>
        reader.SafeAsync(async () =>
        {
            if (KubernetesReader.IsSecretKind(kind))
            {
                var secretNs = RequireNamespace(@namespace);
                if (secretNs.StartsWith("Bloqueado:", StringComparison.Ordinal))
                {
                    return secretNs;
                }

                var secret = await reader.Client.CoreV1.ReadNamespacedSecretAsync(name, secretNs).ConfigureAwait(false);
                return KubernetesReader.YamlOrRedacted(secret, "Secret");
            }

            var ns = kind.Equals("Namespace", StringComparison.OrdinalIgnoreCase)
                ? name
                : RequireNamespace(@namespace);
            if (ns.StartsWith("Bloqueado:", StringComparison.Ordinal))
            {
                return ns;
            }

            object resource = kind.ToLowerInvariant() switch
            {
                "pod" or "pods" => await reader.Client.CoreV1.ReadNamespacedPodAsync(name, ns).ConfigureAwait(false),
                "deployment" or "deployments" => await reader.Client.AppsV1.ReadNamespacedDeploymentAsync(name, ns).ConfigureAwait(false),
                "replicaset" or "replicasets" => await reader.Client.AppsV1.ReadNamespacedReplicaSetAsync(name, ns).ConfigureAwait(false),
                "service" or "services" => await reader.Client.CoreV1.ReadNamespacedServiceAsync(name, ns).ConfigureAwait(false),
                "configmap" or "configmaps" => await reader.Client.CoreV1.ReadNamespacedConfigMapAsync(name, ns).ConfigureAwait(false),
                "namespace" or "namespaces" => await reader.Client.CoreV1.ReadNamespaceAsync(name).ConfigureAwait(false),
                "hpa" or "horizontalpodautoscaler" => await reader.Client.AutoscalingV2.ReadNamespacedHorizontalPodAutoscalerAsync(name, ns).ConfigureAwait(false),
                "statefulset" or "statefulsets" => await reader.Client.AppsV1.ReadNamespacedStatefulSetAsync(name, ns).ConfigureAwait(false),
                "daemonset" or "daemonsets" => await reader.Client.AppsV1.ReadNamespacedDaemonSetAsync(name, ns).ConfigureAwait(false),
                "job" or "jobs" => await reader.Client.BatchV1.ReadNamespacedJobAsync(name, ns).ConfigureAwait(false),
                "ingress" or "ingresses" => await reader.Client.NetworkingV1.ReadNamespacedIngressAsync(name, ns).ConfigureAwait(false),
                _ => throw new InvalidOperationException($"Kind '{kind}' não suportado para get."),
            };

            return KubernetesReader.YamlOrRedacted(resource, kind);
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

    private static string Ready(V1Pod pod)
    {
        var containers = pod.Status?.ContainerStatuses?.Count ?? 0;
        var ready = pod.Status?.ContainerStatuses?.Count(c => c.Ready) ?? 0;
        return $"{ready}/{containers}";
    }

    private static int RestartCount(V1Pod pod) =>
        pod.Status?.ContainerStatuses?.Sum(c => c.RestartCount) ?? 0;

    private static string FormatConfigMaps(V1ConfigMapList list) =>
        KubernetesReader.Table(
            ["NAME", "DATA", "AGE"],
            list.Items.Select(cm => (IReadOnlyList<string>)
            [
                cm.Name(),
                (cm.Data?.Count ?? 0).ToString(),
                KubernetesReader.Age(cm.CreationTimestamp()),
            ]));

    private static string FormatHpas(V2HorizontalPodAutoscalerList list) =>
        KubernetesReader.Table(
            ["NAME", "MIN", "MAX", "CURRENT", "AGE"],
            list.Items.Select(hpa => (IReadOnlyList<string>)
            [
                hpa.Name(),
                (hpa.Spec?.MinReplicas ?? 0).ToString(),
                (hpa.Spec?.MaxReplicas ?? 0).ToString(),
                (hpa.Status?.CurrentReplicas ?? 0).ToString(),
                KubernetesReader.Age(hpa.CreationTimestamp()),
            ]));

    private static string FormatStatefulSets(V1StatefulSetList list) =>
        KubernetesReader.Table(
            ["NAME", "READY", "AGE"],
            list.Items.Select(sts => (IReadOnlyList<string>)
            [
                sts.Name(),
                $"{sts.Status?.ReadyReplicas ?? 0}/{sts.Spec?.Replicas ?? 0}",
                KubernetesReader.Age(sts.CreationTimestamp()),
            ]));

    private static string FormatDaemonSets(V1DaemonSetList list) =>
        KubernetesReader.Table(
            ["NAME", "READY", "AGE"],
            list.Items.Select(ds => (IReadOnlyList<string>)
            [
                ds.Name(),
                $"{ds.Status?.NumberReady ?? 0}/{ds.Status?.DesiredNumberScheduled ?? 0}",
                KubernetesReader.Age(ds.CreationTimestamp()),
            ]));

    private static string FormatSecrets(V1SecretList list) =>
        KubernetesReader.Table(
            ["NAME", "TYPE", "KEYS", "AGE"],
            list.Items.Select(secret => (IReadOnlyList<string>)
            [
                secret.Name(),
                secret.Type ?? "-",
                (secret.Data?.Count ?? 0).ToString(),
                KubernetesReader.Age(secret.CreationTimestamp()),
            ]));
}
