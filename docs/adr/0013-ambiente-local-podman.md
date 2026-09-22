# ADR-0013 — Usar Podman como runtime de containers no ambiente local, mantendo Azure em produção

- **Status:** proposto
- **Data:** 2026-09-22
- **Relacionados:** ADR-0002, ADR-0003, ADR-0004, ADR-0008, ADR-0011
- **Guia prático:** [`docs/AMBIENTE-LOCAL.md`](../AMBIENTE-LOCAL.md)

## Contexto

O ADR-0011 definiu produção: Terraform sobre Azure Container Apps, PostgreSQL
Flexible Server, Service Bus, Redis, AI Search, APIM. E mencionou `.NET Aspire`
para desenvolvimento local, sem dizer **qual runtime de containers** executa isso
na máquina do desenvolvedor.

Hoje o ambiente local é `python run.py` e um arquivo SQLite — zero containers,
zero atrito. Essa simplicidade é uma qualidade real do projeto e qualquer coisa que
a substitua precisa justificar o custo.

Restrições concretas:

- **Os desenvolvedores estão em Windows com WSL2.** Isso elimina boa parte da
  literatura de Podman, que assume Linux nativo, e traz problemas próprios de
  socket, caminho de volume e desempenho de I/O.
- **Docker Desktop tem licenciamento pago** para empresas acima de certo porte.
  Num produto que será vendido a empresas, o time acaba trabalhando dentro de
  clientes onde Docker Desktop simplesmente não é instalável.
- O ADR-0003 fez uma aposta de segurança em **RLS do PostgreSQL com PgBouncer em
  transaction pooling**. Se o ambiente local não reproduzir isso, os testes de
  isolamento entre empresas dão confiança falsa — que é pior que nenhuma.
- **Aspire e Testcontainers são peças do ADR-0011 e do ADR-0001**, e ambos nasceram
  assumindo Docker. Precisam ser validados com Podman antes de virarem dependência.

## Decisão

**Podman é o runtime de containers do ambiente local.** Azure Container Apps
continua sendo produção (ADR-0011), sem mudança.

Ambos executam a mesma imagem OCI construída do mesmo `Dockerfile`. A troca de
runtime é uma troca de *ferramenta de desenvolvimento*, não de arquitetura — e é
por isso que ela cabe num ADR próprio em vez de reabrir o ADR-0011.

### 1. Por que Podman

| Critério | Podman | Docker Desktop |
|---|---|---|
| Licença | Apache 2.0, sem custo | Pago acima de 250 funcionários ou US$ 10M de receita |
| Daemon | Nenhum — processo filho do usuário | Daemon privilegiado |
| Rootless | Padrão | Opcional, menos usado |
| Compatibilidade OCI | Total | Total |
| API Docker | Compatível (socket emulado) | Nativa |
| Instalável em cliente corporativo restrito | Geralmente sim | Frequentemente barrado |

O ponto do daemon não é ideológico: sem daemon root, um container comprometido no
ambiente de desenvolvimento não escala privilégio para a máquina. Para um time que
roda imagens de terceiros (Postgres, Keycloak, emuladores da Microsoft) todo dia,
isso tem valor concreto.

### 2. Containerizar as dependências, não a aplicação

Regra do dia a dia:

```
Containers (Podman)          Processo nativo (dotnet run)
─────────────────────        ────────────────────────────
PostgreSQL + PgBouncer       Catalogo.Api
Valkey (Redis)               Catalogo.Workers
Azurite (Blob)               Catalogo.AppHost  ← orquestra os containers
Service Bus emulator + MSSQL
Keycloak (OIDC)
Aspire Dashboard (OTel)
```

Rodar a API dentro de container em desenvolvimento mata *hot reload*, atrapalha o
depurador e torna cada ciclo 30 s mais lento. O `Catalogo.AppHost` (Aspire) sobe as
dependências via Podman e executa os projetos .NET nativamente.

Existe um perfil `full`, em que **tudo** roda em container, usado para duas coisas
específicas: validar o `Dockerfile` antes de subir para o registro, e reproduzir
localmente um bug que só aparece containerizado. Não é o modo padrão.

### 3. Interface: `compose.yaml`, não Quadlet

`local/compose.yaml` é a interface principal, por três razões:

- **Funciona sem Aspire.** Um contribuidor que só quer rodar os testes de
  integração faz `podman compose up -d` e pronto.
- **Funciona no CI**, que usa Docker (ver item 6) — o mesmo arquivo serve aos dois.
- **É o formato que todo mundo já lê.** Quadlet (unidades systemd) é excelente para
  servidor, mas exige systemd habilitado no WSL2, é específico de Podman e não roda
  no CI. Ganho nulo para custo real.

`podman kube play` também foi considerado e recusado pelo mesmo motivo: alinhar-se a
YAML de Kubernetes teria valor se produção fosse AKS, mas o ADR-0011 escolheu
Container Apps.

### 4. Paridade deliberada onde importa, divergência declarada onde não dá

O erro comum é perseguir paridade total e produzir um ambiente local pesado e
frágil. A decisão é escolher **onde a paridade é obrigatória**:

| Componente | Local | Paridade | Por quê |
|---|---|---|---|
| PostgreSQL 17 | `postgres:17-alpine` | **Obrigatória** | RLS, JSONB, CTE recursiva — o núcleo do ADR-0003/0004 |
| PgBouncer | container | **Obrigatória** | `set_config(...,true)` só é validado com transaction pooling |
| Service Bus | emulador oficial | **Obrigatória** | Outbox e idempotência (ADR-0008) precisam do AMQP real |
| Blob Storage | Azurite | Alta | API compatível |
| Redis | Valkey 8 | Alta | Protocolo idêntico |
| Identidade | Keycloak | **Média — e intencional** | Não é Entra; é OIDC padrão. Ver item 5 |
| Azure AI Search | **ausente** | Nenhuma | Ver item 5 |
| APIM | **ausente** | Nenhuma | Ver item 5 |
| Azure Monitor | Aspire Dashboard | Média | Ambos falam OpenTelemetry |

### 5. As três divergências assumidas, e o que se faz com cada uma

Declarar isso é mais honesto e mais útil do que fingir paridade.

**Identidade — Keycloak local × Entra External ID em produção.** A porta
`IProvedorDeIdentidade` do ADR-0001 existe exatamente para isso: os dois falam
OIDC, e o adaptador é trocado por configuração. O que *não* é testável localmente é
a federação com o IdP do cliente (ADR-0007) — isso exige um ambiente de homologação
com Entra real, e deve estar no plano da Fase 2.

**Busca — AI Search não tem emulador.** Consequência direta e favorável: na Fase 1,
`IIndiceDeBusca` é implementada sobre `tsvector` do próprio PostgreSQL, exatamente
como o ADR-0004 recomendou. Ou seja, a ausência de emulador *reforça* uma decisão já
tomada por outro motivo. Quando o adaptador de AI Search entrar, seus testes vivem
em homologação, não localmente.

**Gateway — APIM não tem equivalente local.** Cota, rate limit e assinatura de plano
(ADR-0009, ADR-0010) não são exercitados em dev. Mitigação: as políticas de APIM são
versionadas como código no Terraform e testadas em homologação; e os limites de
*negócio* (entitlements) ficam na aplicação — que roda local e é testável. A
fronteira do ADR-0010 — "gateway resolve tráfego, aplicação resolve regra" —
funciona a favor aqui.

### 6. O CI continua em Docker

Runners hospedados do GitHub Actions trazem Docker, não Podman. Tentar instalar
Podman neles adicionaria minutos a cada execução sem benefício.

Isso é aceitável porque a diferença fica contida em **duas variáveis de ambiente**
(`DOCKER_HOST` e `TESTCONTAINERS_RYUK_DISABLED`), e tudo mais — imagem, compose,
migrações, testes — é idêntico. A regra que mantém isso verdadeiro: **nenhum
`Dockerfile`, compose ou script pode usar recurso específico de um dos dois
runtimes.**

### 7. Testcontainers com Podman: a configuração e o preço dela

`Catalogo.Integracao.Testes` (ADR-0001) usa Testcontainers para subir PostgreSQL
real com RLS real. Com Podman rootless, duas coisas mudam:

```ini
# ~/.testcontainers.properties
docker.host=npipe:////./pipe/docker_engine   # Windows, com Docker Compatibility ligado
ryuk.disabled=true
```

**Ryuk** é o container que o Testcontainers usa para limpar containers órfãos. Ele
depende de montar o socket do Docker com privilégio, o que não funciona de forma
confiável em Podman rootless.

O preço de desligá-lo é real e precisa ser dito: **containers de teste vazam** se o
processo de teste for morto (Ctrl+C, crash, encerrar o depurador). Mitigações:

- `dev.ps1 limpar` faz `podman container prune` dos containers rotulados pelo
  Testcontainers — parte do fluxo normal, não heroísmo;
- no CI (Docker), Ryuk continua ligado, então a limpeza é automática lá;
- alternativa se o vazamento incomodar: `podman machine set --rootful` e
  `TESTCONTAINERS_RYUK_CONTAINER_PRIVILEGED=true`, trocando a limpeza automática
  pela perda do rootless. Não é o padrão recomendado.

### 8. Armadilhas do Podman que viram regra no repositório

Estas são específicas o bastante para custar horas de quem não souber:

1. **Toda imagem leva registro explícito.** Podman não assume Docker Hub: `postgres:17`
   é ambíguo e pode falhar ou resolver para outro registro. Use
   `docker.io/library/postgres:17-alpine`. Vale para `compose.yaml`, `Dockerfile`
   e Testcontainers.
2. **`host.containers.internal`, não `host.docker.internal`.** Podman usa o primeiro;
   mantém o segundo como alias, mas não conte com isso em script.
3. **O código fica no sistema de arquivos do WSL** (`~/src/...`), nunca em
   `/mnt/c/...`. I/O cruzando a fronteira Windows↔WSL é ordens de grandeza mais
   lento e quebra o *file watcher* do `dotnet watch`.
4. **Rootless não abre portas abaixo de 1024.** Nenhuma porta do projeto está
   abaixo disso — mantenha assim.
5. **Volume com `:Z` em SELinux.** Irrelevante no WSL2/Ubuntu, obrigatório em
   Fedora/RHEL. Mantenha nos arquivos para quem rodar em Linux nativo.
6. **Azure SQL Edge não serve mais.** O instalador oficial do emulador de Service
   Bus ainda o referencia, mas ele **foi aposentado em 30/09/2025**. Use
   `mcr.microsoft.com/mssql/server:2022-latest`.

### 9. O comando de entrada continua curto

O critério de sucesso deste ADR é que um desenvolvedor novo chegue ao "rodando" em
um comando, como hoje acontece com `python run.py`:

```powershell
.\local\dev.ps1 subir      # sobe dependências, aplica migrações, semeia 2 empresas
dotnet run --project src/hosts/Catalogo.AppHost
```

Se o ambiente local exigir um documento de dez páginas para funcionar, a decisão
falhou — por mais correta que esteja no papel.

## Consequências

### Positivas

- Sem custo de licença e sem bloqueio para trabalhar dentro de clientes que proíbem
  Docker Desktop.
- Rootless por padrão reduz a superfície de risco de rodar imagens de terceiros.
- RLS, PgBouncer e outbox — as três apostas mais arriscadas da arquitetura — passam
  a ser exercitadas na máquina do desenvolvedor, não só em homologação.
- A mesma imagem OCI roda local, no CI e no Azure.

### Negativas

- **O ambiente local deixa de ser `python run.py`.** São ~6 containers, alguns GB de
  imagem e uma configuração inicial de WSL2. É o custo real de sair de SQLite.
- **Ryuk desligado deixa containers órfãos.** Precisa de higiene periódica.
- **Podman em Windows tem mais arestas** que Docker Desktop. A curva inicial é
  maior e o suporte do Aspire a Podman é menos exercitado que a Docker.
- **Duas configurações de Testcontainers** (local Podman, CI Docker) — pequena, mas
  é divergência.
- **Consumo de memória**: MSSQL do emulador de Service Bus sozinho pede ~2 GB.
  Máquina com 16 GB é o piso prático.

### Neutras / a monitorar

- Se o Aspire apresentar incompatibilidade recorrente com Podman, o plano B é usar
  apenas `compose.yaml` e perder o painel do Aspire — o que é perda de conforto, não
  de arquitetura.
- O emulador de Service Bus não persiste entidades entre reinícios e tem limites
  (1 namespace, 50 entidades, 256 KB por mensagem). Suficiente para desenvolvimento;
  teste de carga de mensageria é em homologação.
- Vale medir o tempo de "clone → rodando". Acima de 30 minutos, o gargalo precisa
  ser atacado.

## Alternativas consideradas

**Docker Desktop.** Caminho mais suave, melhor suporte do Aspire, menos arestas no
Windows. Recusada pelo licenciamento e pela impossibilidade de instalação em parte
dos ambientes de cliente — restrições que não desaparecem com o tempo.

**Rancher Desktop com `dockerd` (moby).** Gratuito e com daemon Docker de verdade,
o que resolveria Ryuk e Testcontainers sem ajuste. Recusada por margem estreita:
traz um daemon privilegiado de volta e adiciona um k3s embutido que o projeto não
usa. **É a alternativa designada** se o atrito de Podman no Windows se mostrar maior
que o previsto.

**Dev Containers (VS Code) com Podman.** Padronizaria o ambiente inteiro por completo.
Recusada por ora: soma uma camada de indireção sobre um setup que já tem arestas, e
o desempenho de I/O sobre WSL2 piora. Reavaliar depois que o ambiente estabilizar.

**Serviços gerenciados do Azure também em desenvolvimento** (um Postgres por dev).
Paridade perfeita. Recusada: custo mensal por desenvolvedor, exige rede para
trabalhar, e torna o teste de integração lento e compartilhado.

**Nada de containers — Postgres instalado nativamente no WSL.** Mais leve.
Recusada: sem PgBouncer, sem emulador de Service Bus, e com deriva de versão entre
máquinas — que é exatamente o problema que container resolve.

## Revisitar quando

- O Aspire quebrar com Podman em duas versões consecutivas (→ Rancher Desktop).
- O tempo de "clone → rodando" passar de 30 minutos.
- Surgir emulador oficial de AI Search ou de APIM (→ reduzir a lista de divergências).
- O consumo de memória inviabilizar as máquinas do time (→ tornar o emulador de
  Service Bus opcional, com barramento em memória como padrão em dev).
