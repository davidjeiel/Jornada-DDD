#!/usr/bin/env bash
# Gera o contrato OpenAPI a partir do código e grava em contratos/v1/openapi.yaml.
#
#   ./scripts/gerar-contrato.sh              regenera o arquivo commitado
#   ./scripts/gerar-contrato.sh --verificar  falha se o código e o arquivo divergirem (CI)
#
# O arquivo commitado é o contrato revisável (ADR-0009, ADR-0014): mudar uma
# rota, um campo ou um código de resposta aparece como diff no PR, em vez de
# mudar o contrato em silêncio. Depois de gerar, valide com o Spectral:
#
#   npx @stoplight/spectral-cli lint contratos/v1/openapi.yaml

set -euo pipefail

RAIZ="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DESTINO="$RAIZ/contratos/v1/openapi.yaml"
PORTA="${PORTA:-5099}"
SAIDA="$RAIZ/src/hosts/Jornada.Api/bin/Release/net10.0"

dotnet build "$RAIZ/src/hosts/Jornada.Api/Jornada.Api.csproj" -c Release --nologo -v quiet

# Sobe a API em memória, sem banco: o contrato não depende do provedor de
# persistência. A partir da pasta de saída, para achar o appsettings.json.
(cd "$SAIDA" && ASPNETCORE_URLS="http://127.0.0.1:$PORTA" Persistencia__Provedor=memoria \
    exec dotnet Jornada.Api.dll >/dev/null 2>&1) &
PID=$!
trap 'kill "$PID" 2>/dev/null || true' EXIT

for _ in $(seq 1 60); do
  curl -sf "http://127.0.0.1:$PORTA/saude" >/dev/null && break
  sleep 1
done

GERADO="$(mktemp)"
curl -sf "http://127.0.0.1:$PORTA/swagger/v1/swagger.yaml" -o "$GERADO"

if [[ "${1:-}" == "--verificar" ]]; then
  if ! diff -u "$DESTINO" "$GERADO"; then
    echo
    echo "O contrato gerado pelo código difere de contratos/v1/openapi.yaml."
    echo "Se a mudança é intencional, rode ./scripts/gerar-contrato.sh e commite o arquivo."
    exit 1
  fi
  echo "Contrato OpenAPI em dia com o código."
else
  mkdir -p "$(dirname "$DESTINO")"
  mv "$GERADO" "$DESTINO"
  echo "Contrato gravado em contratos/v1/openapi.yaml"
fi
