# syntax=docker/dockerfile:1

# Static output is architecture-independent, so build natively on the build machine.
FROM --platform=$BUILDPLATFORM node:22-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
# This image is the same-origin setup: nginx serves the app and proxies /api and /hubs to the API container, so the
# build uses relative URLs (no VITE_API_URL). A separately hosted API is the Static Web Apps deployment instead.
RUN npm run build

# Unprivileged nginx: runs as a non-root user and listens on 8080.
FROM nginxinc/nginx-unprivileged:1.27-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY security-headers.conf /etc/nginx/snippets/security-headers.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 8080
