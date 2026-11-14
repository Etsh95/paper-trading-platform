# syntax=docker/dockerfile:1

# ---- build -------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from project files only, so the layer is cached until dependencies change.
COPY Directory.Build.props ./
COPY src/PaperTrading.Core/PaperTrading.Core.csproj src/PaperTrading.Core/
COPY src/PaperTrading.Api/PaperTrading.Api.csproj src/PaperTrading.Api/
RUN dotnet restore src/PaperTrading.Api/PaperTrading.Api.csproj

COPY src/ src/
RUN dotnet publish src/PaperTrading.Api/PaperTrading.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

# ---- runtime -----------------------------------------------------------------
# Chiseled: no shell, no package manager, runs as non-root by default. Small attack surface.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "PaperTrading.Api.dll"]
