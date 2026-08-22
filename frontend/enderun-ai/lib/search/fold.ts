/**
 * Türkçe arama katlaması — arayüzdeki TEK kaynağı.
 *
 * "hakedis" yazan kullanıcı "Hakediş" bulmalı; "sube" yazan "Şube".
 * Kullanıcı arama kutusuna Türkçe karakter yazmak için klavye
 * değiştirmez.
 *
 * toLocaleLowerCase("tr") KULLANILMIYOR: Türkçe kipte "I" harfi
 * noktasız "ı"ya dönüyor ve "SCHNEIDER" → "schneıder" oluyor; marka
 * adları aranamaz hale gelirdi. Küçültme yerel bağımsız yapılır,
 * ardından Türkçe harfler ASCII karşılığına katlanır.
 *
 * NEDEN AYRI DOSYA: aynı kural hem komut paletinde hem ekranlardaki
 * arama kutularında geçerli. İki ayrı yerde yazılsaydı biri
 * güncellenip diğeri unutulduğunda aynı metin bir ekranda bulunur,
 * ötekinde bulunamazdı.
 */
const FOLD: Record<string, string> = {
  ı: "i",
  // "İ" (U+0130): toLowerCase() bunu "i" + BİRLEŞİK NOKTA (U+0307)
  // yapıyor ve nokta katlanmadan kalıyordu. Ölçüldü: "insaat" yazan
  // "İnşaat"ı BULAMIYORDU — bu sektörde neredeyse her cari unvanında
  // geçen kelime. Nokta aşağıda ayrıca siliniyor.
  "\u0130": "i",
  ş: "s",
  ğ: "g",
  ü: "u",
  ö: "o",
  ç: "c",
  â: "a",
  î: "i",
  û: "u",
};

export function foldTurkish(text: string) {
  return (
    text
      .toLowerCase()
      // BİRLEŞİK NOKTA ÖNCE SİLİNİYOR. "İ".toLowerCase() iki kod
      // noktası üretiyor ("i" + U+0307) ve aşağıdaki karakter sınıfı
      // ikisini AYRI AYRI görüyor — hiçbiri eşleşmediği için nokta
      // metinde kalıyor ve "istanbul" ile "i̇stanbul" eşleşmiyordu.
      //
      // Veritabanı tarafı (lower + translate) noktayı hiç üretmiyor;
      // yani silinmezse ekran ile sunucu FARKLI sonuç verir.
      .replace(/\u0307/g, "")
      .replace(/[\u0130ışğüöçâîû]/g, (character) => FOLD[character] ?? character)
  );
}

/**
 * Aranan metin, verilen alanlardan HERHANGİ birinde geçiyor mu.
 *
 * Boş sorgu her kaydı geçirir: arama kutusu boşken listenin boşalması,
 * kullanıcıya "kayıt yok" der.
 */
export function matchesSearch(
  query: string,
  ...fields: (string | null | undefined)[]
): boolean {
  const needle = foldTurkish(query.trim());

  if (!needle) return true;

  return fields.some(
    (field) => field && foldTurkish(field).includes(needle),
  );
}
