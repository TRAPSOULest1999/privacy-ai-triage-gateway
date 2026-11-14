using Triage.Api.Models;
using Triage.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient<IPiiRedactionService, PiiRedactionService>();
builder.Services.AddSingleton<ISopRagRepository, InMemorySopRagRepository>();
builder.Services.AddHttpClient<ITriageOrchestrator, TriageOrchestrator>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseSwagger();
app.UseSwaggerUI();

app.MapPost("/api/v1/triage", async (TriageRequest request, ITriageOrchestrator orchestrator, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.RawTicketText))
    {
        return Results.BadRequest(new { error = "RawTicketText is required." });
    }

    var decision = await orchestrator.EvaluateTicketAsync(request, ct);
    return Results.Ok(decision);
})
.WithName("EvaluateSupportTicket")
.WithOpenApi();

app.MapGet("/api/v1/health", () => Results.Ok(new { status = "healthy", zeroSpendMode = true }));

app.Run();

public partial class Program;
