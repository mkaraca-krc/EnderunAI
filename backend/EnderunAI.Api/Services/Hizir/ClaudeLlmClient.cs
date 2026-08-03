using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EnderunAI.Api.Services.Hizir;

/// <summary>
/// Anthropic Messages API istemcisi. Anahtar ortam değişkeninden okunur
/// (ANTHROPIC_API_KEY), koda ya da yapılandırma dosyasına yazılmaz.
///
/// Anahtar yoksa fail-closed: istek hiç gönderilmez, kullanıcıya ne
/// yapması gerektiğini söyleyen açık bir hata döner. Sessizce boş cevap
/// vermek, asistanın çalıştığı ama veri bulamadığı izlenimi yaratırdı.
/// </summary>
public sealed class ClaudeLlmClient : IHizirLlmClient
{
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string ApiVersion = "2023-06-01";

    /// <summary>
    /// Yanıt uzunluğu tavanı. Sohbet cevapları kısa olduğu için düşük
    /// tutuluyor — çıktı token maliyetini sınırlar.
    /// </summary>
    private const int MaxOutputTokens = 2048;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<ClaudeLlmClient> _logger;
    private readonly string? _apiKey;

    public ClaudeLlmClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<ClaudeLlmClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        _apiKey =
            configuration["Hizir:ApiKey"] ??
            Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

        ModelId =
            configuration["Hizir:Model"] ??
            Environment.GetEnvironmentVariable("HIZIR_MODEL") ??
            "claude-sonnet-4-5-20250929";
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public string ModelId { get; }

    public async Task<LlmCompletion> CompleteAsync(
        string systemPrompt,
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<LlmToolDefinition> tools,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                "Hızır henüz yapılandırılmadı: ANTHROPIC_API_KEY ortam " +
                "değişkeni tanımlı değil. Sistem yöneticisinin anahtarı " +
                "sunucu ayarlarına eklemesi gerekiyor.");
        }

        var payload = new JsonObject
        {
            ["model"] = ModelId,
            ["max_tokens"] = MaxOutputTokens,
            ["system"] = systemPrompt,
            ["messages"] = BuildMessages(messages)
        };

        if (tools.Count > 0)
        {
            var toolArray = new JsonArray();

            foreach (var tool in tools)
            {
                toolArray.Add(new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["input_schema"] = JsonSerializer.SerializeToNode(tool.InputSchema)
                });
            }

            payload["tools"] = toolArray;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
        {
            Content = new StringContent(
                payload.ToJsonString(), Encoding.UTF8, "application/json")
        };

        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Add("anthropic-version", ApiVersion);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // Sağlayıcı hata gövdesi anahtar ya da kullanıcı verisi
            // içerebilir; log'a yalnızca durum kodu yazılır.
            _logger.LogError(
                "Hızır LLM çağrısı başarısız: {StatusCode}", response.StatusCode);

            throw new InvalidOperationException(
                "Hızır şu anda cevap veremiyor (yapay zekâ servisine " +
                "ulaşılamadı). Lütfen biraz sonra tekrar deneyin.");
        }

        return ParseCompletion(body);
    }

    private static JsonArray BuildMessages(IReadOnlyList<LlmMessage> messages)
    {
        var array = new JsonArray();

        foreach (var message in messages)
        {
            // Araç sonuçları protokolde kullanıcı rolüyle gönderilir.
            if (message.ToolResults is { Count: > 0 })
            {
                var resultContent = new JsonArray();

                foreach (var result in message.ToolResults)
                {
                    resultContent.Add(new JsonObject
                    {
                        ["type"] = "tool_result",
                        ["tool_use_id"] = result.ToolCallId,
                        ["content"] = result.Content,
                        ["is_error"] = result.IsError
                    });
                }

                array.Add(new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = resultContent
                });

                continue;
            }

            var content = new JsonArray();

            if (!string.IsNullOrWhiteSpace(message.Text))
            {
                content.Add(new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = message.Text
                });
            }

            if (message.ToolCalls is { Count: > 0 })
            {
                foreach (var call in message.ToolCalls)
                {
                    content.Add(new JsonObject
                    {
                        ["type"] = "tool_use",
                        ["id"] = call.Id,
                        ["name"] = call.Name,
                        ["input"] = JsonSerializer.SerializeToNode(
                            call.Arguments, JsonOptions)
                    });
                }
            }

            if (content.Count == 0)
                continue;

            array.Add(new JsonObject
            {
                ["role"] = message.Role == LlmRole.User ? "user" : "assistant",
                ["content"] = content
            });
        }

        return array;
    }

    private static LlmCompletion ParseCompletion(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        var text = new StringBuilder();
        var toolCalls = new List<LlmToolCall>();

        if (root.TryGetProperty("content", out var content) &&
            content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                var type = block.TryGetProperty("type", out var typeValue)
                    ? typeValue.GetString()
                    : null;

                switch (type)
                {
                    case "text":
                        if (block.TryGetProperty("text", out var textValue))
                            text.Append(textValue.GetString());
                        break;

                    case "tool_use":
                        toolCalls.Add(new LlmToolCall(
                            block.GetProperty("id").GetString() ?? string.Empty,
                            block.GetProperty("name").GetString() ?? string.Empty,
                            ParseArguments(block)));
                        break;
                }
            }
        }

        var inputTokens = 0;
        var outputTokens = 0;

        if (root.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("input_tokens", out var input))
                inputTokens = input.GetInt32();
            if (usage.TryGetProperty("output_tokens", out var output))
                outputTokens = output.GetInt32();
        }

        return new LlmCompletion(
            text.Length > 0 ? text.ToString() : null,
            toolCalls,
            inputTokens,
            outputTokens);
    }

    private static Dictionary<string, object?> ParseArguments(JsonElement block)
    {
        var arguments = new Dictionary<string, object?>();

        if (!block.TryGetProperty("input", out var input) ||
            input.ValueKind != JsonValueKind.Object)
        {
            return arguments;
        }

        foreach (var property in input.EnumerateObject())
        {
            arguments[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => property.Value.TryGetInt64(out var longValue)
                    ? longValue
                    : property.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => property.Value.GetRawText()
            };
        }

        return arguments;
    }
}
