FROM mcr.microsoft.com/dotnet/sdk:10.0.400-noble@sha256:4beef5b8919dcaa2dc924233bd069257e883cc7a061e09088a97d152d6a48510 AS build
WORKDIR /source
COPY global.json Directory.Build.props ./
COPY src/RetailVision.Core/*.csproj src/RetailVision.Core/packages.lock.json src/RetailVision.Core/
COPY src/RetailVision.Infrastructure/*.csproj src/RetailVision.Infrastructure/packages.lock.json src/RetailVision.Infrastructure/
COPY src/RetailVision.Vision/*.csproj src/RetailVision.Vision/packages.lock.json src/RetailVision.Vision/
COPY src/RetailVision.Web/*.csproj src/RetailVision.Web/packages.lock.json src/RetailVision.Web/
RUN dotnet restore src/RetailVision.Web --locked-mode
COPY src/ src/
RUN dotnet publish src/RetailVision.Web -c Release --no-restore -o /publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.11-noble@sha256:011bb5f30180717b1c8b65822ff2c99bcb96bc65af0164589751b83c7b4949f7 AS runtime
USER root
RUN apt-get update && apt-get install -y --no-install-recommends curl libgomp1 \
    && rm -rf /var/lib/apt/lists/* \
    && install -d -o app -g app -m 700 /data \
    && mkdir /models
WORKDIR /app
COPY --from=build /publish/ ./
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    Storage__DataDirectory=/data \
    DOTNET_EnableDiagnostics=0
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=20s --timeout=5s --start-period=40s --retries=3 \
    CMD curl --fail --silent --max-time 4 http://127.0.0.1:8080/health || exit 1
ENTRYPOINT ["dotnet", "RetailVision.Web.dll"]

FROM build AS probe-build
COPY tools/RetailVision.ContainerProbe/ tools/RetailVision.ContainerProbe/
RUN dotnet restore tools/RetailVision.ContainerProbe --locked-mode \
    && dotnet publish tools/RetailVision.ContainerProbe -c Release --no-restore -o /probe /p:UseAppHost=false

FROM runtime AS verify
COPY --from=probe-build /probe/ /probe/
COPY tests/Fixtures/manifest.json tests/Fixtures/synthetic.onnx /probe/Fixtures/
HEALTHCHECK NONE
ENTRYPOINT ["dotnet", "/probe/RetailVision.ContainerProbe.dll", "/probe/Fixtures/manifest.json"]

# Default output contains neither test fixtures nor the probe executable.
FROM runtime AS final
