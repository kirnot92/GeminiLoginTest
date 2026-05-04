using System.Text.Json;

namespace GeminiLoginTest;

sealed record CodeAssistLoadSnapshot(
    string? CloudAiCompanionProject,
    GeminiUserTier? CurrentTier,
    IReadOnlyList<GeminiUserTier> AllowedTiers,
    IReadOnlyList<IneligibleTier> IneligibleTiers,
    string RawJson,
    string? OnboardedProject = null)
{
    public string? Project => CloudAiCompanionProject ?? OnboardedProject;

    public void Print()
    {
        Console.WriteLine();
        Console.WriteLine("Code Assist account snapshot:");
        Console.WriteLine($"  Project: {Project ?? "not returned"}");
        if (!string.IsNullOrWhiteSpace(OnboardedProject))
        {
            Console.WriteLine($"  Onboarded project: {OnboardedProject}");
        }

        PrintTier("  Current tier", CurrentTier);
        PrintTierList("  Allowed tiers", AllowedTiers);
        PrintIneligibleTierList("  Ineligible tiers", IneligibleTiers);

        //Console.WriteLine();
        //Console.WriteLine("Raw loadCodeAssist response:");
        //Console.WriteLine(RawJson);
        //Console.WriteLine();
    }

    public static CodeAssistLoadSnapshot FromJson(JsonElement root)
    {
        return new CodeAssistLoadSnapshot(
            GetString(root, "cloudaicompanionProject"),
            TryGetObject(root, "currentTier", GeminiUserTier.FromJson),
            GetArray(root, "allowedTiers", GeminiUserTier.FromJson),
            GetArray(root, "ineligibleTiers", IneligibleTier.FromJson),
            JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true }));
    }

    static void PrintTier(string label, GeminiUserTier? tier)
    {
        if (tier is null)
        {
            Console.WriteLine($"{label}: not returned");
            return;
        }

        tier.Print(label);
    }

    static void PrintTierList(string label, IReadOnlyList<GeminiUserTier> tiers)
    {
        Console.WriteLine($"{label}: {(tiers.Count == 0 ? "none" : tiers.Count)}");
        foreach (var tier in tiers)
        {
            tier.Print($"    - {tier.Id ?? "unknown"}");
        }
    }

    static void PrintIneligibleTierList(string label, IReadOnlyList<IneligibleTier> tiers)
    {
        Console.WriteLine($"{label}: {(tiers.Count == 0 ? "none" : tiers.Count)}");
        foreach (var tier in tiers)
        {
            tier.Print();
        }
    }

    static T? TryGetObject<T>(JsonElement element, string propertyName, Func<JsonElement, T> factory)
    {
        return element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.Object
            ? factory(property)
            : default;
    }

    static IReadOnlyList<T> GetArray<T>(JsonElement element, string propertyName, Func<JsonElement, T> factory)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var values = new List<T>();
        foreach (var item in property.EnumerateArray())
        {
            values.Add(factory(item));
        }

        return values;
    }

    internal static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    internal static bool? GetBoolean(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : null;
    }
}

sealed record GeminiUserTier(
    string? Id,
    string? Name,
    string? Description,
    bool? UserDefinedCloudAiCompanionProject,
    bool? IsDefault,
    bool? HasAcceptedTos,
    bool? HasOnboardedPreviously,
    PrivacyNotice? PrivacyNotice)
{
    public static GeminiUserTier FromJson(JsonElement element)
    {
        return new GeminiUserTier(
            CodeAssistLoadSnapshot.GetString(element, "id"),
            CodeAssistLoadSnapshot.GetString(element, "name"),
            CodeAssistLoadSnapshot.GetString(element, "description"),
            CodeAssistLoadSnapshot.GetBoolean(element, "userDefinedCloudaicompanionProject"),
            CodeAssistLoadSnapshot.GetBoolean(element, "isDefault"),
            CodeAssistLoadSnapshot.GetBoolean(element, "hasAcceptedTos"),
            CodeAssistLoadSnapshot.GetBoolean(element, "hasOnboardedPreviously"),
            element.TryGetProperty("privacyNotice", out var privacyNotice) &&
                privacyNotice.ValueKind == JsonValueKind.Object
                ? PrivacyNotice.FromJson(privacyNotice)
                : null);
    }

    public void Print(string label)
    {
        Console.WriteLine($"{label}:");
        Console.WriteLine($"    Id: {Id ?? "unknown"}");
        Console.WriteLine($"    Name: {Name ?? "unknown"}");
        Console.WriteLine($"    Description: {Description ?? "unknown"}");
        Console.WriteLine($"    Default: {DisplayBool(IsDefault)}");
        Console.WriteLine($"    User-defined project: {DisplayBool(UserDefinedCloudAiCompanionProject)}");
        Console.WriteLine($"    Accepted ToS: {DisplayBool(HasAcceptedTos)}");
        Console.WriteLine($"    Onboarded previously: {DisplayBool(HasOnboardedPreviously)}");

        if (PrivacyNotice is not null)
        {
            //Console.WriteLine($"    Privacy notice shown: {DisplayBool(PrivacyNotice.ShowNotice)}");
            //Console.WriteLine($"    Privacy notice text: {PrivacyNotice.NoticeText ?? "none"}");
        }
    }

    static string DisplayBool(bool? value) => value?.ToString() ?? "unknown";
}

sealed record PrivacyNotice(
    bool? ShowNotice,
    string? NoticeText)
{
    public static PrivacyNotice FromJson(JsonElement element)
    {
        return new PrivacyNotice(
            CodeAssistLoadSnapshot.GetBoolean(element, "showNotice"),
            CodeAssistLoadSnapshot.GetString(element, "noticeText"));
    }
}

sealed record IneligibleTier(
    string? Id,
    string? Name,
    string? Description,
    string? ReasonCode,
    string? ReasonMessage)
{
    public static IneligibleTier FromJson(JsonElement element)
    {
        return new IneligibleTier(
            CodeAssistLoadSnapshot.GetString(element, "id"),
            CodeAssistLoadSnapshot.GetString(element, "name"),
            CodeAssistLoadSnapshot.GetString(element, "description"),
            CodeAssistLoadSnapshot.GetString(element, "reasonCode"),
            CodeAssistLoadSnapshot.GetString(element, "reasonMessage"));
    }

    public void Print()
    {
        Console.WriteLine($"    - {Id ?? "unknown"}");
        Console.WriteLine($"      Name: {Name ?? "unknown"}");
        Console.WriteLine($"      Description: {Description ?? "unknown"}");
        Console.WriteLine($"      Reason: {ReasonCode ?? "unknown"}");
        Console.WriteLine($"      Reason message: {ReasonMessage ?? "unknown"}");
    }
}
