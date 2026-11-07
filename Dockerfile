# syntax=docker/dockerfile:1
FROM node:24.22.1-bookworm-slim AS web
WORKDIR /src/apps/web
COPY apps/web/package*.json ./
RUN npm ci
COPY apps/web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0.403 AS build
WORKDIR /src
COPY . .
RUN dotnet restore OpenCert.sln --locked-mode
RUN dotnet publish apps/api/OpenCert.Api.csproj -c Release --no-restore -o /out/api /p:UseAppHost=false
RUN dotnet publish apps/worker/OpenCert.Worker.csproj -c Release --no-restore -o /out/worker /p:UseAppHost=false
RUN dotnet publish apps/agent/OpenCert.Agent.csproj -c Release --no-restore -o /out/agent /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.14 AS api
WORKDIR /app
COPY --from=build /out/api .
COPY --from=web /src/apps/web/dist ./wwwroot
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "OpenCert.Api.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0.14 AS worker
WORKDIR /app
COPY --from=build /out/worker .
USER $APP_UID
ENTRYPOINT ["dotnet", "OpenCert.Worker.dll"]

FROM mcr.microsoft.com/dotnet/runtime:10.0.14 AS agent
WORKDIR /app
COPY --from=build /out/agent .
USER $APP_UID
ENTRYPOINT ["dotnet", "OpenCert.Agent.dll"]
