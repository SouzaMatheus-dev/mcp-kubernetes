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
        return JoinLines(
            $"contexto={cfg.CurrentContext}",
            $"host={cfg.Host}",
            $"namespace_padrao={cfg.Namespace ?? _config.DefaultNamespace}",
            $"usuario={DescribeUser(cfg)}",
            $"modo=somente_leitura",
            $"kubeconfig={_config.Kubeconfig ?? "(padrao ~/.kube/config)"}");
    }

    public async Task<string> SafeAsync(Func<Task<string>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (HttpOperationException ex)
        {
            var code = (int)ex.Response.StatusCode;
            if (code == 403)
            {
                return $"Forbidden ({code}): o kubeconfig não tem permissão de leitura para este recurso. {ex.Response.Content}";
            }

            if (code == 404)
            {
                return $"Não encontrado ({code}). Informe namespace e nome exatos. {ex.Response.Content}";
            }

            return $"Erro Kubernetes ({code}): {ex.Response.Content}";
        }
        catch (Exception ex)
        {
            return $"Erro ao consultar o cluster: {ex.Message}";
        }
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
