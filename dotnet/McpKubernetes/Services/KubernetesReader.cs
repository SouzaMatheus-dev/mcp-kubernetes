using System.Text;
using k8s;
using k8s.Autorest;
using k8s.Models;

namespace McpKubernetes.Services;

public sealed class KubernetesReader
{
    private readonly McpConfig _config;
    private readonly Lazy<IKubernetes> _client;
    private readonly Lazy<KubernetesClientConfiguration> _kubeConfig;

    public KubernetesReader(McpConfig config)
    {
        _config = config;
        _kubeConfig = new Lazy<KubernetesClientConfiguration>(LoadConfiguration);
        _client = new Lazy<IKubernetes>(() => new Kubernetes(_kubeConfig.Value));
    }

    public IKubernetes Client => _client.Value;

    public KubernetesClientConfiguration KubeConfig => _kubeConfig.Value;

    public string ResolveNamespace(string? requested)
    {
        var ns = _config.ResolveNamespace(requested);
        if (!string.IsNullOrWhiteSpace(ns))
        {
            return ns;
        }

        return KubeConfig.Namespace ?? "default";
    }

    public string DescribeContext()
    {
        var cfg = KubeConfig;
        return JoinLines(DescribeContextLines(cfg).ToArray());
    }

    public async Task<string> DescribeContextAsync()
    {
        var cfg = KubeConfig;
        var lines = DescribeContextLines(cfg).ToList();

        try
        {
            var version = await Client.Version.GetCodeAsync().ConfigureAwait(false);
            lines.Add($"versao_kubernetes={version.GitVersion} (major={version.Major} minor={version.Minor})");
            lines.Add($"plataforma={version.Platform}");
        }
        catch (Exception ex)
        {
            lines.Add($"versao_kubernetes=indisponivel ({ex.Message})");
            lines.Add("dica=se /version já falha, o host do kubeconfig (Rancher/portal) provavelmente está errado ou o token expirou");
        }

        if (LooksLikeRancher(cfg.Host))
        {
            lines.Add("proxy=rancher");
            lines.Add("dica_rancher=o server do kubeconfig deve ser https://<rancher>/k8s/clusters/<id>. 404 aqui costuma ser cluster id antigo ou token de outro cluster.");
        }

        return JoinLines(lines.ToArray());
    }

    public async Task<string> DiagnoseAsync(string? @namespace)
    {
        var ns = ResolveNamespace(@namespace);
        var checks = new List<string>
        {
            "=== Diagnóstico de acesso (somente leitura) ===",
            await DescribeContextAsync().ConfigureAwait(false),
            "",
            "=== Probes ===",
        };

        checks.Add(await ProbeAsync("GET /version", async () =>
        {
            var v = await Client.Version.GetCodeAsync().ConfigureAwait(false);
            return $"ok gitVersion={v.GitVersion}";
        }).ConfigureAwait(false));

        checks.Add(await ProbeAsync("LIST namespaces", async () =>
        {
            var list = await Client.CoreV1.ListNamespaceAsync().ConfigureAwait(false);
            return $"ok count={list.Items.Count}";
        }).ConfigureAwait(false));

        var targetNs = string.IsNullOrWhiteSpace(ns) ? "default" : ns;
        checks.Add(await ProbeAsync($"LIST pods namespace={targetNs}", async () =>
        {
            var list = await Client.CoreV1.ListNamespacedPodAsync(targetNs).ConfigureAwait(false);
            return $"ok count={list.Items.Count}";
        }).ConfigureAwait(false));

        checks.Add(await ProbeAsync($"LIST events namespace={targetNs}", async () =>
        {
            var list = await Client.CoreV1.ListNamespacedEventAsync(targetNs).ConfigureAwait(false);
            return $"ok count={list.Items.Count}";
        }).ConfigureAwait(false));

        checks.Add(await ProbeAsync($"LIST deployments apps/v1 namespace={targetNs}", async () =>
        {
            var list = await Client.AppsV1.ListNamespacedDeploymentAsync(targetNs).ConfigureAwait(false);
            return $"ok count={list.Items.Count}";
        }).ConfigureAwait(false));

        checks.Add(await ProbeAsync($"LIST hpa autoscaling/v2 namespace={targetNs}", async () =>
        {
            var list = await Client.AutoscalingV2.ListNamespacedHorizontalPodAutoscalerAsync(targetNs).ConfigureAwait(false);
            return $"ok count={list.Items.Count}";
        }).ConfigureAwait(false));

        checks.Add(await ProbeAsync($"LIST hpa autoscaling/v1 namespace={targetNs}", async () =>
        {
            var list = await Client.AutoscalingV1.ListNamespacedHorizontalPodAutoscalerAsync(targetNs).ConfigureAwait(false);
            return $"ok count={list.Items.Count}";
        }).ConfigureAwait(false));

        checks.Add(await ProbeAsync($"GET metrics.k8s.io pods namespace={targetNs}", async () =>
        {
            var metrics = await Client.GetKubernetesPodsMetricsByNamespaceAsync(targetNs).ConfigureAwait(false);
            return $"ok count={metrics.Items?.Count() ?? 0}";
        }).ConfigureAwait(false));

        checks.Add("");
        checks.Add("Como ler: ok = API existe e o token alcança. 404 de API group = versão diferente do cluster (normal). 404 de /version ou pods em todos os clusters = kubeconfig/Rancher errado. 403 = RBAC.");
        return string.Join(Environment.NewLine, checks);
    }

    public async Task<string> SafeAsync(Func<Task<string>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (HttpOperationException ex)
        {
            return FormatHttpError(ex);
        }
        catch (Exception ex)
        {
            return $"Erro ao consultar o cluster: {ex.Message}";
        }
    }

    public async Task<T> TryOrFallbackAsync<T>(Func<Task<T>> primary, Func<Task<T>> fallback)
    {
        try
        {
            return await primary().ConfigureAwait(false);
        }
        catch (HttpOperationException ex) when ((int)ex.Response.StatusCode is 404 or 405)
        {
            return await fallback().ConfigureAwait(false);
        }
    }

    public static string FormatHttpError(HttpOperationException ex)
    {
        var code = (int)ex.Response.StatusCode;
        var body = TrimBody(ex.Response.Content);
        var request = ex.Request?.RequestUri?.ToString() ?? "(url desconhecida)";

        if (code == 403)
        {
            return JoinLines(
                $"Forbidden (403): o token não tem get/list neste recurso.",
                $"url={request}",
                body);
        }

        if (code == 401)
        {
            return JoinLines(
                "Não autenticado (401): token expirado ou kubeconfig de outro cluster.",
                "Faça login de novo no portal/Rancher, baixe o kubeconfig e atualize KUBECONFIG.",
                $"url={request}",
                body);
        }

        if (code == 404)
        {
            var hint = ClassifyNotFound(request);
            return JoinLines(
                $"Não encontrado (404). {hint}",
                $"url={request}",
                body);
        }

        return JoinLines($"Erro Kubernetes ({code})", $"url={request}", body);
    }

    public static string Table(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        var materialized = rows.ToList();
        if (materialized.Count == 0)
        {
            return "(0 linha(s) exibida(s))";
        }

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(" | ", headers));
        sb.AppendLine(string.Join(" | ", headers.Select(h => new string('-', Math.Max(3, h.Length)))));
        foreach (var row in materialized)
        {
            sb.AppendLine(string.Join(" | ", row));
        }

        sb.AppendLine();
        sb.Append($"({materialized.Count} linha(s) exibida(s))");
        return sb.ToString();
    }

    public static string YamlOrRedacted(object resource, string kind)
    {
        if (IsSecretKind(kind))
        {
            return RedactSecret(resource);
        }

        return KubernetesYaml.Serialize(resource);
    }

    public static bool IsSecretKind(string kind) =>
        kind.Equals("Secret", StringComparison.OrdinalIgnoreCase) ||
        kind.Equals("secrets", StringComparison.OrdinalIgnoreCase);

    public static string Age(DateTime? timestamp)
    {
        if (timestamp is null)
        {
            return "-";
        }

        var elapsed = DateTime.UtcNow - timestamp.Value.ToUniversalTime();
        if (elapsed.TotalDays >= 1)
        {
            return $"{(int)elapsed.TotalDays}d";
        }

        if (elapsed.TotalHours >= 1)
        {
            return $"{(int)elapsed.TotalHours}h";
        }

        if (elapsed.TotalMinutes >= 1)
        {
            return $"{(int)elapsed.TotalMinutes}m";
        }

        return $"{Math.Max(0, (int)elapsed.TotalSeconds)}s";
    }

    public static string JoinLines(params string[] lines) => string.Join(Environment.NewLine, lines);

    private IEnumerable<string> DescribeContextLines(KubernetesClientConfiguration cfg)
    {
        yield return $"contexto={cfg.CurrentContext}";
        yield return $"host={cfg.Host}";
        yield return $"namespace_padrao={cfg.Namespace ?? _config.DefaultNamespace}";
        yield return $"usuario={DescribeUser(cfg)}";
        yield return $"modo=somente_leitura";
        yield return $"kubeconfig={_config.Kubeconfig ?? "(padrao ~/.kube/config)"}";
    }

    private async Task<string> ProbeAsync(string label, Func<Task<string>> action)
    {
        try
        {
            return $"[ok] {label}: {await action().ConfigureAwait(false)}";
        }
        catch (HttpOperationException ex)
        {
            var code = (int)ex.Response.StatusCode;
            return $"[falha {code}] {label}: {ClassifyNotFound(ex.Request?.RequestUri?.ToString())} {TrimBody(ex.Response.Content)}";
        }
        catch (Exception ex)
        {
            return $"[falha] {label}: {ex.Message}";
        }
    }

    private static string ClassifyNotFound(string? request)
    {
        var url = request ?? "";
        if (url.Contains("/apis/autoscaling/v2", StringComparison.OrdinalIgnoreCase))
        {
            return "Este cluster não tem autoscaling/v2 (comum em Kubernetes antigo). O MCP tenta v1 automaticamente em listar_recursos HPA.";
        }

        if (url.Contains("/apis/metrics.k8s.io", StringComparison.OrdinalIgnoreCase))
        {
            return "metrics-server não instalado neste cluster. Ignore uso_recursos_pods e use events/logs.";
        }

        if (url.Contains("/apis/networking.k8s.io", StringComparison.OrdinalIgnoreCase))
        {
            return "API Ingress networking.k8s.io/v1 ausente (cluster antigo). Use Service/pod.";
        }

        if (url.Contains("/version", StringComparison.OrdinalIgnoreCase) ||
            LooksLikeRancher(url))
        {
            return "Host/caminho do kubeconfig provavelmente errado (Rancher cluster id, proxy ou token de outro cluster). Baixe de novo o kubeconfig desse cluster no navegador.";
        }

        if (url.Contains("/api/v1/namespaces", StringComparison.OrdinalIgnoreCase) &&
            url.Contains("/pods", StringComparison.OrdinalIgnoreCase))
        {
            return "Pod ou namespace não existe neste cluster — ou o token só enxerga outro namespace. Confira o nome e K8S_NAMESPACE.";
        }

        return "Pode ser recurso inexistente, namespace errado, API antiga deste cluster ou kubeconfig apontando para o cluster errado.";
    }

    private static bool LooksLikeRancher(string? host) =>
        !string.IsNullOrWhiteSpace(host) &&
        (host.Contains("rancher", StringComparison.OrdinalIgnoreCase) ||
         host.Contains("/k8s/clusters/", StringComparison.OrdinalIgnoreCase));

    private static string TrimBody(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "";
        }

        var trimmed = content.Trim();
        return trimmed.Length <= 500 ? trimmed : trimmed[..500] + "...";
    }

    private static string DescribeUser(KubernetesClientConfiguration cfg)
    {
        if (!string.IsNullOrWhiteSpace(cfg.Username))
        {
            return cfg.Username;
        }

        return string.IsNullOrWhiteSpace(cfg.AccessToken) ? "(kubeconfig)" : "(token)";
    }

    private KubernetesClientConfiguration LoadConfiguration()
    {
        var kubeconfig = _config.Kubeconfig;
        var context = _config.Context;

        if (!string.IsNullOrWhiteSpace(kubeconfig))
        {
            return KubernetesClientConfiguration.BuildConfigFromConfigFile(
                kubeconfig: new FileInfo(kubeconfig),
                currentContext: string.IsNullOrWhiteSpace(context) ? null : context);
        }

        if (!string.IsNullOrWhiteSpace(context))
        {
            return KubernetesClientConfiguration.BuildConfigFromConfigFile(
                currentContext: context);
        }

        return KubernetesClientConfiguration.BuildDefaultConfig();
    }

    private static string RedactSecret(object resource)
    {
        if (resource is not V1Secret secret)
        {
            return "Secret encontrado. Valores omitidos (somente leitura segura).";
        }

        var keys = secret.Data?.Keys.ToList() ?? [];
        return JoinLines(
            $"kind=Secret",
            $"namespace={secret.Namespace()}",
            $"name={secret.Name()}",
            $"type={secret.Type}",
            $"chaves={keys.Count} ({string.Join(", ", keys)})",
            "data=OMITIDO");
    }
}
