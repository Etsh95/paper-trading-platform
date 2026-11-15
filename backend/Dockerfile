# syntax=docker/dockerfile:1

# ---- build -------------------------------------------------------------------
# The SDK stage runs on the build machine's own architecture and cross-compiles for the target
# (e.g. an arm64 Mac producing an amd64 image for x86 servers), so no slow CPU emulation is involved.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

# Restore first, from project files only, so the layer is cached until dependencies change.
COPY Directory.Build.props ./
COPY src/PaperTrading.Domain/PaperTrading.Domain.csproj src/PaperTrading.Domain/
COPY src/PaperTrading.Application/PaperTrading.Application.csproj src/PaperTrading.Application/
COPY src/PaperTrading.Infrastructure/PaperTrading.Infrastructure.csproj src/PaperTrading.Infrastructure/
COPY src/PaperTrading.Api/PaperTrading.Api.csproj src/PaperTrading.Api/
RUN dotnet restore src/PaperTrading.Api/PaperTrading.Api.csproj -a $TARGETARCH

COPY src/ src/
RUN dotnet publish src/PaperTrading.Api/PaperTrading.Api.csproj -c Release -a $TARGETARCH -o /app --no-restore /p:UseAppHost=false

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
