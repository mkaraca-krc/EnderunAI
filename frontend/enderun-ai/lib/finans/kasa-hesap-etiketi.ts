import { CashAccountType, type CashAccount } from "@/services/cash-account.service";

/**
 * KASA/BANKA HESABI SEÇİM ETİKETİ — TEK KAYNAK.
 *
 * NEDEN VAR: canlıda bir çek yanlış bankadan ödenmiş göründü
 * (805088 — Garanti yaprağı, Fibabanka hesabı). Sebep kullanıcı
 * dikkatsizliği değil, EKRAN: şirketin altı banka hesabının `Name`
 * alanı BİREBİR AYNI ("Ankara Merkez TL Hesabı"). Açılır listede
 * altı özdeş satır görünüyordu.
 *
 * İki iptal edilmiş çekte ise Garanti yaprağı KASADAN ödenmiş
 * işaretlenmiş — yani karışıklık yalnız bankalar arasında değil,
 * kasa ile banka arasında da yaşandı. Bu yüzden etikette TÜR de var.
 *
 * TEK YERDE: üç ayrı ekranda üç ayrı biçim yazılsaydı biri
 * güncellenip diğerleri kalırdı — düzeltmeye çalıştığımız hatanın
 * aynısı (Kural 25).
 *
 * `Name` alanına DOKUNULMUYOR; bu yalnız görüntü. Hesap adlarını
 * Mehmet HP/1 ekranı gelince düzeltecek.
 */
export function kasaHesapEtiketi(
  hesap: Pick<CashAccount, "type" | "code" | "name" | "bankName">
): string {
  const tur = hesap.type === CashAccountType.Bank ? "Banka" : "Kasa";

  // Banka hesabında ayırt edici bilgi BANKA ADI, hesap adı değil.
  // Kasada banka adı yok; orada hesap adı ayırt edicidir.
  const ad =
    hesap.type === CashAccountType.Bank && hesap.bankName?.trim()
      ? hesap.bankName.trim()
      : hesap.name;

  return `${tur} · ${ad} — ${hesap.code}`;
}
