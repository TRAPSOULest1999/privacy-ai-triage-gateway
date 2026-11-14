using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Triage.Api.Models;
using Triage.Api.Services;
using Xunit;

namespace Triage.EvalTests;

public sealed class PrivacyAndTriageEvaluationTests
{
    private readonly ITriageOrchestrator _orchestrator;

    public PrivacyAndTriageEvaluationTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TriageOptions:ConfidenceThreshold"] = "0.85"
            })
            .Build();

        var httpClient = new HttpClient();
        var piiService = new PiiRedactionService(httpClient, config, NullLogger<PiiRedactionService>.Instance);
        var ragRepo = new InMemorySopRagRepository();
        _orchestrator = new TriageOrchestrator(piiService, ragRepo, httpClient, config, NullLogger<TriageOrchestrator>.Instance);
    }

    [Fact]
    public async Task Redaction_ReplacesSensitiveValues_AndPreservesTicketMeaning()
    {
        var result = await _orchestrator.EvaluateTicketAsync(new TriageRequest("email",
            "Invoice INV-773819 was billed twice. Email alex@example.com, call +27 82 555 0199. ID 9909085430083. Card 4111 1111 1111 1111."));

        Assert.Equal("Invoice <ACCOUNT_REF> was billed twice. Email <EMAIL_ADDRESS>, call <PHONE_NUMBER>. ID <SA_ID_NUMBER>. Card <PAYMENT_CARD>.", result.RedactedTicketText);
        Assert.Equal(5, result.PiiEntitiesScrubbed);
        Assert.Equal("BillingReconciliation", result.Category);
    }

    [Fact]
    public async Task Redaction_LeavesNonSensitiveTextUnchanged()
    {
        const string text = "My SIM has no roaming service after provisioning.";
        var result = await _orchestrator.EvaluateTicketAsync(new TriageRequest("portal", text));
        Assert.Equal(text, result.RedactedTicketText);
        Assert.Equal(0, result.PiiEntitiesScrubbed);
        Assert.Equal("NetworkProvisioning", result.Category);
    }

    [Fact]
    public async Task GoldenDataset_EnforcesZeroPiiLeakage_AndAccurateRouting()
    {
        var jsonPath = Path.Combine(AppContext.BaseDirectory, "GoldenDataset", "synthetic_tickets.json");
        var cases = JsonSerializer.Deserialize<List<GoldenTicketCase>>(
            await File.ReadAllTextAsync(jsonPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.NotEmpty(cases);

        foreach (var testCase in cases)
        {
            var result = await _orchestrator.EvaluateTicketAsync(new TriageRequest("EvalHarness", testCase.RawText));

            // Gate 1: Strict 0% PII Leakage Gate
            foreach (var secret in testCase.ForbiddenSubstrings)
            {
                Assert.DoesNotContain(secret, result.RedactedTicketText, StringComparison.OrdinalIgnoreCase);
            }

            Assert.True(result.PiiEntitiesScrubbed > 0, $"Expected PII redaction on ticket {testCase.Id}");

            // Gate 2: Classification & Priority Accuracy
            Assert.Equal(testCase.ExpectedCategory, result.Category);
            Assert.Equal(testCase.ExpectedPriority, result.Priority);

            // Gate 3: Confidence & HITL Escalation Gate
            Assert.Equal(testCase.ExpectedHumanReview, result.RoutedToHumanReview);
        }
    }

    private sealed record GoldenTicketCase(
        string Id,
        string RawText,
        string ExpectedCategory,
        string ExpectedPriority,
        bool ExpectedHumanReview,
        List<string> ForbiddenSubstrings);
}
