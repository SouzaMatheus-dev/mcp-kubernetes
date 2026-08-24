# McpKubernetes

Servidor **MCP especialista em Kubernetes**, **somente leitura**.

Conecte **Gemini CLI**, **VS Code**, **Claude Desktop** ou qualquer cliente MCP
ao cluster usando o **kubeconfig do operador** — sem apply, delete, patch,
scale, rollout restart ou exec.

O MCP **observa, lista e explica**. Quem altera o cluster é um humano.

Repositório: https://github.com/SouzaMatheus-dev/mcp-kubernetes

---

## O que este pacote faz

- Lê o kubeconfig (`~/.kube/config` ou `KUBECONFIG`) — Rancher / API nativa
- Também fala com **Kubernetes Dashboard** (rotas no singular) quando `K8S_API_MODE=dashboard`
- **Não embute** token, senha nem URL de cluster: credenciais só no ambiente do cliente
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
| Cluster | **Rancher/API nativa:** kubeconfig com `get` / `list` / `watch` · **Dashboard:** URL do portal + token de leitura no env |
| Rede | HTTPS até a API nativa **ou** até o Kubernetes Dashboard |
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
dotnet tool install --global McpKubernetes --version 0.1.5
```

---

## Configuração no Gemini CLI

Arquivo do usuário: `%USERPROFILE%\.gemini\settings.json`  
Ou, no projeto: `.gemini/settings.json`  
Credenciais: **somente** em `.gemini/settings.local.json` (gitignored). O pacote não embute token, senha nem URL interna.

Há **três modos**. Uma entrada MCP por cluster.

### 1) Rancher / API nativa (`K8S_API_MODE=native`)

Usa kubeconfig (o mesmo que o `kubectl`). Não coloque usuário/senha do Rancher no MCP.

```json
{
  "mcpServers": {
    "k8s-rancher": {
      "command": "mcp-kubernetes",
      "timeout": 120000,
      "trust": false,
      "env": {
        "K8S_API_MODE": "native",
        "KUBECONFIG": "C:\\Users\\SEU-USUARIO\\.kube\\config",
        "K8S_CONTEXT": "nome-do-contexto",
        "K8S_NAMESPACE": "saf",
        "K8S_READONLY": "true"
      }
    }
  }
}
```

Se o kubeconfig padrão já aponta para o contexto certo, `KUBECONFIG` e `K8S_CONTEXT` podem ser omitidos.

### 2) Kubernetes Dashboard (`K8S_API_MODE=dashboard`)

O portal **não** é a API do control plane. O MCP traduz para rotas no singular
(`/api/v1/namespace`, `/api/v1/pod/{ns}`, `/api/v1/log/{ns}/{pod}/{container}`, …).

`K8S_SERVER` = origem do portal **sem** `#/login`. `K8S_TOKEN` = Bearer do login (só no env local).

```json
{
  "mcpServers": {
    "k8s-dashboard": {
      "command": "mcp-kubernetes",
      "timeout": 120000,
      "trust": false,
      "env": {
        "K8S_API_MODE": "dashboard",
        "K8S_SERVER": "https://seu-dashboard.exemplo.interno",
        "K8S_TOKEN": "",
        "K8S_NAMESPACE": "saf",
        "K8S_SKIP_TLS_VERIFY": "true",
        "K8S_READONLY": "true"
      }
    }
  }
}
```

Deixe `K8S_TOKEN` vazio neste exemplo; cole o valor só em `settings.local.json`.

A tool do Gemini é a **mesma** em todos os clusters (`logs_pod`, `listar_pods`, …).
O HTTP por baixo muda:

| Recurso | API nativa / Rancher | Kubernetes Dashboard |
| --- | --- | --- |
| Namespaces | `/api/v1/namespaces` | `/api/v1/namespace` |
| Pods | `/api/v1/namespaces/{ns}/pods` | `/api/v1/pod/{ns}?itemsPerPage=` |
| Deployments | `/apis/apps/v1/namespaces/{ns}/deployments` | `/api/v1/deployment/{ns}` |
| ReplicaSets | `/apis/apps/v1/namespaces/{ns}/replicasets` | `/api/v1/replicaset/{ns}` |
| Services | `/api/v1/namespaces/{ns}/services` | `/api/v1/service/{ns}` |
| Events | `/api/v1/namespaces/{ns}/events` | `/api/v1/event/{ns}` |
| Logs | `/api/v1/namespaces/{ns}/pods/{pod}/log` | `/api/v1/log/{ns}/{pod}/{container}` |

**Logs:** no Rancher o SDK manda `tailLines` + `sinceSeconds` + `previous`.
No Dashboard o portal exige o **nome do container**; se a tool não receber, o MCP lê o pod e pega o primeiro. A resposta oficial é `{ logs: [ { timestamp, content } ] }` — o MCP junta só o `content`. `sinceSeconds` **não existe** no portal (só `previous` + janela por `offset`).

### 3) Auto (`K8S_API_MODE=auto`, padrão)

| Sinal | Backend |
| --- | --- |
| host contém `dashboard` | Dashboard |
| host contém `rancher` ou `/k8s/clusters/` | API nativa |
| `K8S_SERVER` + `K8S_TOKEN` sem kubeconfig | Dashboard |
| API nativa devolve `404 page not found` (HTML) | tenta Dashboard |
| resto | API nativa / kubeconfig |

### Vários clusters no mesmo Gemini

Uma entrada por portal. O token de um Dashboard **não** vale no outro.

```json
{
  "mcpServers": {
    "k8s-dashboard-a": {
      "command": "mcp-kubernetes",
      "timeout": 120000,
      "trust": false,
      "env": {
        "K8S_API_MODE": "dashboard",
        "K8S_SERVER": "https://dashboard-a.exemplo.interno",
        "K8S_TOKEN": "",
        "K8S_NAMESPACE": "saf",
        "K8S_SKIP_TLS_VERIFY": "true",
        "K8S_READONLY": "true"
      }
    },
    "k8s-dashboard-b": {
      "command": "mcp-kubernetes",
      "timeout": 120000,
      "trust": false,
      "env": {
        "K8S_API_MODE": "dashboard",
        "K8S_SERVER": "https://dashboard-b.exemplo.interno",
        "K8S_TOKEN": "",
        "K8S_NAMESPACE": "saf",
        "K8S_SKIP_TLS_VERIFY": "true",
        "K8S_READONLY": "true"
      }
    },
    "k8s-rancher": {
      "command": "mcp-kubernetes",
      "timeout": 120000,
      "trust": false,
      "env": {
        "K8S_API_MODE": "native",
        "K8S_CONTEXT": "local-saf",
        "K8S_NAMESPACE": "saf",
        "K8S_READONLY": "true"
      }
    }
  }
}
```

### Global tool (mínimo)

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
| `contexto_atual` | — | contexto kube, host, versão do cluster, modo leitura |
| `diagnosticar_acesso` | `namespace` | nativo: /version, pods, HPA v1/v2, metrics · dashboard: /namespace, /pod, /deployment, /event |
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
| `logs_pod` | `name`, `namespace`, `container`, `tail` (1–200), `sinceSeconds` (só nativo), `previous` | texto do log (Dashboard: `logs[].content`) |
| `uso_recursos_pods` | `namespace`, `name` | CPU/memória por container (metrics-server) |

`previous=true` lê o container que acabou de morrer — use em CrashLoop/OOM.

---

## Fluxo sugerido (investigação)

Não varra o cluster. Vá do sintoma ao recurso:

1. `contexto_atual` — confirmar **qual** cluster e a versão do servidor
   Se der 404: rode `diagnosticar_acesso` antes de listar pods
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
| `KUBECONFIG` | `~/.kube/config` | Caminho do kubeconfig (Rancher / API nativa) |
| `MCP_K8S_KUBECONFIG` | — | Alias de `KUBECONFIG` |
| `K8S_CONTEXT` | contexto atual do kubeconfig | Qual contexto usar |
| `K8S_NAMESPACE` | — | Namespace padrão (se a tool não receber `namespace`) |
| `K8S_API_MODE` | `auto` | `auto` · `native` (Rancher/API) · `dashboard` |
| `K8S_SERVER` | — | URL base do Dashboard (sem `#/login`). Só no env local |
| `K8S_TOKEN` | — | Bearer do portal. **Nunca** no pacote nem no git |
| `K8S_DASHBOARD_TOKEN` | — | Alias de `K8S_TOKEN` |
| `K8S_SKIP_TLS_VERIFY` | `false` | `true` se o portal usa certificado interno |
| `K8S_INSECURE_SKIP_TLS_VERIFY` | `false` | Alias de `K8S_SKIP_TLS_VERIFY` |
| `MCP_K8S_SERVER` / `MCP_K8S_TOKEN` / `MCP_K8S_API_MODE` | — | Aliases das variáveis `K8S_*` equivalentes |
| `K8S_READONLY` | `true` | Contrato de leitura (não há tools de escrita) |
| `K8S_MAX_LOG_LINES` | `200` | Teto de `tail` em `logs_pod` |
| `K8S_MAX_ROWS` | `200` | Teto de linhas em listagens |

`auto`: host com `dashboard` → API do portal; host Rancher/`/k8s/clusters/` → API nativa.
Se a API nativa devolver `404 page not found` (HTML do portal), o MCP tenta o Dashboard.

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
- Não coloque tokens, senhas nem URLs internas no git nem neste pacote
- Token do Dashboard / kubeconfig ficam só em `settings.local.json` (gitignored) ou no ambiente da máquina

Manifesto de exemplo (Role somente leitura):
https://github.com/SouzaMatheus-dev/mcp-kubernetes

---

## Problemas comuns

| Sintoma | Causa provável | O que fazer |
| --- | --- | --- |
| `/mcp list` sem `kubernetes` | tool não no PATH | Reabra o terminal; `dotnet tool install --global McpKubernetes` |
| `Forbidden` | RBAC sem get/list | Peça Role de leitura; o MCP não contorna |
| `Não encontrado` (404) | API antiga, namespace errado ou kubeconfig/Rancher | `diagnosticar_acesso`; veja a `url=` no erro |
| 404 em HPA | cluster sem `autoscaling/v2` | o MCP cai para `autoscaling/v1` sozinho |
| 404 em metrics | sem metrics-server | ignore `uso_recursos_pods` |
| 404 em `/version` ou em tudo | kubeconfig/Rancher (cluster id) errado **ou** URL de Dashboard na API nativa | `K8S_API_MODE=dashboard` + `K8S_SERVER`/`K8S_TOKEN` |
| Dashboard 401 | token do portal expirado | renove no login do Dashboard; atualize o env local |
| `Bloqueado: informe namespace` | sem `namespace` e sem `K8S_NAMESPACE` | Passe o namespace na tool ou no `env` |
| log vazio | pod novo ou container errado | `obter_pod`; tente `previous=true` |
| `element of type 'String' ... type 'Object'` | MCP &lt; 0.1.5 no Dashboard (k8s1/k8s3) | `dotnet tool update --global McpKubernetes` → 0.1.5+ e `/mcp reload` |
| `uso_recursos_pods` falha | sem metrics-server | Siga sem CPU/mem; não é bloqueante |
| contexto errado | vários clusters no kubeconfig | defina `K8S_CONTEXT` ou `KUBECONFIG` |

---

## Documentação extra

- Instalação: https://github.com/SouzaMatheus-dev/mcp-kubernetes/blob/main/docs/instalacao.md
- Configuração MCP: https://github.com/SouzaMatheus-dev/mcp-kubernetes/blob/main/docs/configuracao-mcp.md
- Exemplos: https://github.com/SouzaMatheus-dev/mcp-kubernetes/blob/main/docs/exemplos-uso.md

## Licença

MIT
