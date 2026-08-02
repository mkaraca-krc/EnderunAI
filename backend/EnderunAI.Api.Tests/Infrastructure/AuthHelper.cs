using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace EnderunAI.Api.Tests.Infrastructure;

public static class AuthHelper
{
    public const string AdminUsername = "test.admin";
    public const string AdminPassword = "TestAdmin!2026Secure";

    public static async Task<string> LoginAsync(
        HttpClient client,
        string username = AdminUsername,
        string password = AdminPassword)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username,
            password
        });

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<LoginResponse>();
        return payload!.Token;
    }

    public static async Task<HttpClient> CreateAuthorizedClientAsync(TestWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        var token = await LoginAsync(client);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    private sealed record LoginResponse(string Token, int ExpiresInSeconds);
}
