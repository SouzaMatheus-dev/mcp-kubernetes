using System.Globalization;
using System.Text.Json;

namespace McpKubernetes.Services.Dashboard;

internal static class DashboardJson
{
    internal static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
    };

    internal static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    internal static IReadOnlyList<JsonElement> Array(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGet(root, name, out var value) && value.ValueKind == JsonValueKind.Array)
            {
                return value.EnumerateArray().ToList();
            }
        }

        if (TryGet(root, "items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            return items.EnumerateArray().ToList();
        }

        return [];
    }

    internal static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    internal static string Text(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var segment in path)
        {
            if (!TryGet(current, segment, out current))
            {
                return "-";
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString() is { Length: > 0 } text ? text : "-",
            JsonValueKind.Number => current.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "-",
            JsonValueKind.Undefined => "-",
            _ => current.ToString(),
        };
    }

    internal static string ObjectName(JsonElement element) =>
        FirstText(element, ["objectMeta", "name"], ["metadata", "name"], ["name"]);

    internal static string ObjectNamespace(JsonElement element) =>
        FirstText(element, ["objectMeta", "namespace"], ["metadata", "namespace"]);

    internal static DateTime? Timestamp(JsonElement element, params string[] path)
    {
        var text = Text(element, path);
        if (text == "-" || !DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return null;
        }

        return parsed;
    }

    internal static string FirstText(JsonElement element, params string[][] paths)
    {
        foreach (var path in paths)
        {
            var value = Text(element, path);
            if (value != "-")
            {
                return value;
            }
        }

        return "-";
    }

    internal static IReadOnlyDictionary<string, string>? Labels(JsonElement element)
    {
        if (!TryGet(element, "objectMeta", out var meta) && !TryGet(element, "metadata", out meta))
        {
            return null;
        }

        if (!TryGet(meta, "labels", out var labels) || labels.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return labels.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.ToString(), StringComparer.OrdinalIgnoreCase);
    }

    internal static bool MatchesLabels(JsonElement element, string? labelSelector)
    {
        if (string.IsNullOrWhiteSpace(labelSelector))
        {
            return true;
        }

        var labels = Labels(element);
        if (labels is null)
        {
            return false;
        }

        foreach (var part in labelSelector.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var key = part[..eq];
            var value = part[(eq + 1)..];
            if (!labels.TryGetValue(key, out var actual) || !string.Equals(actual, value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    internal static string PrettyRedacted(JsonElement element, string kind)
    {
        if (!KubernetesReader.IsSecretKind(kind))
        {
            return JsonSerializer.Serialize(element, Pretty);
        }

        return KubernetesReader.JoinLines(
            "kind=Secret",
            $"namespace={ObjectNamespace(element)}",
            $"name={ObjectName(element)}",
            $"type={FirstText(element, ["type"], ["secretType"])}",
            "data=OMITIDO");
    }

    internal static string FirstContainer(JsonElement pod)
    {
        foreach (var key in new[] { "containers", "containerStatuses" })
        {
            if (TryGet(pod, key, out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    var name = FirstText(item, ["name"], ["containerName"]);
                    if (name != "-")
                    {
                        return name;
                    }
                }
            }
        }

        return "-";
    }
}
