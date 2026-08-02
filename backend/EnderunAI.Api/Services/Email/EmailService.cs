using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using MimeKit.Text;

namespace EnderunAI.Api.Services.Email;

public sealed class EmailService : IEmailService
{
    private readonly string? _host;
    private readonly int _port;
    private readonly string? _user;
    private readonly string? _pass;
    private readonly string? _fromAddress;
    private readonly string? _fromName;

    public EmailService(IConfiguration configuration)
    {
        _host = Read(configuration, "SMTP_HOST");
        _user = Read(configuration, "SMTP_USER");
        _pass = Read(configuration, "SMTP_PASS");
        _fromAddress = Read(configuration, "SMTP_FROM");
        _fromName = Read(configuration, "SMTP_FROM_NAME");

        var portValue = Read(configuration, "SMTP_PORT");
        _port = int.TryParse(portValue, out var parsed) ? parsed : 465;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_host) &&
        !string.IsNullOrWhiteSpace(_user) &&
        !string.IsNullOrWhiteSpace(_pass) &&
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

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_fromName ?? _fromAddress, _fromAddress!));
        message.To.Add(new MailboxAddress(toName ?? toEmail, toEmail));
        message.Subject = subject;
        message.Body = new TextPart(TextFormat.Html)
        {
            Text = htmlBody
        };

        using var client = new SmtpClient
        {
            Timeout = 15000
        };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(20));

        try
        {
            await client.ConnectAsync(_host!, _port, SecureSocketOptions.SslOnConnect, timeoutCts.Token);
            await client.AuthenticateAsync(_user!, _pass!, timeoutCts.Token);
            await client.SendAsync(message, timeoutCts.Token);
        }
        finally
        {
            if (client.IsConnected)
                await client.DisconnectAsync(true, cancellationToken);
        }
    }

    private static string? Read(IConfiguration configuration, string key)
    {
        var value = Environment.GetEnvironmentVariable(key) ?? configuration[key];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
