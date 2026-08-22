namespace McpKubernetes.Services;

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

    public int MaxLogLines => GetInt("K8S_MAX_LOG_LINES", defaultValue: 200);

    public int MaxRows => GetInt("K8S_MAX_ROWS", defaultValue: 200);

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
