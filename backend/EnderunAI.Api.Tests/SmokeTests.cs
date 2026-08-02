using EnderunAI.Api.Tests.Infrastructure;
using Xunit;

namespace EnderunAI.Api.Tests;

[Collection("Integration")]
public sealed class SmokeTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task HealthEndpoint_Returns200()
    {
        var client = fixture.Factory.CreateClient();
        var response = await client.GetAsync("/api/health");
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Login_WithSeededAdmin_ReturnsToken()
    {
        var client = fixture.Factory.CreateClient();
        var token = await AuthHelper.LoginAsync(client);
        Assert.False(string.IsNullOrWhiteSpace(token));
    }
}
