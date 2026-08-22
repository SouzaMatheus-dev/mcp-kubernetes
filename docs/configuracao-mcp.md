# Configuração MCP

## Gemini CLI — global tool (.NET 8+)

```json
{
  "mcpServers": {
    "kubernetes": {
      "command": "mcp-kubernetes",
      "timeout": 120000,
      "trust": false,
      "env": {
        "K8S_READONLY": "true",
        "K8S_NAMESPACE": "saf-prod"
      }
    }
  }
}
```

## Gemini CLI — dnx (.NET 10+)

```json
{
  "mcpServers": {
    "kubernetes": {
      "command": "dotnet",
      "args": ["dnx", "McpKubernetes", "--yes", "--source", "https://api.nuget.org/v3/index.json"],
      "timeout": 120000,
      "trust": false,
      "env": {
        "K8S_READONLY": "true"
      }
    }
  }
}
```

## kubeconfig fora do padrão

```json
"env": {
  "KUBECONFIG": "C:\\Users\\SEU-USUARIO\\.kube\\config-readonly",
  "K8S_CONTEXT": "prod-ro",
  "K8S_NAMESPACE": "saf-prod"
}
```

Arquivos de exemplo em [`docs/examples/`](examples/).
