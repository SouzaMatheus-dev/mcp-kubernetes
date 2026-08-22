# Exemplos de uso

O agente deve começar pelo escopo mínimo e só então buscar logs/events.

```text
contexto_atual()
listar_namespaces()
listar_deployments(namespace="saf-prod")
listar_pods(namespace="saf-prod", labelSelector="app=payment-api")
obter_pod(name="payment-api-7d9f", namespace="saf-prod")
listar_eventos(namespace="saf-prod", involvedObject="payment-api-7d9f")
logs_pod(name="payment-api-7d9f", namespace="saf-prod", tail=150, previous=true)
```

## Sintomas

| Sintoma | Sequência |
| --- | --- |
| API 500 | Service → pods Ready → logs → events |
| Pod reiniciando | `obter_pod` → events → `logs_pod previous=true` |
| Deploy ruim | `obter_deployment` → ReplicaSets → pods |
| Lentidão | events + `uso_recursos_pods` (se metrics-server existir) |

## Secret

`obter_recurso(kind="Secret", name="app-secret", namespace="saf-prod")` devolve nome, tipo e chaves. Nunca o `data`.
