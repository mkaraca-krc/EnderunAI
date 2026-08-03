using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using EnderunAI.Api.Services.Accounting;
using EnderunAI.Api.Services.Email;
using EnderunAI.Api.Services.Rfq;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Hizir;

public sealed record PendingActionView(
    Guid Id,
    string ActionName,
    string Summary,
    int Status,
    DateTime ExpiresAtUtc,
    string? ResultMessage);

public interface IHizirPendingActionStore
{
    Task<HizirPendingAction> CreateAsync(
        Guid userId,
        string actionName,
        IReadOnlyDictionary<string, object?> arguments,
        string summary,
        string? requiredPermission,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PendingActionView>> GetPendingAsync(
        CancellationToken cancellationToken);

    Task<PendingActionView> ConfirmAsync(Guid id, CancellationToken cancellationToken);

    Task<PendingActionView> CancelAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// Bekleyen eylemlerin deposu ve yürütücüsü.
///
/// GÜVENLİK: Yürütme YALNIZCA <see cref="ConfirmAsync"/> içinden olur ve
/// bu metot yalnızca kullanıcının kendi oturumuyla gelen HTTP ucundan
/// çağrılır. Dil modeline bu metoda giden hiçbir araç tanıtılmaz.
///
/// Onay anında dört kontrol yapılır:
///   1. Kayıt çağıran kullanıcıya mı ait (başkasınınki onaylanamaz)
///   2. Durum hâlâ "Bekliyor" mu (aynı eylem iki kez çalışamaz)
///   3. Süresi dolmuş mu (eski onay sonradan tetiklenemez)
///   4. Kullanıcının izni HÂLÂ var mı (arada yetkisi alınmış olabilir)
/// </summary>
public sealed class HizirPendingActionStore(
    AppDbContext db,
    ICurrentUserService currentUser,
    IUserAuthorizationService authorization,
    IHizirActionAuditor auditor,
    IRfqService rfqService,
    ISupplierInvoiceService supplierInvoiceService,
    IEmailService emailService) : IHizirPendingActionStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    public async Task<HizirPendingAction> CreateAsync(
        Guid userId,
        string actionName,
        IReadOnlyDictionary<string, object?> arguments,
        string summary,
        string? requiredPermission,
        CancellationToken cancellationToken)
    {
        var action = new HizirPendingAction
        {
            UserId = userId,
            ActionName = actionName,
            // Argümanlar burada dondurulur; onay ile yürütme arasında
            // değiştirilemez.
            ArgumentsJson = JsonSerializer.Serialize(arguments),
            Summary = summary,
            RequiredPermission = requiredPermission,
            Status = HizirPendingActionStatus.Pending,
            ExpiresAtUtc = DateTime.UtcNow.Add(Lifetime),
            CreatedByUserId = userId
        };

        db.HizirPendingActions.Add(action);
        await db.SaveChangesAsync(cancellationToken);

        await auditor.RecordAsync(
            userId, currentUser.Username, "hazirlandi", action, cancellationToken);

        return action;
    }

    public async Task<IReadOnlyList<PendingActionView>> GetPendingAsync(
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
            return [];

        var now = DateTime.UtcNow;

        return await db.HizirPendingActions
            .AsNoTracking()
            .Where(x => x.UserId == userId &&
                        x.Status == HizirPendingActionStatus.Pending &&
                        x.ExpiresAtUtc > now)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new PendingActionView(
                x.Id, x.ActionName, x.Summary, (int)x.Status,
                x.ExpiresAtUtc, x.ResultMessage))
            .ToListAsync(cancellationToken);
    }

    public async Task<PendingActionView> ConfirmAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var (action, userId) = await LoadOwnedAsync(id, cancellationToken);

        var now = DateTime.UtcNow;

        if (action.Status != HizirPendingActionStatus.Pending)
        {
            throw new InvalidOperationException(
                $"Bu eylem zaten sonuçlanmış ({StatusName(action.Status)}).");
        }

        if (action.ExpiresAtUtc <= now)
        {
            action.Status = HizirPendingActionStatus.Expired;
            action.ResolvedAtUtc = now;
            await db.SaveChangesAsync(cancellationToken);

            await auditor.RecordAsync(
                userId, currentUser.Username, "suresi_doldu", action, cancellationToken);

            throw new InvalidOperationException(
                "Bu eylemin onay süresi doldu. Lütfen Hızır'a yeniden hazırlatın.");
        }

        // İzin yürütme anında yeniden kontrol edilir: hazırlıktan sonra
        // kullanıcının yetkisi alınmış olabilir.
        if (action.RequiredPermission is not null)
        {
            var snapshot = await authorization.GetAsync(userId, cancellationToken);

            var allowed = snapshot is not null && snapshot.IsActive &&
                snapshot.Permissions.Contains(
                    action.RequiredPermission, StringComparer.OrdinalIgnoreCase);

            if (!allowed)
            {
                action.Status = HizirPendingActionStatus.Cancelled;
                action.ResolvedAtUtc = now;
                action.ResultMessage = "Yetki bulunmadığı için iptal edildi.";
                await db.SaveChangesAsync(cancellationToken);

                await auditor.RecordAsync(
                    userId, currentUser.Username, "yetkisiz_reddedildi",
                    action, cancellationToken);

                throw new InvalidOperationException(
                    "Bu eylemi yapma yetkiniz yok.");
            }
        }

        try
        {
            var result = await ExecuteAsync(action, cancellationToken);

            action.Status = HizirPendingActionStatus.Executed;
            action.ResultMessage = result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            action.Status = HizirPendingActionStatus.Failed;
            action.ResultMessage = exception.Message;
        }

        action.ResolvedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await auditor.RecordAsync(
            userId, currentUser.Username,
            action.Status == HizirPendingActionStatus.Executed
                ? "onaylandi_yurutuldu"
                : "yurutme_hatasi",
            action, cancellationToken);

        if (action.Status == HizirPendingActionStatus.Failed)
            throw new InvalidOperationException(action.ResultMessage ?? "Eylem yürütülemedi.");

        return ToView(action);
    }

    public async Task<PendingActionView> CancelAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var (action, userId) = await LoadOwnedAsync(id, cancellationToken);

        if (action.Status != HizirPendingActionStatus.Pending)
            throw new InvalidOperationException("Bu eylem zaten sonuçlanmış.");

        action.Status = HizirPendingActionStatus.Cancelled;
        action.ResolvedAtUtc = DateTime.UtcNow;
        action.ResultMessage = "Kullanıcı vazgeçti.";

        await db.SaveChangesAsync(cancellationToken);

        await auditor.RecordAsync(
            userId, currentUser.Username, "vazgecildi", action, cancellationToken);

        return ToView(action);
    }

    /// <summary>
    /// Kaydı yükler ve çağıranın sahibi olduğunu doğrular. Başka bir
    /// kullanıcının bekleyen eylemi hiçbir koşulda okunamaz/onaylanamaz.
    /// </summary>
    private async Task<(HizirPendingAction Action, Guid UserId)> LoadOwnedAsync(
        Guid id, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
            throw new InvalidOperationException("Oturum bulunamadı.");

        var action = await db.HizirPendingActions
            .SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, cancellationToken)
            ?? throw new KeyNotFoundException("Bekleyen eylem bulunamadı.");

        return (action, userId);
    }

    /// <summary>
    /// Dondurulmuş argümanlarla gerçek işi yapar. Yalnızca burada,
    /// yalnızca onaydan sonra çalışır.
    /// </summary>
    private async Task<string> ExecuteAsync(
        HizirPendingAction action, CancellationToken cancellationToken)
    {
        var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            action.ArgumentsJson) ?? [];

        string? Arg(string key) =>
            args.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        return action.ActionName switch
        {
            "rfq_ac" => await ExecuteRfqAsync(Arg, cancellationToken),
            "fatura_onaya_gonder" => await ExecuteInvoiceSubmitAsync(Arg, cancellationToken),
            "eposta_gonder" => await ExecuteEmailAsync(Arg, cancellationToken),
            _ => throw new InvalidOperationException(
                $"'{action.ActionName}' için yürütme tanımlı değil.")
        };
    }

    private async Task<string> ExecuteRfqAsync(
        Func<string, string?> arg, CancellationToken cancellationToken)
    {
        var requestNumber = arg("satinalma_talep_no")?.Trim();

        if (string.IsNullOrWhiteSpace(requestNumber))
            throw new InvalidOperationException("Satın alma talep numarası belirtilmemiş.");

        var purchaseRequest = await db.PurchaseRequests
            .Where(x => x.RequestNumber == requestNumber)
            .Select(x => new { x.Id, x.RequestNumber })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                $"'{requestNumber}' numaralı satın alma talebi bulunamadı.");

        DateTime? deadline = DateTime.TryParse(arg("son_teklif_tarihi"), out var parsed)
            ? DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc)
            : null;

        var created = await rfqService.CreateFromPurchaseRequestAsync(
            purchaseRequest.Id,
            new Contracts.Rfq.CreateRfqRequest(
                Title: arg("baslik")?.Trim() ?? $"{purchaseRequest.RequestNumber} teklif isteği",
                ResponseDeadline: deadline,
                Currency: "TRY",
                Description: null,
                Notes: "Hızır asistanı üzerinden, kullanıcı onayıyla oluşturuldu.",
                SupplierCurrentAccountIds: []),
            cancellationToken);

        return $"RFQ oluşturuldu (talep {purchaseRequest.RequestNumber}). " +
               "Tedarikçileri RFQ ekranından ekleyip gönderebilirsiniz.";
    }

    private async Task<string> ExecuteInvoiceSubmitAsync(
        Func<string, string?> arg, CancellationToken cancellationToken)
    {
        var number = arg("fatura_no")?.Trim();

        if (string.IsNullOrWhiteSpace(number))
            throw new InvalidOperationException("Fatura numarası belirtilmemiş.");

        var invoice = await db.SupplierInvoices
            .Where(x => x.InvoiceNumber == number || x.InternalNumber == number)
            .Select(x => new { x.Id, x.InternalNumber, x.InvoiceNumber })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"'{number}' numaralı fatura bulunamadı.");

        await supplierInvoiceService.SubmitAsync(invoice.Id, cancellationToken);

        return $"{invoice.InternalNumber} ({invoice.InvoiceNumber}) faturası onaya gönderildi.";
    }

    /// <summary>
    /// E-posta yalnızca sistemde KAYITLI adreslere gider. Serbest adres
    /// kabul edilmez; aksi halde Hızır üzerinden şirket verisi herhangi
    /// bir dış adrese gönderilebilirdi.
    /// </summary>
    private async Task<string> ExecuteEmailAsync(
        Func<string, string?> arg, CancellationToken cancellationToken)
    {
        var recipient = arg("alici")?.Trim();

        if (string.IsNullOrWhiteSpace(recipient))
            throw new InvalidOperationException("Alıcı belirtilmemiş.");

        var resolved = await ResolveKnownRecipientAsync(recipient, cancellationToken)
            ?? throw new InvalidOperationException(
                $"'{recipient}' sistemde kayıtlı bir e-posta adresine eşleşmedi. " +
                "Hızır yalnızca cari, personel veya kullanıcı kayıtlarındaki " +
                "adreslere e-posta gönderebilir.");

        var subject = arg("konu")?.Trim();
        if (string.IsNullOrWhiteSpace(subject))
            throw new InvalidOperationException("E-posta konusu belirtilmemiş.");

        var body = arg("mesaj")?.Trim();
        if (string.IsNullOrWhiteSpace(body))
            throw new InvalidOperationException("E-posta metni boş olamaz.");

        await emailService.SendAsync(
            resolved.Email, resolved.Name, subject,
            $"<p>{System.Net.WebUtility.HtmlEncode(body).Replace("\n", "<br/>")}</p>",
            cancellationToken);

        return $"E-posta gönderildi: {resolved.Name} <{resolved.Email}>";
    }

    private async Task<(string Email, string Name)?> ResolveKnownRecipientAsync(
        string input, CancellationToken cancellationToken)
    {
        var term = input.ToLowerInvariant();

        var fromCurrentAccount = await db.CurrentAccounts
            .Where(x => x.Email != null && x.Email != "" &&
                        (x.Email.ToLower() == term || x.Title.ToLower().Contains(term)))
            .Select(x => new { Email = x.Email!, Name = x.Title })
            .FirstOrDefaultAsync(cancellationToken);

        if (fromCurrentAccount is not null)
            return (fromCurrentAccount.Email, fromCurrentAccount.Name);

        var fromPersonnel = await db.Personnel
            .Where(x => x.Email != null && x.Email != "" &&
                        (x.Email.ToLower() == term ||
                         (x.FirstName + " " + x.LastName).ToLower().Contains(term)))
            .Select(x => new { Email = x.Email!, Name = x.FirstName + " " + x.LastName })
            .FirstOrDefaultAsync(cancellationToken);

        if (fromPersonnel is not null)
            return (fromPersonnel.Email, fromPersonnel.Name);

        var fromUser = await db.Users
            .Where(x => x.Email != null && x.Email != "" &&
                        (x.Email.ToLower() == term || x.FullName.ToLower().Contains(term)))
            .Select(x => new { Email = x.Email!, Name = x.FullName })
            .FirstOrDefaultAsync(cancellationToken);

        return fromUser is null ? null : (fromUser.Email, fromUser.Name);
    }

    private static PendingActionView ToView(HizirPendingAction action) =>
        new(action.Id, action.ActionName, action.Summary, (int)action.Status,
            action.ExpiresAtUtc, action.ResultMessage);

    private static string StatusName(HizirPendingActionStatus status) => status switch
    {
        HizirPendingActionStatus.Executed => "yürütüldü",
        HizirPendingActionStatus.Cancelled => "iptal edildi",
        HizirPendingActionStatus.Expired => "süresi doldu",
        HizirPendingActionStatus.Failed => "hata aldı",
        _ => status.ToString()
    };
}
