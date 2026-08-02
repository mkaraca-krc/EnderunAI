using System.Net;
using System.Net.Http.Json;
using EnderunAI.Api.Tests.Infrastructure;
using Xunit;

namespace EnderunAI.Api.Tests;

[Collection("Integration")]
public sealed class AuthTests(DatabaseFixture fixture)
{
    // LoginAttemptService IP bazlı ve test süreci boyunca paylaşılan tek bir
    // singleton — her test kendi rastgele X-Forwarded-For değerini kullanır ki
    // testler birbirinin başarısız deneme sayacını etkilemesin.
    private static HttpRequestMessage LoginRequest(string username, string password, string ip) =>
        new(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { username, password }),
            Headers = { { "X-Forwarded-For", ip } }
        };

    private static string NewTestIp() => $"10.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}";

    [Fact]
    public async Task Login_CorrectPassword_Returns200WithToken()
    {
        var client = fixture.Factory.CreateClient();

        var response = await client.SendAsync(
            LoginRequest(AuthHelper.AdminUsername, AuthHelper.AdminPassword, NewTestIp()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(payload.GetProperty("token").GetString()));
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        var client = fixture.Factory.CreateClient();

        var response = await client.SendAsync(
            LoginRequest(AuthHelper.AdminUsername, "kesinlikle-yanlis-sifre", NewTestIp()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_AfterFiveFailures_LocksOutWith429()
    {
        var client = fixture.Factory.CreateClient();
        var ip = NewTestIp();
        var uniqueUser = $"rate-limit-test-{Guid.NewGuid():N}";

        for (var i = 0; i < 5; i++)
        {
            var response = await client.SendAsync(LoginRequest(uniqueUser, "yanlis", ip));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        var lockedResponse = await client.SendAsync(LoginRequest(uniqueUser, "yanlis", ip));

        Assert.Equal((HttpStatusCode)429, lockedResponse.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_Returns401()
    {
        var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync("/api/projects");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
