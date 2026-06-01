# Projeto Webhook

Webhook em F# (Suave) com um script de teste em Python que simula eventos de pagamento e valida confirmacoes/cancelamentos.

## Requisitos

- .NET SDK (para o projeto F#)
- Python 3.10+ (para o script de teste)

## Estrutura

- `WebhookPayment/` - servidor webhook em F#
- `test_webhook.py` - script de teste que envia webhooks e sobe um servidor local

## Como executar o webhook

```powershell
dotnet run --project WebhookPayment
```

O servidor sobe em `http://localhost:5000`.

## Como rodar os testes

Em outro terminal, com o webhook em execucao:

```powershell
python test_webhook.py
```

O script:
- envia requisicoes para `http://localhost:5000/webhook`
- sobe um servidor local em `http://127.0.0.1:5001` para receber `/confirmar` e `/cancelar`

## Parametros opcionais do teste

Voce pode passar argumentos na linha de comando:

```powershell
python test_webhook.py <event> <transaction_id> <amount> <currency> <timestamp> <token>
```

Exemplo:

```powershell
python test_webhook.py payment_success abc123 49.90 BRL 2026-06-01T12:00:00Z meu-token-secreto
```

## Token

O token esperado e `meu-token-secreto` e deve ser enviado no header `X-Webhook-Token`.
