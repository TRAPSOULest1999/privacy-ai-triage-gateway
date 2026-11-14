using Triage.Api.Models;

namespace Triage.Api.Services;

public interface ISopRagRepository
{
    IReadOnlyList<SopDocument> RetrieveRelevantSops(string redactedQuery, int topK = 2);
}

/// <summary>
/// Local RAG SOP Retriever seeded with enterprise carrier-routing, MVNO billing, and security SOPs.
/// Uses lightweight lexical + keyword overlap scoring out of the box and can be swapped with pgvector cosine distance.
/// </summary>
public sealed class InMemorySopRagRepository : ISopRagRepository
{
    private static readonly IReadOnlyList<SopDocument> SeedSops =
    [
        new(
            "SOP-RATE-101",
            "Wholesale Carrier Agreement Rate Mismatch & Margin Floor Alert",
            "Carrier-Routing-Ops",
            4,
            "When a wholesale carrier offer or AGR_RATE update causes negative margin, bilateral rate discrepancy, or missing rate comment on international voice/SMS routes, classify as CarrierRateDiscrepancy with P1/High priority."),
        new(
            "SOP-BILL-204",
            "Subscriber Delinquency, Collection Hold & Double-Debit Reversal",
            "Revenue-Assurance-Billing",
            8,
            "When a subscriber account reports duplicate debit orders, settled balance still restricted in delinquency state-machine, or GL reconciliation variance, classify as BillingReconciliation with P2/Medium or P1/High priority."),
        new(
            "SOP-SEC-309",
            "Unauthorized API Token, OIDC Claim Tampering & Data Privacy Incident",
            "Security-Trust-Ops",
            1,
            "Any alert involving leaked bearer tokens, OIDC/Keycloak authentication bypass, unauthorized bulk export, or unredacted PII exposure must be classified as SecurityPrivacyIncident with P1/Critical priority and routed to human review."),
        new(
            "SOP-PROV-402",
            "MVNO SIM Provisioning, Roaming Profile & VoLTE Activation Delay",
            "Core-Network-Provisioning",
            12,
            "Tickets describing stuck SIM swap, international roaming bar after top-up, or APN/VoLTE provisioning timeout are classified as NetworkProvisioning with P2/Medium priority.")
    ];

    public IReadOnlyList<SopDocument> RetrieveRelevantSops(string redactedQuery, int topK = 2)
    {
        var tokens = redactedQuery
            .ToLowerInvariant()
            .Split([' ', ',', '.', ';', ':', '-', '/', ' ', ' '], StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet();

        return SeedSops
            .Select(sop => new
            {
                Sop = sop,
                Score = ScoreDocument(sop, tokens)
            })
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select(x => x.Sop)
            .ToList();
    }

    private static int ScoreDocument(SopDocument sop, HashSet<string> queryTokens)
    {
        var docWords = $"{sop.Title} {sop.Content} {sop.Department}"
            .ToLowerInvariant()
            .Split([' ', ',', '.', ';', ':', '-', '/'], StringSplitOptions.RemoveEmptyEntries);

        return docWords.Count(queryTokens.Contains);
    }
}
