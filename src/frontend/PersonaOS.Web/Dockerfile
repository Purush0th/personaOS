# PersonaOS web app — build context is this directory:
#   docker build -f src/frontend/PersonaOS.Web/Dockerfile src/frontend/PersonaOS.Web
# The build stage runs on the builder's own platform: its output is static files, the same for
# every target, so the arm64 image does not need its own build. Building it under emulation made
# `npm ci` hang until the release job timed out (v0.1.0-alpha.13 and .14).
FROM --platform=$BUILDPLATFORM node:22-alpine AS build
WORKDIR /src

# Cache the dependency layer until the lockfile changes.
COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npx ng build --configuration production

FROM nginx:1.27-alpine AS runtime

COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/dist/PersonaOS.Web/browser /usr/share/nginx/html

EXPOSE 8080
