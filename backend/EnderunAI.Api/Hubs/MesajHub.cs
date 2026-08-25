using EnderunAI.Api.Security.CurrentUser;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace EnderunAI.Api.Hubs;

/// <summary>
/// KURUMSAL MESAJLAŞMA HUB'I — M3/0 İSKELETİ.
///
/// Bu turda hub yalnız BAĞLANIYOR. Mesaj gönderme, kanal, okundu
/// bilgisi M3/1 ve sonrasında. İskeletin kendi başına deploy
/// edilmesinin sebebi: gerçek zamanlı altyapının canlıda
/// çalıştığını, üstüne veri modeli koymadan ÖNCE görmek.
///
/// KİMLİK ÇEREZDEN GELİYOR, SORGU DİZESİNDEN DEĞİL.
/// Tarayıcı WebSocket el sıkışmasında özel başlık gönderemez ama
/// çerezleri kendiliğinden gönderir. `access_token` sorgu
/// parametresi (SignalR'ın yaygın yolu) BİLEREK kullanılmadı: token
/// URL'e girerse erişim kaydına, tarayıcı geçmişine ve proxy
/// kayıtlarına düşer — portal token'ında yaşadığımız sızıntının
/// aynısı. nginx tarafında `/api/hubs/` için `access_log off` da bu
/// yüzden var.
///
/// KULLANICI BAŞINA GRUP: bağlanan her kullanıcı kendi kimliğiyle
/// adlandırılmış bir gruba giriyor. Aynı kişinin iki cihazı iki
/// bağlantı demek; bildirimi kişiye göndermek isteyen kod
/// bağlantıları tek tek aramak zorunda kalmasın.
/// </summary>
[Authorize]
public sealed class MesajHub(
    ICurrentUserService currentUser,
    ILogger<MesajHub> logger) : Hub
{
    /// <summary>Kişiye yayın yapmak isteyen kodun kullanacağı grup adı.</summary>
    public static string KullaniciGrubu(Guid userId) => $"kullanici:{userId}";

    public override async Task OnConnectedAsync()
    {
        var userId = currentUser.UserId;

        if (userId is null)
        {
            /*
             * KİMLİKSİZ BAĞLANTI KABUL EDİLMEZ.
             *
             * `[Authorize]` bunu zaten engelliyor; bu ikinci kapı,
             * bir gün kimlik çözümü değişirse bağlantının SESSİZCE
             * kimliksiz kurulmasını önlüyor.
             */
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, KullaniciGrubu(userId.Value));

        // KİŞİSEL VERİ YOK: kullanıcı KİMLİĞİ yazılıyor, adı değil.
        logger.LogInformation(
            "Hub bağlantısı açıldı. kullanici={UserId} baglanti={ConnectionId}",
            userId, Context.ConnectionId);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = currentUser.UserId;

        if (userId is not null)
        {
            await Groups.RemoveFromGroupAsync(
                Context.ConnectionId, KullaniciGrubu(userId.Value));
        }

        logger.LogInformation(
            "Hub bağlantısı kapandı. kullanici={UserId} baglanti={ConnectionId}",
            userId, Context.ConnectionId);

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// İskelet turunda tek çağrı: bağlantının GERÇEKTEN kurulduğunu
    /// ve kimliğin taşındığını istemcinin doğrulayabilmesi için.
    /// Kişisel veri döndürmüyor — yalnız kimlik.
    /// </summary>
    public Task<object> Merhaba() =>
        Task.FromResult<object>(new
        {
            baglandi = true,
            kullaniciId = currentUser.UserId
        });
}
