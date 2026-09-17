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
| hesap | **193** Peşin Ödenen Vergiler ve Fonlar — **HER ZAMAN** |
| 295'e yazılır mı | **HAYIR.** Sistem 295'e hiç yazmaz; uzun vadeli sınıflandırmayı müşavir **dönem sonunda** yapar |
| bayrak | **var, varsayılanı AÇIK**, kullanıcı kapatabilir |
| bayrak nasıl belirlenir | **AÇIKÇA SEÇİLİR — proje tarihlerinden TÜRETİLMEZ** |

Bayrak neden kapatılabilir: *"İşlerimiz yıllara sari"* genel bir beyan;
**tek yılda biten bir işte uygulanmaz.**

**Bayrak neden türetilmiyor** (Mehmet Bey, 17.09): *"Boş bitiş
tarihinden çıkarım yapmak sessiz yanlış sonuç üretir — kaçındığımız
kusurun aynısı."* Kural satırının `IsActive` alanı bu bayraktır.

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

## CEVAPLANMIŞ SORULAR

### Soru 1 — uzun vade hesabı: **295 doğru, 293 sürçmeymiş**

Ölçüm bunu zaten göstermişti; canlı hesap planı:

```
193  PEŞİN ÖDENEN VERGİLER VE FONLAR
293  GELECEK YILLAR İHTİYACI STOKLAR      ← STOK hesabı
295  PEŞİN ÖDENEN VERGİLER VE FONLAR      ← 193'ün uzun vadeli karşılığı
```

**Not:** 295 müşavirin **dönem sonunda** kullandığı hesaptır. **Koda
girmez** — sistem oraya hiç yazmaz.

### Soru 2 — mahsup ne zaman: **"Siz 193'e yazın, biz ayırırız"**

Ayrımı müşavir dönem sonunda yapıyor. Bakiyenin uzun vadeliden kısa
vadeliye göçü onun işi; sistemin fiş anında sınıflandırma yapmasına
gerek yok.

---

## DENENİP VAZGEÇİLEN TASARIM

Müşavir cevabı gelmeden önce **iki hesap alanlı** bir ara tasarım
düşünüldü: kural satırında `AccountingAccountId` +
`LongTermAccountingAccountId`, ve proje tarihlerinden türetilen bir
vade kuralı (bitiş yılı > başlangıç yılı), uzun vade hesabı
seçilmemişse fail-closed.

**Kod yazıldı ama kullanılmadan iptal edildi.** Müşavirin iki cevabı
onu gereksiz kıldı: sistem her zaman 193'e yazacaksa ne ikinci alana, ne
vade türetmeye, ne de o fail-closed'a gerek var.

Kod silinmedi: `deploy/park/KesintiVadesi.cs`, gerekçesiyle birlikte.
**Denenip vazgeçilen tasarım, kararın kendisi kadar bilgi taşır** —
biri ileride "vadeye göre ayıralım mı" diye sorarsa cevabı orada:
*sorulmuştu, müşavir gerek olmadığını söyledi.*

## UYGULAMA DURUMU (17.09.2026)

| iş | durum |
|---|---|
| stopaj → 193, her zaman | **kodlanabilir** |
| yıllara sari bayrağı | **açıkça seçilir** (`IsActive`), türetilmez |
| uzun vade ayağı | **YOK** — sistem 295'e yazmaz |
| yeniden sınıflandırma | **YOK** — müşavir dönem sonunda yapar |
| vade türetme / fail-closed | **İPTAL** — konusuz kaldı |
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
