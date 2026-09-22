# ADR-0011 — Terraform sobre Azure Container Apps, com deployment stamps e observabilidade por tenant

- **Status:** proposto
- **Data:** 2026-09-22
- **Relacionados:** ADR-0002, ADR-0003, ADR-0006, ADR-0010

## Contexto

Hoje a infraestrutura é `python run.py` com `debug=True` na porta 5000, um arquivo
SQLite em `dados/` e um CI que roda `pytest -q` em push para `main`. Para
desenvolvimento local é exatamente o suficiente. Para um SaaS vendido a empresas,
falta tudo: ambientes, rede, segredo, identidade gerenciada, backup, DR, escala,
observabilidade e o caminho de promoção entre ambientes.

Duas escolhas dominam este ADR:

1. **Quem executa o container** — e aqui o risco é escolher Kubernetes por reflexo.
   AKS dá controle total e traz consigo upgrade de cluster, tuning de node pool,
   CNI, ingress, cert-manager, política de rede e, mais cedo do que se imagina, um
   service mesh. É trabalho de plataforma em tempo integral, que um time pequeno
   não tem para dar.
2. **Como descrever a infraestrutura** — Terraform ou Bicep, com implicações de
   portabilidade e de ferramental.

Há ainda um requisito que vem de trás: o ADR-0003 previu um tier Soberano com
*deployment stamp* completo, e o ADR-0010 precisa de **custo por tenant** para não
vender abaixo do custo. As duas coisas nascem do IaC ou não nascem.

## Decisão

### 1. Execução: Azure Container Apps

| | Container Apps | AKS |
|---|---|---|
| Operação do cluster | Nenhuma | Upgrade, node pool, CNI, ingress |
| Escala a zero | Sim (essencial para workers) | Só com KEDA configurado à mão |
| Autoescala por fila | KEDA embutido | KEDA a instalar e manter |
| Dapr | Opcional, embutido | A instalar |
| Revisões e tráfego dividido | Nativo (canário sem service mesh) | Requer Argo Rollouts ou mesh |
| Controle fino (DaemonSet, CRD, GPU, mesh) | Limitado | Total |
| Custo de plataforma | Baixo | Alto |

Os dois pontos decisivos para este produto: **escala a zero** para os workers de
outbox, SLA, snapshot e descoberta — que ficam ociosos a maior parte do dia — e
**divisão de tráfego nativa**, que dá canário sem infraestrutura adicional.

Container Apps roda sobre Kubernetes gerenciado; migrar para AKS depois é
reempacotar o mesmo container, não reescrever a aplicação. Isso torna a decisão
reversível a custo baixo, que é o melhor argumento para tomá-la agora.

**Gatilhos para migrar para AKS:** necessidade de operador Kubernetes customizado;
mesh com mTLS entre serviços por exigência de conformidade; workload que precise de
GPU ou de controle de nó; ou mais de ~8 serviços com topologia de rede complexa.

### 2. Descrição: Terraform

Escolhido sobre Bicep por: portabilidade (a decisão de nuvem pode mudar; o
conhecimento de Terraform não se perde), ecossistema de providers — **Stripe, APIM,
Entra e GitHub no mesmo plano de execução**, o que importa diretamente para o
ADR-0010 —, e `plan` mais legível em revisão de PR.

Custo assumido: recursos muito novos do Azure chegam ao provider com algum atraso,
e é preciso gerenciar estado remoto (que o Bicep não exige).

```
infra/
├── modules/
│   ├── rede/                 # VNet, subnets, private endpoints, NSG
│   ├── dados/                # PostgreSQL Flexible Server, Redis, AI Search, Storage
│   ├── aplicacao/            # Container Apps Environment + apps + revisões
│   ├── gateway/              # APIM, products, policies (ADR-0009, ADR-0010)
│   ├── identidade/           # Entra External ID, app registrations (ADR-0007)
│   ├── segredos/             # Key Vault + Managed Identity
│   └── observabilidade/      # Log Analytics, App Insights, alertas, dashboards
├── envs/
│   ├── dev/                  # escala mínima, sem redundância de zona
│   ├── hml/                  # espelho reduzido de produção
│   └── prd/                  # zonas redundantes, réplicas de leitura
└── stamps/
    └── <cliente>/            # tier Soberano (ADR-0003)
```

Estado remoto em Azure Storage com bloqueio. Autenticação do pipeline por **OIDC
federado** com o GitHub Actions — nenhum segredo de nuvem armazenado no repositório.

### 3. Deployment stamps

Um *stamp* é a unidade de escala replicável: rede + banco + Container Apps
Environment + gateway, com todo o resto parametrizado.

- **Padrão e Dedicado** compartilham o stamp regional.
- **Soberano** ganha um stamp próprio, criado a partir do mesmo módulo com outra
  variável.

Por isso `envs/prd` e `stamps/<cliente>` chamam os **mesmos módulos**. Se um stamp
de cliente virar um fork do Terraform, a decisão falhou.

O control plane (ADR-0006) fica **fora** dos stamps, em sua própria assinatura, e
mantém o mapa `tenant → stamp → connection string`.

### 4. Pipeline

```
PR → build + testes (unidade, arquitetura, contrato) + terraform plan comentado no PR
     ↓
merge em main → deploy dev → testes de integração (Testcontainers) → deploy hml → smoke
     ↓
tag de release → aprovação manual → deploy prd em canário (10% → 50% → 100%)
```

- **Migração de banco antes do código**, sempre compatível para trás (expandir →
  migrar → contrair, ADR-0004). Não existe janela de indisponibilidade em SaaS
  multiempresa.
- **Rollback é reverter a revisão** no Container Apps; migração de banco não é
  revertida, é compensada por uma migração nova.
- Um *stamp* efêmero por PR seria ideal, mas custa; comece com dev compartilhado e
  reavalie.

### 5. Observabilidade com `tenant_id` como dimensão

Esta é a decisão que o ADR-0010 exige e que costuma ser descoberta tarde demais.

- **OpenTelemetry** para trace, métrica e log, exportando para Azure Monitor.
- **Toda telemetria carrega `tenant_id`**, injetado no `Activity.Baggage` pelo mesmo
  middleware que resolve o contexto de tenant (ADR-0003). Se o `tenant_id` não
  estiver na telemetria desde o dia 1, responder "qual cliente está sofrendo?" e
  "este cliente dá lucro?" exige retrofit caro.
- `traceparent` reconciliado com `auditoria_evento.correlacao` (ADR-0008): dá para
  ir do evento de negócio auditado ao trace da requisição.
- **Nunca logar dado de negócio do cliente.** Id, tipo e quantidade; nunca conteúdo
  de ativo.

**SLOs iniciais** (a calibrar com dados reais):

| Serviço | SLO |
|---|---|
| API pública — leitura | p95 < 300 ms, disponibilidade 99,9% |
| API pública — escrita | p95 < 800 ms |
| Busca no catálogo | p95 < 500 ms |
| Latência da outbox | p99 < 30 s |
| Reindexação após publicação | p95 < 60 s |

**Custo por tenant** é relatório mensal obrigatório, cruzando consumo medido
(ADR-0010) com custo de infraestrutura rateado. Sem ele, não há como saber se um
cliente do tier Dedicado é lucrativo — e é assim que SaaS perde dinheiro em silêncio.

### 6. Segurança de infraestrutura

- Nenhuma senha em configuração: **Managed Identity** para Postgres, Service Bus,
  Storage e Key Vault.
- **Private endpoints** para os serviços de dados; nada de banco com IP público.
- `terraform plan` no PR com **Checkov** ou **tfsec** falhando em severidade alta.
- Imagem de container escaneada (Trivy) e assinada; registro privado.
- Backup: PITR de 35 dias no Postgres, restauração testada **trimestralmente** —
  backup não testado é backup que não existe.
- DR: RPO 15 min, RTO 4 h no tier Padrão. Réplica geográfica no Corporativo.

### 7. Desenvolvimento local

**.NET Aspire** sobe Postgres, Redis, Azurite e os serviços com `dotnet run` no
AppHost. O `dashboard` do Aspire dá trace e log local sem infraestrutura. É o
equivalente moderno do `python run.py` atual — e manter essa simplicidade de entrada
é o que mantém o projeto agradável de contribuir.

O **runtime de containers** que executa isso na máquina do desenvolvedor é decidido
em [ADR-0013](0013-ambiente-local-podman.md): **Podman**, rootless, em Windows/WSL2,
com Docker no CI. A mesma imagem OCI roda local, no CI e no Container Apps — a troca
de runtime é de ferramenta de desenvolvimento, não de arquitetura, e por isso não
reabre este ADR. O passo a passo está em [`docs/AMBIENTE-LOCAL.md`](../AMBIENTE-LOCAL.md).

## Consequências

### Positivas

- Ambiente reproduzível e auditável; nada provisionado à mão.
- O tier Soberano do ADR-0003 passa a ser configuração, não projeto.
- Escala a zero nos workers reduz custo de forma relevante em contas pequenas.
- Custo por tenant e saúde por tenant ficam disponíveis desde o início.

### Negativas

- **Curva de Terraform + Azure** para um time vindo de `python run.py`.
- **Custo fixo mensal** mesmo sem cliente: APIM, Postgres, Log Analytics e Redis
  têm piso. Dimensione dev e hml para o mínimo.
- **Limitações do Container Apps** podem aparecer tarde (rede, sidecar, tamanho de
  imagem). Risco aceito com gatilho de migração definido.
- Estado do Terraform é ativo crítico: perder ou corromper é incidente sério.

### Neutras / a monitorar

- Avaliar `azd` (Azure Developer CLI) com Aspire para dev/hml — produtividade alta —
  mantendo Terraform como fonte de verdade em produção.
- Reserved Instances e Savings Plans devem entrar na conta assim que a carga
  estabilizar; podem cortar 30–40% do custo de compute e banco.

## Alternativas consideradas

**AKS desde o início.** Controle total e portabilidade entre nuvens. Recusada: o
custo de operação não se paga com um time pequeno e sem demanda de recursos
avançados. Gatilhos de migração registrados acima.

**Azure App Service.** Mais simples ainda. Recusada: sem escala a zero por fila,
sem modelo de revisão adequado e menos natural para workloads em container.

**Serverless puro (Azure Functions + Cosmos).** Custo próximo de zero sem uso.
Recusada: partida a frio na API interativa e desalinhamento com o ADR-0002/0004.

**Bicep em vez de Terraform.** Nativo, sem estado para gerenciar, e recursos novos
no dia do lançamento. Recusada por margem estreita: providers de terceiros
(Stripe, GitHub) no mesmo plano e portabilidade pesaram mais. Escolha defensável
nos dois sentidos — se o time tiver experiência prévia em Bicep, inverta.

**Pulumi.** IaC em C#, mesma linguagem do resto (ADR-0002). Tentador. Recusada:
comunidade e material de referência menores; `terraform plan` em PR é mais fácil de
revisar do que um diff de programa.

## Revisitar quando

- Qualquer gatilho de migração para AKS disparar.
- O custo de infraestrutura passar de ~25% da receita recorrente.
- O primeiro cliente do tier Soberano for contratado — o stamp precisa estar
  exercitado **antes** da assinatura, não depois.
