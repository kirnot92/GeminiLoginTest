using GeminiLoginTest;

const string Prompt =
    "Analyze the image and return only the requested structured fields: predicted_location, predicted_time, and summary.";
const string Model = "gemini-3-flash-preview";

var imageInput = await ImageInput.LoadTestImage();

using var codeAssistClient = new CodeAssistApiClient();
await codeAssistClient.LoginAsync();

Console.WriteLine("Resolving Code Assist project...");
await codeAssistClient.InitializeCodeAssistAsync();

Console.WriteLine("Reading usage before call...");
var beforeQuota = await codeAssistClient.RetrieveUserQuotaAsync();
beforeQuota.PrintBucket("Before", Model);

Console.WriteLine($"Calling Gemini: \"{Prompt}\"");
Console.WriteLine($"Image: {imageInput.Path} ({imageInput.Bytes.Length:N0} bytes, {imageInput.MimeType})");
var response = await codeAssistClient.GenerateContentAsync(
    Model,
    Prompt,
    imageInput);
response.PrintOrThrow();

Console.WriteLine();
Console.WriteLine("Reading usage after call...");
var afterQuota = await codeAssistClient.RetrieveUserQuotaAsync();
afterQuota.PrintBucket("After", Model);

QuotaUsageDelta
    .From(beforeQuota, afterQuota, Model)
    .Print();
