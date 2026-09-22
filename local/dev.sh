#!/usr/bin/env bash
# Ambiente local do Catálogo DDD sobre Podman (ADR-0013).
# Equivalente ao dev.ps1, para quem trabalha dentro do WSL ou em Linux nativo.
#
#   ./local/dev.sh subir | descer | recriar | estado | testar-rls | limpar | psql | pool | logs [serviço]

set -euo pipefail

RAIZ="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE="$RAIZ/compose.yaml"
EMPRESA_A=11111111-1111-1111-1111-111111111111
EMPRESA_B=22222222-2222-2222-2222-222222222222

azul()   { printf '  \033[36m%s\033[0m\n' "$*"; }
ok()     { printf '  \033[32mOK  \033[0m %s\n' "$*"; }
aviso()  { printf '  \033[33m!   \033[0m %s\n' "$*"; }
erro()   { printf '  \033[31mX   \033[0m %s\n' "$*"; }

# Roda psql de dentro do container do Postgres: não exige cliente instalado.
psql_app() {
  podman exec -e PGPASSWORD=dev_app catalogo-postgres \
    psql -h pgbouncer -p 6432 -U catalogo_app -d catalogo -tA -c "$1"
}
psql_migrador() {
  podman exec -e PGPASSWORD=dev_migrador catalogo-postgres \
    psql -h postgres -p 5432 -U catalogo_migrador -d catalogo -tA -c "$1"
}

# psql imprime os rótulos de comando (BEGIN, COMMIT, SET, UPDATE n...) junto
# com os resultados, mesmo com -tA. Sem filtrar, `tail -1` devolve "COMMIT"
# em vez do número — e a verificação passa a comparar lixo.
valor() { grep -Ev '^(BEGIN|COMMIT|ROLLBACK|SET|INSERT|UPDATE|DELETE|SELECT [0-9])' | grep -v '^$' | tail -1; }

verificar() {
  azul 'Verificando pré-requisitos...'
  command -v podman >/dev/null || { erro 'podman não encontrado'; return 1; }
  ok "podman $(podman --version | grep -oE '[0-9]+\.[0-9]+\.[0-9]+')"

  if [[ -z "${DOCKER_HOST:-}" ]]; then
    aviso 'DOCKER_HOST não definido — Testcontainers pode não achar o Podman.'
    aviso "  export DOCKER_HOST=unix://\${XDG_RUNTIME_DIR}/podman/podman.sock"
    aviso '  systemctl --user enable --now podman.socket'
  else
    ok "DOCKER_HOST = $DOCKER_HOST"
  fi
  [[ "${TESTCONTAINERS_RYUK_DISABLED:-}" == "true" ]] \
    || aviso 'TESTCONTAINERS_RYUK_DISABLED != true — Ryuk falha em rootless.'

  # No WSL: código fora do sistema de arquivos do Linux mata o desempenho e o
  # file watcher do `dotnet watch`.
  if grep -qi microsoft /proc/version 2>/dev/null && [[ "$RAIZ" == /mnt/[a-z]/* ]]; then
    aviso "O repositório está em $RAIZ (disco do Windows)."
    aviso 'Mova para ~/src/ no sistema de arquivos do WSL: I/O cruzando a'
    aviso 'fronteira é ordens de grandeza mais lento e quebra o hot reload.'
  fi

  [[ -f "$RAIZ/.env" ]] || { cp "$RAIZ/.env.exemplo" "$RAIZ/.env"; ok 'local/.env criado'; }
}

subir() {
  verificar || true
  azul 'Subindo containers...'
  podman compose -f "$COMPOSE" up -d

  azul 'Aguardando Postgres...'
  for _ in $(seq 1 60); do
    podman exec catalogo-postgres pg_isready -U postgres -d catalogo >/dev/null 2>&1 && break
    sleep 2
  done
  ok 'Postgres pronto'

  azul 'Aguardando PgBouncer...'
  for _ in $(seq 1 30); do psql_app 'SELECT 1' >/dev/null 2>&1 && break; sleep 2; done
  ok 'PgBouncer pronto (porta 6432)'

  aviso 'Service Bus leva ~60 s a mais (o MSSQL sobe antes).'
  estado
  echo
  azul 'Próximos passos:'
  echo '    dotnet run --project src/hosts/Catalogo.AppHost'
  echo '    ./local/dev.sh testar-rls'
}

estado() {
  cat <<'TABELA'

  SERVIÇO            ENDEREÇO                   PAPEL
  ─────────────────  ─────────────────────────  ──────────────────────────
  PgBouncer          localhost:6432             <- a APLICAÇÃO usa esta
  PostgreSQL         localhost:5432             migrações, psql, DBeaver
  Valkey (Redis)     localhost:6379             cache, rate limit
  Azurite (Blob)     localhost:10000            evidências, exports
  Service Bus        localhost:5672 / :5300     outbox (AMQP)
  Keycloak           http://localhost:8080      OIDC (no lugar do Entra)
  Painel OTel        http://localhost:18888     traces e logs

TABELA
  podman compose -f "$COMPOSE" ps --format 'table {{.Names}}\t{{.Status}}' 2>/dev/null || true
}

testar_rls() {
  azul 'Verificando o isolamento entre empresas (ADR-0003)...'; echo
  falhas=0
  checar() {
    local nome="$1" sql="$2" esperado="$3" obtido
    obtido="$(psql_app "$sql" | valor | tr -d '[:space:]')"
    if [[ "$obtido" == "$esperado" ]]; then ok "$nome -> $obtido"
    else erro "$nome -> esperado '$esperado', obtido '$obtido'"; falhas=$((falhas+1)); fi
  }

  checar 'sem contexto não vê nada       ' \
    'SELECT count(*) FROM sandbox.ativo_exemplo' 0
  checar 'Empresa A vê os 2 ativos dela  ' \
    "SELECT set_config('app.tenant_id','$EMPRESA_A',true); SELECT count(*) FROM sandbox.ativo_exemplo" 2
  checar 'Empresa B vê só o dela         ' \
    "SELECT set_config('app.tenant_id','$EMPRESA_B',true); SELECT count(*) FROM sandbox.ativo_exemplo" 1

  # O teste que justifica o PgBouncer existir localmente: o COMMIT explícito
  # encerra a transação, o contexto LOCAL morre com ela, e a mesma conexão —
  # que o pooler devolveria a outra requisição — volta a não ver nada.
  checar 'contexto morre no COMMIT       ' \
    "BEGIN; SELECT set_config('app.tenant_id','$EMPRESA_A',true); COMMIT; SELECT count(*) FROM sandbox.ativo_exemplo" 0

  if psql_app "BEGIN; SELECT set_config('app.tenant_id','$EMPRESA_A',true); INSERT INTO sandbox.ativo_exemplo (tenant_id,codigo,nome) VALUES ('$EMPRESA_B','X-1','invasor'); COMMIT" 2>&1 \
       | grep -qi 'row-level security'; then
    ok 'INSERT com tenant alheio é recusado'
  else
    erro 'INSERT com tenant alheio NÃO foi recusado'; falhas=$((falhas+1))
  fi

  checar 'UPDATE cruzando empresa é nulo ' \
    "SELECT set_config('app.tenant_id','$EMPRESA_A',true); UPDATE sandbox.ativo_exemplo SET nome='x' WHERE tenant_id='$EMPRESA_B'; SELECT count(*) FROM sandbox.ativo_exemplo WHERE nome='x'" 0

  orfas="$(psql_app "SELECT coalesce(string_agg(schema_nome||'.'||tabela,', '),'-') FROM plataforma.auditoria_rls() WHERE tem_coluna_tenant AND NOT (rls_ligado AND rls_forcado AND tem_politica)" | valor | tr -d '[:space:]')"
  [[ "$orfas" == "-" ]] && ok 'nenhuma tabela com tenant_id sem RLS' \
    || { erro "tabelas desprotegidas: $orfas"; falhas=$((falhas+1)); }

  bypass="$(psql_migrador "SELECT count(*) FROM pg_roles WHERE rolname='catalogo_app' AND (rolbypassrls OR rolsuper)" | valor | tr -d '[:space:]')"
  [[ "$bypass" == "0" ]] && ok 'catalogo_app não contorna RLS' \
    || { erro 'catalogo_app PODE contornar RLS'; falhas=$((falhas+1)); }

  echo
  if [[ $falhas -eq 0 ]]; then ok 'Isolamento entre empresas íntegro.'
  else erro "$falhas verificação(ões) falharam. NÃO prossiga sem entender."; exit 1; fi
}

limpar() {
  # Ryuk fica desligado em rootless (ADR-0013 §7): a limpeza é manual e rotineira.
  azul 'Removendo containers órfãos de Testcontainers...'
  podman ps -aq --filter 'label=org.testcontainers=true' | xargs -r podman rm -f >/dev/null
  podman container prune -f >/dev/null
  podman volume prune -f >/dev/null
  ok 'limpo'
}

case "${1:-estado}" in
  verificar)  verificar ;;
  subir)      subir ;;
  descer)     podman compose -f "$COMPOSE" down ;;
  recriar)    podman compose -f "$COMPOSE" down -v; subir ;;
  estado)     estado ;;
  testar-rls) testar_rls ;;
  limpar)     limpar ;;
  logs)       podman compose -f "$COMPOSE" logs ${2:+-f "$2"} ${2:---tail=50} ;;
  psql)       podman exec -it -e PGPASSWORD=dev_app catalogo-postgres \
                psql -h pgbouncer -p 6432 -U catalogo_app -d catalogo ;;
  pool)       podman exec -e PGPASSWORD=dev_migrador catalogo-postgres \
                psql -h pgbouncer -p 6432 -U catalogo_migrador -d pgbouncer -c 'SHOW POOLS' ;;
  *)          erro "comando desconhecido: $1"; exit 1 ;;
esac
