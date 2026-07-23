# PersonaOS dashboard — build context is this directory:
#   docker build -f src/frontend/PersonaOS.Dashboard/Dockerfile src/frontend/PersonaOS.Dashboard
FROM node:22-alpine AS build
WORKDIR /src

# Cache the dependency layer until the lockfile changes.
COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npx ng build --configuration production

FROM nginx:1.27-alpine AS runtime

COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/dist/PersonaOS.Dashboard/browser /usr/share/nginx/html

EXPOSE 8080
