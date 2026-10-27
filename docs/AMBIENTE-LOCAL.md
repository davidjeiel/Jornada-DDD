# Ambiente local — Podman no Windows/WSL2

> Guia prático. A decisão e o porquê estão em
> [ADR-0013](adr/0013-ambiente-local-podman.md); produção continua sendo Azure
> ([ADR-0011](adr/0011-infraestrutura-como-codigo.md)).

No painel-ddd, o ambiente local é `python run.py` e um banco local. Aqui ele
vira containers. A troca só se justifica por uma razão: **as apostas mais
arriscadas da arquitetura — isolamento entre empresas pela chave de partição,
lote atômico por ativo e outbox — só se exercitam contra o Cosmos DB de verdade**
(o emulador). Se elas só forem testadas em homologação, os bugs aparecem tarde e
caros.

---

## 1. Instalação (uma vez)

### 1.1 Podman

Instale o **Podman Desktop** (`winget install RedHat.Podman-Desktop`) e, nele,
crie a máquina:

```powershell
podman machine init --cpus 4 --memory 8192 --disk-size 60
podman machine start
podman info | Select-String -Pattern 'rootless|version'
```

8 GB para a máquina é o mínimo prático: só o MSSQL exigido pelo emulador de
Service Bus pede cerca de 2 GB.

### 1.2 Ligue a compatibilidade com Docker

No Podman Desktop: **Settings → Docker Compatibility → ativar**. Isso expõe o
named pipe que Testcontainers e o Aspire procuram. Confira:

```powershell
docker context list        # deve apontar para npipe:////./pipe/docker_engine
```

### 1.3 Variáveis de ambiente do usuário

```powershell
[Environment]::SetEnvironmentVariable('ASPIRE_CONTAINER_RUNTIME','podman','User')
[Environment]::SetEnvironmentVariable('DOCKER_HOST','npipe:////./pipe/docker_engine','User')
[Environment]::SetEnvironmentVariable('TESTCONTAINERS_RYUK_DISABLED','true','User')
```

Reabra o terminal. Copie também
`local/testcontainers.properties.exemplo` para
`%USERPROFILE%\.testcontainers.properties`.

### 1.4 Onde o repositório fica — isto importa mais do que parece

```
BOM:   \\wsl$\Ubuntu\home\voce\src\painel-ddd     (sistema de arquivos do Linux)
RUIM:  C:\Users\voce\src\painel-ddd               (disco do Windows, via /mnt/c)
```

I/O atravessando a fronteira Windows↔WSL é ordens de grandeza mais lento, e o
*file watcher* do `dotnet watch` simplesmente não dispara. É a causa número um de
"meu ambiente está lentíssimo" — e quase nunca é a primeira suspeita.

---

## 2. Subir

```powershell
git clone https://github.com/davidjeiel/painel-ddd
cd painel-ddd
.\local\dev.ps1 subir
```

O script verifica pré-requisitos, cria `local/.env` a partir do exemplo, sobe os
containers e espera o emulador do Cosmos DB ficar saudável. Depois:

```powershell
.\local\dev.ps1 testar-isolamento   # confirme o isolamento ANTES de escrever código
dotnet run --project src/hosts/Catalogo.AppHost
```

Dentro do WSL ou em Linux nativo, use `./local/dev.sh` com os mesmos comandos.

| Comando | O que faz |
|---|---|
| `subir` | Sobe tudo e espera ficar saudável |
| `descer` / `recriar` | Para; ou apaga volumes e recomeça do zero |
| `estado` | Tabela de endereços + status dos containers |
| `testar-isolamento` | **A suíte de contrato contra o emulador**: isolamento entre empresas em toda porta |
| `limpar` | Recolhe containers órfãos de Testcontainers |
| `logs [serviço]` | Segue os logs |

---

## 3. O mapa: local ↔ Azure

| Papel | Local (Podman) | Produção (Azure) | Paridade |
|---|---|---|---|
| Banco | Emulador do Cosmos DB (vNext) | Azure Cosmos DB, API NoSQL | **Alta** — sem RU nem índices; §6 |
| Cache | Valkey 8 | Cache for Redis | Alta |
| Blob | Azurite | Blob Storage | Alta |
| Mensageria | Emulador oficial de Service Bus | Service Bus | Alta |
| Identidade | Keycloak | Entra External ID | **Parcial** — §6 |
| Busca | Consultas do próprio Cosmos | Azure AI Search | **Nenhuma** — §6 |
| Gateway | *ausente* | API Management | **Nenhuma** — §6 |
| Telemetria | Painel Aspire (OTLP) | Azure Monitor | Alta — mesmo protocolo |
| Execução | `dotnet run` nativo | Container Apps | Média — §4 |

### Endereços

```
Cosmos DB      https://localhost:8081   <- A APLICAÇÃO CONECTA AQUI
Data Explorer  http://localhost:1234       consultar documentos
Valkey         localhost:6379
Azurite        localhost:10000
Service Bus    localhost:5672 (AMQP) / :5300 (gestão, /health)
Keycloak       http://localhost:8080
Painel OTel    http://localhost:18888
```

**HTTPS não é detalhe.** O SDK .NET não fala HTTP com o emulador; o compose já o
sobe com `PROTOCOL=https`, e a aplicação só aceita o certificado autoassinado com
`Cosmos__Emulador=true`.

---

## 4. Containers para as dependências; `dotnet run` para a aplicação

```
Containers                    Processo nativo
──────────────────────        ───────────────────────────
Cosmos DB (emulador)          Catalogo.Api
Valkey, Azurite               Catalogo.Workers
Service Bus + MSSQL           Catalogo.AppHost  ← orquestra os containers
Keycloak, painel OTel
```

Rodar a API em container durante o desenvolvimento mata o *hot reload*, complica
o depurador e acrescenta uns 30 s a cada ciclo. O Aspire sobe as dependências via
Podman e executa os projetos .NET nativamente.

Há um perfil `full`, em que **tudo** roda em container, reservado a dois casos e
só a eles: validar o `Dockerfile` antes de publicar no ACR, e reproduzir um bug
que só aparece containerizado.

```powershell
podman compose -f local/compose.yaml --profile full up --build cosmos api frontend
```

Front em `http://localhost:5173`, API em `http://localhost:5080`. A API sobe
com a carga de demonstração ligada (`Demonstracao__Carregar`): o cenário piloto
do painel-ddd em cada empresa de teste, gravado pelos casos de uso e só em
empresa vazia. Para recomeçar do zero, apague o volume do emulador
(`.\local\dev.ps1 recriar`).

---

## 5. O que o ambiente local existe para provar

### 5.1 Isolamento entre empresas

O Cosmos não tem RLS: o isolamento é imposto pelo adaptador (ADR-0016 §4).
`.\local\dev.ps1 testar-isolamento` roda a suíte de contrato contra o emulador.
Cada teste usa empresas novas, e para toda porta (ativos, relações, validações)
confere que:

| Verificação | Esperado |
|---|---|
| Empresa B lê, lista, busca por código ou checa nome de ativo da A | nada |
| Empresa B lista relações ou validações da A | nada |
| Sessão da empresa B grava ativo em nome da A | recusado, nada gravado |
| Duas edições simultâneas do mesmo ativo | a segunda recebe `CONFLITO_DE_EDICAO` |
| Lote com uma operação que falha | nada do lote entra |

A rede contra o erro que de fato acontece — alguém, meses depois, consultar o
Cosmos por fora do `AcessoDoTenant` — é a guarda em
`tests/Jornada.Arquitetura.Testes`, que lê a IL do adaptador e quebra o build.

### 5.2 O emulador não é a conta real

Ele responde à mesma API, mas não cobra RU, não usa índices de intervalo ou
compostos e não valida autenticação. Desempenho, custo e permissões (identidade
gerenciada com papel de dados de menor privilégio) só se verificam numa conta
Cosmos DB no Azure.

### 5.3 Outbox de verdade

O emulador de Service Bus fala AMQP real, com entrega pelo menos uma vez, lock,
`MaxDeliveryCount` e dead-letter. É o que permite testar localmente que os
consumidores são idempotentes — coisa que um barramento em memória nunca
revelaria. A topologia (tópicos e assinaturas do ADR-0008) está em
`local/servicebus/config.json`; as entidades não sobrevivem ao reinício do
container, e é esse arquivo que as recria.

---

## 6. As três divergências assumidas

Declarar isso é mais útil do que fingir paridade.

**Identidade.** Keycloak não é Entra External ID — é OIDC padrão, que é o que a
porta `IProvedorDeIdentidade` consome. O realm traz duas empresas e três usuários,
incluindo dois da mesma empresa, para exercitar a segregação de função (quem
submete não decide). **Não se testa localmente:** federação com o IdP do cliente,
passkeys e políticas de MFA — isso exige homologação com Entra real, e está na
Fase 2.

**Busca.** Azure AI Search não tem emulador. Na Fase 1 a busca roda sobre
consultas do próprio Cosmos (ADR-0016), e a ausência de emulador reforça esse
adiamento. Se precisar exercitar um
índice externo, `--profile busca` sobe um OpenSearch.

**Gateway.** APIM não tem equivalente local, então cota, rate limit e assinatura
de plano não são exercitados em dev. Mitigação: as políticas do APIM são código
versionado no Terraform e testadas em homologação — e os limites de **negócio**
(entitlements) ficam na aplicação, que roda local. A fronteira do
[ADR-0010](adr/0010-monetizacao-medicao-uso.md) — "gateway resolve tráfego,
aplicação resolve regra" — funciona a favor aqui.

---

## 7. Armadilhas do Podman

| Sintoma | Causa | Correção |
|---|---|---|
| `short-name resolution enforced but cannot prompt` | Podman não assume Docker Hub | Registro explícito: `mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-latest` |
| Container não acha o host | Podman usa outro nome | `host.containers.internal`, não `host.docker.internal` |
| `dotnet watch` não recarrega; tudo lento | Código em `/mnt/c/...` | Mova para `~/src/` no sistema de arquivos do WSL (§1.4) |
| `Container runtime 'podman' could not be found` | Aspire procura no PATH; alias de shell não vale | `sudo apt install -y podman` no WSL, ou link em `/usr/local/bin/podman` |
| Testcontainers não conecta | `DOCKER_HOST` ausente | Ligue Docker Compatibility (§1.2) e defina a variável |
| `NullReferenceException` em teste de integração | Ryuk não funciona em rootless | `TESTCONTAINERS_RYUK_DISABLED=true` |
| Containers acumulando | Ryuk desligado não recolhe | `.\local\dev.ps1 limpar` |
| `The SSL connection could not be established` | Emulador em HTTP, ou certificado autoassinado recusado | `PROTOCOL=https` no emulador e `Cosmos__Emulador=true` na aplicação — já no compose |
| `permission denied` em volume (Fedora/RHEL) | Rótulo do SELinux | Sufixo `:Z` — já presente no compose |
| Porta abaixo de 1024 recusada | Rootless não abre portas privilegiadas | Nenhuma porta do projeto está abaixo disso; mantenha assim |
| Service Bus não sobe | O instalador oficial ainda cita `azure-sql-edge`, **aposentado em 30/09/2025** | O compose já usa `mssql/server:2022-latest` |

---

## 8. CI continua em Docker

Runners hospedados do GitHub Actions trazem Docker, não Podman. Instalar Podman
neles custaria minutos por execução sem benefício.

A divergência é aceitável porque cabe em **duas variáveis de ambiente**:

| | Local | CI |
|---|---|---|
| Runtime | Podman rootless | Docker |
| `DOCKER_HOST` | npipe / socket do usuário | padrão |
| `TESTCONTAINERS_RYUK_DISABLED` | `true` | `false` (Ryuk funciona e limpa) |
| Imagem, compose, migrações, testes | idênticos | idênticos |

A regra que mantém isso verdadeiro: **nenhum `Dockerfile`, compose ou script pode
usar recurso específico de um dos dois runtimes.**

---

## 9. Arquivos

```
local/
├── compose.yaml                      topologia; roda em Podman e em Docker
├── dev.ps1                           Windows
├── dev.sh                            WSL / Linux
├── .env.exemplo                      copie para local/.env (não versionado)
├── testcontainers.properties.exemplo copie para %USERPROFILE%
├── servicebus/config.json            tópicos e assinaturas do ADR-0008
└── keycloak/realm-catalogo.json      2 empresas, 3 usuários
```

Acrescente ao `.gitignore`:

```gitignore
local/.env
local/dados/
```

---

## 10. Critério de sucesso

Um desenvolvedor novo chega a "rodando" em **um comando**, como hoje acontece com
`python run.py`. Se o ambiente local exigir um documento de dez páginas para
funcionar, a decisão do ADR-0013 falhou — por mais correta que esteja no papel.

Meça o tempo de *clone → rodando*. Acima de 30 minutos, o gargalo precisa ser
atacado, não documentado.
