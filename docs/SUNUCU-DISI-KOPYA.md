# SUNUCU DIŞI YEDEK KOPYA — İKİ SEÇENEK (KARAR BEKLİYOR)

**Durum: HAZIRLIK. Uygulanmadı, uygulanmayacak — karar Mehmet Bey'de.**

## SORUN TEK CÜMLE

`/var/backups/enderun` ve `/var/lib/postgresql` **aynı diskte**
(`/dev/vda1`). Şifreleme anahtarı `/etc/enderunai/backup-key` de aynı
diskte. **Sunucuyu kaybedersek yedeği de kaybederiz.** Paket C'nin
yazdığı kurtarma yordamının yükleyeceği dosya olmaz.

Kod tarafı 2026-09-13'te kapatıldı (22 commit GitHub'a itildi); **veri
tarafı hâlâ açık.**

## ÖLÇÜLEN BOYUTLAR (2026-09-13)

| tür | adet | toplam | tek dosya |
|---|---|---|---|
| veritabanı dökümü | 502 | 2,1 GB | 4,8 MB |
| **uploads** | 457 | **18 GB** | **41 MB** |
| proje dosyaları | 456 | 99 MB | 312 KB |
| **toplam** | | **20 GB** | |

Günlük artış: **sessiz günde 47,5 MB** (yalnız gece yedeği),
**yayın günlerinde 600 MB'a kadar** (her yayın bir tur daha alıyor).
30 günlük saklamada kararlı boyut: **~1,4 GB** (sessiz) – **~7,7 GB**
(yoğun). 2026-09-25'teki toplu temizlikten sonra mevcut 20 GB → ~6 GB.

**MALİYETİN HÂKİM KALEMİ `uploads`: %90.** Her yedekte 41 MB'lık dosya
kümesi BAŞTAN kopyalanıyor, oysa içerik nadiren değişiyor. Artımlı ya
da içerik-adresli bir taşıma bu kalemi ~10 kat düşürür. **Taşıma
seçilirken asıl kazanç burada** — sağlayıcı seçiminde değil.

---

## SEÇENEK A — NESNE DEPOLAMA (S3 uyumlu) + OBJECT LOCK

Betik **zaten yazılmış**: `scripts/enderun-yedek-uzak.py`
(`PutObject` + `ListBucket`, **`DeleteObject` YOK**).
`/etc/enderunai/backup-remote.env` içinde `UZAK_YEDEK_ETKIN=hayir`.

- **Saklama yeri:** sağlayıcının nesne kovası. KVKK için **AB ya da
  Türkiye bölgesi** seçilmeli; ABD bölgesi yurt dışı aktarımı büyütür.
- **Neden Object Lock:** sunucu ele geçirilse bile yüklenmiş nesne
  SİLİNEMEZ. Fidye yazılımının ilk işi yedekleri silmektir; kimliğin
  silme yetkisi yoksa silemez.
- **Maliyet (teyit edilecek, liste fiyatı):** ~6 GB kararlı boyutta
  aylık birkaç dolar mertebesi; geri getirme (egress) ücreti ayrı ve
  sağlayıcıya göre çok değişiyor. **Bu sayılar ölçülmedi, sağlayıcıdan
  teyit edilecek.**
- **Artı:** betik hazır; silme yetkisiz kimlik mümkün; sunucudan tamamen
  bağımsız.
- **Eksi:** yurt dışı aktarım değerlendirmesi gerekiyor; egress ücreti
  felaket anında sürpriz olabilir; sağlayıcı hesabı da bir tek nokta.

## SEÇENEK B — İKİNCİ MAKİNE / DEPOLAMA KUTUSU, ÇEKME KİPİYLE

Ayrı bir sunucu (ya da depolama kutusu) yedekleri **kendisi çeker**;
canlı sunucunun oraya yazma yetkisi YOKTUR.

- **Saklama yeri:** ikinci makinenin diski. Fiziksel olarak ayrı veri
  merkezi seçilmeli — aynı makine odası "sunucu dışı" değildir.
- **Neden çekme kipi:** canlı sunucu ele geçirilirse saldırganın
  kopyaya erişimi olmaz; kimlik doğrulama tek yönlüdür (uzak taraf
  canlıya salt-okuma SSH anahtarıyla bağlanır).
- **Maliyet:** sabit aylık kira (kapasiteye göre), egress ücreti yok.
  1 TB'lık bir depolama kutusu bugünkü 6-20 GB için fazlasıyla yeter.
- **Artı:** öngörülebilir sabit maliyet; egress sürprizi yok; sağlayıcı
  Türkiye/AB seçilerek KVKK yükü küçültülebilir.
- **Eksi:** ikinci makinenin kendisi de bakım ister (güncelleme, disk,
  izleme); Object Lock benzeri "silinemezlik" garantisi yoktur —
  sadece yetki ayrımı vardır; kurulum işi A'dan fazla.

---

## ŞİFRE ÇÖZME ANAHTARI — BELGENİN EN ÖNEMLİ BÖLÜMÜ

Yedek dosyaları **zaten `gpg --symmetric AES256` ile şifreli**; sunucu
dışına taşınırken düz veri çıkmaz. Bu iyi haber tek başına yetmez:

> **ANAHTAR YEDEKLE AYNI YERDE DURURSA KOPYA İŞE YARAMAZ.**
>
> · Anahtar yalnız `/etc/enderunai/backup-key`de duruyorsa ve sunucu
>   giderse: uzaktaki kopya **açılamaz**. Elinizde okunamayan 6 GB olur.
> · Anahtar uzaktaki kovaya yedeğin YANINA konursa: şifreleme anlamsız
>   olur — kovayı ele geçiren hem dosyayı hem anahtarı alır.
>
> Anahtar, yedeğin bulunduğu yerden **ayrı bir yerde** ve **en az iki
> nüsha** durmalıdır.

Bugünkü şifreleme "diski çalan okuyamasın" korumasını **vermiyor**
(anahtar aynı diskte); yalnız yanlışlıkla kopyalanan tek dosyayı
koruyor. Bu daha önce de kayıtlıydı, taşıma kararıyla birlikte
çözülmesi gereken şey budur.

**Anahtar nerede durmalı — üç yol:**

| yol | nasıl | riski |
|---|---|---|
| **K1 · Mehmet Bey'de çevrimdışı** | parola yöneticisi + basılı kapalı zarf, kasada | tek kişiye bağımlı; o kişi ulaşılamazsa kurtarma durur |
| **K2 · İki emanetçi** | anahtar iki kişide ayrı ayrı; kurtarma için biri yeter | kişi sayısı arttıkça sızma yüzeyi büyür |
| **K3 · Sağlayıcı anahtar kasası (KMS)** | anahtar bulut kasasında, erişim ayrı hesapla | sağlayıcı hem veriyi hem anahtarı tutarsa ayrım kaybolur — **depolama ile KMS aynı sağlayıcıda olmamalı** |

**Öneri:** K1 + K2 birlikte (Mehmet Bey + bir emanetçi), K3 yalnız
depolamadan FARKLI bir sağlayıcıda düşünülsün.

**Ayrıca sınanmalı:** anahtarın nüshasıyla, sunucuya hiç dokunmadan bir
yedeğin açılabildiği **yılda en az bir kez** gösterilmeli. Denenmemiş
anahtar, denenmemiş yedektir.

---

## KVKK — HUKUK KARARI, BENDE DEĞİL

Veri şifreli çıksa da KVKK bakımından **yurt dışında saklamak
aktarımdır**. Bölge seçimi (Türkiye / AB / diğer) ve gerekli
belgelendirme hukuk tarafının kararı. Bu belge teknik seçenekleri ve
maliyeti koyar; aktarım kararını koymaz.

## KARAR İÇİN GEREKENLER

1. Seçenek A mı B mi (ya da ikisi: A uzun saklama, B sıcak kopya)
2. Sağlayıcı ve **bölge**
3. Anahtar emanet yolu (K1/K2/K3)
4. `uploads` için artımlı taşıma yapılsın mı (maliyetin %90'ı orada)
5. Yıllık anahtar tatbikatının takvimi
