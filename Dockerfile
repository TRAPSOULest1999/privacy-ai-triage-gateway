FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source
COPY src/Triage.Api/Triage.Api.csproj src/Triage.Api/
RUN dotnet restore src/Triage.Api/Triage.Api.csproj
COPY src/Triage.Api/ src/Triage.Api/
RUN dotnet publish src/Triage.Api/Triage.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Triage.Api.dll"]
