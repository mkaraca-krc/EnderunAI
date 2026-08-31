/**
 * Saate ve kullanıcıya göre kişisel karşılama.
 *
 * Hitap (Bey/Hanım) kullanıcının kendi seçtiği alandan gelir. Sistemde
 * cinsiyet bilgisi tutulmuyor ve isimden tahmin edilmiyor: yanlış tahmin
 * kullanıcıyı yanlış hitapla karşılar. Hitap seçilmemişse nötr ama nazik
 * "Sayın" biçimi kullanılır.
 */

export type GreetingUser = {
  fullName?: string | null;
  username?: string | null;
  /** "Bey" | "Hanım" | null — kullanıcı kaydından gelir. */
  honorific?: string | null;
};

/*
 * SAAT DIŞARIDAN GELİR — VARSAYILAN ÜRETİLMEZ.
 *
 * Önce imzası `timeGreeting(now: Date = new Date())` idi. Varsayılan
 * ÇAĞRI ANINDA değerleniyor; pano onu çizim gövdesinde çağırıyordu
 * (`app/dashboard/page.tsx`). Sunucu geçişi derleme anında koştuğu
 * için HTML'e "Günaydın" donuyor, öğleden sonra açan istemci
 * "İyi günler" çiziyor ve her yüklemede hidrasyon uyuşmazlığı
 * doğuyordu (React #418) — canlıda ölçüldü.
 *
 * `null` geçilirse selamlama BOŞ döner: sunucu ve hidrasyon geçişi
 * aynı şeyi çizer, uyuşmazlık imkânsız hâle gelir. Gerçek selamlama
 * bağlanma sonrası gelir.
 */
export function timeGreeting(now: Date | null): string {
  if (!now) return "";

  const hour = now.getHours();

  if (hour >= 5 && hour < 12) return "Günaydın";
  if (hour >= 12 && hour < 18) return "İyi günler";
  if (hour >= 18 && hour < 23) return "İyi akşamlar";
  return "İyi geceler";
}

/** Ad Soyad'dan yalnızca adı alır. */
export function firstName(fullName?: string | null): string | null {
  const trimmed = fullName?.trim();
  if (!trimmed) return null;

  const first = trimmed.split(/\s+/)[0];
  return first || null;
}

/**
 * Kullanıcıya nasıl hitap edileceği:
 * - Hitap seçilmişse: "Ahmet Bey"
 * - Seçilmemişse ve ad soyad varsa: "Sayın Ahmet Yılmaz"
 * - Yalnızca ad varsa: "Ahmet"
 * - Hiçbiri yoksa kullanıcı adı, o da yoksa boş.
 */
export function addressFor(user: GreetingUser | null | undefined): string {
  if (!user) return "";

  const full = user.fullName?.trim();
  const honorific = user.honorific?.trim();
  const first = firstName(full);

  if (first && honorific) return `${first} ${honorific}`;
  if (full && full.includes(" ")) return `Sayın ${full}`;
  if (first) return first;

  return user.username?.trim() ?? "";
}

/**
 * Tam karşılama cümlesi: "Günaydın Ahmet Bey".
 * Kullanıcı bilinmiyorsa yalnızca saat selamı döner.
 */
export function greetingFor(
  user: GreetingUser | null | undefined,
  // Saat çağırandan gelir; `null` ise selamlama boş kalır (yukarıdaki
  // gerekçe). Varsayılan üretmek uyuşmazlığı geri getirirdi.
  now: Date | null
): string {
  const address = addressFor(user);
  const greeting = timeGreeting(now);

  // Saat yoksa (sunucu geçişi) yalnız hitap döner; iki geçiş de aynı.
  if (!greeting) return address;

  return address ? `${greeting} ${address}` : greeting;
}
