namespace EnderunAI.Api.Services.Email;

public interface IEmailService
{
    bool IsConfigured { get; }

    Task SendAsync(
        string toEmail,
        string? toName,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default);
}
