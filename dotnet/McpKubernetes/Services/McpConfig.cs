namespace McpKubernetes.Services;

public enum ApiMode
{
    Auto,
    Native,
    Dashboard,
}

public sealed class McpConfig
{
    public bool ReadOnly => GetBool("K8S_READONLY", defaultValue: true);

    public string? Kubeconfig => FirstNonEmpty(
        Environment.GetEnvironmentVariable("KUBECONFIG"),
        Environment.GetEnvironmentVariable("MCP_K8S_KUBECONFIG"));

    public string? Context => FirstNonEmpty(
        Environment.GetEnvironmentVariable("K8S_CONTEXT"),
        Environment.GetEnvironmentVariable("MCP_K8S_CONTEXT"));

    public string DefaultNamespace => FirstNonEmpty(
        Environment.GetEnvironmentVariable("K8S_NAMESPACE"),
        Environment.GetEnvironmentVariable("MCP_K8S_NAMESPACE")) ?? "";

    /// <summary>
    /// auto | native | dashboard. Credenciais nunca vêm do pacote — só do ambiente.
    /// </summary>
    public ApiMode ApiMode => ParseApiMode(
        FirstNonEmpty(
            Environment.GetEnvironmentVariable("K8S_API_MODE"),
            Environment.GetEnvironmentVariable("MCP_K8S_API_MODE")));

    public string? Server => FirstNonEmpty(
        Environment.GetEnvironmentVariable("K8S_SERVER"),
        Environment.GetEnvironmentVariable("MCP_K8S_SERVER"));

    public string? Token => FirstNonEmpty(
        Environment.GetEnvironmentVariable("K8S_TOKEN"),
        Environment.GetEnvironmentVariable("K8S_DASHBOARD_TOKEN"),
        Environment.GetEnvironmentVariable("MCP_K8S_TOKEN"));

    public bool SkipTlsVerify => GetBool("K8S_SKIP_TLS_VERIFY", defaultValue: false)
        || GetBool("K8S_INSECURE_SKIP_TLS_VERIFY", defaultValue: false);

    public int MaxLogLines => GetInt("K8S_MAX_LOG_LINES", defaultValue: 200);

    public int MaxRows => GetInt("K8S_MAX_ROWS", defaultValue: 200);

    public bool HasToken => !string.IsNullOrWhiteSpace(Token);

    public static string? NormalizeServer(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var server = raw.Trim();
        var hash = server.IndexOf('#');
        if (hash >= 0)
        {
            server = server[..hash];
        }

        return string.IsNullOrWhiteSpace(server) ? null : server.TrimEnd('/');
    }

    public static bool LooksLikeDashboardHost(string? host) =>
        !string.IsNullOrWhiteSpace(host) &&
        host.Contains("dashboard", StringComparison.OrdinalIgnoreCase);

    public static bool LooksLikeRancherHost(string? host) =>
        !string.IsNullOrWhiteSpace(host) &&
        (host.Contains("rancher", StringComparison.OrdinalIgnoreCase) ||
         host.Contains("/k8s/clusters/", StringComparison.OrdinalIgnoreCase));

    private static ApiMode ParseApiMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ApiMode.Auto;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "dashboard" or "k8s-dashboard" or "kubernetes-dashboard" => ApiMode.Dashboard,
            "native" or "kube" or "rancher" or "api" => ApiMode.Native,
            _ => ApiMode.Auto,
        };
    }

    public string ResolveNamespace(string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return requested.Trim();
        }

        return DefaultNamespace;
    }

    private static bool GetBool(string name, bool defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetInt(string name, int defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return int.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
