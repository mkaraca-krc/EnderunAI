using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Procurement;

public sealed record RfqInviteRecipient(Guid SupplierId, string Email, string Name);
public sealed record RfqInvitationResult(Guid InvitationId, string AccessToken, string Status, string? Error);

public interface IRfqInvitationService
{
    Task<IReadOnlyList<RfqInvitationResult>> SendAsync(Guid rfqId, IReadOnlyList<RfqInviteRecipient> recipients, string portalBaseUrl, bool singleUse, CancellationToken cancellationToken = default);
    Task<RfqInvitationResult> ResendAsync(Guid invitationId, string portalBaseUrl, CancellationToken cancellationToken = default);
    Task<RfqSupplierInvitation> ValidateAsync(string token, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task<int> SendDueRemindersAsync(string portalBaseUrl, int hoursBeforeDeadline = 24, CancellationToken cancellationToken = default);
}

public sealed class RfqInvitationService(
    ProcurementDbContext procurementDb,
    RfqInvitationDbContext invitationDb,
    IConfiguration configuration) : IRfqInvitationService
{
    public async Task<IReadOnlyList<RfqInvitationResult>> SendAsync(Guid rfqId, IReadOnlyList<RfqInviteRecipient> recipients, string portalBaseUrl, bool singleUse, CancellationToken cancellationToken = default)
    {
        var rfq = await procurementDb.Rfqs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == rfqId, cancellationToken)
            ?? throw new InvalidOperationException("RFQ bulunamadı.");
        if (rfq.Status == RfqStatus.Cancelled || rfq.Status == RfqStatus.Awarded)
            throw new InvalidOperationException("Kapalı RFQ için davet gönderilemez.");
        if (!rfq.OfferDeadlineUtc.HasValue)
            throw new InvalidOperationException("RFQ teklif son tarihi tanımlı değil.");

        var results = new List<RfqInvitationResult>();
        foreach (var recipient in recipients.Where(x => !string.IsNullOrWhiteSpace(x.Email)))
        {
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var invitation = new RfqSupplierInvitation
            {
                CompanyId = rfq.CompanyId,
                RfqId = rfq.Id,
                SupplierCurrentAccountId = recipient.SupplierId,
                RecipientEmail = recipient.Email.Trim(),
                RecipientName = recipient.Name.Trim(),
                TokenHash = Hash(token),
                ExpiresAtUtc = rfq.OfferDeadlineUtc.Value,
                SingleUse = singleUse
            };
            invitationDb.Invitations.Add(invitation);
            await invitationDb.SaveChangesAsync(cancellationToken);
            results.Add(await SendInvitationAsync(invitation, rfq, token, portalBaseUrl, cancellationToken));
        }
        return results;
    }

    public async Task<RfqInvitationResult> ResendAsync(Guid invitationId, string portalBaseUrl, CancellationToken cancellationToken = default)
    {
        var invitation = await invitationDb.Invitations.FirstOrDefaultAsync(x => x.Id == invitationId, cancellationToken)
            ?? throw new InvalidOperationException("Davet bulunamadı.");
        var rfq = await procurementDb.Rfqs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == invitation.RfqId, cancellationToken)
            ?? throw new InvalidOperationException("RFQ bulunamadı.");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        invitation.TokenHash = Hash(token);
        invitation.Status = RfqInvitationStatus.Pending;
        invitation.ExpiresAtUtc = rfq.OfferDeadlineUtc ?? invitation.ExpiresAtUtc;
        invitation.UpdatedAtUtc = DateTime.UtcNow;
        await invitationDb.SaveChangesAsync(cancellationToken);
        return await SendInvitationAsync(invitation, rfq, token, portalBaseUrl, cancellationToken);
    }

    public async Task<RfqSupplierInvitation> ValidateAsync(string token, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var invitation = await invitationDb.Invitations.FirstOrDefaultAsync(x => x.TokenHash == Hash(token), cancellationToken)
            ?? throw new InvalidOperationException("Geçersiz davet bağlantısı.");
        if (invitation.Status is RfqInvitationStatus.Revoked or RfqInvitationStatus.Expired)
            throw new InvalidOperationException("Davet bağlantısı kullanılamıyor.");
        if (invitation.ExpiresAtUtc <= DateTime.UtcNow)
        {
            invitation.Status = RfqInvitationStatus.Expired;
            await invitationDb.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("Davet bağlantısının süresi dolmuş.");
        }
        if (invitation.SingleUse && invitation.OpenedAtUtc.HasValue)
            throw new InvalidOperationException("Bu davet bağlantısı daha önce kullanılmış.");

        invitation.OpenedAtUtc ??= DateTime.UtcNow;
        invitation.Status = RfqInvitationStatus.Opened;
        invitationDb.Events.Add(new RfqInvitationEvent { InvitationId = invitation.Id, EventType = "Opened", IpAddress = ipAddress, UserAgent = userAgent });
        await invitationDb.SaveChangesAsync(cancellationToken);
        return invitation;
    }

    public async Task<int> SendDueRemindersAsync(string portalBaseUrl, int hoursBeforeDeadline = 24, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var until = now.AddHours(Math.Clamp(hoursBeforeDeadline, 1, 168));
        var invitations = await invitationDb.Invitations
            .Where(x => x.ExpiresAtUtc > now && x.ExpiresAtUtc <= until && x.Status != RfqInvitationStatus.OfferSubmitted && x.Status != RfqInvitationStatus.Revoked)
            .ToListAsync(cancellationToken);
        var sent = 0;
        foreach (var invitation in invitations)
        {
            var rfq = await procurementDb.Rfqs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == invitation.RfqId, cancellationToken);
            if (rfq is null) continue;
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            invitation.TokenHash = Hash(token);
            var result = await SendInvitationAsync(invitation, rfq, token, portalBaseUrl, cancellationToken, true);
            if (result.Error is null) { invitation.ReminderCount++; invitation.LastReminderAtUtc = DateTime.UtcNow; sent++; }
            await invitationDb.SaveChangesAsync(cancellationToken);
        }
        return sent;
    }

    private async Task<RfqInvitationResult> SendInvitationAsync(RfqSupplierInvitation invitation, Rfq rfq, string token, string portalBaseUrl, CancellationToken cancellationToken, bool reminder = false)
    {
        invitation.SendAttemptCount++;
        try
        {
            var host = configuration["Smtp:Host"] ?? Environment.GetEnvironmentVariable("SMTP_HOST");
            var username = configuration["Smtp:Username"] ?? Environment.GetEnvironmentVariable("SMTP_USERNAME");
            var password = configuration["Smtp:Password"] ?? Environment.GetEnvironmentVariable("SMTP_PASSWORD");
            var from = configuration["Smtp:From"] ?? Environment.GetEnvironmentVariable("SMTP_FROM");
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from))
                throw new InvalidOperationException("SMTP ayarları tanımlı değil.");

            var port = int.TryParse(configuration["Smtp:Port"] ?? Environment.GetEnvironmentVariable("SMTP_PORT"), out var parsedPort) ? parsedPort : 587;
            var link = $"{portalBaseUrl.TrimEnd('/')}/rfq/{token}";
            using var message = new MailMessage(from, invitation.RecipientEmail)
            {
                Subject = reminder ? $"Hatırlatma: {rfq.RfqNumber} teklif talebi" : $"Enderun AI teklif talebi: {rfq.RfqNumber}",
                Body = BuildHtml(invitation.RecipientName, rfq, link, reminder),
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8
            };
            using var client = new SmtpClient(host, port) { EnableSsl = true };
            if (!string.IsNullOrWhiteSpace(username)) client.Credentials = new NetworkCredential(username, password);
            await client.SendMailAsync(message, cancellationToken);

            invitation.Status = RfqInvitationStatus.Sent;
            invitation.SentAtUtc = DateTime.UtcNow;
            invitation.LastError = null;
            invitationDb.Events.Add(new RfqInvitationEvent { InvitationId = invitation.Id, EventType = reminder ? "ReminderSent" : "Sent", Detail = invitation.RecipientEmail });
            await invitationDb.SaveChangesAsync(cancellationToken);
            return new RfqInvitationResult(invitation.Id, token, invitation.Status.ToString(), null);
        }
        catch (Exception ex)
        {
            invitation.Status = RfqInvitationStatus.Failed;
            invitation.LastError = ex.Message;
            invitationDb.Events.Add(new RfqInvitationEvent { InvitationId = invitation.Id, EventType = "SendFailed", Detail = ex.Message });
            await invitationDb.SaveChangesAsync(cancellationToken);
            return new RfqInvitationResult(invitation.Id, token, invitation.Status.ToString(), ex.Message);
        }
    }

    private static string BuildHtml(string name, Rfq rfq, string link, bool reminder) => $"""
<!doctype html><html><body style='font-family:Arial,sans-serif;color:#1f2937'>
<h2>Enderun AI – Teklif Talebi</h2><p>Sayın {WebUtility.HtmlEncode(name)},</p>
<p>{(reminder ? "Teklif süresi yaklaşan" : "Aşağıda bilgileri bulunan")} RFQ için teklifinizi güvenli bağlantı üzerinden iletebilirsiniz.</p>
<table><tr><td><b>RFQ No</b></td><td>{WebUtility.HtmlEncode(rfq.RfqNumber)}</td></tr><tr><td><b>Son Tarih</b></td><td>{rfq.OfferDeadlineUtc:dd.MM.yyyy HH:mm} UTC</td></tr><tr><td><b>Para Birimi</b></td><td>{WebUtility.HtmlEncode(rfq.CurrencyCode)}</td></tr></table>
<p><a href='{link}' style='background:#111827;color:white;padding:12px 18px;text-decoration:none;border-radius:6px'>RFQ'yu Görüntüle</a></p>
<p>Bu bağlantı kişiye özeldir ve üçüncü kişilerle paylaşılmamalıdır.</p></body></html>
""";

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
