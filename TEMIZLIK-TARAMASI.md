# Temizlik taraması — birikmiş adaylar

Acil olmayan, ama bir sonraki temizlik turunda tek seferde ele
alınacak kalemler. Buraya bir şey yazmak, o turda mutlaka
değiştirileceği anlamına gelmiyor; **değerlendirilecek** demek.
Her kalem: ne, neden riskli, o turda ne düşünülecek.

## HrPayrollRecord.ActualPayableAmount — fazlalık + yanıltıcı ad

**Ne:** `Models/HumanResources/HrApprovalModels.cs`. Hesaplama
sırasında (`HrApprovalService.cs:1285`) doğrudan
`OfficialNetPayableAmount` değerine eşitleniyor; hemen ardından
`NetPayableAmount` de ona eşitleniyor. Yani üç alan aynı sayıyı
taşıyor.

**Neden riskli:** Adı "fiili ödenecek tutar" izlenimi veriyor, ama
elden ödemeyi İÇERMİYOR. Nakit akış projeksiyonu yazılırken tam bu
tuzağa düşülmesi an meselesiydi: "fiili" diye okunup bordro çıkışı
elden hariç, olduğundan küçük hesaplanacaktı. Şu an alanı yanlış
okuyan bir tüketici YOK (2026-08-10 taraması) — risk tamamen isimde.
Ele geçen tutar için doğru kaynak `SalaryTakeHomeService.TotalTakeHome`
(resmî net + manuel elden + mesai eldeni).

**O turda değerlendirilecek:** ya `OfficialNetEquivalent` olarak
yeniden adlandırmak, ya da alanı tamamen kaldırıp tüketicileri
`OfficialNetPayableAmount`'a bağlamak. İkisi de migration + sözleşme
(`HrApprovalContracts.cs:108`) + frontend dokunuşu istiyor, bu yüzden
tek başına değil temizlik turunda.

**Şimdilik:** XML doc yorumuyla tuzak yazıldı, kod değişmedi.

## "Uç var, ekran yok" taraması — BETİK KURULDU, KARARLAR BEKLİYOR

**Ne:** Backend'de yazılıp frontend'den hiç çağrılmayan uçlar. Bu
oturumda üç tanesi tek tek, tesadüfen çıktı: özlük belge uçları, çek
düzenleme, görev iptali. Üçü de backend'de hazırdı ama kullanıcı
hiçbirine ulaşamıyordu.

**Neden riskli:** Bir uç ekrandan çağrılmıyorsa iş bitmiş sayılmıyor
ama bitmiş görünüyor — paket "kapandı" diye kapanıyor, özellik
kullanılamıyor. Tek tek fark edilmesi şansa kalıyor.

**Yapıldı (2026-08-12):** `scripts/uc-ekran-taramasi.mjs`.
`node scripts/uc-ekran-taramasi.mjs [--json]` ile her an
tekrarlanabilir. 701 ucun tamamını üç kademede raporluyor:

| Kademe | Sayı | Anlamı |
| --- | --- | --- |
| Kesin | 18 | Hiçbir frontend referansıyla eşleşmiyor |
| Şüpheli | 36 | Yalnız değişken yollu bir referansla eşleşiyor |
| Metot | 14 | Yol çağrılıyor ama o HTTP metodu çağrılmıyor |

**Betiği yazarken çıkan ve rapora hâlâ etki eden tuzaklar** — biri
düzeltilmeseydi liste ya yanlış alarmla dolar ya da gerçek boşlukları
gizlerdi:
- Metot düzeyi `[Route("/api/...")]` sınıfın taban yolunu EZER;
  birleştirilince olmayan bir yol üretiliyordu.
- Servislerin `const root = "..."` deseni ve kök üreten fonksiyonlar
  çözülmezse yol segment sayısını tutmuyor (47 sahte kalem).
- Uzun çağrılarda şablon dizgisi satır ortasında bölünüyor; satır
  bazlı tarama böyle bir çağrıyı hiç görmüyor.
- `${query({ companyId })}` iç içe süslü parantez içeriyor; naif
  temizlik segmenti bozup `isg/dashboard`'ı listeye düşürüyordu.
- Yıldızın gerçek segmentleri karşılaması hiç çağrılmayan
  `hakedis/upload`'ı örtüyordu — bu yüzden kesin/şüpheli ayrımı var.
- Metot, çağrının parantez aralığından okunmalı; satır penceresi bir
  sonraki çağrının metodunu yapıştırıyordu. Yükleme yardımcıları
  metodu kendi gövdesinde kuruyor, o da ayrıca çözülüyor.

**Kararlar (2026-08-12):**

| Uç | Karar |
| --- | --- |
| `GET api/hr/bordro-on-kontrol` | Ekran açıldı (`21d8ea01`) |
| `GET api/hr/sgk-bildirim` | Ekran açıldı (`21d8ea01`) |
| `POST api/bildirimler/tara` | **KAPANDI (iç uç)** — ekran gerekmez |
| `GET api/hr/izin-bakiye` | Ekran açıldı (`3fbac3b8`) |
| `GET/PUT api/hakedis-deduction-accounts` | Ekran açıldı (`b83e6962`) |
| `api/project-extra-works` devri | Ekran açıldı (`d6ab3c21`) |
| `api/secretariat/correspondence` akış+ek | Ekran açıldı (`6ba757b7`) |
| Orta grup (6 uç) | Mevcut ekranlara kart/sekme olarak ekleniyor |

**`POST api/bildirimler/tara` neden iç uç:** bildirim taramasını elle
tetikler. `NotificationScanBackgroundService` taramayı 24 saatte bir
zaten koşturuyor; uç yalnızca hata ayıklama ve ops içindir. Kullanıcı
akışında karşılığı yok, o yüzden ekranı olmaması bir eksiklik değil.
Bu satır kalsın ki bir sonraki taramada yeniden "bulgu" sayılmasın.

**Kesin listedeki 18 kalem gözle doğrulandı, hepsi gerçekti:**
İK bordro ön kontrol ve SGK bildirim, izin bakiyesi, üretici fiyat
listesi (liste + oluşturma), hakediş kesinti önerisi, teklif
fiyatlama, ek iş devri, sekreterya evrak akışı ve ekleri, güvenlik
denetim kayıtları, tedarikçi kalite, bildirim taraması (bu sonuncusu
muhtemelen bilinçli bir ops ucu).

## Hayalet alan taraması (2. tur)

**Ne:** "Okunuyor/toplanıyor ama hiç yazılmıyor" alanlar ve yanıltıcı
adlı alanlar. Birinci kalem yukarıdaki `ActualPayableAmount`.

**Neden riskli:** İki yönü de sessiz. Yazılmayan bir alan raporlarda
hep sıfır/boş çıkar ve kimse fark etmez; yanıltıcı adlı bir alan ise
adına güvenilerek okunur ve yanlış sayı üretir. İkisi de test
patlatmaz.

**O turda değerlendirilecek:** Modellerdeki alanları set edildikleri
yerlerle karşılaştırıp hiç yazılmayanları çıkarmak; adı içeriğini
yanlış anlatanları (fiili/gerçek/toplam gibi sözler taşıyıp
kapsamı dar olanlar) ayrı listelemek. Her kalem için: doldur,
yeniden adlandır, ya da kaldır.

## ~~Frontend test altyapısı — Vitest + Testing Library~~ (KAPANDI)

**Kapanış:** 2026-08-11. Vitest 3 + @testing-library/react +
user-event + jsdom kuruldu (`vitest.config.mts`, `tests/setup.ts`),
`npm test` scripti açıldı ve **safe-deploy'a kapı olarak bağlandı** —
kırmızı frontend testi yayını durduruyor (kasten düşen bir testle
doğrulandı).

İlk testler en yüksek riskten seçildi:
- Modal (8 test): Esc kapatıyor, `busy` iken kapatmıyor, zemin
  tıklaması kapatıyor ama panel içi kapatmıyor, Tab odağı panelde
  döndürüyor, kapanınca odak çağıran düğmeye dönüyor, aria bağları,
  gövde kaydırma kilidi.
- ConfirmDialog (8 test): gerekçe boşken onay kapalı, yalnız boşluk
  gerekçe sayılmıyor, kırpılmış metin gönderiliyor, yazılan
  kaybolmuyor, `busy` iken çift gönderim engelli, sunucu hatası
  diyalogda kalıyor.
- Tutar maskelemesi (3 test): yetki yokken saat görünüyor, hiçbir
  tutar ÇİZİLMİYOR ve ekranda para biçimli metin kalmıyor.

**Kurulumda çıkan iki tuzak, notta kalsın:**
- jsdom düzen hesaplamıyor, `offsetParent` hep null. Modal'ın odak
  tuzağı görünürlüğü bununla süzdüğü için test, bileşen doğru
  çalışırken düşüyordu. Bileşeni test ortamına uydurmak yerine
  ORTAMIN eksiği `tests/setup.ts` içinde tamamlandı.
- İki ayrı vite kopyası (kökte 8.x, vitest içinde 7.x) `tsc`'de tip
  çakışması üretiyordu. Kastla örtmek yerine `vite@^7` doğrudan
  bağımlılık yapılıp sürümler hizalandı.

**Sırada (organik büyüsün):** ekran ekran test yazmak yerine, bir
regresyon her yakalandığında o davranışın testini eklemek.

## Giriş arızası teşhizinde çıkan üç yan bulgu (2026-08-12)

Token/çerez arızası kapatıldı (`635f43ba`, `e77a4eeb`). Ararken
çıkan, bugünkü sorunla ilgisi olmayan ama gerçek olan üç kalem:

**1. Denetim izi istemci IP'sini kaybediyor.**
`app/api/backend/[...path]/route.ts` backend'e giderken sıfırdan bir
`Headers()` kuruyor ve `X-Forwarded-For`'u iletmiyor. Sonuç: login
DIŞINDAKİ her işlem `security_audit_events` tablosuna `127.0.0.1`
olarak yazılıyor. Canlıda doğrulandı — tablodaki tüm "Updated"
kayıtları 127.0.0.1. Login route'u (`app/api/auth/login/route.ts`)
başlığı doğru iletiyor, sorun yalnız genel proxy'de. Güvenlik
denetiminin değerini büyük ölçüde düşürüyor.

**2. Service worker oturum açmış sayfaları önbelleğe yazıyor.**
`public/sw.js` ağ-öncelikli ve bilinçli olarak agresif değil, ama
başarılı HER GET yanıtını `caches.put` ile saklıyor — yetkiye bağlı
sayfa gövdeleri dahil. Aynı cihazı paylaşan ikinci bir kullanıcı
çevrimdışıyken bunlara ulaşabilir. Değerlendirilecek: yalnız statik
varlıkları önbelleğe almak, gezinme yanıtlarını dışarıda bırakmak.

**3. HTTPS'siz vhost'ta giriş yapısal olarak imkânsız.**
`/etc/nginx/sites-enabled/enderun-ai` yalnız `listen 80` ile
`enderun-ai.com` ve `www.enderun-ai.com`'u servis ediyor. Çerez
`secure` işaretli olduğu için düz HTTP'de tarayıcı onu ASLA
saklamaz; o adresten giren herkes bugünkü belirtiyi yaşar — giriş
200 döner, oturum açılmaz, hiçbir hata görünmez. Değerlendirilecek:
ya 301 ile HTTPS'e yönlendirmek ya da vhost'u kaldırmak.

**Ayrıca:** `frontend/enderun-ai/.next/dev` 1 Ağustos'tan kalma bir
dev artığı; `tsc --noEmit` çalıştırınca artık var olmayan sayfalar
için dört sahte hata üretiyor. Yayını etkilemiyor, ama tip
kontrolünü kirletiyor.

## `react-hooks/set-state-in-effect` — dört dosyada duran lint hatası

**Ne:** Şu dört yerde eslint hata veriyor ve hepsi HEAD'de zaten
vardı (yeni ekranlar eklenirken `git stash` ile tek tek doğrulandı):

| Dosya | Ne yapıyor |
| --- | --- |
| `components/erp/erp-shell.tsx` | Menü grubu açılma durumunu effect içinde kuruyor |
| `app/projeler/[id]/metraj-takip/page.tsx` | Açılışta veriyi effect içinde yüklüyor |
| `app/sekreterya/evrak/page.tsx` | Üç ayrı effect: yükleme + şirket değişince proje seçimini sıfırlama |

**Neden riskli — ve neden hemen düzeltilmedi:** üçü de DAVRANIŞ
TAŞIYOR. `erp-shell` menünün hangi grubunun açık olduğunu, `evrak`
şirket değiştiğinde seçili projeyi temizlemeyi bu effect'lerle
yapıyor. Kuralı susturmak kolay ama davranışı bozmadan yeniden
yazmak dikkat istiyor; ekran eklerken yol üstünde düzeltilecek bir
şey değil. Bu yüzden dokunulmadı.

**Yayını durdurmuyor:** `npm run build` bu kuralı hata saymıyor, o
yüzden safe-deploy geçiyor. Yani acil değil ama biriktiği için
`npx eslint` çıktısı gürültülü — gerçek yeni hatalar bunların
arasında kaybolabilir. Asıl risk bu.

**O turda değerlendirilecek:** her effect için ya türetilmiş
duruma çevirmek (`useMemo`/render sırasında hesaplama), ya olayı
tetikleyen yere taşımak (şirket seçimi `onChange`'i projeyi orada
sıfırlasın), ya da gerekçeli `eslint-disable` ile bilinçli olduğunu
kayda geçirmek. Üçü de tek tek karar ister; toplu bir düzeltme
davranış değiştirir.

## Paylaşılan dosya-ek altyapısı

**Ne:** Bugün üç ayrı yerde "belgeyi ekle" ihtiyacı var ve hiçbirinde
dosya eki yok:
- özlük belgeleri (İK),
- günlük saha raporu fotoğrafı,
- gider fişi/faturası (Gider Merkezi — belge türü ve numarası
  tutuluyor, dosya tutulmuyor).

Var olan tek yükleme yolu proje dosyaları (`ProjectDocument`, diske
yazıyor) ve bu yol projeye bağlı; şantiye fotoğrafı ya da bir gider
fişi oraya iliştirilemiyor.

**Neden riskli:** Her modül kendi yükleme ucunu yazarsa boyut sınırı,
izin kontrolü, virüs/uzantı denetimi, saklama yolu ve silme davranışı
üç kez ayrı ayrı kurulur — biri eksik kalır ve fark edilmez. Yükleme
uçları güvenlik açısından en pahalı yerdir; üç kopya, üç ayrı risk
demektir.

**O turda değerlendirilecek:** tek bir `Attachment` varlığı +
sahiplik alanı (modül + kayıt kimliği) + ortak yükleme/indirme ucu;
izin kontrolü sahibinden türesin (gider fişi `expense.view`, özlük
belgesi `personnel.view` gibi). Elden/maskeli kayıtların eki de aynı
maskeye tabi olmalı — bir gider fişinin fotoğrafı, giderin kendisi
gizliyken görünmemeli.


## project_boq_items birim fiyat kolonları — ölçeksiz numeric

**Ne:** `project_boq_items` tablosunda `MaterialUnitPrice`,
`LaborUnitPrice` ve `OverheadUnitPrice` kolonları veritabanında
**ölçek belirtilmeden** `numeric` olarak duruyor. Aynı tablodaki
`UnitPrice` ise `numeric(_,6)`; yani dört birim fiyat kolonundan
üçünün tanımlı bir ondalık ölçeği yok.

**Neden riskli:** Depolama ile gösterim arasında sessiz bir uyuşmazlık
riski. Ölçeksiz `numeric` pratikte sınırsız ondalık kabul ediyor;
gösterim tarafında ise `unitPrice` en çok altı hane basıyor (A4/17'de
dörtten altıya çıkarıldı, gerekçesi: gösterim ölçeği kolonun DB
ölçeğini karşılamalı). Bir gün yedi ya da daha fazla ondalıklı bir
değer yazılırsa ekran onu sessizce kırpar — ve bu rakam sözleşmeye
giriyor. Ayrıca ölçeksiz kolon, aynı anlamı taşıyan `UnitPrice` ile
farklı yuvarlama davranışı gösterebiliyor: dört kolon aynı satırda
toplanırken üçü serbest, biri altı haneye yuvarlı.

Bugün canlıda etkilenen kayıt yok (`project_boq_items` tek satır,
2026-08-15 taraması) — kusur bu yüzden görünmüyor, yok olduğu için
değil.

**O turda değerlendirilecek:** üç kolona da tanımlı ölçek vermek ve
`UnitPrice` ile hizalamak (`numeric(18,6)`). Migration gerektiriyor;
mevcut değerlerin altı haneye sığdığı önce doğrulanmalı, sığmayan
varsa yuvarlama kararı ayrıca alınmalı. Aynı taramada
`ContractQuantity` (4) ile birim fiyat ölçeklerinin çarpımının
`TotalAmount` (2) ile tutarlılığı da gözden geçirilsin.

**Şimdilik:** yalnızca kaydedildi, kod ve şema değişmedi. Gösterim
tarafı A4/17'de `unitPrice`e bağlandı, yani bugünkü veriyle doğru
basıyor.

## Alınan çek satış faturasına bağlanamıyor

**Ne:** `Cheque` modelinde `SupplierInvoiceId` ve `ProgressPaymentId`
var, `SalesInvoiceId` YOK. Yani müşteriden satış faturası karşılığı
alınan bir çek, o faturaya iliştirilemiyor.

**Neden riskli:** Nakit akışında satış faturası alacağı, çek tahsil
edilip kasaya girene kadar AÇIK görünmeye devam ediyor. Tedarikçi
tarafında bu bağ var ve kullanılıyor (`GetSupplierInvoiceItemsAsync`
çek karşılığını bakiyeden düşüyor); satış tarafında simetri yok. Aynı
alacak hem çek portföyünde hem açık fatura bakiyesinde durduğu için
beklenen tahsilat **iki kez** görünebiliyor.

Perakende paketinde bu eksiği uydurma bir bağla kapatmak yerine
olduğu gibi bıraktım: yanlış eşleşen bir çek alacağı olduğundan erken
kapatır ve nakit akışını olduğundan iyi gösterirdi — bu, olduğundan
kötü göstermekten daha tehlikeli.

**O turda değerlendirilecek:** `Cheque`e `SalesInvoiceId` eklemek ve
`GetSalesInvoiceItemsAsync` içindeki çek düşümünü açmak; çek girişi
ekranında satış faturası seçimi. Migration + ekran dokunuşu istiyor.

**Şimdilik:** yalnızca kaydedildi. `CashFlowService` içinde eksiğin
neden bilinçli bırakıldığı yorumda yazılı.

## Poz kütüphanesi birim yazımları tekilleştirilmeli

**Ne:** `engineering_positions` tablosunda adet birimi iki ayrı yazımla
duruyor: "Ad" (7.429 kayıt) ve "AD" (7.199 kayıt) — toplam 14.628
kayıt, kütüphanenin yaklaşık %62'si. Aynı birim, iki yazım. Diğer
birimlerde de benzer dağılım olabilir (m/MT, m²/m2), taranmadı.

**Neden riskli:** Kaynak veride aynı şeyin iki yazımı olması, o veriyi
okuyan her yerin kendi eşleştirme kuralını yazmasına yol açıyor. Bugün
`UnitNormalizer` bunu maskeliyor ve reçete aktarımı doğru çalışıyor;
ama normalizasyon bir ÇÖZÜM DEĞİL, bir YAMA. Sözlüğe girmeyen yeni bir
yazım çıktığında (ya da normalizasyondan geçmeyen yeni bir tüketici
eklendiğinde) aynı sorun sessizce geri gelir.

Ayrıca raporlama tarafında gruplama yapan her sorgu "Ad" ve "AD"yi iki
ayrı grup sayar — birim bazlı bir özet istendiğinde sayılar bölünür.

**O turda değerlendirilecek:** kaynak veriyi tekilleştirmek — her birim
için tek yazım seçip `engineering_positions` üzerinde tek seferlik
güncelleme. Öncesinde tam bir birim envanteri çıkarılmalı (yalnız adet
değil, m/m²/m³/kg/saat yazımları da). Güncelleme geri alınabilir olsun
diye eski değer bir denetim kaydına yazılmalı ya da migration Down'ı
yazılabilir olmalı.

`UnitNormalizer` kaynak temizlense bile KALMALI: dışarıdan gelen Excel
dosyaları her zaman serbest yazım taşıyacak, normalizasyon o sınırda
gerekli. Kaldırılacak olan şey kütüphanenin kendi içindeki tutarsızlık.

**Şimdilik:** yalnızca kaydedildi. Normalizasyon canlıda çalışıyor ve
reçete aktarımı bu yazımlar yüzünden satır kaybetmiyor.

## Yıkıcı işlemler zayıf yetkiyle korunuyor — konsolide liste

**Ne:** Defterde kalıcı iz bırakan işlemler (iptal, geri alma, pasife
alma, red) çoğu yerde `edit` ya da `create` yetkisiyle korunuyor;
`delete` yalnız gerçek silme uçlarında aranıyor. R2 taramasında
**27 yıkıcı uçtan 16'sı zayıf yetkili** çıktı.

Muhasebe tarafında desen DOĞRU kurulmuş (fiş iptali → accounting.delete,
hesap pasife alma → accounting.delete); hakediş, satın alma ve İK'da
kurulmamış. Yani tutarsızlık sistematik değil, dağınık.

**Neden riskli:** İptal, kesinleşmiş bir belgeyi ters kayıtla geri
alıyor — muhasebe fişi doğuruyor, stok hareketi yaratıyor, tahsilatı
kapatıyor. Bunu yapabilmek "düzeltme" değil "yıkma" yetkisi; `edit`
bunun için zayıf.

**Zayıf yetkili uçlar (uç + mevcut + olması gereken):**

| Uç | Mevcut | Olması gereken |
|---|---|---|
| `purchase-orders/{id}/cancel` | purchasing-orders.edit | .delete |
| `goods-receipts/{id}/cancel` | purchasing-receipts.edit | .delete |
| `project-measurements/{id}/cancel` | hakedis.edit | .delete |
| `progress-payments/{id}/cancel` | hakedis.edit | .delete |
| `purchase-requests/{id}/cancel` | purchasing-requests.edit | .delete |
| `sales-invoices/{id}/cancel` | accounting.edit | .delete |
| `hr/payroll/records/{id}/cancel` | attendance-payroll.edit | .delete |
| `hr/workforce/{advances,leaves,overtimes}/{id}/reject` | attendance-payroll.edit | ayrı karar |
| `hr/assets/{id}/cancel` | personnel.edit | .delete |
| `tasks/{id}/cancel` | tasks.manage | ayrı karar |
| `accounting/currency-valuation/{id}/reverse` | accounting.manage | .delete |
| `manufacturer-price-lists/{id}/deactivate` | engineering.manage | ayrı karar |

**Bir uç YANLIŞ ALARM, listeye alınmadı:**
`hr/gorevlendirmeler/{id}/iptal` özniteliği `personnel.view` diyor ama
metodun İÇİNDE `CanApproveAsync` kontrolü var ve yalnız Genel Müdür
geçebiliyor. Güvenlik açığı değil. Ancak ARAYÜZ türetmesi için sorun:
öznitelikten türeten bir düğme kapısı, view yetkisi olan herkese
düğmeyi gösterir ve kullanıcı 403 yer. Bu uçta ya öznitelik gerçek
gereksinimi yansıtmalı ya da düğme özel olarak ele alınmalı.

**ETKİ ÖLÇÜMÜ YAPILDI (2026-08-16), daraltma ucuz:**

Rol kataloğunda `edit` olup `delete` olmayan roller:
- purchasing-orders: **0 rol**
- hakedis: **0 rol**
- attendance-payroll: **0 rol**
- purchasing-requests: **0 rol**
- purchasing-receipts: 1 rol (Depo Sorumlusu)
- accounting: 1 rol (Ön Muhasebe)

Canlıdaki 9 aktif kullanıcıda ise **etkilenen kimse yok**: ilgili
izinlere sahip iki kullanıcının (Mehmet Karacabey, Özlem TÜRKMEN)
ikisinde de hem edit hem delete var; Duygu YILDIRICI'da accounting
edit+delete birlikte. Yani bugün daraltma yapılsa **hiçbir kullanıcı
iş yapamaz hale gelmez.**

**YAPILDI (2026-08-16).** Dokuz uç tek seferde daraltıldı:
purchase-orders, goods-receipts, project-measurements,
progress-payments, purchase-requests, sales-invoices,
hr/payroll/records, hr/assets iptalleri Edit -> Delete;
currency-valuation/reverse Manage -> Delete.

Tek seferde yapıldı çünkü uç-uç yapılsaydı yarısı zayıf yarısı güçlü
kalır ve kural öğrenilemezdi.

Arayüz kapıları KENDİLİĞİNDEN takip etti — izin tek kaynaktan geldiği
için ek bir frontend değişikliği gerekmedi. 2237 backend testi geçti.

**HÂLÂ AÇIK — üçü bilinçli bırakıldı:**
  hr/workforce/{advances,leaves,overtimes}/{id}/reject
  tasks/{id}/cancel
  manufacturer-price-lists/{id}/deactivate

RED HER ZAMAN YIKICI SAYILMAYABİLİR: bir izin talebini reddetmek
defterde iz bırakmıyor, yalnız akışı sonlandırıyor. Görev iptali ve
fiyat listesi pasife alma da benzer biçimde tartışmalı. Bunlar ayrı
bir karar.

**AYRICA AÇIK:** `hr/gorevlendirmeler/{id}/iptal` özniteliği
`personnel.view` ama gerçek kontrol metodun içinde (`CanApproveAsync`,
yalnız Genel Müdür). Güvenlik sorunu değil ama ARAYÜZ TÜRETMESİ için
sorun: öznitelikten türeten bir düğme kapısı, view yetkisi olan
herkese düğmeyi gösterir ve kullanıcı 403 yer. Ya öznitelik gerçek
gereksinimi yansıtmalı ya da o düğme özel ele alınmalı (R2/4'te
karşılaşılacak).

---

## Yıkıcı uç taraması YENİLENDİ (2026-08-17) — önceki liste eksikti

Yukarıdaki dokuz-uç listesi, servis katmanını yanlış okuyan bir ölçüm
aracıyla üretildi. İki hata vardı:

1. Araç, bir servis dosyasındaki **ilk** `export const` nesnesini alıyordu.
   `cheque.service.ts` gibi dosyalarda servis nesnesi altıncı export
   olduğu için o dosyanın hiçbir metodu eşleşmedi.
2. Metot gövdesini bulmak için parametre listesinden sonraki ilk `{`
   yerine ilk `{` aranıyordu. `createLoan(payload: { ... })` gibi çok
   satırlı tipli parametrede "gövde" sanılan şey payload TİPİ oldu;
   içinde `method:` bulunmadığı için o metotlar GET sayıldı.

Sonuç: yazan uçların bir kısmı hiç taranmadı. Tarama doğrudan
controller'lardan yeniden yapıldı (frontend'den bağımsız, bu yüzden
servis eşleşmesi sonucu etkilemiyor): **83 yıkıcı uç, 16'sı
delete/manage/approve dışında.**

### Daraltıldı

| Uç | Eski | Yeni | Etki |
|---|---|---|---|
| `DELETE api/tax/payments` | accounting.edit | **accounting.delete** | yalnız "Ön Muhasebe" rolü kaybediyor |

Ödeme kaydını geri almak muhasebede iz bırakıyor; düzenleme değil.
Aynı desendeki dokuz uçla tutarlı hale getirildi.

**Canlı kullanıcı doğrulaması yapılamadı:** bu turda veritabanı erişimi
yoktu. Rol tarafı statik olarak `RoleCatalog`'tan ölçüldü (15 rol, 2'si
tam yetkili). Daha önceki turda 9 aktif kullanıcının hiçbirinde "Ön
Muhasebe" rolü yoktu; o günden beri kullanıcı eklendiyse tek etkilenen
o rol olur. Geri alınması tek satır.

### Daraltılamaz — modülde delete anahtarı YOK

| Uç | Mevcut | Neden |
|---|---|---|
| `DELETE api/cash-flow/tahmini-giderler/{id}` | cashflow.view | modülün TEK anahtarı `cashflow.view`; yazma da okuma da aynı kapıda |
| `DELETE api/company-settings/bank-accounts/{id}` | company-settings.edit | `company-settings.delete` yok |
| `DELETE api/kurumlar-vergisi-oranlari/{id}` | company-settings.edit | aynı |
| `POST api/tasks/{id}/cancel` | tasks.manage | `tasks.delete` yok; modülde hiç ayrım yok |

`cashflow` en dikkat çekeni: **görüntüleme izniyle kayıt silinebiliyor.**
Anahtar ailesinin genişletilmesi gerekiyor (`cashflow.edit`,
`cashflow.delete`); bu bir izin katalogu kararı, tek uçluk değil.

### Yıkıcı sayılmadı — bilinçli

| Uç | Mevcut | Gerekçe |
|---|---|---|
| `PUT hr/personnel/assignments/{id}/close` | personnel.edit | atamayı kapatmak silme değil, `EndDate` yazmak |
| `PUT project-sites/assignments/{id}/close` | personnel.edit | aynı |
| `POST rfq/{id}/close` | purchasing-rfq.edit | RFQ kapatmak normal akış adımı (kazanan seçildikten sonra) |
| `POST api/bildirimler/{id}/kapat` | (izin yok) | metot içinde görünürlük kontrolü var: göremediği bildirimi kapatamıyor |
| `POST api/hizir/actions/{id}/cancel` | ai.use | kullanıcının kendi işlemini iptali |

### Yeni kayıt: kredi durumu

`POST finansal-araclar/krediler/{id}/durum` **finance.edit** istiyor ve
gövdesinde "iptal" durumu da var. Rota adında "cancel/iptal" geçmediği
için yıkıcı taramasına düşmüyor. Durum makinesinin hangi geçişleri
yıkıcı saydığı ayrı bir karar; şu an tek uç bütün geçişleri taşıyor.

---

## Arayüzde üç ayrı yetki mekanizması var (2026-08-17)

R2 yayılırken ortaya çıktı. Aynı işi yapan üç desen:

| Desen | Ekran sayısı | Örnek |
|---|---|---|
| `useModuleActions(modül).can(eylem)` | 34 | R2 ile eklenen |
| `usePermissions()` + satır içi `has("x.y")` | 18 | `finans/gider-merkezi` |
| `hasPermission(session, "x.y")` | 1+ | `satin-alma/butce-onay` |

Üçü de aynı `/auth/me` verisine bakıyor, yani **ikinci bir izin haritası
değil** — davranış farkı yok. Fark denetlenebilirlikte: satır içi
anahtarlar hangi ucun istediğini yazmıyor, o yüzden uçtan ayrışıp
ayrışmadığı gözle görülmüyor.

Ölçüm yapıldı: 18 satır içi ekranın anahtarları uçlarla karşılaştırıldı.
**İki gerçek sapma bulundu, ikisi de düzeltildi:**

- `satin-alma/butce-onay` — bütçe formu `purchasing.approve ||
  finance.approve` ile açılıyordu, uç yalnız `purchasing.approve`
  istiyor. Yalnız finans onayı olan kullanıcı formu dolduruyor, reddi
  KAYDEDERKEN yiyordu.
- `projeler/[id]` — işveren portalı (create/delete/edit) ve işçilik
  kaydı (personnel.create) hiç kapılı değildi; ekran yalnızca
  `expense.manage` kapısını taşıyordu.

Kalan 16 ekranın anahtarları uçlarla eşleşiyor; **çevirme işi
bekliyor, hata beklemiyor.** Çevirmenin kazancı tek: kapı yorumu ucun
adını taşıyor ve gelecekteki sapma testle yakalanabiliyor.

---

## `useCurrentUser` her örnekte ayrı `/auth/me` atıyordu (2026-08-17) — DÜZELTİLDİ

Kanca modül düzeyinde önbellek tutmuyordu; her çağıran kendi isteğini
açıyordu. R2 öncesinde bu görünmüyordu (sayfa başına bir-iki örnek),
R2 ile ekranın modülü dışında izin isteyen her düğme ikinci bir
`useModuleActions` çağrısı doğurduğu için çoğaldı — `finans/vergi`
üç modülün iznini istiyor, `santiyeler/[siteId]` de üç.

Söz (promise) modül düzeyine alındı: ilk çağıran isteği başlatır,
diğerleri aynı sözü bekler.

**Yalnızca başarılı yanıt önbellekleniyor.** 401 önbellekte kalsaydı
giriş sonrası kullanıcı hâlâ oturumsuz görünürdü: giriş `router.push`,
çıkış `router.replace` ile yapılıyor, yani tam sayfa yüklemesi yok ve
modül durumu sıfırlanmıyor. İki akış da `clearCurrentUserCache()`
çağırıyor, test bunu doğruluyor.

`ErpShell` kendi `auth/me` çağrısını `apiClient` ile doğrudan yapıyor
ve bu önbelleği KULLANMIYOR — sayfa başına hâlâ bir fazladan istek var.
Ayrı bir tur işi.

---

## Hayalet arayüz: ekran var, uç yok (2026-08-17)

R2/4d yığın 1'de bir düğmenin ucunu ararken çıktı; aranan tek düğmeden
büyük bir desen oldu.

### 1. Fiyat farkı otomatik hesabı — KALDIRILDI

Hakediş detayındaki "Fiyat Farkı Hesabı" paneli
`POST price-difference-calculations/calculate` çağırıyordu.
**Böyle bir uç backend'de yok** — `PriceDifferenceController` yalnız
`price-difference-profiles` ve `price-difference-indexes` rotalarını
taşıyor, "calculate" adlı hiçbir metot yok. `PriceDifferenceCalculation`
diye bir model de yok. Yani düğme 404 dönüyordu.

**Fiyat farkı ise gerçekten kullanılıyor — elle.**
`ProgressPayment.PriceDifferenceAmount` hakediş formunda elle giriliyor
ve şuralara akıyor:

| Tüketen | Ne yapıyor |
|---|---|
| `HakedisExportController:124` | Excel çıktısına "Fiyat Farkı" satırı |
| `FinanceDashboardController:66` | gerçekleşen hakedişlerin toplamı |
| `HakedisProfitService:41` | **kâr hesabına giriyor** |
| `HakedisTrackingController:64` | takip listesinde kolon |

Karar: **panel bütünüyle kaldırıldı** (düğme, üç seçim kutusu, sonuç
bloğu, durum, işleyici, kullanılmayan importlar). Elle giriş yolu
çalıştığı için hiçbir yetenek kaybedilmedi; kaldırılan şey yalnızca
çalışmayan otomatik hesap. Yarım bir düğme bırakmak, panelin
gerisini bırakmaktan kötü olurdu.

**Ana veri ekranları KASITLI olarak duruyor** (`fiyat-farki/profiller`,
`fiyat-farki/endeksler`). Silinmedi çünkü formül yazıldığında ihtiyaç
duyulacak veri onlarda — ve girilmiş endeksler çöpe atılmamalı.

### 2. Ana veri atıl: profiller ve endeksler hiçbir şey beslemiyor

Tarama sonucu net: `PriceDifferenceProfile`, `PriceDifferenceCoefficient`
ve `PriceDifferenceIndexPeriod` tablolarına **yalnız kendi CRUD
controller'ı** dokunuyor. Başka hiçbir kod okumuyor.

Yani iki ekran veri girişi kabul ediyor, kaydediyor ve o veri hiçbir
sonuç üretmiyor. Aylık Yİ-ÜFE endeksi giren biri varsa o emek şu an
karşılıksız.

Model kamu formülünün tam kendisi: katsayılar **A, B1..B5, C**
(4735 sayılı kanun fiyat farkı esasları), endeksler işçilik / akaryakıt
/ malzeme / makine / çimento (formülün Yİ-ÜFE alt endeksleri),
`PriceDifferenceCalculationType` = kamu formülü / sabit oran / elle.
Biri işi doğru kurmaya başlamış, ana veriyi bitirmiş, hesabı yazmamış.

**Fiyat farkı ayrı paket olarak kurulursa netleşmesi gerekenler:**
  - formül: kamu Yİ-ÜFE standardı mı, sözleşmeye özel endeks mi
  - hesap hakedişe hangi aşamada işlenecek (taslakta mı, onayda mı)
  - `PriceDifferenceAmount` elle mi kalacak, hesaptan mı gelecek
    (ikisi birden olursa hangisi kazanır)
  - geçmiş hakedişler yeniden hesaplanacak mı

### 3. PDF çıktısının TAMAMI yazılmamış — KARAR BEKLİYOR

`services/report.service.ts` beş PDF ucu tanımlıyor:

    /api/reports/progress-payment/{id}/pdf
    /api/reports/price-difference/{id}/pdf
    /api/reports/deductions/{id}/pdf
    /api/reports/purchase-order/{id}/pdf
    /api/reports/stock-issue/{id}/pdf

**Backend'de `api/reports` diye bir rota yok** ve `QuestPDF / iText /
DinkToPdf / Puppeteer / wkhtmltopdf` — PDF üretebilecek **hiçbir
kütüphane projede yok.** Yani beşi de ölü.

İkisi GÖRÜNÜR bir düğmeye bağlı:
  - `hakedis/[id]` "PDF" düğmesi (Excel yanında; **Excel çalışıyor**:
    `api/hakedis-export/{id}/excel`)
  - `satin-alma/siparis/[id]` "Sipariş PDF İndir" (alternatifi YOK)

Bu turda **kaldırılmadı**, çünkü fiyat farkından farklı: orada elle
giriş yolu vardı, burada yeteneğin kendisi yok. Düğmeyi silmek eksik
özelliği gizlemek olur.

Hiçbir düğmeye bağlı OLMAYAN üç işleyici (`downloadPricePdf`,
`downloadDeductionPdf`, `downloadProgressPdf`) ölü kod olarak
`hakedis/[id]`'den kaldırıldı.

**Karar gerekiyor:** PDF çıktısı gerçek bir ihtiyaç mı?
  - Evet ise → ayrı paket: kütüphane seçimi + şablonlar (hakediş,
    sipariş, kesinti, stok çıkışı). Excel şablonları referans olur.
  - Hayır ise → iki görünür düğme kaldırılır, `report.service.ts`
    silinir.

---

## Arayüzde uygulanamayan yetki ayrımları — uç bölünmeli (2026-08-17, R2/4d)

"Yıkıcı aksiyon delete yetkisi ister" kuralı bazı modüllerde
UYGULANAMIYOR: uç ayrım yapmıyor. Arayüzde uydurmak "gizli ama izinli"
üretirdi — kullanıcı düğmeyi göremez ama API'den işlemi yine yapar.

| Ekran / uç | Tek anahtar | Ayrılması gereken |
|---|---|---|
| `POST purchase-returns/{id}/durum` | purchasing-receipts.edit | üç düğme (tedarikçiye gönderildi / kapat / **iptal**) aynı uca gidiyor; durum ilerletme tek uçta |
| `POST tasks/{id}/*` | tasks.manage | oluştur/başlat/tamamla/**iptal** ayrımı yok; `tasks.delete` anahtarı da yok |
| `POST vehicles`, `POST vehicles/{id}/assignments` | vehicle.manage | modülde yalnız `vehicle.view` + `vehicle.manage`; create/edit/delete yok |
| `secretariat.manage` (kargo, ziyaretçiler) | secretariat.manage | create/edit/delete ayrımı yok |
| `subcontractor.manage` | subcontractor.manage | aynı |
| `salary.manage` (ücret kartları) | salary.manage | create/update/**delete** aynı anahtarda |
| `company-settings.edit` | company-settings.edit | banka hesabı **silme** de edit'te; `company-settings.delete` yok |

Bunların hepsinde arayüz ucun istediği anahtara eşitlendi ve gerekçe
kodda yorum olarak yazıldı. Ayrım isteniyorsa sıra şu: önce
`PermissionCatalog`'a anahtar, sonra uçta `RequirePermission`, sonra
rol dağıtımı (etki ölçümü), en son arayüz — arayüz her zaman SON.

### Ayrıca: kredi durumu tek uçta
`POST finansal-araclar/krediler/{id}/durum` (finance.edit) gövdesinde
"iptal" durumu da var. Rota adında "cancel/iptal" geçmediği için yıkıcı
taramasına düşmüyor. Durum makinesinin hangi geçişlerinin yıkıcı
sayıldığı ayrı bir karar.

---

## `personeller` ekranı paylaşılan izin önbelleğine taşınmalı (2026-08-17)

**Borç, acil değil — bugün çalışıyor.**

`app/insan-kaynaklari/personeller/page.tsx` izinleri kendisi çözüyor:
`apiClient<{permissions}>("auth/me")` çağırıp `Set<string>` kuruyor
(satır ~311). Bu, arayüzdeki **dördüncü** izin çözme yolu:

| # | Yol | Ekran |
|---|---|---|
| 1 | `useModuleActions(modül).can(eylem)` | R2 ile 50+ |
| 2 | `usePermissions()` + satır içi `has("x.y")` | 15 |
| 3 | `hasPermission(session, "x.y")` | butce-onay |
| 4 | **kendi `auth/me` + kendi Set'i** | **personeller** |

Dördü de aynı veriye bakıyor; davranış farkı yok. İki maliyeti var:

1. **Paylaşılan önbelleği kullanmıyor.** `useCurrentUser` artık modül
   düzeyinde tek istek yapıyor; bu ekran onun dışında kalıyor ve
   sayfa başına fazladan bir `auth/me` isteği açıyor.
2. Denetlenebilirlik: anahtarlar satır içi, hangi ucun istediği
   yazılmıyor.

**`hasAllPermissions` bayrağına bakmaması SORUN DEĞİL.** Bayrak
`PermissionCatalog.HasEveryPermission(permissions)` ile, yani dizinin
tamamlığından türetiliyor (`AuthController:116`); süper kullanıcının
dizisi eksiksiz geldiği için `permissions.has(x)` doğru sonuç veriyor.
Bu ekranda süper kullanıcı hatası yok.

**NEDEN R2 TURUNDA ÇEVRİLMEDİ:** kurulan izin kümesi bir VERİ ÇEKME
effect'ini besliyor (`if (!permissions.has("salary.view")) return;`
satır ~447 ve `has("extra_payment.view")` satır ~476). `usePermissions`
kancasına geçmek `loading` durumunu o effect'lerin sıralamasına doğru
bağlamayı gerektiriyor — yanlış yapılırsa maaş/elden verisi hiç
çekilmez ya da iki kez çekilir. Bir arayüz kapılama süpürmesine
iliştirilecek iş değil.

**Ayrı refactor olarak yapılmalı**, kendi testiyle: izinler yüklenmeden
effect'in çalışmadığı, yüklendikten sonra bir kez çalıştığı ve
`salary.view` olmayan kullanıcıda elden verisinin hiç istenmediği
doğrulanmalı.

---

## PDF kararı VERİLDİ: ölü düğmeler kaldırıldı (2026-08-17)

Önceki kayıtta "karar bekliyor, çünkü fiyat farkından farklı — orada
elle giriş yolu vardı, burada yeteneğin kendisi yok" yazmıştım.
**Bu tespit eksikti ve düzeltildi:** her iki ekranda da ÇALIŞAN bir
yazdırma sayfası, ölü düğmenin tam yanında duruyor.

| Ekran | Ölü uç | Çalışan alternatif |
|---|---|---|
| `hakedis/[id]` | `/api/reports/progress-payment/{id}/pdf` | `hakedis/[id]/yazdir` (571 satır, print stilleri) **+ Excel** (`api/hakedis-export/{id}/excel`) |
| `satin-alma/siparis/[id]` | `/api/reports/purchase-order/{id}/pdf` | `satin-alma/siparis/[id]/yazdir` (500 satır) |

İkisi de ekrandan link olarak veriliyor. Hakediş ekranındaki yorum
zaten bunu söylüyormuş: *"NATURA formatında, logo antetli çıktı; PDF
tarayıcının yazdırma penceresinden alınır."* Yani PDF yeteneği
sunucu üretimi olarak değil, YAZDIRMA SAYFASI olarak sağlanmış.

Kaldırıldı:
  - `hakedis/[id]` "Hakediş PDF İndir" düğmesi + `downloadPdf` işleyicisi
  - `satin-alma/siparis/[id]` "Sipariş PDF İndir" düğmesi + işleyicisi
    + `downloadingPdf` durumu
  - `services/report.service.ts` (tamamı — beş ucun hiçbiri yoktu)

Her iki ekrana kaldırma gerekçesi yorum olarak yazıldı, test yazdırma
yolunun DURDUĞUNU da doğruluyor (silinirse düşer).

**Sunucu tarafı PDF ayrıca istenirse** (imzalı çıktı, otomatik e-posta
eki, arşivleme gibi bir ihtiyaç) ayrı paket: kütüphane seçimi +
şablonlar. Yazdırma sayfaları ve Excel şablonları referans olur.

### Fiyat farkı kararı da netleşti

Endeks ve profil ekranları **kaldırılmadı.** Gerekçe "belki lazım olur"
değil: `ProgressPayment.PriceDifferenceAmount` bugün ELLE giriliyor ve
hakediş Excel çıktısına, finans panosuna, hakediş takibine ve
**kâr hesabına** (`HakedisProfitService`) akıyor. Yani eskalasyon
pratikte kullanılıyor; eksik olan yalnızca otomatik hesap. Ana veriyi
silmek girilmiş endeksleri geri dönüşsüz atmak olurdu.

Hesap ucu da YAZILMADI: uydurulacak şey formülün kendisi olurdu.
Netleşmesi gerekenler yukarıdaki fiyat farkı kaydında listeli.

---

## Ölü ekran KALDIRILDI: proje kesinti politikası (2026-08-17)

`app/projeler/[id]/kesintiler` — 463 satır, menüden erişilebilir,
**tamamen ölü.** Hem listesi hem kaydı `progress-payment-deduction-rules`
ucuna gidiyordu; o uç backend'de HİÇ YOK: controller yok, rota yok,
`ProgressPaymentDeductionRule` modeli bile yok. Ekran açılıyor, liste
yüklenmiyor, "Kural Kaydet" her zaman hata veriyordu.

**Kesintiler pratikte çalışıyor**, ama belge başına: hakediş
oluşturulurken/düzenlenirken `request.Deductions` ile giriliyor
(`ProgressPaymentsController.ApplyDeductions`), kesinti türleri ve
varsayılan oranları `lib/hakedis/calculation.ts` içinde tanımlı ve
sunucudaki `HakedisDeductionType` ile eşleşiyor.

Bu, fiyat farkı ve PDF ile **aynı desenin üçüncü örneği**: elle giriş
çalışıyor, otomasyon/kural katmanı hiç yazılmamış, arayüz o katman
varmış gibi davranıyor.

Kaldırılan:
  - `app/projeler/[id]/kesintiler/page.tsx` (463 satır)
  - `services/deduction-rule.service.ts` (112 satır)
  - proje detayındaki **iki giriş noktası**: "Kesinti Politikası" modül
    kartı ve "Finansal Sözleşme Oranları" panelindeki
    "Kesinti Politikasını Aç" bağlantısı

`lib/hakedis/calculation.ts` içindeki `DeductionType` DOKUNULMADI —
o çalışan olan; silinen ölü servisin kendi kopyasıydı.

**Kural motoru ileride istenirse** netleşmesi gerekenler: kural proje
bazında mı sözleşme bazında mı; hakedişe hangi aşamada uygulanacak;
belge üzerinde elle değiştirilebilecek mi (elle giriş bugün çalışıyor,
kural onu ezerse geçmiş hakedişler ne olacak).

---

## R2/4 KAPANIŞ ÖLÇÜMÜ (2026-08-17)

Yazan aksiyonu olan **97 ekran**, üç yoldan biriyle korunuyor:

| Yol | Ekran |
|---|---|
| Düğme kapısı (`useModuleActions`) | 69 |
| Rota kapısı (tam sayfa aksiyon ekranı) | 21 |
| Satır içi izin (önceden var olan, uçlarla eşleştiği ölçüldü) | 15 |

**KAPANIŞ ÖLÇÜMÜM İKİ KEZ EKSİK ÇIKTI, ikisi de aynı kökten:** ölçüm
aracı uç izni ÇÖZÜLEMEYEN ekranları sessizce dışarıda bırakıyordu.
Önce "2 ekran kaldı" dedim; sözleşme testinin mantığını simüle edince
11 çıktı (dördü zaten rota kuralıyla kapalıymış, biri ölü ekran, altısı
gerçek iş).

Bu yüzden kapanışa bir **sözleşme testi** eklendi
(`tests/module-actions.test.ts`, "yazan aksiyonu olan her ekran bir kapı
taşıyor"): yeni bir ekran yazan uç çağırıp hiçbir kapı taşımazsa test
düşüyor ve hata mesajı iki çözüm yolunu da söylüyor. Sonda ile
doğrulandı: uydurma kapısız bir ekran eklendiğinde adıyla yakalandı.

Amacı kapsamı dondurmak değil; yeni ekranın hangi yolu seçtiğini
BİLİNÇLİ karar yapmak. Artık ölçüm aracının bir hatası testi kör
bırakamaz — test aracı değil, dosyaları tarıyor.

---

## R3 Adım 0: VERİ KAPSAMI 122 CONTROLLER'IN 10'UNDA UYGULANIYOR (2026-08-17)

R3 merdivende "UserDataScope arayüzü" diye tanımlanmıştı. Ölçüm asıl
boşluğun arayüzde DEĞİL zorlamada olduğunu gösterdi.

### Model beş seviye taşıyor, yazma yolu ikisini üretiyor

`DataScopeType`: All / Company / Branch / Project / Site.
`CurrentDataScopeSnapshot` beşini de destekliyor (ayrı Guid kümeleri +
`Apply()` metotları). Ama `UserManagementController` yalnızca iki şey
yazabiliyor: `All` satırı ya da seçilen şantiyeler için `Site` satırları.
**Şirket/şube/proje kapsamı arayüzden hiç verilemiyor** — ancak elle SQL
ile oluşur.

İyi olan taraf: `SiteOnly` rol seçilip hiç şantiye atanmazsa
**fail-closed** davranılıyor (hiç satır eklenmiyor → hiçbir şey
görünmüyor). Kod yorumunda "önceden yanlışlıkla kısıtsız erişim
veriliyordu" diye düzeltme kaydı var.

### Zorlama ölçümü

Sınıflandırma tahminle değil, döndürülen VARLIĞIN kapsam alanı taşıyıp
taşımadığından türetildi: 173 `BaseEntity` varlığının **112'si**
`CompanyId`/`BranchId`/`ProjectId`/`ProjectSiteId` taşıyor.

| | |
|---|---|
| Kapsam alanı taşıyan varlığa dokunan GET ucu | 210 |
| Kapsam uygulayan controller'da | 28 |
| **Kapsamsız** | **182** (67 controller) |

Ham 182 yanıltıcı: bugün kapsam kısıtı yalnız iki role uygulanıyor
(`SiteOnly` = **Şantiye Şefi**, **Formen**). Gerçek açık:

| Rol | Erişebildiği KAPSAMSIZ okuma ucu |
|---|---|
| Şantiye Şefi (19 izin) | **39** |
| Formen (10 izin) | **24** |

### Doğrulanmış iki örnek

`GET hr/recruitment/candidates` — `personnel.view` istiyor, gövdesi
tam olarak `db.JobCandidates.AsNoTracking().OrderByDescending(...)`.
**Hiçbir süzgeç yok, companyId bile yok.** `JobCandidate` modelinde
`IdentityNumber` alanı var. Yani bir Formen, bütün şirketlerdeki tüm
iş başvurusu adaylarını kimlik numarasıyla listeleyebiliyordu.

`GET hr/personnel` — yalnızca İSTEĞE BAĞLI `companyId`/`projectId`
parametreleriyle süzülüyordu; parametre gönderilmezse hepsi dönüyordu.
Arama kutusu `IdentityNumber` içinde de arıyor.

Alternatif bir şirket kapsamı mekanizması aranıp bulunamadı
(`ICurrentCompany`, `X-Company` başlığı vb. YOK) — yani bu uçlarda
kapsam hiçbir katmanda uygulanmıyordu.

**Maaş ve elden ödeme maskeleri ayrı ve çalışıyor**
(`ISalaryVisibilityService` enjekte edilmiş); açık olan kimlik ve liste
kapsamıydı.

### R3 ikiye ayrıldı

**R3a — zorlama (backend, asıl güvenlik sınırı).** 39 + 24 ucun kapsam
sorması. Desen zaten kurulu ve testli (10 controller).

**R3b — arayüz.** Kapsam atama ekranı. Zorlama gerçek olmadan yapılırsa
YANLIŞ GÜVEN üretir: yönetici "bu kullanıcıyı projeyle sınırladım" der,
kullanıcı 112 controller'dan her şeyi görmeye devam eder.

### R3a yığın 1 — personel ailesi (YAPILDI)

Süzgeç TEK KAYNAĞA eklendi:
`CurrentDataScopeSnapshot.Apply(IQueryable<Personnel>)`. Controller'a
dağıtılmadı çünkü personel şantiyeye doğrudan değil
`ProjectSiteAssignment` üzerinden bağlı ve "aktif atama" tanımı
(`IsActive && !IsDeleted && EndDate == null`) yerlere göre kayardı.

`PersonnelController`: liste, `veri-eksikleri`, detay. Detayda **404**
dönüyor (403 değil) — `ProjectSitesController` desenıyle aynı, kaydın
varlığını sızdırmamak için.

**AÇIK KARAR — `HrRecruitment` bilinçli olarak bu yığına alınmadı:**
şantiye kapsamlı kullanıcının aday havuzunda ne görmesi gerektiği
personeldeki kadar net değil. Aday havuzu şantiye verisi değil İK ofisi
verisi; doğru cevap muhtemelen "hiçbir şey" ama bu personelden daha sert
bir kesme ve ayrı karar istiyor.
