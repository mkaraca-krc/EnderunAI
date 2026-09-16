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

**DÜZELTME (2026-09-16, Kural 94):** ilk yazdığım "hiçbir bildirim
yolu yok" cümlesi YANLIŞTI. Yol VARDI — `enderun-uyari@` şablonu,
dosya + e-posta kanallı — ve `enderun-backup` ile
`enderun-geri-yukleme-tatbikati` ona BAĞLIYDI. Kırmızı yanan birim
(`enderun-sorgu-cirasi`) **bağlı değildi.** Doğru cümle: kanal
kurmak yetmez, kanala bağlanmayan kapı kanalı olmayanla aynıdır. Çıra doğru çalıştı, doğru yandı ve
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

---

## 1) KIRMIZININ GİDECEĞİ YER · **KURULDU, KIRMIZI-YEŞİL KANITLI**

**TEŞHİSİM DÜZELTİLDİ (Kural 94).** "Hiçbir bildirim yolu yok" dedim;
ölçüm çürüttü. Yol VARDI — `enderun-uyari@` şablonu (dosya + e-posta,
susturma mantığıyla) — ve `enderun-backup` ile
`enderun-geri-yukleme-tatbikati` ona BAĞLIYDI. Kırmızı yanan birim
bağlı değildi. **Kanal kurmak yetmez; kanala bağlanmayan kapı, kanalı
olmayanla aynıdır.**

Kurulan (yeni teslim kanalı DEĞİL — okunan yerlere bağlandı):

| parça | dosya |
|---|---|
| kaydedici | `deploy/scripts/kirmizi-kaydet.sh` |
| şablon birim | `deploy/systemd/enderun-kirmizi-kaydet@.service` |
| kapı | `deploy/scripts/okunmamis-kirmizi-kapisi.sh` |
| okundu işaretle | `deploy/scripts/kirmizi-okundu.sh` |
| defter | `/var/lib/enderun-ai/okunmamis-kirmizilar.txt` |
| arşiv | `/var/lib/enderun-ai/okunan-kirmizilar.txt` |

**Beş birimin beşi de bağlandı** (sorgu-cirasi · tam-takim · gunluk-ozet
· backup · geri-yukleme-tatbikati). Mevcut `enderun-uyari@`ye
DOKUNULMADI; ikisi farklı iş (haber vermek / yayını durdurmak).

### Kırmızı-yeşil — uçtan uca, gerçek yoldan

```
1. defter boş           → kapı GEÇTİ (çıkış 0)
2. sahte kırmızı        → systemd OnFailure ile deftere DÜŞTÜ
3. kapı                 → KIRMIZI (çıkış 1)
4. ucuz kapılar         → "DÜŞTÜ: okunmamış kırmızı", pahalı turlara GİRİLMEDİ
5. kirmizi-okundu.sh    → satır ARŞİVE taşındı (silinmedi), not ve zamanla
6. kapı                 → GEÇTİ (çıkış 0)
7. ucuz kapılar         → ✓ okunmamış kırmızı
```

Beyaz listeye hiçbir şey eklenmedi; 15 Eylül'ün kırmızısı doğru bir
kırmızıydı (7 istek, hepsi 401, sızan yok).

---

## 4) TEST SAYISI ÇIRASI · **ÇİZGİ GERÇEĞE ÇEKİLDİ**

```
önce : arka uç çizgi 3009 · gerçek 3127 · gevşeklik 118
       ön yüz  çizgi  492 · gerçek  559 · gevşeklik  67
sonra: gevşeklik 0 · 0   (dinamik de: 25/25, 16/16)
```

Gerekçe çizgi dosyalarının içine yazıldı: gevşek bir çizgi ilerlemeyi
de gerilemeyi de gizler, çıra süse döner. Boşluk gece eklenen
testlerden gelmiyordu; birikmiş borçtu.

---

## 2) ISITMA ADAYI (geçersiz gövde) · **ÖLÇÜLDÜ, DÜŞTÜ** — canlıya dokunulmadı

Ölçüm rig'de yapıldı: **aynı yayım çıktısı** (`publish/`),
`enderun_ai_test`, ayrı port (5158), kendi disk kökü. Canlıda 0 sn
kesinti. Hazırlık yoklaması `/api/health` ile — o bir `MapGet`,
denetleyici boru hattına girmez, ölçümü ısıtmaz.

| kol | koşu 1 | koşu 2 |
|---|---|---|
| **A** (ön çağrı yok, kontrol) | 0,2718 s | 0,4575 s |
| **B** (önce geçersiz gövde → 400) | 0,2915 s | 0,3705 s |

Geçersiz gövde **400 döndü** ve **denetim satırı yazmadı** (12→12 ve
14→14, iki koşuda da doğrulandı) — yani adayın "yazmaz" iddiası doğru.
**Ama giriş yolunu ısıtmıyor.** Hedef 0,05; B'nin en iyisi 0,29 — on
kat uzak.

### Maliyet veritabanında DEĞİL

Rig günlüğü: ilk `DbCommand` 42 ms (bağlantı), geri kalanı 1-6 ms.
`FROM users` sorgusu başlangıçta zaten koşuyor. Yani 0,27-0,45 sn'lik
bedel EF sorgu derlemesi değil.

### İKİ RİG KUSURU — ikisi de bende

1. **Kol C geçersiz çıktı.** "Yazan anonim uç" diye `access-requests`
   çağırdım; **400** döndü (yük biçimi yanlış), hiçbir şey yazmadı,
   yani ölçtüğünü sandığım şeyi ölçmedi. Düzeltmedim, **KALDIRDIM**:
   o uç `[Required] Password` alanı istiyor ve **parola taşıyan bir
   ucu her yayında ısıtma için çağırmak yanlış olur.**

2. **Ölçüm gürültülü.** Aynı A kolu iki koşuda 0,2718 ve 0,4575 verdi
   — %70 sapma. Aletin ilk hükmünde "KISMEN düştü" diyen bir dal
   vardı; o dal **gürültü okuyordu** ve kaldırıldı. Hüküm daraltıldı:
   `B ≥ 0,20` → yetmiyor (sapmadan etkilenmez) · `B ≤ 0,05` → yarıyor
   · arası → **ÖLÇEMEDİ**, "her kolu 3 kez soğuk başlatıp ortancayı
   alın" der.

### Karar hazır değil — ölçümle birlikte geliyor

Aday ② düştü. Geriye ③ kalıyor ama **③'ün ne yapması gerektiği henüz
bilinmiyor**: maliyet ne genel MVC boru hattında (logo denendi), ne
denetleyici etkinleştirmede (geçersiz gövde denendi), ne de
veritabanında (günlükle ölçüldü). Bir sonraki adım ③'ü tasarlamak
değil, **kalan bedelin nerede olduğunu ölçmek** olmalı.

**Canlıda hiçbir değişiklik yapılmadı; giriş çağrısı ısıtmada duruyor.**

### KOL D ve E — dört kol, üçer koşu (2026-09-16)

| kol | ortanca | en iyi | üç koşu |
|---|---|---|---|
| A (ön çağrı yok) | **0,4335** | 0,4196 | 0,4196 · 0,4335 · 0,4636 |
| B (geçersiz gövde 400) | **0,3454** | 0,3380 | 0,3454 · 0,3607 · 0,3380 |
| **D (bugünkü ısıtma çağrısı)** | **0,0449** | 0,0382 | 0,0582 · 0,0382 · 0,0450 |
| **E (var olan kullanıcı, yanlış parola)** | **0,0402** | 0,0392 | 0,0392 · 0,0402 · 0,0445 |

#### SEÇENEK ① ÖLÇÜMDE DÜŞTÜ

D (0,045) ≈ A'nın onda biri. **Bugünkü ısıtma çağrısı işe yarıyor.**
Çıkarmak ilk kullanıcıya **~0,39 sn** kaybettirir; "kaybedilen bir şey
olmaz" varsayımı yanlıştı. Çağrı yerinde duruyor.

#### HİPOTEZ (parola özeti) ÇÜRÜDÜ

E ≈ D. Oysa D `passwordService.Verify`ı **hiç çalıştırmaz** (kullanıcı
yok → kısa devre). Verify çalışmadan da aynı ısınma elde ediliyor;
maliyetin yeri parola özeti DEĞİL.

#### ÇÜRÜRKEN ÖLÇTÜRDÜĞÜ ŞEY — Verify'ın kendi bedeli

Ön-çağrıların KENDİ süreleri:

```
D ön-çağrı (Verify YOK) : 0,458 s
E ön-çağrı (Verify VAR) : 0,784 · 0,887 · 0,781 s
```

Aradaki **~0,32 sn** Verify'ın bedeli. Özet kasıtlı yavaştır; yani
**gerçek bir kullanıcının başarılı girişi, ısıtmadan bağımsız olarak
bu bedeli ödeyecek.** Bu ölçüm, o 0,32'nin ne kadarının tek seferlik
JIT ne kadarının kalıcı özet maliyeti olduğunu AYIRMIYOR — ayrı ölçüm
konusu, tahmin yazılmadı.

#### MALİYETİN YERİ DARALDI

B işleyiciye girmiyor (model doğrulamasında duruyor) → 0,345.
D işleyiciye giriyor → 0,045.
Fark işleyici GÖVDESİNDE: EF sorgu boru hattı + `SaveChanges`/izleyici
zinciri. Ham SQL değil (rig günlüğü: 1-6 ms).

**③ TASARLANMADI.** Yazma yapmayan bir aday yalnız SELECT'i ısıtırsa
`SaveChanges` zinciri soğuk kalabilir; D ve E'nin ikisi de satır
yazıyor. Bir sonraki ölçüm bunu ayırmalı.

**Canlıda hiçbir değişiklik yapılmadı.**

---

## E-POSTA UYARISI — "KANAL DEĞİL" ETİKETİ YAZILDI

ÖLÇÜM: `/var/log/enderun-uyari.log` içinde gönderim satırı **0**, kuru
koşu satırı **3**. Birim bir kez bile e-posta teslim etmemiş.

Etiket `enderun-uyari@.service`in içine yazıldı (hem canlı hem depo
kopyası; kutu ayrışma kapısı 24 dosyada ayrışma yok diyor). Ayak
KALDIRILMADI; ilk gerçek gönderimi Mehmet Bey yapacak ve etiket o gün
ölçümle güncellenecek.

Ayrıca ayrışma kapısı gerçek bir eksik yakaladı: gece sunucuya
eklediğim `OnFailure=enderun-kirmizi-kaydet` satırları depo
kopyalarında yoktu. Beş birimin kopyası eşitlendi.

---

## ⚠ BULGU — GECENİN BÜTÜN "BELLEK YETERSİZ" DÜŞMELERİNİN SEBEBİ (2026-09-16)

### Ne bulundu

Tekrarlanan `dotnet test` koşuları arkada **Roslyn derleme sunucusu**
(`VBCSCompiler`) bırakıyor ve o süreç büyüyor. Ölçüm:

```
VBCSCompiler PID 573406 : 5.500.888 kB RSS  (~5,5 GB)
makine toplam           : 7.894 MB
o anda kullanılabilir   : 650 MB
```

İki süreç PID ile kapatıldıktan sonra **kullanılabilir bellek
650 MB → 6.161 MB**. Canlı arka uca (PID 507422) dokunulmadı, sağlık
200 kaldı.

### Neyi açıklıyor

Gece boyunca şunlar "sistem belleği azaldığı için" öldürüldü:
K5 rig'i (İKİ kez) · yayın bekleyicisi (üç kez) · anonim uç mutasyon
turu.

> **DÜZELTME (2026-09-16, Kural 94).** İlk yazdığım "hepsinin ortak
> sebebi buydu" cümlesi FAZLA GENİŞTİ ve ölçüm onu çürüttü.
>
> Derleme sunucuları temizlendikten SONRA, kullanılabilir bellek
> **6.194 MB** iken K5 rig'i ÜÇÜNCÜ kez öldürüldü — üstelik **iki
> satırlık çıktıyla**, yani hiç başlayamadan. O anda makinede büyük
> süreç yoktu.
>
> Aynı rig **`systemd-run` altında sorunsuz ilerledi.** Yani K5'i
> öldüren şey sistem belleği değil, **koşturduğum arka plan görev
> gözcüsüydü**. Derleme sunucusu bulgusu GEÇERLİ (5,5 GB gerçekten
> geri geldi) ama **K5 düşmelerini AÇIKLAMIYOR**; iki ayrı olayı tek
> sebebe bağlamıştım.
>
> DERS: bir sebep bulunca, o sebeple açıklanabilecek HER olayı ona
> bağlamak cazip olur. Açıklamanın kapsamı da ölçülmeli — "bu bulgu
> şunu da açıklar" ayrı bir iddiadır ve ayrı kanıt ister.

### `dotnet build-server shutdown` YETMEDİ

Resmî yol çağrıldı ve *"VB/C# compiler server shut down successfully"*
dedi — **ama iki süreç de ayakta kaldı** (bellek 597→611 MB, yani
değişmedi). Süreçler ancak PID ile TERM edilince gitti.

DERS: bir temizlik komutunun "başarılı" demesi, temizlendiğini
göstermez. Ölçü, komutun çıktısı değil BELLEĞİN KENDİSİ.

### Yapılmadı — karar bekliyor

Test/yayın turlarının sonuna bir temizlik adımı koymak mantıklı
görünüyor ama kendi başıma eklemedim: `safe-deploy` içinde süreç
öldürmek, yanlış PID seçilirse canlıyı düşürür. Doğru tasarım
(hangi süreç, hangi ölçütle, canlıyı nasıl korur) bir karar konusu.
