# syntax=docker/dockerfile:1

# ── Build ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/ ./src/

RUN dotnet publish src/DNXCameraServer.Web/DNXCameraServer.Web.csproj \
      -c Release -o /app/publish /p:UseAppHost=false

# ── Runtime (Alpine / musl) ──────────────────────────────────────────
# go2rtc es un binario estático (verificado con `ldd`: not a dynamic
# executable), así que funciona en musl sin dependencias.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime

ARG TARGETARCH
ENV GO2RTC_VERSION=1.9.15

RUN apk add --no-cache curl ca-certificates icu-libs \
 && mkdir -p /opt/go2rtc \
 && case "$TARGETARCH" in \
      amd64) SHA=20b46c6910734cb05ecefb234066f36868ff19b42ba1418a7a32e13a3e035712 ;; \
      arm64) SHA=af416f22b21a24ab83b8d78642190b4932ae695b4e386853f11d1b0319781763 ;; \
      *) echo "arquitectura no soportada: $TARGETARCH" >&2; exit 1 ;; \
    esac \
 && curl -fsSL -o /opt/go2rtc/go2rtc \
      "https://github.com/AlexxIT/go2rtc/releases/download/v${GO2RTC_VERSION}/go2rtc_linux_${TARGETARCH}" \
 && echo "$SHA  /opt/go2rtc/go2rtc" | sha256sum -c - \
 && chmod +x /opt/go2rtc/go2rtc \
 && rm -rf /var/cache/apk/*

WORKDIR /app
COPY --from=build /app/publish ./
COPY data/models ./data/models

ENV ASPNETCORE_HTTP_PORTS=8090 \
    DATA_DIR=/data \
    RECORDINGS_DIR=/recordings \
    GO2RTC_BINARY=/opt/go2rtc/go2rtc \
    GO2RTC_API=http://127.0.0.1:1984 \
    GO2RTC_RTSP=127.0.0.1:18554 \
    RTSP_LISTEN_PORT=8554 \
    DOTNET_EnableDiagnostics=0 \
    TZ=Europe/Madrid

EXPOSE 8090/tcp 8554/tcp 8555/udp 8555/tcp

ENTRYPOINT ["dotnet", "DNXCameraServer.Web.dll"]
