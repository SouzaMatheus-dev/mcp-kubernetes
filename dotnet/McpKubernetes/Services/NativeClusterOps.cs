using k8s;
using k8s.Models;

namespace McpKubernetes.Services;

public sealed class NativeClusterOps(McpConfig config, KubernetesReader reader) : IClusterOps
{
    public string ApiKind => "native";

    public Task<string> DescribeContextAsync() => reader.DescribeContextAsync();

    public Task<string> DiagnoseAsync(string? @namespace) => reader.DiagnoseAsync(@namespace);

    public async Task<string> ListNamespacesAsync()
    {
        var list = await reader.Client.CoreV1.ListNamespaceAsync().ConfigureAwait(false);
        var rows = list.Items.Select(ns => (IReadOnlyList<string>)
        [
            ns.Name(),
            ns.Status?.Phase ?? "-",
            KubernetesReader.Age(ns.CreationTimestamp()),
        ]);
        return KubernetesReader.Table(["NAMESPACE", "STATUS", "AGE"], rows);
    }

    public async Task<string> ListPodsAsync(string @namespace, string? labelSelector)
    {
        var list = await reader.Client.CoreV1.ListNamespacedPodAsync(
            @namespace,
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
    }

    public async Task<string> GetPodAsync(string name, string @namespace)
    {
        var pod = await reader.Client.CoreV1.ReadNamespacedPodAsync(name, @namespace).ConfigureAwait(false);
        return KubernetesReader.YamlOrRedacted(pod, "Pod");
    }

    public async Task<string> LogsPodAsync(
        string name,
        string @namespace,
        string? container,
        int tail,
        int sinceSeconds,
        bool previous)
    {
        await using var stream = await reader.Client.CoreV1.ReadNamespacedPodLogAsync(
            name,
            @namespace,
            container: EmptyToNull(container),
            tailLines: tail,
            sinceSeconds: sinceSeconds > 0 ? sinceSeconds : null,
            previous: previous).ConfigureAwait(false);

        using var readerStream = new StreamReader(stream);
        var text = await readerStream.ReadToEndAsync().ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(text) ? "(log vazio)" : text.TrimEnd();
    }

    public async Task<string> ListEventsAsync(string @namespace, string? involvedObject)
    {
        var fieldSelector = string.IsNullOrWhiteSpace(involvedObject)
            ? null
            : $"involvedObject.name={involvedObject}";

        var list = await reader.Client.CoreV1.ListNamespacedEventAsync(
            @namespace,
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
    }

    public async Task<string> ListDeploymentsAsync(string @namespace, string? labelSelector)
    {
        var list = await reader.Client.AppsV1.ListNamespacedDeploymentAsync(
            @namespace,
            labelSelector: EmptyToNull(labelSelector)).ConfigureAwait(false);

        var rows = list.Items.Take(config.MaxRows).Select(d => (IReadOnlyList<string>)
        [
            d.Name(),
            $"{d.Status?.ReadyReplicas ?? 0}/{d.Spec?.Replicas ?? 0}",
            (d.Status?.UnavailableReplicas ?? 0).ToString(),
            KubernetesReader.Age(d.CreationTimestamp()),
        ]);
        return KubernetesReader.Table(["NAME", "READY", "UNAVAILABLE", "AGE"], rows);
    }

    public async Task<string> ListReplicaSetsAsync(string @namespace, string? labelSelector)
    {
        var list = await reader.Client.AppsV1.ListNamespacedReplicaSetAsync(
            @namespace,
            labelSelector: EmptyToNull(labelSelector)).ConfigureAwait(false);

        var rows = list.Items.Take(config.MaxRows).Select(rs => (IReadOnlyList<string>)
        [
            rs.Name(),
            $"{rs.Status?.ReadyReplicas ?? 0}/{rs.Spec?.Replicas ?? 0}",
            KubernetesReader.Age(rs.CreationTimestamp()),
        ]);
        return KubernetesReader.Table(["NAME", "READY", "AGE"], rows);
    }

    public async Task<string> ListServicesAsync(string @namespace)
    {
        var list = await reader.Client.CoreV1.ListNamespacedServiceAsync(@namespace).ConfigureAwait(false);
        var rows = list.Items.Take(config.MaxRows).Select(svc => (IReadOnlyList<string>)
        [
            svc.Name(),
            svc.Spec?.Type ?? "-",
            svc.Spec?.ClusterIP ?? "-",
            string.Join(",", svc.Spec?.Ports?.Select(p => $"{p.Port}/{p.Protocol}") ?? []),
        ]);
        return KubernetesReader.Table(["NAME", "TYPE", "CLUSTER-IP", "PORTS"], rows);
    }

    public async Task<string> GetResourceAsync(string kind, string name, string @namespace)
    {
        if (KubernetesReader.IsSecretKind(kind))
        {
            var secret = await reader.Client.CoreV1.ReadNamespacedSecretAsync(name, @namespace).ConfigureAwait(false);
            return KubernetesReader.YamlOrRedacted(secret, "Secret");
        }

        object resource = kind.ToLowerInvariant() switch
        {
            "pod" or "pods" => await reader.Client.CoreV1.ReadNamespacedPodAsync(name, @namespace).ConfigureAwait(false),
            "deployment" or "deployments" => await reader.Client.AppsV1.ReadNamespacedDeploymentAsync(name, @namespace).ConfigureAwait(false),
            "replicaset" or "replicasets" => await reader.Client.AppsV1.ReadNamespacedReplicaSetAsync(name, @namespace).ConfigureAwait(false),
            "service" or "services" => await reader.Client.CoreV1.ReadNamespacedServiceAsync(name, @namespace).ConfigureAwait(false),
            "configmap" or "configmaps" => await reader.Client.CoreV1.ReadNamespacedConfigMapAsync(name, @namespace).ConfigureAwait(false),
            "namespace" or "namespaces" => await reader.Client.CoreV1.ReadNamespaceAsync(name).ConfigureAwait(false),
            "hpa" or "horizontalpodautoscaler" => await reader.TryOrFallbackAsync<object>(
                async () => await reader.Client.AutoscalingV2.ReadNamespacedHorizontalPodAutoscalerAsync(name, @namespace).ConfigureAwait(false),
                async () => await reader.Client.AutoscalingV1.ReadNamespacedHorizontalPodAutoscalerAsync(name, @namespace).ConfigureAwait(false)).ConfigureAwait(false),
            "statefulset" or "statefulsets" => await reader.Client.AppsV1.ReadNamespacedStatefulSetAsync(name, @namespace).ConfigureAwait(false),
            "daemonset" or "daemonsets" => await reader.Client.AppsV1.ReadNamespacedDaemonSetAsync(name, @namespace).ConfigureAwait(false),
            "job" or "jobs" => await reader.Client.BatchV1.ReadNamespacedJobAsync(name, @namespace).ConfigureAwait(false),
            "ingress" or "ingresses" => await reader.Client.NetworkingV1.ReadNamespacedIngressAsync(name, @namespace).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Kind '{kind}' não suportado para get."),
        };

        return KubernetesReader.YamlOrRedacted(resource, kind);
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
            "configmap" or "configmaps" => FormatConfigMaps(
                await reader.Client.CoreV1.ListNamespacedConfigMapAsync(
                    @namespace,
                    labelSelector: EmptyToNull(labelSelector)).ConfigureAwait(false)),
            "hpa" or "horizontalpodautoscaler" or "horizontalpodautoscalers" =>
                await ListHpasAsync(@namespace, labelSelector).ConfigureAwait(false),
            "statefulset" or "statefulsets" => FormatStatefulSets(
                await reader.Client.AppsV1.ListNamespacedStatefulSetAsync(
                    @namespace,
                    labelSelector: EmptyToNull(labelSelector)).ConfigureAwait(false)),
            "daemonset" or "daemonsets" => FormatDaemonSets(
                await reader.Client.AppsV1.ListNamespacedDaemonSetAsync(
                    @namespace,
                    labelSelector: EmptyToNull(labelSelector)).ConfigureAwait(false)),
            "secret" or "secrets" => FormatSecrets(
                await reader.Client.CoreV1.ListNamespacedSecretAsync(
                    @namespace,
                    labelSelector: EmptyToNull(labelSelector)).ConfigureAwait(false)),
            _ => $"Kind '{kind}' não suportado. Use Pod, Deployment, ReplicaSet, Service, ConfigMap, HPA, StatefulSet, DaemonSet, Event ou Secret (metadados).",
        };
    }

    public async Task<string> UsoRecursosPodsAsync(string @namespace, string? name)
    {
        var metrics = await reader.Client.GetKubernetesPodsMetricsByNamespaceAsync(@namespace).ConfigureAwait(false);
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
    }

    private async Task<string> ListHpasAsync(string ns, string? labelSelector)
    {
        var selector = EmptyToNull(labelSelector);
        return await reader.TryOrFallbackAsync(
            async () => FormatHpas(await reader.Client.AutoscalingV2
                .ListNamespacedHorizontalPodAutoscalerAsync(ns, labelSelector: selector).ConfigureAwait(false)),
            async () => FormatHpasV1(await reader.Client.AutoscalingV1
                .ListNamespacedHorizontalPodAutoscalerAsync(ns, labelSelector: selector).ConfigureAwait(false))).ConfigureAwait(false);
    }

    private static string? EmptyToNull(string? value) =>
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

    private static string FormatHpasV1(V1HorizontalPodAutoscalerList list) =>
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
