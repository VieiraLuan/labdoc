FROM node:22-alpine AS build
WORKDIR /src
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npx ng build --configuration production

FROM nginx:1.27-alpine AS runtime
ENV API_UPSTREAM=host.docker.internal:5177
COPY nginx.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /src/dist/ui/browser /usr/share/nginx/html
EXPOSE 8080
