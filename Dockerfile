# syntax=docker/dockerfile:1
#
# #305: two images, one file. `api` is WebAPI (port 5001), `web` is the React + HeroUI v3 SPA (port 5000).
#
# Bring the stack up with `docker compose up -d --build`. Operator instructions, including how to
# apply a schema change and how to switch the OpenAPI surface off, live in docs/DOCKER.md.
#
#   docker build --target api -t extra-efficiency-api .
#   docker build --target web -t extra-efficiency-web .

# --- api restore & publish -------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /src
COPY src/BusinessObjects/BusinessObjects.csproj src/BusinessObjects/
COPY src/Contracts/Contracts.csproj src/Contracts/
COPY src/DataAccess/DataAccess.csproj src/DataAccess/
COPY src/Services/Services.csproj src/Services/
COPY src/WebAPI/WebAPI.csproj src/WebAPI/
RUN dotnet restore src/WebAPI/WebAPI.csproj

FROM restore AS publish
COPY src/BusinessObjects/ src/BusinessObjects/
COPY src/Contracts/ src/Contracts/
COPY src/DataAccess/ src/DataAccess/
COPY src/Services/ src/Services/
COPY src/WebAPI/ src/WebAPI/
RUN dotnet publish src/WebAPI/WebAPI.csproj -c Release -o /out/api --no-restore

# --- api -------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api

# The base image carries no HTTP client and compose's healthcheck is a curl against /healthz.
# Only this image installs it: nothing health-checks the web app, which does no I/O of its own.
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=publish /out/api ./

# The audio store is the only path this app writes that must survive a redeploy (compose mounts a
# named volume here). Creating it and handing it to the app user is what makes a fresh volume
# arrive app-owned instead of root-owned; without that, the first upload fails with EACCES.
# FileStore:AudioDirectory has to be set explicitly because its default is ContentRoot/../../App_Data,
# which in this image resolves to /App_Data — outside the writable tree.
RUN mkdir -p /app/App_Data/audio && chown -R $APP_UID:$APP_UID /app
ENV ASPNETCORE_URLS=http://+:5001 \
    FileStore__AudioDirectory=/app/App_Data/audio

# The images define a non-root `app` user (uid 1654). 5001 is above the privileged range, so this
# costs nothing.
USER $APP_UID
EXPOSE 5001
ENTRYPOINT ["dotnet", "WebAPI.dll"]

# --- web build & runtime ---------------------------------------------------------------------
FROM node:22-alpine AS web-build
WORKDIR /web
COPY src/web/package.json src/web/package-lock.json* ./
RUN npm ci
COPY src/web/ ./
RUN npm run build

FROM nginx:1.27-alpine AS web
COPY --from=web-build /web/dist /usr/share/nginx/html
COPY src/web/nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 5000
CMD ["nginx", "-g", "daemon off;"]
