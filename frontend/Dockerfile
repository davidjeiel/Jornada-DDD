FROM docker.io/library/node:22-alpine AS build
WORKDIR /app

COPY frontend/package*.json ./
RUN npm ci
COPY frontend/. .
# Sibling de /app, igual a docs/ ser sibling de frontend/ no repositório — é o
# que faz o import relativo em App.tsx (`../../docs/CARTILHA.md?raw`) resolver
# igual em `npm run build` local e dentro do container (ADR-0013: mesma
# imagem, mesmo comportamento nos dois runtimes).
COPY docs/CARTILHA.md /docs/CARTILHA.md
RUN npm run build

FROM docker.io/library/nginx:1.27-alpine
COPY frontend/nginx.conf /etc/nginx/conf.d/default.conf
COPY frontend/docker-entrypoint-resolver.sh /docker-entrypoint-resolver.sh
RUN chmod +x /docker-entrypoint-resolver.sh
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
# Roda ANTES do entrypoint padrão da imagem: grava o DNS resolver de verdade
# no nginx.conf, depois continua a inicialização normal. CMD explícito porque
# sobrescrever ENTRYPOINT, aqui, não herdou o CMD da imagem base (virou vazio).
ENTRYPOINT ["/docker-entrypoint-resolver.sh"]
CMD ["nginx", "-g", "daemon off;"]
