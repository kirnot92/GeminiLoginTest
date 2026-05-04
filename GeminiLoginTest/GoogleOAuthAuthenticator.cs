using Google.Apis.Auth.OAuth2;
using Google.Apis.Util.Store;

namespace GeminiLoginTest;

sealed class GoogleOAuthAuthenticator
{
    static readonly string[] Scopes =
    [
        "https://www.googleapis.com/auth/cloud-platform",
        "https://www.googleapis.com/auth/userinfo.email",
        "https://www.googleapis.com/auth/userinfo.profile"
    ];

    readonly string _tokenStorePath;

    public GoogleOAuthAuthenticator(string? tokenStorePath = null)
    {
        _tokenStorePath = tokenStorePath
            ?? Path.Combine(AppContext.BaseDirectory, ".gemini-oauth-token");
    }

    public async Task<UserCredential> AuthorizeAsync(ClientSecrets clientSecrets, CancellationToken cancellationToken = default)
    {
        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            clientSecrets,
            Scopes,
            "gemini-console-user",
            cancellationToken,
            new FileDataStore(_tokenStorePath, fullPath: true));

        if (credential.Token.IsStale)
        {
            await credential.RefreshTokenAsync(cancellationToken);
        }

        return credential;
    }
}
