using Google.Apis.Auth.OAuth2;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace GeminiLoginTest;

sealed class CodeAssistApiClient : IDisposable
{
    const string Endpoint = "https://cloudcode-pa.googleapis.com/v1internal";
    const int MaxGenerateAttempts = 6;

    // clientId와 clientSecret은 이미 공개되어있는 값임. 아래 링크 참조.
    // https://github.com/google-gemini/gemini-cli/blob/main/packages/core/src/code_assist/oauth2.ts
    const string OAuthClientId = "681255809395-oo8ft2oprdrnp9e3aqf6av3hmdib135j.apps.googleusercontent.com";
    const string OAuthClientSecret = "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";

    readonly HttpClient _httpClient;
    readonly bool _disposeHttpClient;
    readonly Random _jitter = new();
    string? _accessToken;
    string? _project;
    CodeAssistLoadSnapshot? _loadSnapshot;

    public CodeAssistApiClient()
        : this(new HttpClient(), disposeHttpClient: true)
    {
    }

    public CodeAssistApiClient(HttpClient httpClient, bool disposeHttpClient = false)
    {
        this._httpClient = httpClient;
        this._disposeHttpClient = disposeHttpClient;
    }

    public async Task LoginAsync(CancellationToken cancellationToken = default)
    {
        var clientSecrets = new ClientSecrets
        {
            ClientId = OAuthClientId,
            ClientSecret = OAuthClientSecret
        };

        Console.WriteLine($"OAuth client: {MaskClientId(clientSecrets.ClientId)}");
        Console.WriteLine("A browser window will open if authorization is required.");

        var authenticator = new GoogleOAuthAuthenticator();
        var credential = await authenticator.AuthorizeAsync(clientSecrets, cancellationToken);
        this._accessToken = credential.Token.AccessToken;
    }

    public async Task InitializeCodeAssistAsync(CancellationToken cancellationToken = default)
    {
        var body = new
        {
            metadata = CreateMetadata()
        };

        using var document = await this.PostJsonAsync("loadCodeAssist", body, cancellationToken);
        var root = document.RootElement;
        var snapshot = CodeAssistLoadSnapshot.FromJson(root);

        if (!string.IsNullOrWhiteSpace(snapshot.Project))
        {
            this._project = snapshot.Project;
            this._loadSnapshot = snapshot;
            return;
        }

        var onboardProject = await this.TryOnboardDefaultTierAsync(root, cancellationToken);
        if (!string.IsNullOrWhiteSpace(onboardProject))
        {
            var onboardedSnapshot = snapshot with { OnboardedProject = onboardProject };
            this._project = onboardedSnapshot.Project;
            this._loadSnapshot = onboardedSnapshot;
            return;
        }

        throw new InvalidOperationException("Code Assist project was not returned and onboarding did not produce one.");
    }

    public CodeAssistLoadSnapshot GetLoadSnapshot()
    {
        return this._loadSnapshot ??
            throw new InvalidOperationException("Call InitializeCodeAssistAsync before reading the Code Assist account snapshot.");
    }

    public async Task<QuotaSnapshot> RetrieveUserQuotaAsync(CancellationToken cancellationToken = default)
    {
        var body = new { project = this.RequireProject() };
        using var document = await this.PostJsonAsync("retrieveUserQuota", body, cancellationToken);
        return QuotaSnapshot.FromJson(document.RootElement);
    }

    public async Task<GeminiGenerateContentResult> GenerateContentAsync(
        string model,
        string prompt,
        ImageInput? imageInput = null,
        CancellationToken cancellationToken = default)
    {
        var parts = new List<object>();
        if (imageInput is not null)
        {
            parts.Add(new
            {
                inline_data = new
                {
                    mime_type = imageInput.MimeType,
                    data = Convert.ToBase64String(imageInput.Bytes)
                }
            });
        }

        parts.Add(new { text = prompt });

        return await this.SendGenerateContentRequestAsync(
            model,
            parts.ToArray(),
            CreateJsonResponseGenerationConfig(),
            cancellationToken);
    }

    async Task<GeminiGenerateContentResult> SendGenerateContentRequestAsync(
        string model,
        object[] parts,
        object? generationConfig,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; attempt <= MaxGenerateAttempts; attempt++)
        {
            var body = new
            {
                model,
                project = this.RequireProject(),
                user_prompt_id = Guid.NewGuid().ToString("N"),
                request = new
                {
                    contents = new[]
                    {
                        new
                        {
                            role = "user",
                            parts
                        }
                    },
                    generationConfig,
                    session_id = Guid.NewGuid().ToString("N")
                }
            };

            try
            {
                using var document = await this.PostJsonAsync("generateContent", body, cancellationToken);
                var rawBody = document.RootElement.GetRawText();
                return GeminiGenerateContentResult.Success(ExtractText(document.RootElement), rawBody);
            }
            catch (CodeAssistApiException ex) when (ex.IsRetryable && attempt < MaxGenerateAttempts)
            {
                var delay = this.GetRetryDelay(attempt, ex.RetryAfter);
                Console.WriteLine(
                    $"Retrying after transient error: attempt {attempt}/{MaxGenerateAttempts}, " +
                    $"status {ex.StatusCode}, reason {ex.ErrorReason ?? "unknown"}, " +
                    $"delay {delay.TotalSeconds:0.#}s");
                await Task.Delay(delay, cancellationToken);
            }
            catch (CodeAssistApiException ex)
            {
                return GeminiGenerateContentResult.Failure(ex.StatusCode, ex.ReasonPhrase, ex.ResponseBody);
            }
        }

        throw new InvalidOperationException("Unreachable retry state.");
    }

    public void Dispose()
    {
        if (this._disposeHttpClient)
        {
            this._httpClient.Dispose();
        }
    }

    async Task<JsonDocument> PostJsonAsync(
        string method,
        object body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Endpoint}:{method}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", this.RequireAccessToken());
        request.Headers.Add("x-goog-api-client", "gemini-login-test/1.0");
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var response = await this._httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new CodeAssistApiException(
                (int)response.StatusCode,
                response.ReasonPhrase,
                responseBody,
                GetRetryAfter(response));
        }

        return JsonDocument.Parse(responseBody);
    }

    async Task<string?> TryOnboardDefaultTierAsync(
        JsonElement loadCodeAssistResponse,
        CancellationToken cancellationToken)
    {
        var tierId = FindDefaultTierId(loadCodeAssistResponse);
        if (string.IsNullOrWhiteSpace(tierId))
        {
            return null;
        }

        var body = new
        {
            tierId,
            cloudaicompanionProject = (string?)null,
            metadata = CreateMetadata()
        };

        using var onboardDocument = await this.PostJsonAsync("onboardUser", body, cancellationToken);
        var operation = onboardDocument.RootElement;
        if (TryExtractOnboardProject(operation, out var project))
        {
            return project;
        }

        if (!TryGetString(operation, "name", out var operationName))
        {
            return null;
        }

        for (var i = 0; i < 24; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            using var operationDocument = await this.GetOperationAsync(operationName, cancellationToken);
            if (TryExtractOnboardProject(operationDocument.RootElement, out project))
            {
                return project;
            }

            if (operationDocument.RootElement.TryGetProperty("done", out var done) &&
                done.ValueKind == JsonValueKind.True)
            {
                break;
            }
        }

        return null;
    }

    async Task<JsonDocument> GetOperationAsync(
        string operationName,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Endpoint}/{operationName}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", this.RequireAccessToken());

        using var response = await this._httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new CodeAssistApiException(
                (int)response.StatusCode,
                response.ReasonPhrase,
                responseBody,
                GetRetryAfter(response));
        }

        return JsonDocument.Parse(responseBody);
    }

    string RequireAccessToken()
    {
        return string.IsNullOrWhiteSpace(this._accessToken)
            ? throw new InvalidOperationException("Call LoginAsync before using the Code Assist API.")
            : this._accessToken;
    }

    string RequireProject()
    {
        return string.IsNullOrWhiteSpace(this._project)
            ? throw new InvalidOperationException("Call InitializeCodeAssistAsync before using project-scoped Code Assist APIs.")
            : this._project;
    }

    TimeSpan GetRetryDelay(int attempt, TimeSpan? retryAfter)
    {
        if (retryAfter is not null && retryAfter.Value > TimeSpan.Zero)
        {
            return retryAfter.Value;
        }

        var baseDelay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt)));
        var jitter = TimeSpan.FromMilliseconds(this._jitter.Next(250, 1250));
        return baseDelay + jitter;
    }

    static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta is { } delta)
        {
            return delta;
        }

        if (retryAfter.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }

        return null;
    }

    static string MaskClientId(string? clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return "(unknown)";
        }

        var visible = Math.Min(12, clientId.Length);
        return clientId[..visible] + "...";
    }

    static object CreateMetadata() => new
    {
        ideType = "IDE_UNSPECIFIED",
        platform = "PLATFORM_UNSPECIFIED",
        pluginType = "GEMINI"
    };

    static object CreateJsonResponseGenerationConfig() => new
    {
        responseMimeType = "application/json",
        responseJsonSchema = new
        {
            type = "object",
            properties = new
            {
                predicted_location = new
                {
                    type = "string",
                    description = "The most likely location shown in the image. Use unknown if it cannot be inferred."
                },
                predicted_time = new
                {
                    type = "string",
                    @enum = new[] { "day", "night", "afternoon" },
                    description = "The likely time period shown in the image."
                },
                summary = new
                {
                    type = "string",
                    description = "A concise summary of the visual evidence and overall scene."
                }
            },
            required = new[] { "predicted_location", "predicted_time", "summary" },
            additionalProperties = false
        }
    };

    static string? FindDefaultTierId(JsonElement root)
    {
        if (!root.TryGetProperty("allowedTiers", out var allowedTiers) ||
            allowedTiers.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var tier in allowedTiers.EnumerateArray())
        {
            var isDefault = tier.TryGetProperty("isDefault", out var defaultElement) &&
                defaultElement.ValueKind == JsonValueKind.True;
            if (isDefault && TryGetString(tier, "id", out var tierId))
            {
                return tierId;
            }
        }

        return null;
    }

    static bool TryExtractOnboardProject(JsonElement root, out string project)
    {
        project = string.Empty;
        if (!root.TryGetProperty("response", out var response) ||
            !response.TryGetProperty("cloudaicompanionProject", out var companionProject))
        {
            return false;
        }

        if (TryGetString(companionProject, "id", out project))
        {
            return true;
        }

        if (companionProject.ValueKind == JsonValueKind.String)
        {
            project = companionProject.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(project);
        }

        return false;
    }

    static string ExtractText(JsonElement root)
    {
        if (!root.TryGetProperty("response", out var response) ||
            !response.TryGetProperty("candidates", out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array)
        {
            return root.GetRawText();
        }

        var builder = new StringBuilder();
        foreach (var candidate in candidates.EnumerateArray())
        {
            if (!candidate.TryGetProperty("content", out var content) ||
                !content.TryGetProperty("parts", out var parts) ||
                parts.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in parts.EnumerateArray())
            {
                if (TryGetString(part, "text", out var text))
                {
                    builder.Append(text);
                }
            }
        }

        return builder.Length > 0 ? builder.ToString() : root.GetRawText();
    }

    static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }
}
