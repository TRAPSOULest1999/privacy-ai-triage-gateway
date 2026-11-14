using System.Net.Http.Json;
using System.Text.Json;
using Triage.Api.Models;

namespace Triage.Api.Services;

public interface ITriageOrchestrator
{
    Task<TriageDecisionResponse> EvaluateTicketAsync(TriageRequest request, CancellationToken ct = default);
}

public sealed class TriageOrchestrator(
    IPiiRedactionService piiRedaction,
    ISopRagRepository ragRepository,
    HttpClient httpClient,
    IConfiguration config,
    ILogger<TriageOrchestrator> logger) : ITriageOrchestrator
{
    private readonly double _confidenceThreshold =
        double.TryParse(config["TriageOptions:ConfidenceThreshold"], out var threshold) ? threshold : 0.85;

    public async Task<TriageDecisionResponse> EvaluateTicketAsync(TriageRequest request, CancellationToken ct = default)
    {
        // Step 1: Scrub PII before any RAG lookup or LLM prompt construction
        var redaction = await piiRedaction.ScrubAsync(request.RawTicketText, ct);

        // Step 2: Retrieve relevant SOPs using ONLY the redacted ticket
        var matchedSops = ragRepository.RetrieveRelevantSops(redaction.SanitizedText, topK: 2);

        // Step 3: Classify via local Ollama (with deterministic fallback when running in CI without GPU)
        var llmOutput = await ClassifyWithOllamaOrFallbackAsync(redaction.SanitizedText, matchedSops, ct);

        // Step 4: Enforce Confidence Gate & Security Escalation Policy (HITL)
        var requiresHumanReview =
            llmOutput.Confidence < _confidenceThreshold ||
            string.Equals(llmOutput.Category, "SecurityPrivacyIncident", StringComparison.OrdinalIgnoreCase);

        var destination = requiresHumanReview
            ? "HITL-Manual-Review-Queue"
            : $"Auto-Queue:{llmOutput.Category}";

        return new TriageDecisionResponse(
            AuditId: Guid.NewGuid(),
            RedactedTicketText: redaction.SanitizedText,
            PiiEntitiesScrubbed: redaction.ScrubbedCount,
            DetectedEntityTypes: redaction.DetectedEntityTypes,
            Category: llmOutput.Category,
            Priority: llmOutput.Priority,
            Confidence: Math.Round(llmOutput.Confidence, 3),
            MatchedSopCode: llmOutput.MatchedSopCode,
            RoutedToHumanReview: requiresHumanReview,
            RoutingDestination: destination,
            Rationale: llmOutput.Rationale);
    }

    private async Task<LlmTriageOutput> ClassifyWithOllamaOrFallbackAsync(
        string sanitizedTicket,
        IReadOnlyList<SopDocument> sops,
        CancellationToken ct)
    {
        var ollamaEndpoint = config["TriageOptions:OllamaEndpoint"];
        var chatModel = config["TriageOptions:ChatModel"] ?? "llama3.2:3b";

        if (!string.IsNullOrWhiteSpace(ollamaEndpoint))
        {
            try
            {
                var sopContext = string.Join("", sops.Select(s => $"- [{s.SopCode}] ({s.Department}, SLA {s.SlaHours}h): {s.Content}"));
                var systemPrompt =
                    "You are a privacy-preserving enterprise triage classifier. " +
    "Respond only with a valid JSON object using these fields:\n" +
    "{\"category\":\"GeneralInquiry\",\"priority\":\"P3\"," +
    "\"confidence\":0.8,\"matched_sop_code\":\"SOP-001\"," +
    "\"rationale\":\"Brief explanation\"}\n" +
    "Choose one category: CarrierRateDiscrepancy, BillingReconciliation, " +
    "SecurityPrivacyIncident, NetworkProvisioning, or GeneralInquiry.\n" +
    "Choose one priority: P1, P2, or P3.\n" +
    "Confidence must be a number between 0.0 and 1.0 inclusive.\n" +
    "Use null for matched_sop_code when no SOP applies.";

                var payload = new
                {
                    model = chatModel,
                    response_format = new { type = "json_object" },
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = $"SOP Context:{sopContext}" +
                        $"Sanitized Ticket:{sanitizedTicket}" }
                    },
                    temperature = 0.1
                };

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(4));

                var response = await httpClient.PostAsJsonAsync($"{ollamaEndpoint.TrimEnd('/')}/chat/completions", payload, cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cts.Token), cancellationToken: cts.Token);
                    var content = doc.RootElement
                        .GetProperty("choices")[0]
                        .GetProperty("message")
                        .GetProperty("content")
                        .GetString();

                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        var parsed = JsonSerializer.Deserialize<LlmTriageOutput>(content);
                        if (parsed is not null)
                        {
                            return parsed;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Local Ollama endpoint not active; using deterministic rule-grounded classifier.");
            }
        }

        return DeterministicFallbackClassifier(sanitizedTicket, sops);
    }

    private static LlmTriageOutput DeterministicFallbackClassifier(string sanitizedText, IReadOnlyList<SopDocument> sops)
    {
        var lower = sanitizedText.ToLowerInvariant();
        var primarySop = sops.FirstOrDefault()?.SopCode ?? "SOP-GEN-000";

        if (lower.Contains("token") || lower.Contains("unauthorized") || lower.Contains("breach") || lower.Contains("oidc") || lower.Contains("leak"))
        {
            return new LlmTriageOutput(
                "SecurityPrivacyIncident",
                "P1",
                0.94,
                "SOP-SEC-309",
                "Matched security/token exposure indicators; mandatory human security review enforced.");
        }

        if (lower.Contains("rate") || lower.Contains("agr_rate") || lower.Contains("carrier") || lower.Contains("margin") || lower.Contains("route"))
        {
            return new LlmTriageOutput(
                "CarrierRateDiscrepancy",
                "P1",
                0.91,
                "SOP-RATE-101",
                "Matched wholesale carrier rate agreement or margin floor discrepancy rules.");
        }

        if (lower.Contains("debit") || lower.Contains("delinquency") || lower.Contains("reconciliation") || lower.Contains("gl") || lower.Contains("billed") || lower.Contains("invoice"))
        {
            return new LlmTriageOutput(
                "BillingReconciliation",
                "P2",
                0.89,
                "SOP-BILL-204",
                "Matched billing lifecycle, delinquency hold, or GL reconciliation workflow.");
        }

        if (lower.Contains("roaming") || lower.Contains("sim") || lower.Contains("volte") || lower.Contains("provisioning"))
        {
            return new LlmTriageOutput(
                "NetworkProvisioning",
                "P2",
                0.88,
                "SOP-PROV-402",
                "Matched SIM/roaming network provisioning runbook.");
        }

        return new LlmTriageOutput(
            "GeneralInquiry",
            "P3",
            0.62,
            primarySop,
            "Ambiguous intent below confidence threshold; escalated to human triage queue.");
    }
}
