using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EnderunAI.Api.Contracts;

namespace EnderunAI.Api.Services.AI;

public sealed class HizirChatService(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IHizirDashboardAggregator dashboardAggregator) : IHizirChatService
{
    public async Task<HizirChatResponse> ReplyAsync(HizirChatRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            throw new ArgumentException("Mesaj boş olamaz.", nameof(request));

        var apiKey = configuration["OpenAI:ApiKey"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("OPENAI_API_KEY tanımlı değil.");

        var snapshot = await dashboardAggregator.GetSnapshotAsync(cancellationToken);
        var model = configuration["OpenAI:Model"] ?? Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-5";
        var input = new List<object>();

        if (request.History is not null)
        {
            foreach (var item in request.History.TakeLast(12))
            {
                var role = item.Role is "assistant" or "user" ? item.Role : "user";
                input.Add(new { role, content = item.Content });
            }
        }

        input.Add(new
        {
            role = "user",
            content = $"ERP anlık görüntüsü: {JsonSerializer.Serialize(snapshot)}\n\nKullanıcı mesajı: {request.Message.Trim()}"
        });

        var payload = JsonSerializer.Serialize(new
        {
            model,
            instructions = "Sen Enderun AI içindeki Hızır adlı kurumsal dijital asistansın. Türkçe, kısa, net ve temkinli konuş. Şirket verisi yoksa uydurma. Kritik işlemleri kullanıcı onayı olmadan yapılmış gibi gösterme. Kullanıcıya Mehmet Bey diye hitap et.",
            input
        });

        var client = httpClientFactory.CreateClient("OpenAI");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        httpRequest.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"OpenAI isteği başarısız: {(int)response.StatusCode} {body}");

        using var document = JsonDocument.Parse(body);
        var reply = ExtractOutputText(document.RootElement);
        if (string.IsNullOrWhiteSpace(reply))
            throw new InvalidOperationException("Hızır geçerli bir cevap üretemedi.");

        return new HizirChatResponse(reply, "Hızır", DateTime.UtcNow);
    }

    private static string? ExtractOutputText(JsonElement root)
    {
        if (!root.TryGetProperty("output", out var output)) return null;
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content)) continue;
            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("type", out var type)
                    && type.GetString() == "output_text"
                    && part.TryGetProperty("text", out var text))
                    return text.GetString();
            }
        }
        return null;
    }
}
