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

## Frontend test altyapısı — Vitest + Testing Library

**Ne:** Frontend'de otomatik test yok. Bugün tek güvence
`tsc --noEmit`, eslint ve `npm run build`; davranış test edilmiyor.

**Neden riskli ve neden ŞİMDİ sıraya giriyor:** Modal standardı
(`components/ui/modal.tsx`, `confirm-dialog.tsx`) app-wide
yayılmadan önce kurulmalı. Bileşen bir kez onlarca ekrana
dağıldıktan sonra Esc davranışı, odak tuzağı, odağın geri
verilmesi, gövde kaydırma kilidi ve "gerekçe zorunlu → onay
düğmesi kapalı" kuralı elle doğrulanamaz hale gelir; bir
regresyon sessizce her ekrana birden yayılır.

**O turda değerlendirilecek:** Vitest + @testing-library/react +
jsdom kurulumu ve ilk testlerin modal/ConfirmDialog üzerine
yazılması: Esc kapatıyor, odak modalın içinde kalıyor, kapanınca
tetikleyen düğmeye dönüyor, gerekçe boşken onay verilemiyor,
`busy` sırasında çift gönderim engelleniyor. Sıra: altyapı →
modal testleri → standardın app-wide yayılması.

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

