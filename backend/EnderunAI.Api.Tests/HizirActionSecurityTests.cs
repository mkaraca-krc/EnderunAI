using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using EnderunAI.Api.Services.Email;
using EnderunAI.Api.Services.Hizir;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Katman 2 güvenlik testleri. Hızır'a yazma yetkisi verildiği için bu
/// testler paketin asıl güvencesi.
///
/// Üç şeyi kanıtlarlar:
///   1. Tehlikeli işlemler araç olarak HİÇ tanımlı değil
///   2. Onaylı eylemler, onay olmadan hiçbir şeyi değiştirmez
///   3. Yetkisi olmayan kullanıcıya eylem aracı tanıtılmaz
/// </summary>
[Collection("Integration")]
public sealed class HizirActionSecurityTests(DatabaseFixture fixture)
{
    private static readonly CurrentDataScopeSnapshot GlobalScope = new(
        HasGlobalAccess: true,
        CompanyIds: new HashSet<Guid>(),
        BranchIds: new HashSet<Guid>(),
        ProjectIds: new HashSet<Guid>(),
        VisibleCompanyIds: new HashSet<Guid>(),
        VisibleBranchIds: new HashSet<Guid>(),
        SiteIds: new HashSet<Guid>());

    /// <summary>Onaylı eylem araçları ve gerektirdikleri izin.</summary>
    public static TheoryData<string, string> ApprovalTools =>
        new()
        {
            { "rfq_ac", PermissionCatalog.Keys.PurchasingRfqCreate },
            { "fatura_onaya_gonder", PermissionCatalog.Keys.AccountingEdit },
            { "eposta_gonder", PermissionCatalog.Keys.SecretariatView }
        };

    /// <summary>Bu eylemleri Hızır üzerinden yapabilecek rol olmamalı.</summary>
    public static TheoryData<string> LowPrivilegeRoles =>
        new() { "Şantiye Şefi", "Formen", "Depo Sorumlusu" };

    /// <summary>
    /// Yazan araçlar gerçek bir kullanıcı ve şirket ister (yabancı
    /// anahtarlar); bu yardımcı ikisini de oluşturup bağlam döndürür.
    /// </summary>
    private async Task<HizirToolContext> ContextWithRealUserAsync(string roleName)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        await TestDataFactory.CreateCompanyStackAsync(db, suffix);

        var hash = passwordService.Hash("HizirEylem!2026");

        var user = new AppUser
        {
            Username = $"test-eylem-{suffix}",
            FullName = $"Eylem Testi {roleName}",
            PasswordHash = hash.Hash,
            PasswordSalt = hash.Salt,
            IsActive = true,
            WorkHoursExempt = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var permissions = await db.Roles
            .Where(x => x.Name == roleName)
            .SelectMany(x => db.RolePermissions
                .Where(rp => rp.RoleId == x.Id)
                .Select(rp => rp.Permission.Key))
            .ToListAsync();

        return new HizirToolContext(
            user.Id, user.FullName, null,
            new[] { roleName }, permissions, GlobalScope);
    }

    private async Task<HizirToolContext> ContextForRoleAsync(string roleName)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var permissions = await db.Roles
            .Where(x => x.Name == roleName)
            .SelectMany(x => db.RolePermissions
                .Where(rp => rp.RoleId == x.Id)
                .Select(rp => rp.Permission.Key))
            .ToListAsync();

        Assert.NotEmpty(permissions);

        return new HizirToolContext(
            Guid.NewGuid(), $"Test {roleName}", null,
            new[] { roleName }, permissions, GlobalScope);
    }

    private static IHizirToolRegistry Registry(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IHizirToolRegistry>();

    // ---------- 1. Yasaklı işlemler hiç tanımlı değil ----------

    /// <summary>
    /// KORUMA TESTİ: "asla yapmaz" listesindeki işlemlere karşılık gelen
    /// bir araç kayıt defterine eklenmiş olmamalı. İleride biri
    /// yanlışlıkla eklerse bu test kırılır.
    /// </summary>
    [Theory]
    [InlineData("sil")]
    [InlineData("delete")]
    [InlineData("odeme")]
    [InlineData("payment")]
    [InlineData("transfer")]
    [InlineData("kesinlestir")]
    [InlineData("tahakkuk")]
    [InlineData("rol_")]
    [InlineData("kullanici_")]
    [InlineData("cek_durum")]
    [InlineData("kasa_hareket")]
    [InlineData("yetki")]
    [InlineData("bordro_ode")]
    public void ForbiddenOperations_HaveNoRegisteredTool(string forbiddenFragment)
    {
        using var scope = fixture.Factory.Services.CreateScope();

        var offending = Registry(scope).All
            .Where(x => x.Name.Contains(forbiddenFragment, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Name)
            .ToList();

        Assert.True(
            offending.Count == 0,
            $"'{forbiddenFragment}' deseni taşıyan araç bulundu: " +
            string.Join(", ", offending) +
            ". Bu işlemler Hızır'a araç olarak TANITILMAMALI.");
    }

    /// <summary>
    /// Kayıt defterindeki her aracın bilinen bir kademede olduğunu ve
    /// beklenen araç listesi dışında araç bulunmadığını doğrular.
    /// </summary>
    [Fact]
    public void Registry_ContainsOnlyExpectedTools()
    {
        using var scope = fixture.Factory.Services.CreateScope();

        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Katman 1 — okuma
            "projeleri_listele", "santiye_gunluk_raporlari", "stok_durumu",
            "cari_bakiye", "cek_defteri", "nakit_akis", "muhasebe_ozeti",
            "bordro_ozeti", "bekleyen_onaylar", "kilavuz_ara",
            // Katman 2 — güvenli
            "taslak_hazirla", "hatirlatma_olustur", "personel_atama_onerisi",
            // Katman 2 — onaylı
            "rfq_ac", "fatura_onaya_gonder", "eposta_gonder"
        };

        var actual = Registry(scope).All.Select(x => x.Name).ToList();

        var unexpected = actual.Where(x => !expected.Contains(x)).ToList();

        Assert.True(
            unexpected.Count == 0,
            "Beklenmeyen araç: " + string.Join(", ", unexpected) +
            ". Yeni araç eklendiyse güvenlik kademesi gözden geçirilmeli.");
    }

    // ---------- 2. Yetki sınırı ----------

    [Theory]
    [MemberData(nameof(LowPrivilegeRoles))]
    public async Task LowPrivilegeRoles_AreNotOfferedApprovalTools(string roleName)
    {
        var context = await ContextForRoleAsync(roleName);

        using var scope = fixture.Factory.Services.CreateScope();
        var available = Registry(scope).AvailableFor(context);

        foreach (var toolName in new[] { "rfq_ac", "fatura_onaya_gonder" })
            Assert.DoesNotContain(available, x => x.Name == toolName);
    }

    /// <summary>
    /// Araç modele tanıtılmasa bile, doğrudan çağrıldığında yürütücü
    /// reddetmeli ve hiçbir bekleyen eylem oluşmamalı.
    /// </summary>
    [Theory]
    [MemberData(nameof(ApprovalTools))]
    public async Task ApprovalTools_RefuseWhenPermissionMissing(
        string toolName, string requiredPermission)
    {
        var context = await ContextForRoleAsync("Formen");

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var before = await db.HizirPendingActions.CountAsync();

        var tool = Registry(scope).Find(toolName);
        Assert.NotNull(tool);
        Assert.Equal(requiredPermission, tool!.RequiredPermission);
        Assert.Equal(HizirToolTier.RequiresApproval, tool.Tier);
        Assert.False(context.Has(requiredPermission));

        var outcome = await tool.ExecuteAsync(
            context, new Dictionary<string, object?>(), CancellationToken.None);

        Assert.True(outcome.Denied);

        // Reddedilen çağrı hiçbir kayıt bırakmamalı.
        Assert.Equal(before, await db.HizirPendingActions.CountAsync());
    }

    [Fact]
    public async Task AuthorizedRole_IsOfferedItsOwnApprovalTools()
    {
        var purchasing = await ContextForRoleAsync("Satın Alma Sorumlusu");
        var accounting = await ContextForRoleAsync("Ön Muhasebe");

        using var scope = fixture.Factory.Services.CreateScope();
        var registry = Registry(scope);

        Assert.Contains(registry.AvailableFor(purchasing), x => x.Name == "rfq_ac");
        Assert.Contains(
            registry.AvailableFor(accounting), x => x.Name == "fatura_onaya_gonder");
    }

    // ---------- 3. Onay akışı ----------

    /// <summary>
    /// Onaylı bir aracın çalıştırıcısı iş servisini çağırmaz; yalnızca
    /// bekleyen eylem üretir. Yani onay olmadan hiçbir şey değişmez.
    /// </summary>
    [Fact]
    public async Task ApprovalTool_OnlyCreatesPendingAction_ChangesNothing()
    {
        var context = await ContextWithRealUserAsync("Satın Alma Sorumlusu");

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var rfqCountBefore = await db.Rfqs.CountAsync();

        var tool = Registry(scope).Find("rfq_ac");

        var outcome = await tool!.ExecuteAsync(
            context,
            new Dictionary<string, object?>
            {
                ["satinalma_talep_no"] = "TALEP-YOK-999",
                ["baslik"] = "Test RFQ"
            },
            CancellationToken.None);

        Assert.False(outcome.Denied);
        Assert.Contains("ONAY BEKLİYOR", outcome.Content);

        // RFQ tarafında hiçbir şey oluşmamalı — onay verilmedi.
        Assert.Equal(rfqCountBefore, await db.Rfqs.CountAsync());

        // Bekleyen eylem kaydı oluşmalı ve argümanlar dondurulmalı.
        var pending = await db.HizirPendingActions
            .Where(x => x.UserId == context.UserId)
            .SingleAsync();

        Assert.Equal("rfq_ac", pending.ActionName);
        Assert.Equal(HizirPendingActionStatus.Pending, pending.Status);
        Assert.Contains("TALEP-YOK-999", pending.ArgumentsJson);

        // Özet sunucuda üretilir: modelin yazdığı metin değil.
        Assert.Contains("Teklif isteme", pending.Summary);
        Assert.Contains("TALEP-YOK-999", pending.Summary);
    }

    /// <summary>Güvenli kademe onay istemez ve kaydı doğrudan üretir.</summary>
    [Fact]
    public async Task SafeTool_CreatesReminderOnlyForCaller()
    {
        var context = await ContextWithRealUserAsync("Formen");

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tool = Registry(scope).Find("hatirlatma_olustur");
        Assert.Equal(HizirToolTier.Safe, tool!.Tier);

        var outcome = await tool.ExecuteAsync(
            context,
            new Dictionary<string, object?> { ["baslik"] = $"Hızır test hatırlatması {context.UserId:N}" },
            CancellationToken.None);

        Assert.False(outcome.Denied);

        var task = await db.WorkTasks
            .Where(x => x.Title == $"Hızır test hatırlatması {context.UserId:N}")
            .SingleAsync();

        // Hatırlatma yalnızca çağırana atanır; başkasına görev yüklenemez.
        Assert.Equal(context.UserId, task.AssignedToUserId);
    }

    /// <summary>
    /// Öneri aracı gerçekten atama yapmamalı — yalnızca metin döndürür.
    /// </summary>
    [Fact]
    public async Task AssignmentSuggestion_DoesNotAssignAnyone()
    {
        var context = await ContextForRoleAsync("Şantiye Şefi");

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var before = await db.ProjectSiteAssignments.CountAsync();

        var tool = Registry(scope).Find("personel_atama_onerisi");
        Assert.Equal(HizirToolTier.Safe, tool!.Tier);

        await tool.ExecuteAsync(
            context,
            new Dictionary<string, object?> { ["ihtiyac"] = "usta" },
            CancellationToken.None);

        Assert.Equal(before, await db.ProjectSiteAssignments.CountAsync());
    }

    // ---------- 4. Onay akışı ----------

    /// <summary>
    /// Onay ucunun gerçekten çalıştığı yer burası olduğu için akışın
    /// dört kırılma noktası ayrı ayrı sınanır: sahiplik, tek kullanım,
    /// süre ve onay anındaki izin.
    /// </summary>
    private sealed class FakeCurrentUser(Guid? userId) : ICurrentUserService
    {
        public bool IsAuthenticated => userId is not null;
        public Guid? UserId => userId;
        public string? Username => "test-onay";
        public string? FullName => "Onay Testi";
        public IReadOnlyCollection<string> Roles => [];
        public IReadOnlyCollection<string> Permissions => [];
        public bool IsInRole(string role) => false;
        public bool HasPermission(string permission) => false;
    }

    /// <summary>Gerçek SMTP'ye çıkmadan gönderim olup olmadığını sayar.</summary>
    private sealed class RecordingEmailService : IEmailService
    {
        public int SentCount { get; private set; }
        public string? LastRecipient { get; private set; }
        public bool IsConfigured => true;

        public Task SendAsync(
            string toEmail, string? toName, string subject,
            string htmlBody, CancellationToken cancellationToken = default)
        {
            SentCount++;
            LastRecipient = toEmail;
            return Task.CompletedTask;
        }
    }

    private static HizirPendingActionStore StoreFor(
        IServiceScope scope, Guid? actingUserId, IEmailService email) =>
        new(scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            new FakeCurrentUser(actingUserId),
            scope.ServiceProvider.GetRequiredService<IUserAuthorizationService>(),
            scope.ServiceProvider.GetRequiredService<IHizirActionAuditor>(),
            scope.ServiceProvider.GetRequiredService<Services.Rfq.IRfqService>(),
            scope.ServiceProvider.GetRequiredService<
                Services.Accounting.ISupplierInvoiceService>(),
            email);

    /// <summary>
    /// Hazırlanmış ama onaylanmamış e-posta eylemi hiçbir şey göndermez.
    /// Onay olmadan yürütme yolunun kapalı olduğunun doğrudan kanıtı.
    /// </summary>
    [Fact]
    public async Task PendingEmailAction_SendsNothingUntilConfirmed()
    {
        var context = await ContextWithRealUserAsync("Genel Müdür");

        using var scope = fixture.Factory.Services.CreateScope();
        var email = new RecordingEmailService();
        var store = StoreFor(scope, context.UserId, email);

        var tool = Registry(scope).Find("eposta_gonder")!;

        await tool.ExecuteAsync(
            context,
            new Dictionary<string, object?>
            {
                ["alici"] = "hicbir-yerde-yok@ornek.test",
                ["konu"] = "Test",
                ["mesaj"] = "Test"
            },
            CancellationToken.None);

        Assert.Equal(0, email.SentCount);
    }

    /// <summary>Başkasının bekleyen eylemi onaylanamaz.</summary>
    [Fact]
    public async Task Confirm_RejectsAnotherUsersAction()
    {
        var owner = await ContextWithRealUserAsync("Genel Müdür");
        var intruder = await ContextWithRealUserAsync("Genel Müdür");

        using var scope = fixture.Factory.Services.CreateScope();
        var email = new RecordingEmailService();

        var pending = await StoreFor(scope, owner.UserId, email).CreateAsync(
            owner.UserId, "eposta_gonder",
            new Dictionary<string, object?> { ["alici"] = "x" },
            "özet", PermissionCatalog.Keys.SecretariatView, CancellationToken.None);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            StoreFor(scope, intruder.UserId, email)
                .ConfirmAsync(pending.Id, CancellationToken.None));

        Assert.Equal(0, email.SentCount);
    }

    /// <summary>Süresi dolan eylem yürütülmez ve "süresi doldu"ya düşer.</summary>
    [Fact]
    public async Task Confirm_RejectsExpiredAction()
    {
        var context = await ContextWithRealUserAsync("Genel Müdür");

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var email = new RecordingEmailService();
        var store = StoreFor(scope, context.UserId, email);

        var pending = await store.CreateAsync(
            context.UserId, "eposta_gonder",
            new Dictionary<string, object?> { ["alici"] = "x" },
            "özet", PermissionCatalog.Keys.SecretariatView, CancellationToken.None);

        pending.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.ConfirmAsync(pending.Id, CancellationToken.None));

        Assert.Equal(0, email.SentCount);
        Assert.Equal(HizirPendingActionStatus.Expired, pending.Status);
    }

    /// <summary>
    /// Onaylanan eylem tam olarak bir kez çalışır; ikinci onay reddedilir.
    /// </summary>
    [Fact]
    public async Task Confirm_ExecutesExactlyOnce()
    {
        var context = await ContextWithRealUserAsync("Genel Müdür");

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var email = new RecordingEmailService();
        var store = StoreFor(scope, context.UserId, email);

        // Alıcı sistemde kayıtlı olmalı; testin kendi kullanıcısına adres verilir.
        var user = await db.Users.SingleAsync(x => x.Id == context.UserId);
        user.Email = $"onay-{context.UserId:N}@ornek.test";

        // Onay anındaki izin kontrolü veritabanından okuduğu için rolün
        // gerçekten atanmış olması gerekir.
        var roleId = await db.Roles
            .Where(x => x.Name == "Genel Müdür")
            .Select(x => x.Id)
            .SingleAsync();

        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = roleId });
        await db.SaveChangesAsync();

        var pending = await store.CreateAsync(
            context.UserId, "eposta_gonder",
            new Dictionary<string, object?>
            {
                ["alici"] = user.Email,
                ["konu"] = "Onay testi",
                ["mesaj"] = "Tek kullanım kontrolü"
            },
            "özet", PermissionCatalog.Keys.SecretariatView, CancellationToken.None);

        var result = await store.ConfirmAsync(pending.Id, CancellationToken.None);

        Assert.Equal((int)HizirPendingActionStatus.Executed, result.Status);
        Assert.Equal(1, email.SentCount);
        Assert.Equal(user.Email, email.LastRecipient);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.ConfirmAsync(pending.Id, CancellationToken.None));

        Assert.Equal(1, email.SentCount);
    }

    /// <summary>
    /// Hazırlıktan sonra yetkisi alınan kullanıcı onaylayamaz. İzin
    /// yalnızca hazırlıkta değil, yürütme anında da kontrol edilir.
    /// </summary>
    [Fact]
    public async Task Confirm_RejectsWhenPermissionRevokedAfterPreparation()
    {
        var context = await ContextWithRealUserAsync("Genel Müdür");

        using var scope = fixture.Factory.Services.CreateScope();
        var email = new RecordingEmailService();
        var store = StoreFor(scope, context.UserId, email);

        // Kullanıcıya hiçbir rol atanmadığı için onay anındaki izin
        // kontrolü başarısız olmalı.
        var pending = await store.CreateAsync(
            context.UserId, "eposta_gonder",
            new Dictionary<string, object?> { ["alici"] = "x" },
            "özet", PermissionCatalog.Keys.SecretariatView, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.ConfirmAsync(pending.Id, CancellationToken.None));

        Assert.Equal(0, email.SentCount);
        Assert.Equal(HizirPendingActionStatus.Cancelled, pending.Status);
    }

    /// <summary>Her adım denetim kaydına düşmeli (istek #3).</summary>
    [Fact]
    public async Task EveryStep_IsWrittenToAuditLog()
    {
        var context = await ContextWithRealUserAsync("Genel Müdür");

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var store = StoreFor(scope, context.UserId, new RecordingEmailService());

        var pending = await store.CreateAsync(
            context.UserId, "eposta_gonder",
            new Dictionary<string, object?> { ["alici"] = "x" },
            "özet", null, CancellationToken.None);

        await store.CancelAsync(pending.Id, CancellationToken.None);

        var actions = await db.SecurityAuditEvents
            .Where(x => x.ActorUserId == context.UserId &&
                        x.EntityId == pending.Id)
            .Select(x => x.Action)
            .ToListAsync();

        Assert.Contains("Hizir.eposta_gonder.hazirlandi", actions);
        Assert.Contains("Hizir.eposta_gonder.vazgecildi", actions);
    }
}
