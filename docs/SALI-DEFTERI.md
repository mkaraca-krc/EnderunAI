# GECE DEFTERİ — 2026-09-15 gecesi → 2026-09-16 09:00

Ölçümler buraya yazılır. Yukarı (rapora) yalnız **İHLAL**, **ÖLÇEMEDİ**
ve **durduran şey** taşınır.

---

## YAYIN N — VEKİL/1 + logo-önce ısıtma

### Uçuş öncesi (şart a) — 2026-09-15 20:51 UTC

| kapı | sonuç |
|---|---|
| yedek tazeliği | ✓ `db_20260915_182657.dump.gpg`, **2 saatlik**, 5.008.772 bayt |
| geri yükleme tatbikatı | ✓ 2026-09-15 03:31, **BAŞARILI**, 242 tablonun satır sayısı damgayla TAM eşleşti |
| kapsam kapısı | ✓ **YEŞİL 6/6**, paket `4bc57cdd..121b3540` |
| ağaç temiz + origin eşit | ✓ 0 kirli dosya, 0 ileri / 0 geri |

Dördü de yeşil → yayın açıldı.

### ⚠ TALİMATLA ÇELİŞEN ÖLÇÜM

Talimat "sistemde kimse yok" diyordu. **ÖLÇÜM: var.**
`78.175.232.135` son 60 dakikada 1.411 istek attı ve **20:45:13 UTC'de
(23:45 TRT) hâlâ tıklıyordu** — `/parola`, `/raporlar` ön getirmeleri.

KARAR VE GEREKÇE (tek satır): yayın ERTELENMEDİ, çünkü kesinti turun
EN SONUNDA oluşur (~50 dk sonra) ve bedeli ölçülü — 7 sn, oturum
düşmüyor; 15 Eylül akşam yayınında tam bu kullanıcıyla doğrulandı.

### Tetikler — YAYINDAN ÖNCE İLAN EDİLDİ

TABAN (20:00–21:00 UTC, 60 dk): toplam **1435** istek · `/api/` **398** ·
401 = **0** · 502 dışı 5xx = **0** · 502 = **0** · çıkış = **0** ·
başarılı giriş = **0**.

| # | tetik | eşik | eşik aşılırsa |
|---|---|---|---|
| T1 | 502 dışı 5xx | herhangi bir 5 dk kovasında **> 0** | GERİ AL, dur |
| T2 | 401 | herhangi bir 5 dk kovasında **> 5** | GERİ AL, dur |
| T3 | çıkış (`auth/logout`) | herhangi bir 5 dk kovasında **> 2** | GERİ AL, dur |
| T4 | sağlık ucu | `!= 200` | GERİ AL, dur |

PENCERE: 30 dakika, **5 dakikada bir** sayılır.
ÖLÇEMEDİ KURALI: bir kovada `/api/` isteği **< 5** ise o kova
"GEÇTİ" değil **ÖLÇEMEDİ** yazılır — trafik yokken boş küme her
iddiayı doğrular.

### YAYIN DENEMESİ 1 — DURDU (21:39 UTC), CANLIYA DOKUNULMADI

Arka uç testleri: **3271 testin 1'i düştü**, 27 dk 4 sn.
Düşen: `PsqlCizgisiTests.YeniDogrudanPsqlCagrisi_Eklenmemis`.

**KAPI HAKLIYDI VE KUSUR BENİMDİ.** Dün gece yazdığım
`deploy/scripts/vekil-adres-olcumu.sh` doğrudan `sudo -u postgres psql`
çağırıyordu; çizgi 0, ben 1 yaptım. Çıra bunu yakalayıp yayını
durdurdu — yani **bir ölçüm aleti, ölçüm disiplinini ihlal ederek**
canlıya çıkmak üzereydi.

DÜZELTME: istisna EKLENMEDİ, çağrı kanonik araca çevrildi
(`vt-sorgu.sh` — veritabanı adını zorunlu kılar, bakım veritabanlarını
reddeder, `current_database()` basar). Bir ölçüm aletinin bu güvenceye
en çok ihtiyacı olan yer burasıydı.

DOĞRULANDI: `PsqlCizgisiTests` 5/5 yeşil; alet dönüşümden sonra da
doğru ısırıyor (KIRMIZI, çıkış 1 — düzeltme henüz yayında değil).
Canlı sürüm dokunulmadan `1.0.0+4bc57cdd`, iki servis ayakta.

NOT: doğrulama koşusu 2 `sonda-vekil-` satırı daha yazdı (yayından
gelmiyor).

### YAYIN DENEMESİ 2 — ÇIKTI (22:48:09 UTC) · `4bc57cdd → 8abeb57c`

8 commit · arka uç 3271/3271 yeşil (31 dk 25 sn) · göç yok.

**Kesinti 9 saniye** (22:47:43–22:47:52), 4 adet 502, hepsi SignalR
`negotiate`. **502 dışı 5xx: 0.**

**Kesinti anında oturumda BİRİ VARDI** (`78.175.232.135`, 11 istek).
22:48:03'te hub'a geri bağlandı, **401 = 0, `/login` dönüşü = 0** —
oturum yayını atlattı (üçüncü kez doğrulandı).

Sürüm zinciri: arka uç `1.0.0+8abeb57c`, ön yüz etiketi `8abeb57c`.

#### Ölçüm 1 — VEKİL/1: **YEŞİL** (kırmızı→yeşil, aynı aletle)

```
ÖNCE  (4bc57cdd): 203.0.113.41 → 127.0.0.1 · 198.51.100.62 → 127.0.0.1   KIRMIZI
SONRA (8abeb57c): 203.0.113.41 → 203.0.113.41 · 198.51.100.62 → 198.51.100.62   YEŞİL
```

#### Ölçüm 2 — ısıtma: **HİPOTEZ ÇÜRÜDÜ, logo YETMEDİ**

```
logo (yazmasız okuma) : 0,1437 sn
giriş ilk             : 0,2376 sn     (beklenen ≤ 0,05)
giriş 2. / 3.         : 0,0198 / 0,0149 sn
```

Önceki iki yayında giriş ilk 0,2277 ve 0,2267'ydi — **neredeyse hiç
düşmedi.** Yani o ~215 ms genel MVC boru hattı JIT'i DEĞİL; giriş
yoluna özgü bir şey (en olası aday `AuthController`'ın kendi bağımlılık
grafiği — logo ucunun grafiği çok küçük).

Betik bunu kendi söyledi: *"yazmasız okuma YETMEDİ — giriş çağrısı
ÇIKARILMASIN; ölçüm raporlanmalı."* **Giriş çağrısı KALDIRILMADI.**
Seçenek ② ölçümde düştü; sıradaki aday ③ (aynı yolu koşturan, yazmayan
uç). Karar sabah.

#### Nöbet (30 dk, 5 dk kovalar)

| kova | api | 5xx | 401 | çıkış | health | sonuç |
|---|---|---|---|---|---|---|
| 1 | 4 | 0 | 0 | 0 | 200 | **ÖLÇEMEDİ** (api<5) |
| 2 | 4 | 0 | 0 | 0 | 200 | **ÖLÇEMEDİ** (api<5) |

Gece trafiği eşiğin altında; "tetikler temiz" denemez, ÖLÇEMEDİ denir.

---

## 2.1 S1 — stok listesi sütunları · **BİTTİ**

**Talimatın üçüncü maddesi zaten kapalıymış:** `page.tsx:185`'te yorum
yok (satır numarası kaymış); `isActive` yorumu 2026-09-13'te
düzeltilmiş ve düzeltmenin hikâyesini kendisi anlatıyor. Dokunmadım.

**Kart aktifliği satırda zaten var:** Malzeme sütununda rozet
(`kartDurumEtiketi`) + dışa aktarımda sözcük (`kartDurumIsareti`).
Testle çivilendi, değiştirilmedi.

**ASIL KUSUR ÖLÇÜLEREK BULUNDU.** Sütun iki hâl tanıyordu ve kritik
olmayan HER satıra `Normal` yazıyordu. Canlı ölçüm:

```
warehouse_stock_levels ... 0 satır
warehouse_stocks ......... 0 satır
stock_movements .......... 0 satır
```

Yani dokuz kartın dokuzunda `Normal` yazıyordu; ne stok vardı, ne
asgari seviye, ne tek bir hareket. Sütun bilgi taşımıyordu ve taşıdığı
tek şey YANLIŞTI — "normal seviyede" ölçülmemiş bir hükümdür (Kural 88).

YAPILAN: başlık `Stok Durumu` → **`Stok Seviyesi`**; üçüncü hâl eklendi
— seviye tanımlı değilse **`—`** (açıklaması: "Asgari seviye tanımlı
değil — ölçülmedi"). Hüküm saf bir modüle çıkarıldı
(`lib/inventory/stok-seviyesi.ts`), iki ayrı çağrıyla besleniyor:
"kritik mi" sunucuda kalıyor (kopya mantık yok), "tanımlı mı" ayrı
soruluyor.

SINANDI: 9 test yeşil. Mutasyon — `tanimsiz→normal` (eski kusur geri)
**2 kırmızı**; başlık `Stok Durumu`'na geri **1 kırmızı**; geri alınca
9/9. Lint: değişiklikten önce de sonra da aynı 1 hata + 1 uyarı
(dokunmadığım borç), çıra 3/3 yeşil.

---

## 2.3 ATEŞLEME/1 — ENVANTER (2026-09-15 gecesi, ateşleme YAPILMADI)

### ⚠ ENVANTERİN KENDİSİNİN BULGUSU

**Yayın günlüğünde makine okunur "kapı yandı" işareti YOK** (`[KAPI]` /
`gate=` / `KAPI-SONUC` desenleri: **0 eşleşme**). Kapıların sonucu
serbest metinle yazılıyor. Sonuç: "bu kapı en son ne zaman kırmızı
yandı" sorusu bugün **mekanik olarak cevaplanamıyor**; aşağıdaki
tablonun yarısı bu yüzden BİLİNMİYOR.

Bunu ölçerken kendi aletim de yanıldı: gevşek bir desen, "Kesinti
kapısı GEÇTİ … hatas**ız**" satırındaki *hata* kelimesini kırmızı
sandı ve yanlış bir tarih üretti. Yakalandı ve düzeltildi (Kural 84).

### Ucuz kapılar (13) — `ucuz-kapilar.sh`

| # | kapı | son KIRMIZI |
|---|---|---|
| 1 | kurumsal kimlik | BİLİNMİYOR |
| 2 | tip kontrolü | BİLİNMİYOR |
| 3 | ön yüz derlemesi | BİLİNMİYOR |
| 4 | sır tarayıcı (aralık) | **2026-09-06** |
| 5 | kutu ayrışması | BİLİNMİYOR |
| 6 | yayın kapsamı sondası | BİLİNMİYOR (sonda; her koşuda 14/14 yeşil) |
| 7 | derleme kilidi ölü scope sondası | BİLİNMİYOR (sonda; 5/5 yeşil) |
| 8 | yıkıcı beyan okuyucusu | BİLİNMİYOR (sonda; 5/5 yeşil) |
| 9 | kesinti vekil katmanı sondası | BİLİNMİYOR (sonda; 4/4 yeşil, 09-15'te doğdu) |
| 10 | açık veritabanı | **2026-09-08** |
| 11 | şema sapması | **2026-09-15** (bugün 2 kez: biri gerçek, biri HOME rig kusuru) |
| 12 | hesap beyan sapması | BİLİNMİYOR |
| 13 | sır bekçisi (tüm depo) | **2026-09-08** |

### safe-deploy iç kapıları

| kapı | son KIRMIZI |
|---|---|
| ağaç kapısı (commit edilmemiş) | **2026-09-09** |
| silinen savunma kontrolü (Kural 72) | **2026-09-09** |
| katalog silme kapısı | **2026-09-08** |
| backend testleri | **2026-09-09** |
| frontend testleri | **2026-09-10** |
| göç kapısı | **2026-09-08** (desen gevşek olabilir — doğrulanmadı) |
| sağlık kontrolü | **2026-08-02** |
| yayın kapsamı kapısı | BİLİNMİYOR |
| yıkıcı beyan kapısı | BİLİNMİYOR *(09-15 yayınında durdurduğu biliniyor ama günlükte tarihli iz bulunamadı)* |
| eski parça kapısı | BİLİNMİYOR |
| **kesinti kapısı** | **HİÇ YANMAMIŞ** — 19 koşu, 19 GEÇTİ. (Sondası var ve ısırıyor: 4/4.) |
| giriş döngüsü kapısı | HİÇ YANMAMIŞ — her koşuda **ÖLÇEMEDİ** (trafik yok) |
| proxy/websocket duman kontrolü | BİLİNMİYOR |

### Zamanlayıcıya bağlı aletler

| alet | son KIRMIZI |
|---|---|
| gece tam takım | **2026-09-15 ilk koşu: 3 kırmızı** (2'si gerçek, 1'i yanlış alarm). Son koşu YEŞİL 3259/3259. |
| **sorgu dizgesi çırası** | **2026-09-15 00:20 — KIRMIZI, VE KİMSE GÖRMEDİ** (aşağıda) |
| günlük özet | BİLİNMİYOR (kırmızı kavramı yok; rapor üretir) |
| yedek | BİLİNMİYOR |
| geri yükleme tatbikatı | BİLİNMİYOR (her koşu başarılı) |
| etiketsiz hüküm çırası | **TASARIM GEREĞİ HİÇ YANMAZ** (yalnız bilgi) |
| ajan izin çizgisi | BİLİNMİYOR |

---

## ⚠ BULGU — SORGU DİZGESİ ÇIRASI KIRMIZI YANDI, KİMSEYE ULAŞMADI (2026-09-15 00:20 UTC)

### Ne oldu

`enderun-sorgu-cirasi.service` 15 Eylül 00:20'de **çıkış 1** ile bitti:

```
[sorgu-çıra] KIRMIZI — /api/ uçlarında BEYAZ LİSTEDE OLMAYAN parametre:
    cmd
    host
    sql
```

`systemctl show` bugün hâlâ `Result=exit-code` diyor. **15 saat boyunca
kimse görmedi** — ben `NRestarts/Result` taramasında rastladım.

### AÇIK YOK — ölçüldü (Kural 92: hangi kapıya vuruldu, sonucu ne)

Tüm günlükler tarandı. O parametreleri taşıyan `/api/` isteği **7 adet**,
hepsi **tek kaynaktan** (`34.125.163.20`) ve **hepsi 401**:

```
GET /api/system?cmd=`echo GSCAN_CMDI`        401
GET /api/system?cmd=; echo GSCAN_CMDI #      401
GET /api/exec?cmd=...                        401  (x2)
GET /api/ping?host=...                       401  (x2)
GET /api/v0/run_sql?id=probe&sql=SELECT+1    401
```

`GSCAN_CMDI` imzası genel bir komut-enjeksiyonu tarayıcısıdır. Bu uçlar
**bizde yok**; varsayılan-ret yetki politikası yönlendirmeden ÖNCE
reddetmiş. **2xx yok, çalışan hiçbir şey yok, sızan hiçbir şey yok.**
404 yerine 401 dönmesi ayrıca bilgi sızdırmıyor.

### ASIL KUSUR: KIRMIZININ GİDECEĞİ YER YOK

`/etc/systemd/system/enderun-sorgu-cirasi.service` içinde **`OnFailure=`
YOK**, hiçbir bildirim yolu yok. Çıra doğru çalıştı, doğru yandı ve
**kimseye ulaşmadı**. Kural 89'un kardeşi: *kırmızısı kimseye ulaşmayan
bir kapı, kapı değildir.*

### YAPILMADI — SABAH KARARI

Bildirim yolu eklemedim: (a) gece listesinde yoktu, (b) "nereye
bildirilecek" bir karar gerektiriyor (günlük özete iliştirmek mi, ayrı
bir damga dosyası mı, başka bir yer mi). Beyaz listeye de EKLEMEDİM —
`cmd/host/sql` bizim meşru parametrelerimiz değil; eklemek kapıyı
yeşile boyamak olurdu.

ÖNERİ (karar sizin): çıra sonucunu her gece `gunluk-ozet` çıktısına tek
satır olarak iliştirmek — zaten okunan bir yere düşer, yeni kanal
gerekmez.
## 2.6 — fiş tipi eşlemesi: talimat 4 diyor, ÖLÇÜM 6
Record<AccountingVoucherType,string>: fisler/page.tsx · fisler/[id]/page.tsx
Record<number,string> (tür denetimi KAYIP): yevmiye/page.tsx · buyuk-defter/page.tsx
elle <option> listesi (YAZMA YOLU, en tehlikelisi): fisler/yeni/page.tsx · fisler/[id]/duzenle/page.tsx
Arka uç enum: Journal=0 Collection=1 Payment=2 Opening=3 Closing=4

## 2.7 — talimatın sayıları tutmuyor + TASARIM KARARIYLA ÇELİŞİYOR
Talimat: "7 AllowAnonymous + kalan 9 muaf tek beyan listesinde toplansın".
ÖLÇÜM: MuafUclar.txt'te 9 değil **22** kayıt var, hepsinin gerekçesi yazılı.
VE dosyanın başlığı birleştirmeyi AÇIKÇA REDDEDİYOR:
  "[AllowAnonymous] TAŞIYAN UÇLAR BU LİSTEDE DEĞİLDİR — anonimlik zaten
   gürültülü bir beyandır, sessiz yokluk değil."
Yani iki liste BİLEREK ayrı. Birleştirmek yazılı bir kararı geri alır.
ÖNERİ: ayrı bir anonim-uç beyan listesi (birleştirme değil). Karar sabah.

## 2.2 — K5 sondası zaten var, 390 ve 1280'de koşuyor
tests/duzen/k5-tasiran-eleman.spec.ts. 768 ve 1536 eklenecek.
Playwright gerektiriyor; bellek 130MB iken açılmadı, yayından sonra.

---

## 2.4 say.sh — Kural 84'ü araca çevirme · **BİTTİ**

**İlk üç madde zaten kapalıydı** (ölçüldü, `say.sh` başlığından):
özyineleme varsayılan · taranan kapsam her koşuda basılıyor · yüzde
tuzağı `--etiketten-sayi` ile kapalı.

**Dördüncü madde ölçülünce daralttı.** "Çıplak ls/find/grep sayımlarını
buna çevirin" — taradım: `deploy/scripts` + `scripts` altında **21**
tane var. AMA çoğu `printf '%s\n' "$x" | grep -c .` biçiminde ve bunlar
**akıştaki satırı** sayıyor, dosya taramıyor. `say.sh` "şu kök altında
şu desene uyan kaç dosya var" sorusunu yanıtlar; farklı soru.
Hepsini çevirmek araç taşımacılığı olurdu ve kodu kötüleştirirdi —
**çevirmedim.**

Gerçek hedef glob'la DOSYA sayanlardı. Biri gerçekten kusurluydu:

`sorgu-dizgesi-cirasi.sh` **saydığı küme ile okuduğu küme farklıydı**:

```
okunan : access.log + access.log.1 + access.log.*.gz
sayılan: access.log*                ← HEPSİ
```

Bugün ikisi de 17 veriyor (ölçüldü; `delaycompress` sayesinde yalnız
`.1` sıkıştırılmamış). Sapma bugün YOK — ama iki liste ayrı ayrı
bakımdaydı, yani sapma bir gün gelirdi ve çıra "17 dosya tarandı"
derken 16'sını tarardı. Liste **tek kaynağa** indirildi; okuma ve
sayım aynı diziden besleniyor.

Ek olarak ÖLÇEMEDİ dalı eklendi: okunacak günlük yoksa çıra "temiz"
demiyor, **çıkış 3** veriyor. Pozitif kontrolle kanıtlandı (boş dizin →
ÖLÇEMEDİ). Sondam önce yanlış değişkeni çevirdi (`GUNLUK_DIZINI`,
doğrusu `SORGU_CIRA_GUNLUK`) ve "ısırmadı" diye yanlış hüküm kurmama
ramak kaldı — Kural 93, aynı gece üçüncü kez.

**KALICI PARÇA — MUHAFIZ:** `CiplakGlobSayimiTests` (4 test). Yeni bir
`ls <glob> | wc -l` eklenirse kırmızı yanar; akıştaki satır sayımlarını
YAKALAMAZ (yanlış kırmızı yakmasın diye ayrıca sınandı). Mutasyon:
çıplak glob geri konuldu → **tam 1 kırmızı**; geri alınca 4/4.

### Nöbet penceresi — SONUÇ (22:48–23:23 UTC)

| tetik | eşik | ölçülen | sonuç |
|---|---|---|---|
| T1 502 dışı 5xx | > 0 | **0** | temiz |
| T2 401 | > 5 | **0** | temiz |
| T3 çıkış | > 2 | **0** | temiz |
| T4 sağlık | ≠ 200 | **200** | temiz |

Pencere toplamı: 68 istek, **41 `/api/`**.

**AMA KOVA BAZINDA ÖLÇEMEDİ.** Beş kovanın beşinde de `/api/` = 4,
yani ilan ettiğim 5'lik tabanın altında. Pencere BÜTÜNÜ ölçülebilir
(41 istek / 35 dk ≈ 70 saat⁻¹, 50'lik tabanın üstünde) ve o düzeyde
dört tetik de temiz; **5 dakikalık çözünürlükte ise hiçbir şey
kanıtlanmadı.** İkisi birden yazılıyor çünkü "tetikler yeşil" cümlesi
tek başına yanıltıcı olurdu.

Başarılı giriş: **0** — canlıda hâlâ kimse giriş yapmadı.

---

## 2.5 duzen-testi.sh rig zemini · **ZATEN KAPALIYMIŞ**

Üç şart da yerinde, ölçüldü:

1. **Şirketi kendisi tohumluyor** — `SELECT ... FROM companies LIMIT 1;
   IF v_sirket IS NULL THEN INSERT INTO companies ...` (satır 336-341).
2. **Tohumlamanın kendi pozitif kontrolü var** — SQL hatasızlığı yeterli
   sayılmıyor; şirket ve konuşma sayısı tohumlamadan SONRA ayrıca
   sayılıyor (satır 501-503, `vt-sorgu.sh` üzerinden). Betiğin kendi
   cümlesi: *"'hata yok' ile 'zemin kuruldu' aynı şey değil: bir INSERT
   sessizce 0 satır etkilemiş olabilir."*
3. **Zemin kurulamazsa ÖLÇEMEDİ, İHLAL değil** — çıkış **3**, ve üç ayrı
   satırla "rig hiçbir ÜRÜN davranışı ölçmedi" deniyor (satır 509-512).

Değiştirilecek bir şey bulamadım; **dokunmadım.** Bu, gece listesinde
zaten kapalı çıkan DÖRDÜNCÜ madde.

---

## 2.2 K5 — dört genişlik · **KOD BİTTİ, SINIFLANDIRMA ÖLÇEMEDİ**

Sonda `390 · 768 · 1280 · 1536`e çıkarıldı. Her genişliğin NEDEN
seçildiği yazıldı (768 hiç ölçülmemişti; 1536 taşmanın sıfırlandığı üst
uç — nerede bittiğini ölçmeden "düzeldi" denemez).

**KAPI EKLENDİ:** ölçülen genişlik sayısı 4'ün altına düşerse KIRMIZI.
Sonda yalnız rapor basıyordu; biri diziden bir ölçek silse ya da bir
viewport sessizce kurulamasa YİNE YEŞİL YANARDI (Kural 90). Ayrıca
**istenen değil OLAN genişlik** sayılıyor: `setViewportSize` başka bir
değere düşerse o ölçek sayılmıyor ve satır "ÖLÇEMEDİ" diyor.

**SINIFLANDIRMA YAPILAMADI — ÖLÇEMEDİ.** Sonda rig gerektiriyor
(`duzen-testi.sh`: arka uç + ön yüz + vekil + Playwright) ve rig iki
kez BELLEK YETERSİZLİĞİNDEN düştü. Tahmin yazılmadı. Artık süreç
kalmadı, portlar temiz.

> SIRA BAĞIMLILIĞI: talimat 2.2'yi 2.5'ten önce koymuştu ama 2.2 rig'e
> muhtaç; bağımlılık ters yönde.

---

## 2.6 Enum süpürmesi — muhasebe fiş tipi · **BİTTİ**

**Talimat 4 dosya diyordu; ölçüm ALTI buldu, sonda YEDİNCİYİ buldu.**

| biçim | dosya |
|---|---|
| `Record<AccountingVoucherType, string>` | `fisler/page.tsx` · `fisler/[id]/page.tsx` |
| `Record<number, string>` — **tür denetimi kayıp** | `yevmiye/page.tsx` · `buyuk-defter/page.tsx` |
| elle `<option value={n}>` — **YAZMA YOLU** | `fisler/yeni/page.tsx` · `fisler/[id]/duzenle/page.tsx` |
| elle `<option value="n">` — **süzgeç, dizge değerli** | `fisler/page.tsx` ← **yapısal sonda buldu** |

Yedincisi ilk taramamda görünmedi çünkü değerleri DİZGE tutuyordu
(`value="0"`, `value={0}` değil). Kendi gözümle bulamadığım kopyayı
sonda buldu.

**TÜR DENETİMİNİ SIKMAK İKİ GERÇEK KUSUR ÇIKARDI.** Tek kaynak
`Record<AccountingVoucherType, string>` yazılınca derleyici,
`Record<number, string>` kullanan iki dosyanın **düz bir sayıyla
indekslediğini** gösterdi (TS7053). Kayıp olan denetim tam buydu; o iki
yer `fisTipiEtiketi()`ne bağlandı ve bilinmeyen değer artık sessiz
geçmiyor, adıyla basılıyor.

### Üç sonda, üçü de mutasyonla kırmızı

| sonda | mutasyon | sonuç |
|---|---|---|
| 1 yön | Tahsil ↔ Tediye | **3 kırmızı** |
| 2 bütünlük (arka uç enum'undan okunuyor) | arka uca `Devir = 5` eklendi | **1 kırmızı** |
| 3 yapısal | elle `<option>Tahsil` geri kondu | **1 kırmızı** |

Geri alınca 11/11.

**KENDİ YANLIŞ KIRMIZIM:** yapısal sondanın ilk deseni
`<option value={0..4}>` idi ve `hesap-plani` ekranlarındaki **Borç /
Alacak** listesini de yakaladı — başka bir enum. Desen etikete
daraltıldı; hem ısırdığı hem de başka enum'u yakalamadığı ayrıca
sınandı (Kural 84: sonda ölçtüğü ayrımı korumak zorundadır).

**Öteki 12 eşlemeye DOKUNULMADI.**

### Yan bulgu — test sayısı çırası gevşek

Çıra yeşil (kaynak 2716 = koşucu 2716) ama kendi çıktısı şunu yazıyor:
`arka uç: çizgi 3009 · gerçek 3127 · gevşeklik 118` ve
`ön yüz: çizgi 492 · gerçek 559 · gevşeklik 67`.

Çizgi gerçeğin epey altında ve çıra bunu **yalnız bilgi olarak** basıyor,
kırmızı yakmıyor. "Çizginin ALTINDA olmak da kırmızıdır" kuralına
aykırı. Bu boşluk benim eklediğim testlerden gelmiyor (önceden vardı);
çizgi sıkmak bilinçli bir karar olduğu için DOKUNMADIM.
