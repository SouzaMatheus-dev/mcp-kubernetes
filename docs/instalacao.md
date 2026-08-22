# Instalação

## Requisitos

- .NET **8+** SDK (global tool) ou .NET **10+** (`dnx`)
- kubeconfig com permissão de leitura
- Acesso de rede à API do cluster

## Global tool

```powershell
dotnet tool install --global McpKubernetes
mcp-kubernetes
```

Atualizar:

```powershell
dotnet tool update --global McpKubernetes
```

## dnx (.NET 10+)

Não instala ferramenta permanente. O cliente MCP baixa e executa:

```powershell
dotnet dnx McpKubernetes --yes
```

## Verificar

```powershell
kubectl config current-context
kubectl auth can-i get pods
kubectl auth can-i create deployments
```

Esperado: `get pods` = yes. `create deployments` = no.

No Gemini:

```text
/mcp list
```

O servidor `kubernetes` deve listar só tools de leitura (`contexto_atual`, `listar_pods`, `logs_pod`, …).
