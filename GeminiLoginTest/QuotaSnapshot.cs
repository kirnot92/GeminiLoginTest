using System.Text.Json;

namespace GeminiLoginTest;

sealed record QuotaSnapshot(IReadOnlyList<QuotaBucket> Buckets)
{
    public static QuotaSnapshot FromJson(JsonElement root)
    {
        if (!root.TryGetProperty("buckets", out var bucketsElement) ||
            bucketsElement.ValueKind != JsonValueKind.Array)
        {
            return new QuotaSnapshot([]);
        }

        var buckets = new List<QuotaBucket>();
        foreach (var bucket in bucketsElement.EnumerateArray())
        {
            buckets.Add(new QuotaBucket(
                GetString(bucket, "modelId"),
                GetString(bucket, "tokenType"),
                GetDouble(bucket, "remainingFraction"),
                GetString(bucket, "resetTime")));
        }

        return new QuotaSnapshot(buckets);
    }

    public QuotaBucket? FindBucket(string model) =>
        Buckets.FirstOrDefault(bucket => string.Equals(bucket.ModelId, model, StringComparison.OrdinalIgnoreCase));

    public void PrintBucket(string label, string model)
    {
        var bucket = FindBucket(model);
        if (bucket is null)
        {
            Console.WriteLine($"{label}: no quota bucket found for {model}");
            return;
        }

        bucket.Print(label);
    }

    static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    static double? GetDouble(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetDouble(out var value)
            ? value
            : null;
    }
}

sealed record QuotaBucket(
    string? ModelId,
    string? TokenType,
    double? RemainingFraction,
    string? ResetTime)
{
    public string DisplayRemainingPercent =>
        RemainingFraction is null
            ? "unknown"
            : $"{RemainingFraction.Value * 100:0.####}%";

    public void Print(string label)
    {
        Console.WriteLine($"{label}: {ModelId} remaining {DisplayRemainingPercent}, reset {ResetTime ?? "unknown"}");
    }
}

sealed record QuotaUsageDelta(
    QuotaBucket? Before,
    QuotaBucket? After)
{
    public static QuotaUsageDelta From(QuotaSnapshot before, QuotaSnapshot after, string model) =>
        new(before.FindBucket(model), after.FindBucket(model));

    public double? ConsumedPercentPoints =>
        Before?.RemainingFraction is null || After?.RemainingFraction is null
            ? null
            : Math.Max(0, (Before.RemainingFraction.Value - After.RemainingFraction.Value) * 100);

    public void Print()
    {
        Console.WriteLine();
        Console.WriteLine("Usage consumed by this call:");
        Console.WriteLine(ConsumedPercentPoints is null
            ? "  Percent: unknown"
            : $"  Percent: {ConsumedPercentPoints.Value:0.####}%p");
    }
}
