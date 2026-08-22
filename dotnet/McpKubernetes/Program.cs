using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using McpKubernetes.Services;
using McpKubernetes.Tools;

var builder = Host.CreateEmptyApplicationBuilder(settings: null);

builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.AddSingleton<McpConfig>();
builder.Services.AddSingleton<KubernetesReader>();
builder.Services.AddSingleton<KubernetesTools>();
builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInstructions =
            "Especialista em Kubernetes corporativo (somente leitura). " +
            "Antes de concluir: 1) ContextoAtual para saber o cluster, " +
            "2) escopo mínimo de namespace/workload, " +
            "3) ListarPods/ObterWorkload para estado, " +
            "4) ListarEventos e LogsPod para evidência. " +
            "Nunca aplique, delete, faça scale, restart ou exec. " +
            "Não leia valores de Secret. Se a correção exigir escrita, apenas recomende.";
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
