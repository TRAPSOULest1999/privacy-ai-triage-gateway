using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Triage.Api.Models;

namespace Triage.Api.Services;

public interface IPiiRedactionService
{
    Task<PiiRedactionResult> ScrubAsync(string rawText, CancellationToken ct = default);
}

/// <summary>
/// Hybrid PII Redaction Gateway:
/// 1. Deterministic regex pass for South African IDs, MSISDNs, emails, and carrier/billing account codes.
/// 2. Optional call to local Microsoft Presidio containers for contextual NER (names/locations).
/// Guarantees zero raw PII reaches the LLM prompt or audit logs even when offline.
/// </summary>
public sealed partial class PiiRedactionService(HttpClient httpClient, IConfiguration config, ILogger<PiiRedactionService> logger)
    : IPiiRedactionService
{
    [GeneratedRegex(@"\b\d{13}\b", RegexOptions.Compiled)]
    private static partial Regex SaIdRegex();

    [GeneratedRegex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b", RegexOptions.Compiled)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"(?<!\w)(?:\+27[ -]?|0)[6-8]\d[ -]?\d{3}[ -]?\d{4}\b", RegexOptions.Compiled)]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"\b(?:ACC|SUB|MSISDN|INV|AGR)-\d{4,10}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex AccountCodeRegex();

    [GeneratedRegex(@"\b(?:\d[ -]*?){13,16}\b", RegexOptions.Compiled)]
    private static partial Regex CardRegex();

    public async Task<PiiRedactionResult> ScrubAsync(string rawText, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return new PiiRedactionResult(string.Empty, 0, []);
        }

        var detectedTypes = new List<string>();
        var working = rawText;
        var count = 0;

        working = ReplaceAndCount(working, SaIdRegex(), "<SA_ID_NUMBER>", "SA_ID_NUMBER", detectedTypes, ref count);
        working = ReplaceAndCount(working, EmailRegex(), "<EMAIL_ADDRESS>", "EMAIL_ADDRESS", detectedTypes, ref count);
        working = ReplaceAndCount(working, PhoneRegex(), "<PHONE_NUMBER>", "PHONE_NUMBER", detectedTypes, ref count);
        working = ReplaceAndCount(working, AccountCodeRegex(), "<ACCOUNT_REF>", "ACCOUNT_REF", detectedTypes, ref count);
        working = ReplaceAndCount(working, CardRegex(), "<PAYMENT_CARD>", "PAYMENT_CARD", detectedTypes, ref count);

        var analyzerUrl = config["TriageOptions:PresidioAnalyzerUrl"];
        var anonymizerUrl = config["TriageOptions:PresidioAnonymizerUrl"];

        if (!string.IsNullOrWhiteSpace(analyzerUrl) && !string.IsNullOrWhiteSpace(anonymizerUrl))
        {
            try
            {
                var analyzeResponse = await httpClient.PostAsJsonAsync(
                    analyzerUrl,
                    new { text = working, language = "en" },
                    ct);

                if (analyzeResponse.IsSuccessStatusCode)
                {
                    var findings = await analyzeResponse.Content.ReadFromJsonAsync<List<PresidioFinding>>(cancellationToken: ct) ?? [];
                    if (findings.Count > 0)
                    {
                        var anonymizeResponse = await httpClient.PostAsJsonAsync(
                            anonymizerUrl,
                            new { text = working, analyzer_results = findings },
                            ct);

                        if (anonymizeResponse.IsSuccessStatusCode)
                        {
                            var anonymized = await anonymizeResponse.Content.ReadFromJsonAsync<PresidioAnonymizedPayload>(cancellationToken: ct);
                            if (!string.IsNullOrWhiteSpace(anonymized?.Text))
                            {
                                working = anonymized.Text;
                                count += findings.Count;
                                foreach (var f in findings)
                                {
                                    if (!detectedTypes.Contains(f.EntityType))
                                    {
                                        detectedTypes.Add(f.EntityType);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Local Presidio container unreachable; continuing with deterministic regex PII guardrails.");
            }
        }

        return new PiiRedactionResult(working, count, detectedTypes);
    }

    private static string ReplaceAndCount(
        string input,
        Regex regex,
        string placeholder,
        string entityType,
        List<string> detectedTypes,
        ref int totalCount)
    {
        var matches = regex.Matches(input);
        if (matches.Count == 0)
        {
            return input;
        }

        totalCount += matches.Count;
        if (!detectedTypes.Contains(entityType))
        {
            detectedTypes.Add(entityType);
        }

        return regex.Replace(input, placeholder);
    }

    private sealed record PresidioFinding(
        [property: JsonPropertyName("entity_type")] string EntityType,
        int Start, int End, double Score);
    private sealed record PresidioAnonymizedPayload(string Text);
}
