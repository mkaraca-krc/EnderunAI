# HAKEDİŞ KESİNTİ KARARLARI

**Kaynak: mali müşavir Ali Koyuncu, 17.09.2026**

> **BU DOSYA KANONİK KOPYADIR.** Aynı kararların bir kopyası claude.ai
> proje tarafında da duruyor; ayrışırlarsa **bu dosya geçerlidir** ve
> öteki buna göre güncellenir (Mehmet Bey, 17.09.2026). İki kopya varken
> hangisinin doğru olduğunu bilmemek, yanlış kopyadan kötüdür.

---

## EKLENECEK TÜRLER

Üçünde de **oran ve hesap KOD SABİTİ DEĞİL** — sözleşme/proje bazlı
kural satırından gelir.

### Stopaj (gelir vergisi kesintisi)

| alan | değer |
|---|---|
| oran | **%5** |
| hesap (kısa vade) | **193** Peşin Ödenen Vergiler ve Fonlar |
| hesap (uzun vade) | **AÇIK SORU** — aşağıya bakın |
| sözleşme bazlı bayrak | **var, varsayılanı AÇIK** |

Bayrak neden kapatılabilir: *"İşlerimiz yıllara sari"* genel bir beyan;
**tek yılda biten bir işte uygulanmaz.**

### Damga vergisi

| alan | değer |
|---|---|
| oran | sözleşmeye göre değişken — **VARSAYILAN ORAN YOK** |
| hesap | **193** |

### Gecikme cezası (sözleşme)

| alan | değer |
|---|---|
| oran | sözleşmeye göre değişken — **VARSAYILAN ORAN YOK** |
| hesap | **689** |
| ayrım | `OhsPenalty`den **AYRI TÜR** — farklı hesap, farklı itiraz yolu |

---

## MEVCUT TÜR — TEYİT EDİLDİ

### Teminat kesintisi

| alan | değer |
|---|---|
| hesap | **226** Verilen Depozito ve Teminatlar *(müşavir onayladı)* |
| iade | **KESİN KABULDE** |

---

## EKLENMEYECEK

### KDV tevkifatı

| konu | karar |
|---|---|
| oran | 4/10 |
| hesap | **391** tevkifatlı satış KDV |
| hakediş kesinti satırı | **YAZILMAZ** |

Gerekçe: bu **fatura/KDV katmanıdır**, ödemenin net tutarını düşüren bir
kesinti değil. Kesinti tablosuna satır yazmak **çifte düşüm** olur.
Hakediş ekranında **bilgi amaçlı gösterilebilir.**

---

## AÇIK SORULAR (müşavire gitti, cevap bekleniyor)

### Soru 1 — uzun vade hesabı: 293 mü 295 mi?

Karar metni "293" diyor. **Ölçüm bununla çelişiyor**; canlı hesap planı:

```
193  PEŞİN ÖDENEN VERGİLER VE FONLAR
293  GELECEK YILLAR İHTİYACI STOKLAR      ← STOK hesabı
295  PEŞİN ÖDENEN VERGİLER VE FONLAR      ← 193'ün uzun vadeli karşılığı
```

`295` canlıda **var**. 293'e stopaj yazmak peşin ödenen vergiyi stok
hesabına kaydetmek olurdu.

### Soru 2 — ayrım "yıllara sari mi" değil, "mahsup ne zaman"

Mehmet Bey'in düzeltmesi (17.09.2026): 193/295 ayrımı vade sorusu
DEĞİL, **mahsup zamanı** sorusudur. **Bakiye zamanla uzun vadeliden kısa
vadeliye göç eder** — yani hesap fiş anında sabitlenirse bir yıl sonra
**yanlış sınıfta kalır.**

Müşavirin iki seçeneği:

| seçenek | ne demek |
|---|---|
| **(a)** | hep **193**'e yazılır, **dönem sonunda** müşavir sınıflandırır |
| **(b)** | baştan vadeye göre ayrılır **ve yeniden sınıflandırma adımı kurulur** |

**CEVAP GELMEDEN YENİDEN SINIFLANDIRMA KODU YAZILMAYACAK.**

---

## UYGULAMA DURUMU (17.09.2026)

| iş | durum |
|---|---|
| kısa vade (193) ayağı | **kodlanabilir** — tartışmasız |
| uzun vade ayağı | **BEKLEMEDE** — Soru 1 ve 2 |
| yeniden sınıflandırma | **BEKLEMEDE** — Soru 2 |
| vade belirsizse | **FAIL-CLOSED** — fiş üretilmez (testle çivilenir) |
| KDV tevkifatı | eklenmeyecek |

---

## KAPANMIŞ MADDE — "Diğer %0,3"

Varsayılan oran **kaldırıldı** (17.09.2026). Gerekçesi dört yerde arandı
(satır yorumu · arka uç · belgeler · doğuran commit `7119732e`) ve
hiçbirinde bulunamadı; müşavir de tanımadı. Enum XML belgesi diğer
türlerin oranını yazarken `Other` için oran yazmıyor. Muhafız:
`tests/kesinti-varsayilan-oran.test.ts`.

**Açık kalan benzer bulgu: Barter %40.** Enum belgesi "şantiye bazında
değişken oranlı" diyor, ekran %40'ı sabitliyor — belge ile davranış
çelişiyor. Karar bekliyor.
