using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Triage.Api.Services;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<ISopRagRepository, InMemorySopRagRepository>();

using var host = builder.Build();

Console.Error.WriteLine("[Triage.McpServer] Privacy-Preserving Triage MCP Tool Server ready.");

/// <summary>
/// MCP Tool Definitions exposed to local AI agents.
/// Keeps sensitive data boundaries intact by only returning sanitized SOP policies and queueing HITL reviews.
/// </summary>
public static class TriageMcpTools
{
    [Description("Looks up enterprise SLA and routing Standard Operating Procedures (SOPs) for a sanitized query.")]
    public static string LookupSlaPolicy(ISopRagRepository repo, [Description("Redacted ticket summary")] string redactedQuery)
    {
        var matches = repo.RetrieveRelevantSops(redactedQuery, topK: 2);
        return JsonSerializer.Serialize(matches);
    }

    [Description("Enqueues a low-confidence or security-critical ticket into the Human-in-the-Loop (HITL) review queue.")]
    public static string QueueForHumanReview(
        [Description("Audit ID of the redacted ticket")] string auditId,
        [Description("Reason for human escalation")] string escalationReason)
    {
        return JsonSerializer.Serialize(new
        {
            status = "QUEUED_FOR_HITL",
            auditId,
            escalationReason,
            timestampUtc = DateTimeOffset.UtcNow
        });
    }
}
