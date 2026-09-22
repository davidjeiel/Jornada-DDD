# Ambiente local — Podman no Windows/WSL2

> Guia prático. A decisão e o porquê estão em
> [ADR-0013](adr/0013-ambiente-local-podman.md); produção continua sendo Azure
> ([ADR-0011](adr/0011-infraestrutura-como-codigo.md)).

Hoje o ambiente local é `python run.py` e um arquivo SQLite. Isso vai virar seis
containers. A troca só se justifica por uma razão: **três das apostas mais
arriscadas da arquitetura — isolamento entre empresas por RLS, transaction
pooling e outbox transacional — não podem ser exercitadas com SQLite.** Se elas
só forem testadas em homologação, os bugs aparecem tarde e caros.

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
containers e espera Postgres e PgBouncer ficarem saudáveis. Depois:

```powershell
.\local\dev.ps1 testar-rls     # confirme o isolamento ANTES de escrever código
dotnet run --project src/hosts/Catalogo.AppHost
```

Dentro do WSL ou em Linux nativo, use `./local/dev.sh` com os mesmos comandos.

| Comando | O que faz |
|---|---|
| `subir` | Sobe tudo e espera ficar saudável |
| `descer` / `recriar` | Para; ou apaga volumes e recomeça do zero |
| `estado` | Tabela de endereços + status dos containers |
| `testar-rls` | **As 7 verificações de isolamento entre empresas** |
| `psql` | Shell SQL como `catalogo_app`, via PgBouncer |
| `pool` | `SHOW POOLS` — inspeciona o pooler durante um teste |
| `limpar` | Recolhe containers órfãos de Testcontainers |
| `logs [serviço]` | Segue os logs |

---

## 3. O mapa: local ↔ Azure

| Papel | Local (Podman) | Produção (Azure) | Paridade |
|---|---|---|---|
| Banco | `postgres:17-alpine` | Database for PostgreSQL Flexible Server | **Total** |
| Pooler | PgBouncer (container) | PgBouncer gerenciado | **Total** |
| Cache | Valkey 8 | Cache for Redis | Alta |
| Blob | Azurite | Blob Storage | Alta |
| Mensageria | Emulador oficial de Service Bus | Service Bus | Alta |
| Identidade | Keycloak | Entra External ID | **Parcial** — §6 |
| Busca | `tsvector` do Postgres | Azure AI Search | **Nenhuma** — §6 |
| Gateway | *ausente* | API Management | **Nenhuma** — §6 |
| Telemetria | Painel Aspire (OTLP) | Azure Monitor | Alta — mesmo protocolo |
| Execução | `dotnet run` nativo | Container Apps | Média — §4 |

### Endereços

```
PgBouncer      localhost:6432      <- A APLICAÇÃO CONECTA AQUI
PostgreSQL     localhost:5432         migrações, psql, DBeaver
Valkey         localhost:6379
Azurite        localhost:10000
Service Bus    localhost:5672 (AMQP) / :5300 (gestão, /health)
Keycloak       http://localhost:8080
Painel OTel    http://localhost:18888
```

**A porta 6432 não é detalhe.** Apontar a aplicação para 5432 funciona
perfeitamente e, em silêncio, deixa de exercitar o transaction pooling — que é
exatamente onde bug de contexto de tenant se esconde. Veja §5.

---

## 4. Containers para as dependências; `dotnet run` para a aplicação

```
Containers                    Processo nativo
──────────────────────        ───────────────────────────
PostgreSQL + PgBouncer        Catalogo.Api
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
podman compose -f local/compose.yaml --profile full up --build
```

Ele está comentado em `local/compose.yaml` até o `Dockerfile` existir — o que
acontece na Fase 1 ([ADR-0012](adr/0012-estrategia-de-migracao.md)).

---

## 5. O que o ambiente local existe para provar

### 5.1 Isolamento entre empresas

`.\local\dev.ps1 testar-rls` roda sete verificações:

| # | Verificação | Esperado |
|---|---|---|
| 1 | Sem contexto de tenant, não se vê nada | 0 linhas |
| 2 | Empresa A vê os 2 ativos dela | 2 |
| 3 | Empresa B vê só o dela | 1 |
| 4 | **Contexto morre no COMMIT** | 0 |
| 5 | `UPDATE` cruzando empresa não afeta nada | 0 |
| 6 | `INSERT` com `tenant_id` alheio é recusado pelo banco | erro de RLS |
| 7 | Nenhuma tabela com `tenant_id` sem RLS completa | vazio |

A verificação 4 é a razão de o PgBouncer não ser opcional aqui. Em *session
pooling* — ou sem pooler — a conexão pertence a uma requisição do início ao fim, e
um contexto de tenant vazado **nunca aparece**. Só em *transaction pooling* a
conexão volta ao pool entre transações e pode ser entregue à requisição de outra
empresa. O `set_config('app.tenant_id', ..., true)` existe por causa disso, e o
`true` (LOCAL) é o que faz o contexto morrer junto com a transação.

A verificação 7 é a rede contra o erro que de fato acontece: alguém cria uma
tabela nova seis meses depois e esquece a política. `plataforma.exigir_rls()`
levanta exceção nomeando as tabelas culpadas, e o teste de integração a chama.

### 5.2 Uma armadilha que custa caro se descoberta em produção

Na política de RLS, **não** escreva:

```sql
USING (tenant_id = current_setting('app.tenant_id', true)::uuid)   -- ERRADO
```

Depois que uma GUC personalizada é usada uma vez na sessão, "limpá-la" (fim da
transação com `set_config` LOCAL) devolve **string vazia, não NULL**. E `''::uuid`
não dá NULL — dá erro:

```
ERROR:  invalid input syntax for type uuid: ""
```

Ou seja: a requisição sem contexto **explodiria com erro de tipo** em vez de
simplesmente não enxergar nada. Use o auxiliar, que faz o `NULLIF`:

```sql
USING (tenant_id = plataforma.tenant_atual())                      -- CERTO
```

`tenant_id = NULL` é NULL, a política nega tudo, e a falha é fechada e limpa.
Este comportamento está verificado nas asserções de `testar-rls`.

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

**Busca.** Azure AI Search não tem emulador. Consequência favorável: na Fase 1 a
porta `IIndiceDeBusca` roda sobre `tsvector` do próprio Postgres, exatamente como
o [ADR-0004](adr/0004-persistencia-poliglota.md) já recomendava. A ausência de
emulador reforça uma decisão tomada por outro motivo. Se precisar exercitar um
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
| `short-name resolution enforced but cannot prompt` | Podman não assume Docker Hub | Registro explícito: `docker.io/library/postgres:17-alpine` |
| Container não acha o host | Podman usa outro nome | `host.containers.internal`, não `host.docker.internal` |
| `dotnet watch` não recarrega; tudo lento | Código em `/mnt/c/...` | Mova para `~/src/` no sistema de arquivos do WSL (§1.4) |
| `Container runtime 'podman' could not be found` | Aspire procura no PATH; alias de shell não vale | `sudo apt install -y podman` no WSL, ou link em `/usr/local/bin/podman` |
| Testcontainers não conecta | `DOCKER_HOST` ausente | Ligue Docker Compatibility (§1.2) e defina a variável |
| `NullReferenceException` em teste de integração | Ryuk não funciona em rootless | `TESTCONTAINERS_RYUK_DISABLED=true` |
| Containers acumulando | Ryuk desligado não recolhe | `.\local\dev.ps1 limpar` |
| `unsupported startup parameter: extra_float_digits` | Npgsql manda parâmetros que o PgBouncer recusa | Já tratado em `ignore_startup_parameters` |
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
├── postgres/
│   ├── 01-papeis.sql                 papéis SEM BYPASSRLS + auditoria de RLS
│   └── 02-sandbox-rls.sql            prova o isolamento antes de existir código .NET
├── pgbouncer/
│   ├── pgbouncer.ini                 transaction pooling
│   └── userlist.txt                  credenciais de brinquedo
├── servicebus/config.json            tópicos e assinaturas do ADR-0008
└── keycloak/realm-catalogo.json      2 empresas, 3 usuários
```

Acrescente ao `.gitignore`:

```gitignore
local/.env
local/dados/
```

`local/postgres/02-sandbox-rls.sql` é andaime: apague quando as migrações reais do
EF Core existirem. Ele serve para que, no dia 1 — antes de haver uma linha de C# —
já se possa provar que o ambiente reproduz a aposta de segurança do produto.

---

## 10. Critério de sucesso

Um desenvolvedor novo chega a "rodando" em **um comando**, como hoje acontece com
`python run.py`. Se o ambiente local exigir um documento de dez páginas para
funcionar, a decisão do ADR-0013 falhou — por mais correta que esteja no papel.

Meça o tempo de *clone → rodando*. Acima de 30 minutos, o gargalo precisa ser
atacado, não documentado.
