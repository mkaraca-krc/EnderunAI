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

/** Saat dilimine göre selamlama. */
export function timeGreeting(now: Date = new Date()): string {
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
  now: Date = new Date()
): string {
  const address = addressFor(user);
  const greeting = timeGreeting(now);

  return address ? `${greeting} ${address}` : greeting;
}
