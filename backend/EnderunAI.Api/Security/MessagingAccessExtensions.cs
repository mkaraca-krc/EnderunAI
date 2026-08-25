using EnderunAI.Api.Models.Messaging;

namespace EnderunAI.Api.Security;

/// <summary>
/// MESAJLAŞMA ERİŞİM KAPISI — ÜYELİK, KAPSAM DEĞİL.
///
/// Sistemdeki diğer her yerde erişim sorusu "bu kayıt senin
/// kapsamında mı" idi ve `ApplyScope` onu cevaplıyordu. Mesajlaşmada
/// soru başka: **"bu konuşmanın tarafı mısın"**.
///
/// İKİSİ AYRI KAPIDIR VE İKİSİ DE GEREKİR:
///   - Kapsam, yanlış şirketin verisini engeller.
///   - Üyelik, doğru şirketteki BAŞKASININ konuşmasını engeller.
///
/// GLOBAL VERİ KAPSAMI BU KAPIYI AÇMAZ. `ApplyScope` global erişimli
/// kullanıcı için sorguyu olduğu gibi geçiriyor — mesajlaşmada aynı
/// şeyi yapsaydık Admin ve Genel Müdür herkesin özel konuşmasını
/// okurdu. Karar açık: **kimse başkasının konuşmasını okuyamaz, GM
/// dahil.** Bu yüzden burada `HasGlobalAccess` kısayolu YOK ve
/// olmamalı; sonda testi bunu koruyor.
///
/// AYRILMIŞ ÜYE OKUYAMAZ: `LeftAtUtc` dolu olan satır erişim
/// vermiyor. Üyelik satırı silinmiyor çünkü "o tarihte kim
/// görüyordu" sorusunun tek cevabı o satır.
/// </summary>
public static class MessagingAccessExtensions
{
    /// <summary>
    /// Kullanıcının ÜYE OLDUĞU konuşmalar. Kapsam süzgeci AYRICA
    /// uygulanır — bu metot onun yerine geçmez.
    /// </summary>
    public static IQueryable<Conversation> ApplyMembership(
        this IQueryable<Conversation> query, Guid userId) =>
        query.Where(x => x.Members.Any(m =>
            m.UserId == userId && m.LeftAtUtc == null));

    /// <summary>
    /// Kullanıcının okuyabileceği mesajlar: yalnız üyesi olduğu
    /// konuşmalardan.
    /// </summary>
    public static IQueryable<Message> ApplyMembership(
        this IQueryable<Message> query, Guid userId) =>
        query.Where(x => x.Conversation.Members.Any(m =>
            m.UserId == userId && m.LeftAtUtc == null));
}
