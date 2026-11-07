# Build from repository root. Render uses this wrapper because Docker targets are not a Blueprint field.
FROM node:26.12.1-bookworm-slim AS web
WORKDIR /src/apps/web
COPY apps/web/package*.json ./
RUN npm ci
COPY apps/web/ ./
RUN npm run build
FROM mcr.microsoft.com/dotnet/sdk:10.0.403 AS build
WORKDIR /src
COPY . .
RUN dotnet restore apps/api/OpenCert.Api.csproj --locked-mode
RUN dotnet publish apps/api/OpenCert.Api.csproj -c Release --no-restore -o /out /p:UseAppHost=false
FROM mcr.microsoft.com/dotnet/aspnet:10.0.14
WORKDIR /app
COPY --from=build /out .
COPY --from=web /src/apps/web/dist ./wwwroot
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "OpenCert.Api.dll"]
