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
