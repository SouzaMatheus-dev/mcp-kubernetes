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

## Rancher / API nativa

Use kubeconfig (contexto já logado). Não coloque usuário/senha do Rancher no MCP.

```json
"env": {
  "K8S_API_MODE": "native",
  "K8S_CONTEXT": "local-saf",
  "K8S_NAMESPACE": "saf",
  "K8S_READONLY": "true"
}
```

## Kubernetes Dashboard

O portal **não** é a API do control plane. O MCP traduz para rotas no singular
(`/api/v1/pod/{ns}`, `/api/v1/namespace`, …).

Credenciais **não** vão no NuGet. Só no env do Gemini, em arquivo local:

```json
"env": {
  "K8S_API_MODE": "dashboard",
  "K8S_SERVER": "https://seu-dashboard.exemplo.interno",
  "K8S_TOKEN": "(cole o token do portal só aqui)",
  "K8S_NAMESPACE": "saf",
  "K8S_SKIP_TLS_VERIFY": "true",
  "K8S_READONLY": "true"
}
```

`K8S_SERVER` é a origem sem `#/login`. Uma entrada MCP por cluster.

## Vários clusters

Três entradas (`k8s1`, `k8s3`, `k8s4`), cada uma com `env` próprio.
O token de um portal **não** vale no outro.
