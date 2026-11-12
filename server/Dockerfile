# syntax=docker/dockerfile:1
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
ARG VERSION=0.0.0
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/EasyStock.Server/EasyStock.Server.csproj src/EasyStock.Server/
RUN case "$TARGETARCH" in amd64) A=x64 ;; arm64) A=arm64 ;; *) A=$TARGETARCH ;; esac \
    && echo "$A" > /arch \
    && dotnet restore src/EasyStock.Server -a "$A"
COPY src/ src/
# Cross-compiled on the build platform, so arm64 images need no QEMU for the build.
RUN dotnet publish src/EasyStock.Server -c Release -a "$(cat /arch)" --no-restore \
    -p:Version="$VERSION" -p:UseAppHost=false -p:OpenApiGenerateDocuments=false -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG VERSION=0.0.0
LABEL org.opencontainers.image.source="https://github.com/Mathorck/EasyStockServer" \
      org.opencontainers.image.version="$VERSION"
ENV ASPNETCORE_HTTP_PORTS=8080 \
    EASYSTOCK_DATA_DIR=/data \
    DOTNET_gcServer=0
WORKDIR /app
COPY --from=build /out .
RUN mkdir /data && chown $APP_UID /data
USER $APP_UID
VOLUME /data
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD ["dotnet", "EasyStock.Server.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "EasyStock.Server.dll"]
