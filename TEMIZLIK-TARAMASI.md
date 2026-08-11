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

## "Uç var, ekran yok" taraması

**Ne:** Backend'de yazılıp frontend'den hiç çağrılmayan uçlar. Bu
oturumda üç tanesi tek tek, tesadüfen çıktı: özlük belge uçları, çek
düzenleme, görev iptali. Üçü de backend'de hazırdı ama kullanıcı
hiçbirine ulaşamıyordu.

**Neden riskli:** Bir uç ekrandan çağrılmıyorsa iş bitmiş sayılmıyor
ama bitmiş görünüyor — paket "kapandı" diye kapanıyor, özellik
kullanılamıyor. Tek tek fark edilmesi şansa kalıyor.

**O turda değerlendirilecek:** Toplu sweep — controller route
listesini çıkarıp (`[Http*]` öznitelikleri) frontend `services/`
altındaki `apiClient` çağrılarıyla karşılaştırmak, eşleşmeyenleri
listelemek. Çıkan listede her kalem için karar: ekran aç, ucu kaldır,
ya da bilinçli olarak iç/otomasyon ucu diye işaretle. Sweep'i
tekrarlanabilir bir betiğe bağlamak, tek seferlik elle taramaya
tercih edilir.

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

