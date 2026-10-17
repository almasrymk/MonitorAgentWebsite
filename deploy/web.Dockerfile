# The three web apps. Build from the repository root:
#   docker build -f deploy/web.Dockerfile --target site -t monitoragent-site .
#   docker build -f deploy/web.Dockerfile --target edge -t monitoragent-edge .
FROM node:22-alpine AS build
WORKDIR /web
ENV PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1
COPY web/package.json web/package-lock.json web/.npmrc ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
# The portal and the admin panel live under /portal/ and /admin/ on the site's own domain, so the API's
# __Host- cookies reach all three apps.
RUN npx ng build site \
 && npx ng build portal --base-href /portal/ \
 && npx ng build admin --base-href /admin/

# The public website, rendered on the server for search engines. The server bundle needs no node_modules.
FROM node:22-alpine AS site
# The server only needs node itself: drop npm and corepack and the CVEs that ship with them.
RUN rm -rf /usr/local/lib/node_modules /usr/local/bin/npm /usr/local/bin/npx /usr/local/bin/corepack
WORKDIR /app
COPY --from=build /web/dist/site ./
ENV NODE_ENV=production PORT=4000
USER node
EXPOSE 4000
CMD ["node", "server/server.mjs"]

# Caddy rebuilt with a current Go toolchain and golang.org/x/net: the official binary lags behind on both, and Trivy
# fails the build on their fixable CVEs. Bump the versions here when Caddy or Go ship fixes.
FROM golang:1.27-alpine AS caddy
RUN go install github.com/caddyserver/xcaddy/cmd/xcaddy@v0.4.5 \
 && xcaddy build v2.11.7 --output /usr/bin/caddy --replace golang.org/x/net=golang.org/x/net@v0.61.0

# The front door: HTTPS, the portal and admin files, and routing to the API and the site.
FROM caddy:2.11-alpine AS edge
COPY --from=caddy /usr/bin/caddy /usr/bin/caddy
COPY deploy/Caddyfile /etc/caddy/Caddyfile
COPY --from=build /web/dist/portal/browser /srv/portal
COPY --from=build /web/dist/admin/browser /srv/admin
