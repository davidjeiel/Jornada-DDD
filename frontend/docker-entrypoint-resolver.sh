#!/bin/sh
# O endereço do DNS embutido do runtime de containers muda conforme quem sobe
# o compose: Podman/netavark usa 10.89.x.x (aardvark-dns); Docker usa
# 127.0.0.11. Hardcodar qualquer um quebra o outro ambiente (ADR-0013: nenhum
# Dockerfile pode depender de detalhe específico de um dos dois runtimes).
# Por isso o valor é lido de /etc/resolv.conf, sempre certo em qualquer um,
# e substituído por um marcador literal (nunca por sintaxe de variável do
# nginx, para não colidir com $host/$remote_addr/etc. do próprio nginx.conf).
set -e

DNS_RESOLVER=$(awk '/^nameserver/ { print $2; exit }' /etc/resolv.conf)
if [ -n "$DNS_RESOLVER" ]; then
    sed -i "s/__DNS_RESOLVER__/$DNS_RESOLVER/" /etc/nginx/conf.d/default.conf
fi

# Onde a API está: o nome do serviço muda de ambiente para ambiente. "|" como
# delimitador do sed, porque o valor é uma URL cheia de "/".
ENDERECO_DA_API="${ENDERECO_DA_API:-http://api:8080}"
sed -i "s|__ENDERECO_DA_API__|$ENDERECO_DA_API|" /etc/nginx/conf.d/default.conf

exec /docker-entrypoint.sh "$@"
