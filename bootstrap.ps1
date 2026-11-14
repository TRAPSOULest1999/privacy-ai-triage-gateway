$ErrorActionPreference = "Stop"

Write-Host "1. Starting $0 local AI & privacy infrastructure (Ollama, Presidio, pgvector)..." -ForegroundColor Cyan
docker compose up -d

Write-Host "2. Pulling local LLM (llama3.2:3b) and Embedding Model (nomic-embed-text) inside Ollama..." -ForegroundColor Cyan
docker exec -it triage-ollama ollama pull llama3.2:3b
docker exec -it triage-ollama ollama pull nomic-embed-text

if (-not (Test-Path "PrivacyTriage.sln")) {
    Write-Host "3. Creating .NET 8 Solution and project references..." -ForegroundColor Cyan
    dotnet new sln -n PrivacyTriage
    dotnet sln add src/Triage.Api/Triage.Api.csproj
    dotnet sln add src/Triage.McpServer/Triage.McpServer.csproj
    dotnet sln add tests/Triage.EvalTests/Triage.EvalTests.csproj
}

Write-Host "4. Restoring and running xUnit privacy & triage evaluation suite..." -ForegroundColor Cyan
dotnet test tests/Triage.EvalTests/Triage.EvalTests.csproj --logger "console;verbosity=normal"

Write-Host "Done! Run 'dotnet run --project src/Triage.Api' to launch Swagger UI." -ForegroundColor Green
