using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace EnderunAI.Api.Services.Email;

/// <summary>
/// Turhost SMTP üzerinden gönderim (srvc141.trwww.com:465).
///
/// 465 numaralı port bağlantının başından itibaren TLS ister
/// (implicit SSL / SslOnConnect). .NET'in yerleşik
/// System.Net.Mail.SmtpClient sınıfı yalnızca STARTTLS destekler ve bu
/// portta çalışmaz; bu yüzden MailKit kullanılıyor.
///
/// Kimlik bilgileri ortam değişkenlerinden okunur, koda/git'e girmez.
/// Brevo gönderimi <see cref="EmailService"/> içinde duruyor; aktif
/// kanal EMAIL_PROVIDER ile seçiliyor (bkz. Program.cs).
/// </summary>
public sealed class SmtpEmailService : IEmailService
{
    private const int TimeoutSeconds = 30;

    private readonly ILogger<SmtpEmailService> _logger;

    private readonly string? _host;
    private readonly int _port;
    private readonly string? _user;
    private readonly string? _password;
    private readonly string? _fromAddress;
    private readonly string? _fromName;

    public SmtpEmailService(IConfiguration configuration, ILogger<SmtpEmailService> logger)
    {
        _logger = logger;

        _host = Read(configuration, "SMTP_HOST");
        _user = Read(configuration, "SMTP_USER");
        _password = Read(configuration, "SMTP_PASS");
        _fromAddress = Read(configuration, "SMTP_FROM");
        _fromName = Read(configuration, "SMTP_FROM_NAME");

        var portValue = Read(configuration, "SMTP_PORT");
        _port = int.TryParse(portValue, out var parsed) ? parsed : 465;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_host) &&
        !string.IsNullOrWhiteSpace(_user) &&
        !string.IsNullOrWhiteSpace(_password) &&
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
            throw new InvalidOperationException(
                "E-posta yapılandırılmamış: SMTP_HOST, SMTP_USER, SMTP_PASS ve " +
                "SMTP_FROM ortam değişkenlerinin tanımlı olması gerekiyor.");
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_fromName ?? _fromAddress, _fromAddress));
        message.To.Add(new MailboxAddress(toName ?? toEmail, toEmail));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        using var timeoutCts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

        using var client = new SmtpClient
        {
            Timeout = TimeoutSeconds * 1000
        };

        try
        {
            // 465 → SslOnConnect; 587/25 kullanılırsa STARTTLS'e düşer.
            var security = _port == 465
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTls;

            await client.ConnectAsync(_host, _port, security, timeoutCts.Token);
            await client.AuthenticateAsync(_user, _password, timeoutCts.Token);
            await client.SendAsync(message, timeoutCts.Token);
            await client.DisconnectAsync(true, timeoutCts.Token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Hata metni sunucu yanıtı içerebilir ama parola içermez;
            // yine de log'a yalnızca tür ve mesaj yazılır.
            _logger.LogError(
                exception, "SMTP e-posta gönderimi başarısız ({Host}:{Port})", _host, _port);

            throw new InvalidOperationException(
                $"E-posta gönderilemedi: {exception.Message}", exception);
        }
    }

    private static string? Read(IConfiguration configuration, string key)
    {
        var value = Environment.GetEnvironmentVariable(key) ?? configuration[key];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
