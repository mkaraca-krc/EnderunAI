
/// <summary>
/// Kendi parolasını değiştirme isteği.
///
/// YENİ PAROLA İKİ KEZ ALINIYOR: yazım hatası, kullanıcıyı kendi
/// hesabından kilitleyebilecek tek hata türü. Sunucuda da kontrol
/// ediliyor — tarayıcıdaki kontrol bir kolaylık, garanti değil.
/// </summary>
public sealed record ChangePasswordRequest(
    string? CurrentPassword,
    string? NewPassword,
    string? NewPasswordConfirm);
