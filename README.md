# McpKubernetes

Servidor **MCP especialista em Kubernetes**, **somente leitura**.

Conecte **Gemini CLI**, **VS Code**, **Claude Desktop** ou qualquer cliente MCP
ao cluster usando o **kubeconfig do operador** — sem apply, delete, patch,
scale, rollout restart ou exec.

O MCP **observa, lista e explica**. Quem altera o cluster é um humano.

Repositório: https://github.com/SouzaMatheus-dev/mcp-kubernetes

---

## O que este pacote faz

- Lê o kubeconfig (`~/.kube/config` ou `KUBECONFIG`)
- Lista namespaces, Deployments, ReplicaSets, Pods, Services, events
- Obtém o YAML/status de um workload
- Lê logs com recorte (`tail`, `sinceSeconds`, `previous`)
- Consulta CPU/memória se o metrics-server existir
- Recusa Secret: devolve só nome, tipo e quantidade de chaves

## O que este pacote NÃO faz

Não existem tools para:

- `kubectl apply` / `delete` / `patch` / `edit`
- rollout restart, scale, alterar replicas
- alterar ConfigMap, Secret, Deployment, Namespace
- `kubectl exec`, port-forward, Helm install/uninstall

Se a correção exigir escrita, o agente deve **recomendar** a ação.
O servidor simplesmente não tem como executá-la.

---

## Requisitos

| Item | Detalhe |
| --- | --- |
| .NET | **8+** para global tool · **10+** para `dnx` |
| Cluster | kubeconfig válido com `get` / `list` / `watch` |
| Rede | acesso HTTPS à API do Kubernetes |
| Métricas | metrics-server (opcional; só para `uso_recursos_pods`) |

Confira o acesso **antes** de ligar o MCP:

```powershell
kubectl config current-context
kubectl auth can-i get pods
kubectl auth can-i list events
kubectl auth can-i create deployments
```

Esperado: `get pods` e `list events` = **yes**. `create deployments` = **no**
(perfil de leitura). O MCP não eleva privilégio: se o RBAC negar, a tool
devolve `Forbidden`.

---

## Instalação

### Global tool (.NET 8+) — recomendado no corporativo

```powershell
dotnet tool install --global McpKubernetes
dotnet tool update --global McpKubernetes
```

O comando instalado é `mcp-kubernetes`. Teste no terminal:

```powershell
# o processo espera stdio MCP; Ctrl+C para sair
mcp-kubernetes
```

Se o comando não for encontrado, feche o terminal e abra outro (PATH).

### dotnet dnx (requer .NET 10 SDK)

Não instala ferramenta permanente. O cliente baixa e executa:

```powershell
dotnet --version   # precisa ser 10.0.x
dotnet dnx McpKubernetes --yes --source https://api.nuget.org/v3/index.json
```

> Em máquinas com .NET 8/9, use a **global tool**. `dnx` não existe nesses SDKs.

### Versão específica

```powershell
dotnet tool install --global McpKubernetes --version 0.1.1
```

---

## Configuração no Gemini CLI

Arquivo do usuário: `%USERPROFILE%\.gemini\settings.json`  
Ou, no projeto: `.gemini/settings.json`

### Global tool

```json
{
  "mcpServers": {
    "kubernetes": {
      "command": "mcp-kubernetes",
      "timeout": 120000,
      "trust": false,
      "env": {
        "K8S_READONLY": "true",
        "K8S_NAMESPACE": "seu-namespace"
      }
    }
  }
}
```

### dnx (.NET 10+)

```json
{
  "mcpServers": {
    "kubernetes": {
      "command": "dotnet",
      "args": [
        "dnx",
        "McpKubernetes",
        "--yes",
        "--source",
        "https://api.nuget.org/v3/index.json"
      ],
      "timeout": 120000,
      "trust": false,
      "env": {
        "K8S_READONLY": "true"
      }
    }
  }
}
```

Reinicie o Gemini e valide:

```text
/mcp list
```

Devem aparecer só tools de leitura: `contexto_atual`, `listar_pods`,
`logs_pod`, `listar_eventos`, … Nenhuma tool de apply/delete/exec.

Peça no chat: *“Qual o contexto Kubernetes atual?”* — a tool `contexto_atual`
deve responder com cluster, host e `modo=somente_leitura`.

### kubeconfig fora do padrão

```json
"env": {
  "KUBECONFIG": "C:\\Users\\SEU-USUARIO\\.kube\\config-readonly",
  "K8S_CONTEXT": "prod-ro",
  "K8S_NAMESPACE": "saf-prod",
  "K8S_READONLY": "true"
}
```

---

## Outros clientes MCP

O transporte é **stdio**. Qualquer cliente que aceite `command` + `env` funciona.

**VS Code** (`mcp.json` / settings):

```json
{
  "servers": {
    "kubernetes": {
      "type": "stdio",
      "command": "mcp-kubernetes",
      "env": {
        "K8S_READONLY": "true"
      }
    }
  }
}
```

**Claude Desktop** (`claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "kubernetes": {
      "command": "mcp-kubernetes",
      "env": {
        "K8S_READONLY": "true"
      }
    }
  }
}
```

---

## Um servidor, vários namespaces

Configure **uma entrada por cluster/contexto**. Troque o alvo com o parâmetro
`namespace` em cada tool — não precisa de um MCP por namespace.

```text
contexto_atual()
listar_namespaces()
listar_deployments(namespace="saf-prod")
listar_pods(namespace="saf-prod", labelSelector="app=payment-api")
logs_pod(name="payment-api-7d9f", namespace="saf-prod", tail=150, previous=true)
```

Dois ambientes (DEV + PROD) = duas entradas no `settings.json`, cada uma com
`K8S_CONTEXT` ou `KUBECONFIG` diferente.

---

## Ferramentas MCP

### Inventário

| Ferramenta | Parâmetros | O que devolve |
| --- | --- | --- |
| `contexto_atual` | — | contexto kube, host da API, namespace padrão, modo leitura |
| `listar_namespaces` | — | namespaces visíveis (nome, status, age) |
| `listar_deployments` | `namespace`, `labelSelector` | replicas ready/desired, unavailable, age |
| `listar_replica_sets` | `namespace`, `labelSelector` | ReplicaSets do namespace |
| `listar_pods` | `namespace`, `labelSelector` | ready, phase, restarts, age, node |
| `listar_services` | `namespace` | type, clusterIP, ports |
| `listar_recursos` | `kind`, `namespace`, `labelSelector` | listagem por kind |

### Detalhe

| Ferramenta | Parâmetros | O que devolve |
| --- | --- | --- |
| `obter_pod` | `name`, `namespace` | YAML do pod (status, probes, imagem, limites) |
| `obter_deployment` | `name`, `namespace` | YAML do Deployment |
| `obter_service` | `name`, `namespace` | YAML do Service |
| `obter_recurso` | `kind`, `name`, `namespace` | get genérico (veja kinds abaixo) |

Kinds aceitos em `obter_recurso` / `listar_recursos`:
`Pod`, `Deployment`, `ReplicaSet`, `Service`, `ConfigMap`, `HPA`,
`StatefulSet`, `DaemonSet`, `Job`, `Ingress`, `Namespace`, `Event`,
`Secret` (somente metadados).

### Evidência

| Ferramenta | Parâmetros | O que devolve |
| --- | --- | --- |
| `listar_eventos` | `namespace`, `involvedObject` | type, reason, kind, objeto, message |
| `logs_pod` | `name`, `namespace`, `container`, `tail` (1–200), `sinceSeconds`, `previous` | texto do log |
| `uso_recursos_pods` | `namespace`, `name` | CPU/memória por container (metrics-server) |

`previous=true` lê o container que acabou de morrer — use em CrashLoop/OOM.

---

## Fluxo sugerido (investigação)

Não varra o cluster. Vá do sintoma ao recurso:

1. `contexto_atual` — confirmar **qual** cluster
2. `listar_namespaces` ou use o namespace do mapa/pedido
3. `listar_deployments` / `listar_pods` **só nesse namespace**
4. `obter_pod` ou `obter_deployment` do alvo
5. `listar_eventos` com `involvedObject` = nome do pod/deploy
6. `logs_pod` com `tail` pequeno; se reiniciou, `previous=true`
7. Só então formule hipótese. Sem evidência = “evidência insuficiente”

### Cenários

| Sintoma | Sequência |
| --- | --- |
| API 500 | Service → pods Ready → `logs_pod` → events |
| Pod reiniciando | `obter_pod` (restartCount, lastState) → events → `logs_pod previous=true` |
| OOMKilled | events (`OOMKilled`) + limites em `obter_pod` |
| Deploy “quebrado” | `obter_deployment` → ReplicaSets → pods (`ImagePullBackOff`, probes) |
| Lentidão | events + `uso_recursos_pods` (se metrics-server) |
| Consumer com lag | replicas, restarts, logs do worker |

Exemplos de pedido ao agente:

- “A API payment-api no namespace saf-prod está retornando 500.”
- “O pod payment-api-7d9f está reiniciando. Investigue sem alterar nada.”
- “Liste events Warning de saf-prod dos últimos minutos.”

---

## Variáveis de ambiente

| Variável | Padrão | Descrição |
| --- | --- | --- |
| `KUBECONFIG` | `~/.kube/config` | Caminho do kubeconfig |
| `MCP_K8S_KUBECONFIG` | — | Alias de `KUBECONFIG` |
| `K8S_CONTEXT` | contexto atual do kubeconfig | Qual contexto usar |
| `K8S_NAMESPACE` | — | Namespace padrão (se a tool não receber `namespace`) |
| `K8S_READONLY` | `true` | Contrato de leitura (não há tools de escrita) |
| `K8S_MAX_LOG_LINES` | `200` | Teto de `tail` em `logs_pod` |
| `K8S_MAX_ROWS` | `200` | Teto de linhas em listagens |

Se `namespace` não for passado e `K8S_NAMESPACE` estiver vazio, a tool
responde `Bloqueado: informe namespace` (exceto `listar_namespaces` e
`contexto_atual`). Isso evita varrer o cluster “por garantia”.

---

## Segurança

- Nenhuma operação mutável está implementada no código
- `Secret`: nunca retorna `data` / valores — só nome, type e chaves
- Permissões reais vêm do **RBAC** do cluster
- Prefira ServiceAccount só com `get`, `list`, `watch` e `pods/log`
- `trust: false` no cliente MCP: confirme tools inesperadas
- Não coloque tokens de cluster no git; use `KUBECONFIG` local

Manifesto de exemplo (Role somente leitura):
https://github.com/SouzaMatheus-dev/mcp-kubernetes

---

## Problemas comuns

| Sintoma | Causa provável | O que fazer |
| --- | --- | --- |
| `/mcp list` sem `kubernetes` | tool não no PATH | Reabra o terminal; `dotnet tool install --global McpKubernetes` |
| `Forbidden` | RBAC sem get/list | Peça Role de leitura; o MCP não contorna |
| `Não encontrado` | namespace/nome errados | `listar_namespaces` + `listar_pods` |
| `Bloqueado: informe namespace` | sem `namespace` e sem `K8S_NAMESPACE` | Passe o namespace na tool ou no `env` |
| log vazio | pod novo ou container errado | `obter_pod`; tente `previous=true` |
| `uso_recursos_pods` falha | sem metrics-server | Siga sem CPU/mem; não é bloqueante |
| contexto errado | vários clusters no kubeconfig | defina `K8S_CONTEXT` ou `KUBECONFIG` |

---

## Documentação extra

- Instalação: https://github.com/SouzaMatheus-dev/mcp-kubernetes/blob/main/docs/instalacao.md
- Configuração MCP: https://github.com/SouzaMatheus-dev/mcp-kubernetes/blob/main/docs/configuracao-mcp.md
- Exemplos: https://github.com/SouzaMatheus-dev/mcp-kubernetes/blob/main/docs/exemplos-uso.md

## Licença

MIT
