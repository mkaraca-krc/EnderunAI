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
