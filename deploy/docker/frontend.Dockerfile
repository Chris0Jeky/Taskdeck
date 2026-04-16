FROM node:24.13.1-bookworm-slim AS build
WORKDIR /app

COPY frontend/taskdeck-web/package.json frontend/taskdeck-web/package-lock.json ./
RUN npm ci

COPY frontend/taskdeck-web/ ./

ARG VITE_API_BASE_URL=/api
ENV VITE_API_BASE_URL=$VITE_API_BASE_URL

RUN npm run build

# nginx-unprivileged runs as UID 101 (nginx) and listens on 8080 by default,
# so we avoid needing root to bind port 80 or CAP_NET_BIND_SERVICE. This
# matches our frontend.conf which already declares `listen 8080;`.
FROM nginxinc/nginx-unprivileged:1.27-alpine AS runtime
WORKDIR /usr/share/nginx/html

# The base image installs wget via busybox, so the HEALTHCHECK below has a
# lightweight HTTP client without extra apk installs. We only need the
# config file override and the built static assets.
COPY --chown=nginx:nginx deploy/nginx/frontend.conf /etc/nginx/conf.d/default.conf
COPY --from=build --chown=nginx:nginx /app/dist ./

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD wget --spider -q http://localhost:8080/healthz || exit 1
