using System.Text.Json.Serialization;

namespace Triage.Api.Models;

public sealed record TriageRequest(
    string SourceChannel,
    string RawTicketText);

public sealed record PiiRedactionResult(
    string SanitizedText,    
    int ScrubbedCount,
    IReadOnlyList<string> DetectedEntityTypes);

public sealed record SopDocument(
    string SopCode,
    string Title,
    string Department,
    int SlaHours,
    string Content);

public sealed record LlmTriageOutput(
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("priority")] string Priority,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("matched_sop_code")] string MatchedSopCode,
    [property: JsonPropertyName("rationale")] string Rationale);

public sealed record TriageDecisionResponse(
    Guid AuditId,
    string RedactedTicketText,
    int PiiEntitiesScrubbed,
    IReadOnlyList<string> DetectedEntityTypes,
    string Category,
    string Priority,
    double Confidence,
    string MatchedSopCode,
    bool RoutedToHumanReview,
    string RoutingDestination,
    string Rationale);
