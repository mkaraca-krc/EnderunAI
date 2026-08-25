# Yedek Depolama — Sağlayıcı Soru Listesi

**Enderun AI / Mehmet Karacabey**

Bir inşaat ERP sisteminin gece yedeklerini sunucu dışında saklamak istiyoruz.
Yedekler **bize ait bir anahtarla şifrelenmiş** olarak gönderilir; içeriği
sizin tarafınızda okunamaz.

**Hacim:** günde yaklaşık 45 MB, toplam 25–50 GB bandında.
Küçük bir hacim — fiyattan çok **aşağıdaki beş şart** belirleyici.

---

## 1. Silme yetkisi olmayan anahtar veya nesne kilidi

**Bu şart sağlanmıyorsa diğerlerine bakmıyoruz.**

Yedeğin sunucu dışında durmasının sebebi, sunucuya biri girdiğinde
yedeklerin de kaybolmasını önlemek. Sunucudaki anahtar yedekleri
silebiliyorsa, uzak kopya bu işi görmez.

İkisinden **biri** yeterli:

- **(a)** Yükleme anahtarı **yazabilsin ama silemesin** — yetkileri ayrı
  ayrı verilebiliyor mu?
- **(b)** **Nesne kilidi** (object lock / immutability / WORM): yüklenen
  dosya, belirlediğimiz süre boyunca **hiç kimse tarafından**
  silinemesin — hesabın sahibi dahil.

> **Sorumuz:** (a) mı, (b) mi, yoksa ikisi de var mı? Nesne kilidi varsa
> en uzun süre ne kadar?

## 2. S3 uyumlu erişim

Sistemimiz S3 protokolüyle konuşuyor. Araya çevirici koymak istemiyoruz.

> **Sorumuz:** S3 uyumlu bir adres (endpoint), erişim anahtarı ve gizli
> anahtar veriyor musunuz? Standart S3 araçları çalışır mı?

## 3. Sürümleme ve otomatik silme kuralı

Elle temizlik yapmak istemiyoruz; eski yedekler kendiliğinden düşsün.
Hedeflediğimiz saklama:

| Aralık | Kaç kopya |
|---|---|
| Günlük | 14 |
| Haftalık | 8 |
| Aylık | 12 |

> **Sorumuz:** Yaşam döngüsü (lifecycle) kuralı tanımlanabiliyor mu?
> Sürümleme (versioning) var mı?

## 4. Veri merkezi Türkiye'de — yazılı olarak

Verinin Türkiye'de kalması bizim için **tercih değil şart**. Yurt dışına
çıkması durumunda ayrı bir hukuki değerlendirme gerekiyor ve bundan
kaçınmak istiyoruz.

> **Sorumuz:** Verinin fiziksel olarak duracağı veri merkezi hangi
> şehirde? Bunu sözleşmede veya yazılı teklifte belirtebiliyor musunuz?
> Yedekleme/çoğaltma amacıyla dahi yurt dışına kopyalanıyor mu?

## 5. Anahtar yönetimi

> **Sorumuz:**
> - Birden fazla erişim anahtarı üretebiliyor muyuz? (Yükleme için ayrı,
>   geri okuma için ayrı)
> - Bir anahtar sızarsa **tek başına** iptal edilebiliyor mu — diğerleri
>   çalışmaya devam ederek?
> - Anahtarların hangi işlemi yaptığı görülebiliyor mu (erişim kaydı)?

---

## Fiyat

Yukarıdaki beş şart sağlanıyorsa:

- 50 GB ve 100 GB için aylık ücret
- İndirme (dışarı veri aktarımı) ücretli mi — felaket anında **tamamını
  bir kerede indireceğiz**, o çekiş ne kadar tutar?
- İstek/işlem başına ayrı ücret var mı?
- Asgari taahhüt süresi var mı?

---

**Not:** Kurulumu biz yapacağız, teknik destek paketi gerekmiyor.
İhtiyacımız olan tek şey S3 adresi, anahtarlar ve yukarıdaki şartların
yazılı teyidi.
