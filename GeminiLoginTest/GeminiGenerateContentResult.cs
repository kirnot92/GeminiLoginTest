namespace GeminiLoginTest;

sealed record GeminiGenerateContentResult(
    bool IsSuccess,
    int StatusCode,
    string? ReasonPhrase,
    string Text,
    string RawBody)
{
    public static GeminiGenerateContentResult Success(string text, string rawBody) =>
        new(true, 200, "OK", text, rawBody);

    public static GeminiGenerateContentResult Failure(int statusCode, string? reasonPhrase, string rawBody) =>
        new(false, statusCode, reasonPhrase, string.Empty, rawBody);

    public void PrintOrThrow()
    {
        if (!IsSuccess)
        {
            throw new InvalidOperationException(
                $"Gemini API call failed: {StatusCode} {ReasonPhrase}{Environment.NewLine}{RawBody}");
        }

        Console.WriteLine();
        Console.WriteLine("Gemini JSON response:");
        Console.WriteLine(Text);
    }
}
