# Servidor do MB Extreme (site, licenças, afiliados e acesso remoto).
# Construir a partir da raiz do repositório:  docker build -f deploy/Dockerfile -t mbextreme-server .
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/MBExtreme.LicenseServer/MBExtreme.LicenseServer.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    Data__Directory=/data
VOLUME /data
EXPOSE 8080
ENTRYPOINT ["dotnet", "MBExtreme.LicenseServer.dll"]
