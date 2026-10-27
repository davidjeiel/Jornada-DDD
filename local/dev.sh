#!/usr/bin/env bash
# Ambiente local do Catálogo DDD sobre Podman (ADR-0013).
# Equivalente ao dev.ps1, para quem trabalha dentro do WSL ou em Linux nativo.
#
#   ./local/dev.sh subir | descer | recriar | estado | testar-isolamento | limpar | logs [serviço]

set -euo pipefail

RAIZ="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE="$RAIZ/compose.yaml"

azul()   { printf '  \033[36m%s\033[0m\n' "$*"; }
ok()     { printf '  \033[32mOK  \033[0m %s\n' "$*"; }
aviso()  { printf '  \033[33m!   \033[0m %s\n' "$*"; }
erro()   { printf '  \033[31mX   \033[0m %s\n' "$*"; }

# Chave PÚBLICA e fixa do emulador do Cosmos (documentada pela Microsoft).
COSMOS_LOCAL='AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw=='

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

  azul 'Aguardando o emulador do Cosmos DB...'
  for _ in $(seq 1 60); do
    podman exec catalogo-cosmos curl -fsS http://127.0.0.1:8080/ready >/dev/null 2>&1 && break
    sleep 2
  done
  ok 'Cosmos DB pronto (porta 8081)'

  aviso 'Service Bus leva ~60 s a mais (o MSSQL sobe antes).'
  estado
  echo
  azul 'Próximos passos:'
  echo '    dotnet run --project src/hosts/Catalogo.AppHost'
  echo '    ./local/dev.sh testar-isolamento'
}

estado() {
  cat <<'TABELA'

  SERVIÇO            ENDEREÇO                   PAPEL
  ─────────────────  ─────────────────────────  ──────────────────────────
  Cosmos DB          https://localhost:8081     <- a APLICAÇÃO usa esta
  Data Explorer      http://localhost:1234      consultar documentos
  Valkey (Redis)     localhost:6379             cache, rate limit
  Azurite (Blob)     localhost:10000            evidências, exports
  Service Bus        localhost:5672 / :5300     outbox (AMQP)
  Keycloak           http://localhost:8080      OIDC (no lugar do Entra)
  Painel OTel        http://localhost:18888     traces e logs

TABELA
  podman compose -f "$COMPOSE" ps --format 'table {{.Names}}\t{{.Status}}' 2>/dev/null || true
}

testar_isolamento() {
  # Sem RLS, o isolamento entre empresas é imposto pelo adaptador (ADR-0016 §4).
  # A prova é a suíte de contrato contra o emulador: toda consulta, em toda
  # porta, conferida com duas empresas.
  azul 'Verificando o isolamento entre empresas no Cosmos DB (ADR-0016)...'; echo
  JORNADA_TESTES_COSMOS="$COSMOS_LOCAL" JORNADA_TESTES_COSMOS_EMULADOR=true \
    dotnet run --project "$RAIZ/../tests/Jornada.Persistencia.Testes" -- Cosmos
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
  testar-isolamento) testar_isolamento ;;
  limpar)     limpar ;;
  logs)       podman compose -f "$COMPOSE" logs ${2:+-f "$2"} ${2:---tail=50} ;;
  *)          erro "comando desconhecido: $1"; exit 1 ;;
esac
