# CANLI/1 — pilota çıkma kapısı

> **Bu dosya kapının kendisidir.** Konuşmada tutulan kapı, kapı
> değildir (KY3, 2026-09-08). Her turun sonunda güncellenir ve
> rapordaki tablo buradan üretilir.

Her madde üç şey taşır: **bugünkü durum**, **yeşil sayılması için
gereken ölçüm**, ve o ölçümün **bugünkü sonucu**. Durum üç değerden
biri: `YEŞİL` (ölçüldü, geçti) · `KISMEN` (bir kısmı ölçüldü) ·
`AÇIK` (ölçülmedi ya da düştü).

Son güncelleme: 2026-09-08

| # | madde | durum |
|---|---|---|
| K1 | Yetki ısırıyor | KISMEN |
| K2 | Hassas ekranlar izne bağlı | KISMEN |
| K3 | Yayın güvenilir | KISMEN |
| K4 | Kırık ekran yok | YEŞİL |
| K5 | Telefondan kullanılabiliyor | KISMEN |
| K6 | Yedek kanıtlı | YEŞİL |
| K7 | Hesap tablosu | YEŞİL |
| K8 | İzleme (NÖBET/1) | YEŞİL |
| K9 | Testler canlıya dokunmuyor | YEŞİL |

---

## K1 — Yetki ısırıyor

**Yeşil için gereken ölçüm:** kısıtlanmış bir kullanıcının kısıtlı
ekranı GERÇEKTEN açamadığı, kod okuyarak değil ÇAĞIRARAK gösterilmeli
(Kural 70); ve bir rol kaldırması yeniden başlatmadan SAĞ ÇIKMALI.

**Bugünkü sonuç:** SEED/1(b) canlıda; rig'de iki ayak yeşil
(`/dashboard` ve `/muhasebe` kısıtlıyken açılmıyor). Kalıcılık ayağı
ölçüldü: kaldırma kaydı tablosu var, tohumlayıcı ona bakıyor.

**Neden KISMEN:** Mehmet kendi doğrulamasını yapmadı. Ayrıca
KATALOG/1 açık — katalogdan çıkarılan bir izin veritabanında kalıyor
(AC1). Fiilî açık kapı bugün yok ama onu kapatan şey tesadüf.

## K2 — Hassas ekranlar izne bağlı

**Yeşil için gereken ölçüm:** her ekranın izin bağlantısı, kontrolü
GERÇEKTEN çağırarak ölçülmeli; bağlantısız ekran sayısı sıfır olmalı.

**Bugünkü sonuç:** 188 ekran tarandı, 15'i bağsızdı; KARAR 2 ile 4 yeni
izin üretilip bağlandı. Muhasebe granülerliği (K2 alt maddesi) hâlâ
açık.

## K3 — Yayın güvenilir

**Yeşil için gereken ölçüm:** yayın boyunca kullanıcıya bozuk cevap
gitmemeli (kesinti kapısı), ve göç zinciri canlı şemayı birebir
üretmeli.

**Bugünkü sonuç:** kesinti kapısı kalıcı ve son üç yayında 0 hata;
atomik takas hem ön yüzde hem arka uçta. Göç zinciri canlı şemayı
birebir üretiyor (5801=5801, 0/0).

**Neden KISMEN:** SQUASH/1 uygulanmadı; yeniden başlatmanın ~2 sn'lik
boşluğu kabul edilmiş risk olarak duruyor (S5 uyarısı var, engel yok).

## K4 — Kırık ekran yok

**Yeşil için gereken ölçüm:** yayın sırasında ve sonrasında parça
(chunk) hatası sayısı sıfır olmalı, ve ölçüm ZİYARET EDİLMEMİŞ bir
yoldan yapılmalı.

**Bugünkü sonuç:** DAĞITIM/1 ile 24→0, sonra 10→0; son üç yayında 0.

## K5 — Telefondan kullanılabiliyor

**Yeşil için gereken ölçüm:** gerçek tarayıcıda, gerçek çözünürlükte
(390x664 gibi) ölçüm — jsdom değil, grep değil.

**Bugünkü sonuç:** MESAJ/3 Parça 1 canlıda (`7b6b0fd4`); composer
görünen alanda kalıyor, ölçüm gerçek tarayıcıyla yapıldı.

**Neden KISMEN:** yalnız mesajlaşma ekranı ölçüldü. Diğer ekranlar
için telefon çözünürlüğünde ölçüm YAPILMADI.

## K6 — Yedek kanıtlı

**Yeşil için gereken ölçüm:** yedeğin GERİ YÜKLENEREK doğrulanması —
"dosya var" yeterli değil.

**Bugünkü sonuç:** gecelik yedek + üç ayda bir geri yükleme tatbikatı
koşuyor; tatbikat satır damgasıyla karşılaştırıyor. Şifreleme
akışta, anahtarsız yedek alınmıyor (`44fbec4d`).

**Not (2026-09-08, düzeltme):** "SIZINTI/1 yedeği şişiriyor" varsayımı
ÖLÇÜLDÜ ve doğru çıkmadı — tar+gzip ile tüm `uploads/` 41 MB, yalnız
gerçek dosyalar 40 MB. Fark ~1 MB. Şişme yedekte değil, DİSK BLOKLARINDA:
21 KB'lık `project-files/` diskte 34 MB yer kaplıyordu.

**Açık kalan bağımlılık:** AK-3 — yedekler, anahtar ve canlı veritabanı
aynı diskte. Tek disk arızası hepsini götürür.

## K7 — Hesap tablosu

**Yeşil için gereken ölçüm:** tüm hesaplar (yalnız aktifler değil),
rolleri, veri kapsamı, son giriş, durum, kişisel izin ve kısıt
sayılarıyla listelenmeli.

**Bugünkü sonuç (2026-09-08):**

| kullanıcı | ad | roller | kapsam | son giriş | durum | kişisel izin | kişisel kısıt |
|---|---|---|---:|---|---|---:|---:|
| mehmet | Mehmet Karacabey | Admin + Genel Müdür | 1 | 2026-09-08 | aktif | 0 | 0 |
| smemis | Sedat MEMİŞ | Teknik Ofis | 1 | 2026-07-27 | aktif | 0 | 84 |
| uakkaya | Uğur AKKAYA | Finans + Satın Alma + Ön Muhasebe + İK + İSG | 1 | 2026-09-07 | aktif | 0 | 19 |
| vtepe | Veysel TEPE | Araç Sorumlusu | 1 | hiç | aktif | 0 | 0 |
| asakcak | Afife SAKÇAK | Sekreterya | 1 | 2026-07-27 | PASİF | 2 | 96 |
| cboran | Cennet BORAN | Teknik Ofis | 1 | hiç | PASİF | 0 | 0 |
| ccihan | Cuma CİHAN | Formen | 1 | 2026-08-04 | PASİF | 0 | 7 |
| dyildirici | Duygu YILDIRICI | Finans + İK | 1 | 2026-08-10 | PASİF | 5 | 0 |
| hkutlu | Haluk KUTLU | Teknik Koordinatör | 1 | 2026-07-27 | PASİF | 0 | 0 |
| ioktem | İsmail ÖKTEM | Teknik Ofis | 1 | hiç | PASİF | 0 | 0 |
| iyavuzkanat | İsmail YAVUZKANAT | Teknik Ofis | 1 | 2026-08-13 | PASİF | 9 | 7 |
| oturkmen | Özlem TÜRKMEN | Finans + Satın Alma + İK | 1 | 2026-08-12 | PASİF | 27 | 2 |
| ralici | Rıdvan ALICI | Teknik Koordinatör | 1 | 2026-08-03 | PASİF | 0 | 0 |

**Tablodan çıkan iki not:**
- `vtepe` aktif ama HİÇ giriş yapmamış.
- Dört pasif hesabın kişisel izin/kısıt yükü duruyor (`asakcak` 96
  kısıt, `oturkmen` 27 ek izin). Pasif hesap yeniden açılırsa o ayarlar
  da geri gelir.

**AC1 ile bağlantı:** `projects.delete` bugün dört rolde. Teknik Ofis'i
taşıyan tek aktif kullanıcı `smemis` ve onda bu izin kişisel olarak
KISITLI (canlıda doğrulandı: `smemis | projects.delete | DENY`).
Teknik Koordinatör'ü taşıyan aktif kullanıcı yok. Yani fiilî açık kapı
yok — ama onu kapatan şey TESADÜF. KATALOG/1 sapmayı kapatıyor.

## K8 — İzleme (NÖBET/1)

**Yeşil için gereken ölçüm:** bir birim düştüğünde haberin GERÇEKTEN
gittiği gösterilmeli; ve gürültü sınırı ile "düzeldi" bildirimi
çalışmalı.

**Bugünkü sonuç:** `enderunai-backend`, `enderunai-frontend` ve
`enderun-backup` OnFailure taşıyor; sonda kuru koşuda dört ayakta
yeşil (düştü → susturuldu → düzelmedi → düzeldi). Kanal teslimi
2026-09-07'de gerçek gönderimle kanıtlandı.

**Kapsam kararı (Mehmet, 2026-09-08):** derin sonda (kimlik
doğrulamalı uç) YAPILMIYOR — AK-6 izleme hesabı pilot öncesi
açılmıyor, yeni kimlik yüzeyi istenmiyor. K8 bu kapsamda yeşil.

**Dürüst sınır:** nöbetçi izlediği sunucunun İÇİNDE koşuyor. Sunucu
ölürse nöbetçi de ölür. "Servis bozuldu"yu yakalar, "sunucu öldü"yü
yakalamaz — dışarıdan bakan göz AK-7.

## K9 — Testler canlıya dokunmuyor

**Yeşil için gereken ölçüm:** testler koşarken canlı dizinlerin dosya
sayısı DEĞİŞMEMELİ; ve ölçümün pozitif kontrolü olmalı (dosyalar
gerçekten yazıldı, sadece başka yere).

**Bugünkü sonuç:** SIZINTI/1 kapandı.

| | önce | sonra |
|---|---:|---:|
| düzeltmeden önce, aynı suite | 16 615 | 16 655 (+40) |
| düzeltmeden sonra | 16 696 | 16 696 (0) |
| `project-files/` | 3 250 | 3 250 (0) |

Pozitif kontrol: testlerin geçici kökünde 28 dosya — yükleme gerçekten
oldu. Rig tam koşumda 24/24 geçti, canlı dizinler değişmedi.

Fail-closed kapı: `enderun_ai_test`e bağlı bir süreç canlı yazma
köküne bakıyorsa BAŞLAMIYOR (sonda: çıkış 134).

---

## TOHUM/1 — tohum parolası durumu

**Veritabanından KANITLANAMIYOR, sebebi ölçüldü.**
`PasswordChangedAtUtc` kolonu 2026-09-03 göçüyle geldi; parola yazan
bir yol ise 2026-07-27'den beri var. Aradaki beş haftada yapılmış bir
değişiklik hiçbir iz bırakmaz.

**Kesin olanlar:**
- `SEED_ADMIN_PASSWORD` canlı ortam dosyasında hâlâ tanımlı.
- Tohumlayıcı mevcut kullanıcının parolasını hiç güncellemiyor —
  yalnız oluştururken yazıyor.
- 13 hesabın 11'i 2026-09-04'ten beri parolasını değiştirmemiş;
  değiştiren ikisi de bugün pasif. Dört aktif hesabın dördü de
  değiştirmemişler listesinde.

Kesin cevap ancak kimlik bilgisini denemekle alınır; denenmedi.
