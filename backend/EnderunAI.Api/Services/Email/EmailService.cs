using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace EnderunAI.Api.Services.Email;

/// <summary>
/// Brevo (eski adıyla Sendinblue) transactional e-posta HTTP API'si
/// üzerinden gönderim yapar. SMTP yerine tercih edilme sebebi: sunucu
/// sağlayıcısında SMTP portları (25/465/587) kapalı ve açtırılamıyor;
/// Brevo API'si HTTPS/443 üzerinden çalıştığı için bu kısıtı bypass eder.
/// API anahtarı BREVO_API_KEY ortam değişkeninden okunur, koda/git'e
/// girmez.
/// </summary>
public sealed class EmailService : IEmailService
{
    private const string SendEndpoint = "https://api.brevo.com/v3/smtp/email";

    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly string? _fromAddress;
    private readonly string? _fromName;

    public EmailService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _apiKey = Read(configuration, "BREVO_API_KEY");
        _fromAddress = Read(configuration, "SMTP_FROM");
        _fromName = Read(configuration, "SMTP_FROM_NAME");
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_apiKey) &&
        !string.IsNullOrWhiteSpace(_fromAddress);

    public async Task SendAsync(
        string toEmail,
        string? toName,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("E-posta yapılandırılmamış.");
        }

        var payload = new BrevoSendRequest
        {
            Sender = new BrevoContact { Email = _fromAddress!, Name = _fromName ?? _fromAddress },
            To = [new BrevoContact { Email = toEmail, Name = toName ?? toEmail }],
            Subject = subject,
            HtmlContent = htmlBody
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, SendEndpoint)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("api-key", _apiKey);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(20));

        using var response = await _httpClient.SendAsync(request, timeoutCts.Token);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            throw new InvalidOperationException(
                $"Brevo e-posta gönderimi başarısız ({(int)response.StatusCode}): {body}");
        }
    }

    private static string? Read(IConfiguration configuration, string key)
    {
        var value = Environment.GetEnvironmentVariable(key) ?? configuration[key];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed class BrevoSendRequest
    {
        [JsonPropertyName("sender")]
        public required BrevoContact Sender { get; set; }

        [JsonPropertyName("to")]
        public required BrevoContact[] To { get; set; }

        [JsonPropertyName("subject")]
        public required string Subject { get; set; }

        [JsonPropertyName("htmlContent")]
        public required string HtmlContent { get; set; }
    }

    private sealed class BrevoContact
    {
        [JsonPropertyName("email")]
        public required string Email { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
