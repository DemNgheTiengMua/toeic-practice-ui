# syntax=docker/dockerfile:1
#
# #287: two images, one file. `api` is WebAPI (port 5001), `mvc` is WebMVC (port 5000).
#
# Why one Dockerfile and not two: the two apps share four of the six projects, so they share a
# restore, a compiler and a runtime version. Two Dockerfiles would let one of them gain a step the
# other never gets — a stage, a package or a base-image bump — and the drift would be invisible
# until a container failed. One file makes the shared part shared by construction.
#
# Why the runtime stages are separate: each image must contain exactly one app, its own entrypoint
# and its own port, and neither may drag the other's dependencies in.
#
# Bring the stack up with `docker compose up -d --build`. Operator instructions, including how to
# apply a schema change and how to switch the OpenAPI surface off, live in docs/DOCKER.md.
#
#   docker build --target api -t extra-efficiency-api .
#   docker build --target mvc -t extra-efficiency-mvc .

# --- restore ---------------------------------------------------------------------------------
# Project files only, no source: package restore is the slow, source-independent layer, so an
# edit to a .cs file reuses it. The .slnx and the test projects are deliberately absent — the
# images carry neither.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /src
COPY src/BusinessObjects/BusinessObjects.csproj src/BusinessObjects/
COPY src/Contracts/Contracts.csproj src/Contracts/
COPY src/DataAccess/DataAccess.csproj src/DataAccess/
COPY src/Services/Services.csproj src/Services/
COPY src/WebAPI/WebAPI.csproj src/WebAPI/
COPY src/WebMVC/WebMVC.csproj src/WebMVC/
RUN dotnet restore src/WebAPI/WebAPI.csproj && dotnet restore src/WebMVC/WebMVC.csproj

# --- publish ---------------------------------------------------------------------------------
# Both apps in one layer, so the two runtime stages below copy from the same build rather than
# compiling the shared projects twice in two unrelated caches.
FROM restore AS publish
COPY src/ src/
RUN dotnet publish src/WebAPI/WebAPI.csproj -c Release -o /out/api --no-restore \
 && dotnet publish src/WebMVC/WebMVC.csproj -c Release -o /out/mvc --no-restore

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

# --- mvc -------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS mvc

WORKDIR /app
COPY --from=publish /out/mvc ./

# No volume and no writable directory: the web app serves committed assets from wwwroot through
# MapStaticAssets and writes nothing. Only the API's audio store is writable, so only it persists.
RUN chown -R $APP_UID:$APP_UID /app
ENV ASPNETCORE_URLS=http://+:5000

USER $APP_UID
EXPOSE 5000
ENTRYPOINT ["dotnet", "WebMVC.dll"]
