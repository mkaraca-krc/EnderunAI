# Mehmet Bey'e: 150 ve 153 hesaplarında "Proje Zorunlu" kutucuğunu kaldırma

**Süre:** iki hesap, her biri ~30 saniye.
**Neden:** 150 ve 153 bilanço (stok) hesapları. Bugün "Proje Zorunlu"
işaretli oldukları için **mal kabul, projesiz depo çıkışı, projesiz sayım
ve depodan zimmet** dört yolu da fiş kesemiyor ve hata veriyor. Bu dört
yol prova zemininde düzeltmeyle birlikte çalıştı (ölçüldü).

**Bunu neden siz yapıyorsunuz:** canlıdaki tek yönetici kimliği sizin
hesabınız. Ajan sizin kimliğinizle girse, yapmadığınız bir işlemin faili
olarak siz kaydedilirdiniz. Kaydın doğru olması için işlemi sizin
yapmanız gerekiyor.

---

## Adım adım

### 1. Ekranı aç
Sol menüde **MUHASEBE** grubunu aç → **"Hesap Planı"**.
(Doğrudan adres: `/muhasebe/hesap-plani`)

### 2. 150'yi bul
Sayfanın üstündeki **arama kutusuna `150` yaz.**
Listede **`150 — İlk Madde ve Malzeme`** satırını göreceksiniz. Üstüne
tıklayın; hesabın kendi sayfası açılır (başlıkta `150 - İlk Madde ve
Malzeme` yazar).

### 3. Kutucuğu bul
Formda aşağı inin. Alanların sırası:
Durum · Hesap Kodu (gri, değiştirilemez) · Hesap Adı · Hesap Karakteri ·
sonra **çerçeveli üç kutucuk kartı** gelir. Her kart **solda bir onay
kutusu**, sağında **kalın başlık** ve altında küçük açıklama taşır:

```
┌──────────────────────────────────────────────────────────┐
│ ☑  Kayıt Yapılabilir                                      │
│    Muhasebe fişlerinde bu hesaba kayıt girilebilir.       │
├──────────────────────────────────────────────────────────┤
│ ☑  Proje Zorunlu              ← BU KARTTAKİ KUTUYU AÇIN   │
│    Fiş satırında proje seçimi zorunlu olur.               │
├──────────────────────────────────────────────────────────┤
│ ☐  Masraf Merkezi Zorunlu     ← DOKUNMAYIN                │
│    Fiş satırında masraf merkezi seçilmelidir.             │
└──────────────────────────────────────────────────────────┘
```

### 4. Yap
**"Proje Zorunlu"** kartındaki onay kutusunun **işaretini kaldırın**
(tıklayınca ☑ → ☐ olur).

**"Masraf Merkezi Zorunlu"** ve **"Kayıt Yapılabilir"** kartlarına
**dokunmayın.**

### 5. Kaydet
Sayfanın altındaki **"Değişiklikleri Kaydet"** düğmesine basın.

### 6. Aynısını 153 için tekrarlayın
Hesap Planı'na dönün, aramaya `153` yazın,
**`153 — Ticari Mallar`** satırını açın, aynı kutucuğu kaldırın, kaydedin.

---

## Beklenen ve beklenmeyen

| ne görürseniz | ne demek | ne yapın |
|---|---|---|
| "Güncellendi" mesajı | tamam | ikinciye geçin |
| **"Proje Zorunlu" kutusu zaten boşsa** | biri daha önce yapmış | dokunmayın, bana yazın |
| **Hesap Adı'nı yanlışlıkla değiştirdiyseniz** | kaydetmeden sayfadan çıkın | baştan başlayın |
| Kaydetmede hata mesajı | sayfa eskimiş olabilir | sayfayı yenileyip tekrar deneyin |

**Başka hiçbir hesaba dokunmayın.** Özellikle `320`, `120` ve `159`
hesapları mali müşavir cevabını bekliyor.

---

## Bittiğinde bana haber verin

Ben şunları ölçeceğim:
1. Canlıda `150` ve `153` gerçekten `Proje Zorunlu = hayır` mı,
2. Beyan sapma sayacı **2 → 0** düştü mü,
3. Dört yolun canlıda çalıştığı (mal kabul için hangi kalem/depo/tutarla
   deneyeceğimi önceden yazacağım, deneyip **geri alacağım** ve geri
   aldığımı ayrıca ölçeceğim),
4. Sonucu tarih/saat/aktör ile `docs/CANLI-1.md`'ye yazacağım.
