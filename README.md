# MCP-Kubernetes

Servidor MCP corporativo para **Kubernetes** em modo **somente leitura**.

Conecte o Gemini CLI (ou outro cliente MCP) ao cluster usando o **kubeconfig**
do operador — sem apply, delete, scale, restart ou exec.

## Pacote

| Registro | Pacote | Instalação | SDK |
| --- | --- | --- | --- |
| **NuGet** (global tool) | `McpKubernetes` | `dotnet tool install --global McpKubernetes` | .NET **8+** |
| **NuGet** (`dnx`) | `McpKubernetes` | `dotnet dnx McpKubernetes --yes` | .NET **10+** |

> `dotnet dnx` exige SDK 10. Em máquinas com .NET 8/9, use a **global tool** (`mcp-kubernetes`).

## Início rápido (global tool, .NET 8+)

```powershell
dotnet tool install --global McpKubernetes
```

Gemini (`%USERPROFILE%\.gemini\settings.json` ou `.gemini/settings.json` do projeto):

```json
{
  "mcpServers": {
    "kubernetes": {
      "command": "mcp-kubernetes",
      "env": {
        "K8S_READONLY": "true",
        "K8S_NAMESPACE": "saf-prod"
      }
    }
  }
}
```

O servidor lê `~/.kube/config` (ou `KUBECONFIG`). Valide com `contexto_atual`.

## Início rápido (`dotnet dnx`, .NET 10+)

```json
{
  "mcpServers": {
    "kubernetes": {
      "command": "dotnet",
      "args": ["dnx", "McpKubernetes", "--yes", "--source", "https://api.nuget.org/v3/index.json"],
      "env": {
        "K8S_READONLY": "true"
      }
    }
  }
}
```

## Ferramentas MCP

| Ferramenta | Descrição |
| --- | --- |
| `contexto_atual` | Contexto kube, host e modo leitura |
| `listar_namespaces` | Namespaces visíveis |
| `listar_pods` | Pods do namespace |
| `obter_pod` | Detalhe do pod |
| `logs_pod` | Logs com recorte (`tail` / `since` / `previous`) |
| `listar_eventos` | Events do namespace ou do objeto |
| `listar_deployments` | Deployments |
| `obter_deployment` | Detalhe do Deployment |
| `listar_replica_sets` | ReplicaSets |
| `listar_services` | Services |
| `obter_service` | Detalhe do Service |
| `obter_recurso` | Get por kind/name |
| `listar_recursos` | List por kind |
| `uso_recursos_pods` | CPU/memória (metrics-server) |

Não existem tools de escrita. Secret devolve só metadados (nome, tipo, chaves).

## Variáveis de ambiente

| Variável | Padrão | Descrição |
| --- | --- | --- |
| `KUBECONFIG` | `~/.kube/config` | Caminho do kubeconfig |
| `K8S_CONTEXT` | contexto atual | Contexto a usar |
| `K8S_NAMESPACE` | — | Namespace padrão das consultas |
| `K8S_READONLY` | `true` | Contrato de leitura (não há tools de escrita) |
| `K8S_MAX_LOG_LINES` | `200` | Limite de linhas de log |
| `K8S_MAX_ROWS` | `200` | Limite de linhas em listagens |

## Segurança

- Nenhuma operação mutável está implementada
- Secret nunca retorna `data`
- As permissões reais vêm do RBAC do cluster — o MCP não eleva privilégio
- Prefira um kubeconfig / ServiceAccount só com `get/list/watch`

## Documentação

| Guia | Conteúdo |
| --- | --- |
| [Instalação](docs/instalacao.md) | NuGet, requisitos e verificação |
| [Configuração MCP](docs/configuracao-mcp.md) | Gemini CLI |
| [Exemplos de uso](docs/exemplos-uso.md) | Sintomas e tools |
| [Publicação NuGet](docs/publicacao-nuget.md) | Pack e publish |

## Desenvolvimento

```powershell
git clone https://github.com/SouzaMatheus-dev/mcp-kubernetes.git
cd mcp-kubernetes
dotnet build dotnet/McpKubernetes/McpKubernetes.csproj
```

## Licença

MIT — veja [LICENSE](LICENSE).
