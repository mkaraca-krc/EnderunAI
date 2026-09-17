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

---

## 2.2 K5 — SINIFLANDIRMA **YAPILDI** (ÖLÇEMEDİ kapandı)

Rig `systemd-run` altında koştu ve **4/4 genişlik ölçüldü**; kapsam
kapısı da ilk gerçek koşusunda yeşil yandı.

### Rig canlıyı doğruladı (Kural 81)

| genişlik | rig taşma | Mehmet Bey (tarayıcı, canlı) |
|---|---|---|
| 390 | 0 | — |
| 768 | 0 | — |
| **1280** | **72 px** | **70 px** |
| 1536 | **0** | **0** |

İki bağımsız ölçüm aynı yeri gösterdi. Rig'in canlıyı taklit ettiği
böylece ölçülmüş oldu — varsayılmadı.

### Taşan tek ekran: `/dashboard`

`kasa-banka` ve `odeme-planlari` dört genişlikte de temiz.

### Ata zinciri — çivi tek satırda görünüyor

```
a                                            w= 106  min-width=AUTO   ← taşıran
div.erp-quick-grid                           w= 274  min-width=0px  cols=85,1 86,0 92,2 105,6  (toplam 369!)
div.erp-panel.dashboard-quick-actions-widget w= 320  min-width=0px
aside                                        w= 320  min-width=auto
section.enderun-dashboard-layout             w=1016  cols=678px 320px
```

Sütunlar toplamı **369 px**, kap **274 px**. Fark taşma.

### SINIFLANDIRMA: **ÇİVİLEYEN KURAL** (üçüncü kova)

Ne "meşru geniş" (tablo değil, dört küçük bağlantı) ne de "sebepsiz
geniş" (içerik zaten küçük). Bir CSS kuralı küçülmeyi YASAKLIYOR.

`app/globals.css` içinde `.erp-quick-grid` **İKİ KEZ** tanımlı:

```
satır  664 : grid-template-columns: repeat(2, minmax(0, 1fr))   ← DOĞRU
satır 1092 : grid-template-columns: repeat(4, 1fr)              ← SONRA, kazanıyor
```

Aynı özgüllük; kaynak sırası gereği sonraki kazanır. Ve fark tam
sebeptir: **`1fr` = `minmax(auto, 1fr)`** — asgarisi `auto` olduğu için
her sütun içeriğinin doğal genişliğinin ALTINA inmeyi reddeder.
`minmax(0, 1fr)` sıfıra kadar iner.

**DÜZELTME DOSYADA ZATEN YAZILI VE EZİLİYOR.** Biri 664'te doğrusunu
yazmış; 1092'deki küçültülmüş blok onu geçersiz kılıyor. Bu, fiş tipi
eşlemesiyle aynı aile: **iki kopya, zamanla iki davranış.**

1000 px altında `@media` iki sütuna düşürdüğü için 768 temiz; üstünde
dört sütun `1fr` ile kalıyor ve 320 px'lik `aside` içinde patlıyor.

### YAPILMADI

Tek simgelik düzeltme belli (`1fr` → `minmax(0, 1fr)`, ya da 1092'deki
kopyanın kaldırılması) ama **uygulamadım**: görev sınıflandırmaydı, ve
iki kopyadan HANGİSİNİN kalacağı (2 sütun mu 4 sütun mu) bir tasarım
kararı — ölçüm o soruyu cevaplamıyor.

---

## KARAR ① — QUICK-GRID: 4 SÜTUNLU KOPYA SİLİNDİ

Mehmet Bey'in gerekçesi: ızgara 320 px'lik dar `aside` içinde; dört
sütun demek sütun başına ~80 px demek ve bağlantı metni sığmıyor —
taşmanın sebebi bu. İki sütun ~150 px verir, okunur.

**Düzeltme "4'ü `minmax(0,1fr)` yapmak" DEĞİLDİ.** Kopyanın kendisi
kaldırıldı; iki kopyadan birini düzeltmek ikinci davranışı korumaktı.

| satır | ne yapıldı |
|---|---|
| 664 (taban, `repeat(2, minmax(0,1fr))`) | **KALDI** — tek tanım |
| 1092 (taban, `repeat(4, 1fr)`) | **SİLİNDİ** |
| 1097 (`@media(max-width:1000px)` → `repeat(2,1fr)`) | **SİLİNDİ** — 664 zaten 2 sütun; üstelik `1fr` (çivileyen biçim) |
| 1013 (`@media (max-width:760px)` → `1fr`) | **KALDI** — meşru mobil davranış |
| `.erp-quick-grid a{...}` | **KALDI** — farklı seçici; işaretlemede sınıf yok, bağlantıları asıl o biçimlendiriyor |

`.erp-quick-grid a` kuralını da silseydim bağlantılar çerçevesiz ve
dolgusuz kalırdı — "kopyayı sil" talimatı o seçiciyi kapsamıyordu,
ölçtüm ve ayırdım.

### DÜZELTME CANLIDA DOĞRULANDI — kırmızı/yeşil aynı aletle

```
önce  (iki kopya, repeat(4,1fr) kazanıyor) : 1280 → 72 px taşma
sonra (tek tanım, repeat(2,minmax(0,1fr))) : 1280 → 0
```

Dört genişlik de temiz (390 · 768 · 1280 · 1536), kapsam kapısı 4/4,
sonda `1 passed`. Rig'in canlıyı taklit ettiği aynı ölçümde kanıtlıydı
(Mehmet Bey tarayıcıdan 70 px, rig 72 px).

**Muhafız:** `QuickGridTekTanimTests` (4 test). `@media` blokları
ayıklanıp TABAN tanım sayılıyor; `.erp-quick-grid a` tanım sayılmıyor.
Mutasyon: ikinci taban tanım → **1 kırmızı**; `minmax(0,1fr)` → `1fr`
→ **1 kırmızı**. Geri alınca 4/4.

---

## KARAR ④ — İKİ HATA ARACA TAŞINDI

**Hata 1 — kendini öldürme.** `pgrep -f VBCSCompiler` çağıran kabuğun
kendi komut satırını eşleştirdi; bulunan pid öldürülünce kabuk öldü
(çıkış 144). **Kural 78 depoda yazılıydı ve yine düştüm** — kuralın
yetmediğinin kanıtı.

**Hata 2 — kendini sayma.** `ps | grep -c` kendi boru hattını saydı;
"kalan: 4" yazdım, gerçek **0**'dı. `grep -ci error`ın `error.log`
dosya adlarını sayması ile aynı aile.

**KANCANIN KENDİ TAVSİYESİ TUZAĞA YÖNLENDİRİYORDU.** Reddetme metni
*"pgrep ile pid'i bulup kill &lt;pid&gt; kullanın"* diyordu — yani tam
beni öldüren yolu öneriyordu. Metin düzeltildi.

Yapılanlar:

| yer | değişiklik |
|---|---|
| `surec-durdur.sh` | **`--listele --desen <metin>`** kipi: ÖLDÜRMEZ, PID+RSS basar ve SAYIYI verir; kendini ve atasını dışlar |
| `pkill-kancasi.sh` | yasak desen `pgrep`i de kapsıyor; tavsiye metni düzeltildi |
| `PkillYasagiTests` | `\b(pkill\|pgrep)\s+...-f` arıyor |

`--listele` çağırarak sınandı: deseni kendi komut satırında taşımasına
rağmen `VBCSCompiler` için **0** verdi (kendini saymadı), canlı arka uç
için **1** (529 MB) — yani hem dışlıyor hem buluyor.

### Yan tuzak — `systemctl show` yanılttı

`--collect` ile başlatılan bir birim bitince SİLİNİYOR ve
`systemctl show` o birim için **varsayılan** döndürüyor: günlükte
`FAILURE` yazarken `Result=success` okudum. Birim sonucu artık
günlükten okunuyor, `show`dan değil.

### Kendi kuralımı çiğnedim

"Rig'i başka hiçbir ağır işle aynı anda koşturmayın" talimatına rağmen
rig koşarken `dotnet test` başlattım; publish SIGTERM aldı (143) ve
**doğrulama koşusu düştü**. Tekrarı tek başına koşuldu.

---

## KARAR ③ — DERLEYİCİ SUNUCUSU KAYNAĞINDA KESİLDİ

Mehmet Bey: *"safe-deploy'a temizlik EKLEMEYİN. Kaynağı kesin."*
Süreç öldürme yayın betiğine girmedi.

### Dört kol ölçüldü — tahmin edilmedi

| kol | bayrak | süre | kalıntı |
|---|---|---|---|
| A | yok (bugünkü hâl) | 268 sn | **1** |
| B | `UseSharedCompilation=false` | 249 sn | **0** |
| C | `nodeReuse=false` | 252 sn | **1** |
| D | ikisi birden | 270 sn | **0** |

**Yalnız B çalışıyor.** `nodeReuse` MSBuild işçi düğümlerini yönetir,
Roslyn derleyici sunucusunu DEĞİL — bu yüzden **eklenmedi**. D, B'den
iyi değil, yalnız daha yavaş. "Ne olur ne olmaz" diye çalışmayan bir
bayrak eklemek, sonraki okuyucuya onun çalıştığını söylerdi.

**BEDEL ÖLÇÜLEMEDİ:** 249 / 268 / 270 farkları, A kolunun kendi koşular
arası sapmasının (244 ↔ 268) içinde. Paylaşılan sunucuyu kapatmanın
ölçülebilir bir maliyeti çıkmadı.

### İLK ÖLÇÜMÜM GEÇERSİZDİ — kendi rig kusurum

İlk turda B/C/D 3-5 saniye sürdü ve kalıntı 0 verdi. Sebep: kollar
arasında ara çıktı (`obj/`) duruyordu, koşular **artımlıydı ve hiçbir
şey derlemedi**. Ve hiçbir şey derlemeyen bir koşu derleyici sunucusu
da BAŞLATMAZ — yani "kalıntı=0" bayrağın işe yaradığını değil,
**derleme olmadığını** gösteriyordu. Ölçüm her kolda kaynak damgası
tazelenerek tekrarlandı; A tekrarında 268 sn çıktı (ilk turdaki 244 ile
tutarlı), yani taban sağlam.

### Uygulanan

`safe-deploy.sh` içindeki `dotnet publish` ve `dotnet test` çağrılarına
**yalnız** `-p:UseSharedCompilation=false`.

**Muhafız:** `PaylasilanDerleyiciKapaliTests` (4 test) — her `dotnet
publish|test` çağrısı bayrağı taşımalı, ve `nodeReuse` eklenmemiş
olmalı. Mutasyon: bayrak kaldırıldı → 1 kırmızı; `nodeReuse` eklendi →
1 kırmızı. Geri alınca 4/4.

### GECENİN DÖRDÜNCÜ AYNI HATASI

Muhafızın ilk deseni `fail "dotnet publish başarısız oldu."` satırını —
bir HATA MESAJINI — çağrı sandı. Aynı ayrımı bu gece dört kez yapmak
zorunda kaldım:

| sonda | bahsi çağrı sandığı yer |
|---|---|
| `AnonimUcCirasiTests` | `UcKapisiDenetimi` — niteliği adıyla anlatıyor |
| fiş tipi yapısal sondası | `hesap-planı` Borç/Alacak — başka enum |
| `PkillYasagiTests` | `pkill-kancasi.sh` — yasağı adıyla anlatıyor |
| `PaylasilanDerleyiciKapaliTests` | `fail "..."` — hata mesajı |

İkinci bir kusur daha: devam satırı desenim yanlıştı — devamı "içinde
`\` olan satır" diye arıyordu, oysa devam ÖNCEKİ satırın `\` ile
bitmesiyle belirlenir ve **son devam satırında ters eğik çizgi yoktur**;
bayrak tam oradaydı. Düzeltildi ve çok satırlı örnekle ayrıca sınandı.

---

## F) HAKEDİŞ-KESİNTİ/1 — **YALNIZ ÖLÇÜM** (kod yazılmadı, şema değişmedi)

### (1) Kesinti kalemleri nerede tutuluyor

| tablo | ne tutar |
|---|---|
| `progress_payment_deductions` | hakediş başına kesinti SATIRLARI |
| `progress_payment_deduction_rules` | tür başına varsayılan ORAN/kural (`Rate`, `CalculationBase`, `IsAutomatic`) |
| `subcontractor_progress_payment_deductions` | taşeron hakedişinin karşılığı |

Satır alanları: `DeductionType` · `Description` · `Rate` · `BaseAmount`
· `Amount` · `IsManualAmount` · `CumulativeAmount` ·
`CumulativeBaseAmount` · `PreviousAmount` · `AccountingAccountId`.

Yani yapı **kümülatif hakediş mantığına göre kurulmuş** (önceki dönem,
bu dönem, kümülatif ayrı ayrı) ve muhasebe hesabına bağlanabiliyor.

### (2) Kullanıcı nereden giriyor — **GİRİŞ YOLU VAR**

`/hakedis/yeni` ve `/hakedis/[id]/duzenle` →
`components/hakedis/hakedis-editor.tsx`.

Kesinti tablosunun altında **her tür için bir düğme** var:
`+ Kesin teminat`, `+ All-risk sigorta`, `+ Malzeme kesintisi`,
`+ Barter`, `+ Yemek`, `+ Konaklama / kamp`, `+ İSG ceza`,
`+ İSG katılımı`, `+ Diğer kesinti`. Basınca o türde bir satır ekleniyor
ve varsayılan oranı geliyor.

**Avans mahsubu AYRI bir yoldan** giriliyor (`offsets` / `advanceOffsets`),
kesinti satırı olarak değil — açık avans malzemesine bağlanıyor.

### (3) Tür listesi — **VAR**, 10 üye

| # | enum | etiket (ekranda) | varsayılan oran |
|---|---|---|---|
| 0 | `Other` | Diğer kesinti | %0,3 |
| 1 | `PerformanceBond` | Kesin teminat | %5 |
| 2 | `AllRiskInsurance` | All-risk sigorta | %0,5 |
| 3 | `MaterialDeduction` | Malzeme kesintisi | %10 |
| 4 | `Barter` | Barter | %40 |
| 5 | `Meal` | Yemek | alt kalemli (kahvaltı/öğlen/akşam/kumanya) |
| 6 | `Accommodation` | Konaklama / kamp | alt kalemli (yatılı/evci) |
| 7 | `OhsPenalty` | İSG ceza | alt kalemli |
| 8 | `OhsContribution` | İSG katılımı | alt kalemli |
| 9 | `AdvanceOffset` | *(ekranda düğmesi yok — ayrı yoldan)* | — |

Ekranda **9 düğme**, enum'da **10 üye**; fark `AdvanceOffset` ve bu
kasıtlı görünüyor.

### CANLIDA HENÜZ KULLANILMAMIŞ

```
progress_payment_deductions ....... 0 satır
progress_payment_deduction_rules .. 0 satır
progress_payments ................. 1 satır
```

Yani mekanizma var, **hiç kesinti girilmemiş** ve **hiç kural
tanımlanmamış** — varsayılan oranlar şu an yalnız koddaki sabitlerden
geliyor.

### ÖNERİ LİSTESİ — Mehmet Bey'in listesiyle karşılaştırma

| Mehmet Bey'in maddesi | sistemde |
|---|---|
| stopaj | **YOK** |
| teminat / kesin teminat | VAR (`PerformanceBond`) |
| avans mahsubu | VAR (ayrı yoldan, `AdvanceOffset`) |
| malzeme mahsubu | VAR (`MaterialDeduction`) |
| ceza | VAR (`OhsPenalty` — ama YALNIZ İSG cezası) |
| KDV tevkifatı | **YOK** |
| diğer | VAR (`Other`) |

**İKİ EKSİK: stopaj ve KDV tevkifatı.** İkisi de mevzuat kaynaklı ve
ikisi de ORANI mevzuatla belirlenen kalemler — "diğer"e sıkıştırmak
hakedişte yanlış hesaba düşmelerine yol açar (`AccountingAccountId`
alanı tam da bunun için var).

Ayrıca **`OhsPenalty` yalnız İSG cezası**; sözleşme gecikme cezası
(likidite/gecikme tazminatı) için ayrı bir tür yok.

**KARAR SABAH MEHMET BEY'DE. Gece şema değiştirilmedi, kod yazılmadı.**

---

## F KARARLARI — mali müşavir cevaplarıyla (2026-09-17)

| tür | karar | hesap | not |
|---|---|---|---|
| **Damga vergisi** | EKLENECEK | **193** | oran KOD SABİTİ DEĞİL — sözleşme bazlı kural satırından |
| **Gecikme cezası (sözleşme)** | EKLENECEK | **689** | `OhsPenalty`den AYRI tür |
| **Stopaj** | **BEKLEMEDE** | — | müşavir cevapsız bıraktı (1. maddede KDV tevkifatını yazmış), tekrar soruldu |
| **KDV tevkifatı** | **EKLENMEYECEK** | — | kesinti satırı olmayacak; aşağıda |
| Teminat kesintisi | mevcut, teyit bekliyor | 193 mü 126/226 mı? | iade **kesin kabulde**; hesap sabitlenmeyecek |

**KDV tevkifatı neden kesinti satırı olmayacak:** müşavir 4/10 oranını
**391 tevkifatlı satış KDV** hesabında izliyor — bu fatura/KDV katmanı,
ödemenin net tutarını düşüren bir kesinti değil. Kesinti tablosuna satır
yazmak **çifte düşüm** olurdu. Hakediş ekranında bilgi amaçlı
gösterilebilir.

**Damga vergisi ve gecikme cezası sabah onayıyla girecek — gece kod
yazılmadı, şema değişmedi.**

### "Diğer %0,3" — ÖLÇÜLDÜ, GEREKÇE YOK, KALDIRILDI

Dört yerde arandı:

| nerede | sonuç |
|---|---|
| satırın yanında yorum | yok |
| arka uçta karşılığı | **yok** (sunucuda böyle bir varsayılan hiç tanımlı değil) |
| belgeler | yok |
| doğuran commit `7119732e` (04.08.2026) | mesajında **hiç geçmiyor** |

Pozitif kontrol: aynı arama `defaultRate: 5`i buldu, yani alet çalışıyor.

**Ayırıcı kanıt enum'un kendisi:** `HakedisDeductionType` XML belgesi
`PerformanceBond` için "(%5)", `AllRiskInsurance` için "(%0,5)",
`MaterialDeduction` için "(%10)" yazıyor — `Other` için yalnız
"Serbest kalem" diyor, **oran yok**.

Müşavir de tanımadı. → `defaultRate: 0.3` → **0**. Muhafız:
`tests/kesinti-varsayilan-oran.test.ts`.

### MUHAFIZ İLK KOŞUSUNDA İKİNCİ BİR ORAN BULDU: **Barter %40**

Ve bu **%0,3'ten daha ağır**: orada belge SESSİZDİ, burada belge
**ÇELİŞİYOR**. Enum'un XML özeti aynen şöyle diyor:

> *Barter — hakedişin mal/hizmet olarak ödenecek kısmı. **Şantiye
> bazında değişken oranlı.***

Ekran ise %40'ı sabitliyor. **Kod, kendi belgesinin aksini yapıyor.**

KALDIRILMADI — o günkü karar yalnız "Diğer"i kapsıyordu. Bulgu testin
içinde, adıyla, gerekçeli istisna olarak duruyor; liste büyüyemez
(ölü istisna kapısı da var). **Karar sabah.**

---

## FATURA-TEVKİFAT/1 — **YALNIZ ÖLÇÜM** (kod yazılmadı)

### (1) Alış faturasında tevkifat bayrağı/oranı var mı

**TUTAR var, ORAN ve BAYRAK yok.**

`supplier_invoices`: `Subtotal` · `VatTotal` · `GrandTotal` ·
**`WithholdingAmount`**. Satırlarda (`supplier_invoice_items`):
`VatRate` · `VatAmount`.

Yani tevkifat **başlık düzeyinde tek bir TUTAR** olarak duruyor.
Müşavirin tarif ettiği "alınan hizmete göre değişen oran (nakliye 2/10,
demir çelik 5/10)" **modellenmemiş**: ne fatura başına oran alanı var,
ne hizmet türü–oran tablosu, ne de tevkifatlı/değil bayrağı.

**GİRİŞ YOLU YOK.** `WithholdingAmount` yalnız **e-Fatura içe
aktarımından** doluyor (`EInvoiceImportService.cs:393,463`). Ön yüzde
`satin-alma` altında tevkifat girişi bulunamadı.

**Canlı veri: 14 alış faturası, tevkifatlı olan 0.**

### (2) Tevkifat varken KDV nasıl hesaplanıyor

Muhasebe tarafı **mevcut ve çift taraflı doğru**
(`AccountingIntegrationService`):

```
tevkifat > VatTotal ise HATA (fail-closed)
indirilecek KDV = VatTotal - tevkifat   -> 191 (borç)
tevkifat kısmı                          -> 191.05 sorumlu sıfatıyla beyan (borç)
                                        -> 360.002 sorumlu sıfatıyla ödenecek (alacak)
```

Hesaplar yapılandırılmamışsa **fişi üretmiyor, hata veriyor** — sessiz
geçmiyor.

### (3) 191 eşlemesi nerede

`AccountingIntegrationService.cs:205-211`:
`VatInAccountId` → **`191.01.03` / `191`** · `ReverseChargeVatInputAccountId`
→ **`191.05`** · ayrıca `ReverseChargeVatPayableAccountId` → **`360.002`**.

### ⚠ MÜŞAVİRİN TARİFİYLE BİR FARK VAR

Müşavir *"hepsi 191'de tek kodda izlenebiliyor"* dedi. Sistem ise
**ayırıyor**: indirilebilir kısım `191.01.03`, tevkifatlı kısım
`191.05`, karşılığı `360.002`. Sistemin yaptığı muhasebe olarak daha
ayrıntılı — ama **müşavirin beklediği düzen bu değil**. Tasarım
konuşmasından önce bu farkın kapatılması gerekiyor: ya sistem tek koda
iner, ya müşavir 191.05'i kabul eder.

**Tasarım ölçümden sonra konuşulacak. Gece kod yazılmadı.**

---

## E1 AÇILIŞ STOKU — MÜŞAVİR KURALI (kayda geçti, BAŞLATILMADI)

| konu | kural |
|---|---|
| tarih | **fiziki sayım tarihine** açılış fişi |
| değerleme | **son alış fiyatı** üzerinden |
| fark — noksan | **197 Sayım Tesellüm Noksanı** |
| fark — fazla | **397 Sayım Tesellüm Fazlası** |

**ENGEL KALKTI ama SIRA GELMEDİ.** Açılış girişi **malzeme kartları
açılmadan başlatılmayacak** (Mehmet Bey, 2026-09-17).

---

## BELLEK — İKİ AYRI MADDE (2026-09-17)

> Bunlar **tek madde değil**. Birleştirilirse ileride biri "derleyici
> sorunu çözüldü" diye okur ve yanlış yerde arar (Mehmet Bey).

### ③ KALINTI — **KAPANDI, ÖLÇÜLDÜ**

Sorun: `dotnet` arkada bir derleyici sunucusu **bırakıyordu** ve o süreç
koşular arasında **yaşamaya devam ediyordu**; biri 5,5 GB'a ulaşmıştı.

Ölçüm (her kolda gerçek derleme):

| kol | bayrak | süre | **kalan** süreç |
|---|---|---|---|
| A | yok | 268 sn | **1** |
| B | `UseSharedCompilation=false` | 249 sn | **0** |
| C | `nodeReuse=false` | 252 sn | **1** |
| D | ikisi | 270 sn | **0** |

**Çözüm B.** Şu an `deploy/park/`de park; doğru katmandan
(`derleme-kos.sh` sarmalayıcısı) yeniden yapılacak.

### ⚠ YENİ — **TEPE KULLANIM: AÇIK**

Sorun **başka**: derlemenin **kendi tepe kullanımı**, bayraktan bağımsız.

| ölçüm | değer |
|---|---|
| `csc.dll` tek başına | **3,86 GB** |
| aynı koşuda `csc` + `VBCSCompiler` birlikte | **3,09 + 2,41 = 5,5 GB** |
| o anda kullanılabilir bellek | **475 MB** |
| takas | 4 GB'ın **3,9'u dolu** |

**Bayrak bunu çözmüyor.** ③ "kalıntıyı" kaldırıyor, tepe aynı kalıyor —
hatta paylaşılan sunucu AÇIKKEN iki süreç birden ayakta oluyor.

Bu, 17 Eylül'de iki test koşusunun **SIGTERM (143)** almasının sebebi.

**DENENECEKLER (ölçülerek, hiçbiri yapılmadı):**

1. Paralel derlemeyi kısmak (`-m:1` / `BuildInParallel=false`) — tepe
   düşer mi, süre ne kadar uzar?
2. Paylaşılan sunucu kapalıyken bellek davranışı — B kolunda tepe
   ölçülmedi, yalnız kalıntı ölçüldü. **Ölçüm eksiği bende.**
3. VSCode sunucusunun koşu sırasında kapatılması — ölçüldü: **750 MB**
   (`server/node` 499 MB + `csdevkit` 259 MB).

### Bu gece yapılan iki tedbir

**1. OOM önceliği.** Ölçüldü (değiştirmeden önce): canlı birimler
**zaten korunuyordu** — `OOMScoreAdjust=-500`, hem birimde hem çalışan
süreçte, 26 Ağustos'tan beri. Eksik olan **diğer yarısıydı**: dağıtım
koşusu `0` ile koşuyordu. `safe-deploy.sh` artık **kendi skorunu 700'e
çekiyor** (alt süreçler devralır) ve yazamazsa uyarıyor.

**2. Bellek kapısı.** `safe-deploy.sh` kapsam kapısından hemen sonra,
pahalı turlardan önce kullanılabilir belleği ölçüyor; eşiğin altındaysa
**başlamıyor** ve ne yapılacağını söylüyor.

Eşik **2048 MB** seçildi. İki uçtan:

| gözlem | kullanılabilir |
|---|---|
| **düşen** koşular | ~1,2 GB |
| **geçen** dağıtımlar | 3.471 MB ve 5.192 MB |

Arada geniş boşluk var; eşik düşen tarafa yakın ama ondan belirgin
yukarıya kondu. **Bu bir tahmindir ve öyle etiketlendi** — elde iki
geçen, bir düşen koşu var. Sayı tek yerde (`BELLEK_ESIGI_MB`), veri
geldikçe düzeltmek tek satır.

Kapı çağrılarak sınandı: eşik 99999 → **dağıtım başlamaz**; ve şu anki
gerçek durumda (475 MB) kapı **haklı olarak kırmızı** — bu, kapının
canlı kanıtı.

---

## A DAĞITIMI — GERİ DÖNÜŞ YOLU (koşudan ÖNCE yazıldı, 2026-09-17)

> *"Sonra öğrenilen geri dönüş yolu, geri dönüş yolu değildir."*

### Komut

```
deploy/scripts/geri-al.sh --prova     # kuru koşum, dokunmaz
deploy/scripts/geri-al.sh --uygula    # gerçek geri dönüş
```

### Prova KOŞULDU (varsayılmadı) — çıktısı

```
arka uç yedeği HAZIR : 75M, dll 2026-09-15 18:25:12
ön yüz yedeği HAZIR  : 88M, BUILD_ID GXGK0CQakTv-DpyrGQjkp
şu anki arka uç      : dll 2026-09-15 22:45:04
şu anki ön yüz       : BUILD_ID MnWyVOWdp30tDYn5LhXoF
çalışan sürüm        : 1.0.0+8abeb57c
```

Yapacakları: `publish ← publish-rollback` · `.next ←
frontend-next-rollback` · iki servisi restart · healthcheck.

### Dizin uyuşmazlığı ARANDI, YOK

`safe-deploy` günlüğü "eski yayın `publish-eski`de bekliyor" diyor,
`geri-al.sh` ise `publish-rollback` okuyor — **farklı dizinler**, bu
yüzden kontrol edildi:

`safe-deploy.sh:662-664` takastan ÖNCE `publish-rollback`i `publish`ten
tazeliyor. `publish-eski` atomik takasın yer değiştirmiş eski dizini,
geri dönüş kaynağı değil. **İkisi uyuşuyor.**

### Süre

Ölçülen bileşenler: **75 MB + 88 MB kopyalama**, iki servis restart,
healthcheck (en çok 30 sn).

**DÜRÜST SINIR: gerçek bir geri dönüş süresi ÖLÇÜLMEDİ.** Bileşenlerden
tahmin ~1-2 dakika, kesinti dağıtım takasıyla benzer mertebede (ölçülen
9 sn). Bu bir tahmindir; ilk gerçek geri dönüşte ölçülecek.

### Veritabanına dokunur mu — **HAYIR**

`geri-al.sh` göç geri almıyor ve bunu açıkça yazıyor
(*"GÖÇ GERİ ALINMAZ"*).

Ve bu akşam **zaten şema değişikliği yok** — ölçüldü:

```
paket        : 25 commit
göç dosyası  : 0
şema/sql     : 0
```

Yani geri dönüş **yalnız derlenmiş çıktıyı** değiştirir.

### Uçuş öncesi

`kullanılabilir bellek 6.089 MB` (eşik 2.048) → bellek kapısı **geçer**.
