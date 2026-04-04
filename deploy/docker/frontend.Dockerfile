FROM node:24.13.1-alpine AS build
WORKDIR /app

COPY frontend/taskdeck-web/package.json frontend/taskdeck-web/package-lock.json ./
RUN npm ci

COPY frontend/taskdeck-web/ ./

ARG VITE_API_BASE_URL=/api
ENV VITE_API_BASE_URL=$VITE_API_BASE_URL

RUN npm run build

FROM nginx:1.27-alpine AS runtime
WORKDIR /usr/share/nginx/html

COPY deploy/nginx/frontend.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist ./

EXPOSE 8080
