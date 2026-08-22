using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace McpKubernetes.Services.Dashboard;

internal sealed class DashboardApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _server;
    private string _bearer;
    private bool _loginAttempted;

    private DashboardApiClient(HttpClient http, string server, string bearer)
    {
        _http = http;
        _server = server;
        _bearer = bearer;
    }

    public string Server => _server;

    public static DashboardApiClient Create(string server, string token, bool skipTlsVerify)
    {
        var handler = new HttpClientHandler();
        if (skipTlsVerify)
        {
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri(server.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromMinutes(2),
        };
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return new DashboardApiClient(http, server.TrimEnd('/'), token);
    }

    public async Task<JsonElement> GetJsonAsync(string relativeUrl, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(HttpMethod.Get, relativeUrl, null, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Dashboard {(int)response.StatusCode} {relativeUrl}: {Trim(body)}");
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return JsonDocument.Parse("{}").RootElement.Clone();
        }

        return DashboardJson.Parse(body);
    }

    public async Task<string> GetRawAsync(string relativeUrl, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(HttpMethod.Get, relativeUrl, null, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Dashboard {(int)response.StatusCode} {relativeUrl}: {Trim(body)}");
        }

        return body;
    }

    public void Dispose() => _http.Dispose();

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string relativeUrl,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        var response = await SendOnceAsync(method, relativeUrl, content, cancellationToken).ConfigureAwait(false);
        if ((int)response.StatusCode is 401 or 403 && !_loginAttempted)
        {
            response.Dispose();
            await TryLoginAsync(cancellationToken).ConfigureAwait(false);
            response = await SendOnceAsync(method, relativeUrl, content, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    private async Task<HttpResponseMessage> SendOnceAsync(
        HttpMethod method,
        string relativeUrl,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativeUrl.TrimStart('/'));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _bearer);
        if (content is not null)
        {
            request.Content = content;
        }

        return await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task TryLoginAsync(CancellationToken cancellationToken)
    {
        _loginAttempted = true;
        var payload = JsonSerializer.Serialize(new { token = _bearer });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await SendOnceAsync(HttpMethod.Post, "api/v1/login", content, cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return;
        }

        try
        {
            var json = DashboardJson.Parse(body);
            var jwe = DashboardJson.FirstText(json, ["jweToken"], ["token"]);
            if (jwe != "-")
            {
                _bearer = jwe;
            }
        }
        catch (JsonException)
        {
            // login sem JWE — segue com o token original
        }
    }

    private static string Trim(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "";
        }

        var trimmed = content.Trim();
        return trimmed.Length <= 400 ? trimmed : trimmed[..400] + "...";
    }
}
