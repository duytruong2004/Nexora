namespace NEXORA.Application.Consultation;

public enum ProductConnectivityRequirement
{
    Wired,
    Wireless
}

/// <summary>Applies explicit product attributes only when the catalog evidence can verify them.</summary>
public static class ConsultationProductRequirementMatcher
{
    public static ProductConnectivityRequirement? GetConnectivityRequirement(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var normalized = ConsultationNameNormalizer.Normalize(input);
        var asksWireless = ContainsAny(normalized, "khong day", "wireless", "bluetooth");
        var asksWired = ContainsAny(normalized, "co day", "wired");
        if (asksWireless == asksWired) return null;

        return asksWireless ? ProductConnectivityRequirement.Wireless : ProductConnectivityRequirement.Wired;
    }

    public static bool Matches(ConsultationProduct product, ProductConnectivityRequirement requirement)
    {
        var evidence = ConsultationNameNormalizer.Normalize(string.Join(' ',
            new[] { product.Name, product.Description }
                .Concat(product.Specifications.SelectMany(value => new[]
                {
                    value.Name, value.Value, value.DisplayValue ?? string.Empty
                }))));

        var wirelessEvidence = ContainsAny(evidence, "khong day", "wireless", "bluetooth");
        var wiredEvidence = ContainsAny(evidence, "co day", "wired");

        return requirement switch
        {
            ProductConnectivityRequirement.Wireless => wirelessEvidence && !wiredEvidence,
            ProductConnectivityRequirement.Wired => wiredEvidence && !wirelessEvidence,
            _ => false
        };
    }

    private static bool ContainsAny(string value, params string[] phrases) =>
        phrases.Any(phrase => value.Contains(phrase, StringComparison.Ordinal));
}
