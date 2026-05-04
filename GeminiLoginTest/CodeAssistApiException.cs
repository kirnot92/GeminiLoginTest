using System.Text.Json;
using System.Text.RegularExpressions;

namespace GeminiLoginTest;

sealed class CodeAssistApiException : Exception
{
    public CodeAssistApiException(int statusCode, string? reasonPhrase, string responseBody, TimeSpan? retryAfter = null)
        : base($"Code Assist API call failed: {statusCode} {reasonPhrase}")
    {
        StatusCode = statusCode;
        ReasonPhrase = reasonPhrase;
        ResponseBody = responseBody;
        RetryAfter = retryAfter ?? TryExtractRetryAfter(responseBody);
        ErrorReason = TryExtractErrorReason(responseBody);
    }

    public int StatusCode { get; }

    public string? ReasonPhrase { get; }

    public string ResponseBody { get; }

    public TimeSpan? RetryAfter { get; }

    public string? ErrorReason { get; }

    public bool IsRetryable =>
        StatusCode is 408 or 499 ||
        StatusCode is >= 500 and <= 599 ||
        (StatusCode == 429 && ErrorReason is "MODEL_CAPACITY_EXHAUSTED" or "RATE_LIMIT_EXCEEDED");

    static string? TryExtractErrorReason(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (!document.RootElement.TryGetProperty("error", out var error) ||
                !error.TryGetProperty("details", out var details) ||
                details.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var detail in details.EnumerateArray())
            {
                if (detail.TryGetProperty("reason", out var reason) &&
                    reason.ValueKind == JsonValueKind.String)
                {
                    return reason.GetString();
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    static TimeSpan? TryExtractRetryAfter(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (!document.RootElement.TryGetProperty("error", out var error) ||
                !error.TryGetProperty("message", out var messageElement) ||
                messageElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var message = messageElement.GetString();
            if (string.IsNullOrWhiteSpace(message))
            {
                return null;
            }

            var match = Regex.Match(message, @"reset after (?<seconds>\d+)s", RegexOptions.IgnoreCase);
            return match.Success && int.TryParse(match.Groups["seconds"].Value, out var seconds)
                ? TimeSpan.FromSeconds(seconds + 1)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
