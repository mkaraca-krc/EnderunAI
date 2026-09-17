# PARK — YAPILMIŞ AMA YANLIŞ KATMANA YAZILMIŞ İŞLER

Buradaki dosyalar **silinmedi, kenara alındı**. Her birinin neden
park edildiği ve nasıl devam edileceği aşağıda.

---

## `PaylasilanDerleyiciKapaliTests.cs` — ③ paylaşılan derleyici (2026-09-17)

### Ne yapılmıştı

`dotnet` arkada bir Roslyn derleyici sunucusu (`VBCSCompiler`) bırakıyor
ve büyüyor; biri 5,5 GB tutuyordu, PID ile kapatınca kullanılabilir
bellek 650 MB'dan 6.161 MB'a çıktı.

Dört kol ölçüldü (her kolda **gerçek derleme**, publish geçici dizine):

| kol | bayrak | süre | kalan VBCSCompiler |
|---|---|---|---|
| A | yok | 268 sn | **1** |
| B | `UseSharedCompilation=false` | 249 sn | **0** |
| C | `nodeReuse=false` | 252 sn | **1** |
| D | ikisi birden | 270 sn | **0** |

**Yalnız B çalışıyor.** `nodeReuse` MSBuild işçi düğümlerini yönetir,
Roslyn sunucusunu değil. Bedel ölçülemedi: farklar A kolunun kendi
koşular arası sapmasının (244 ↔ 268) içinde.

### Neden park edildi

Bayrağı `safe-deploy.sh` içindeki **çağırana** yazdım. Oysa
`DerlemeKosucuGuardTests` şunu söylüyor: *her derleme çağrısı
KOŞUCUDAN (`scripts/derleme-kos.sh`) geçmeli.* Yani bayrağın yeri
çağıran değil, **sarmalayıcının içi**.

Üstelik eklerken çok satırlı komutun ortasına yorum koydum ve **satır
devamını kırdım**: publish, derleme kilidini ATLAR hâle geldi. Kapı
bunu yakalayıp dağıtımı durdurdu — yanlış alarm değil, gerçek bozulma.

Mehmet Bey'in kararı (2026-09-17): **muhafız gevşetilmez**, ③ dağıtım
yolundan çıkarılır, A (K5 düzeltmesi) önce iner.

### Nasıl devam edilecek

1. A dağıtılıp **üç ölçütüyle** doğrulanacak (CSS karması değişmiş ·
   yeni dosyada `repeat(4,1fr)` ve çıplak `1fr` yok · widget'ta iki
   eşit sütun, taşma 0).
2. Sonra bayrak **`scripts/derleme-kos.sh` sarmalayıcısının içine**
   yazılacak. Çağıran değişmeyecek, muhafız hiç değişmeyecek.
3. Bu muhafız buradan geri alınacak ve **koşucuyu** sınayacak biçimde
   güncellenecek (çağıranı değil).
4. Kabul ölçütü değişmedi: **dağıtım derlemesinden sonra kalan
   `VBCSCompiler` = 0**, ve derleme süresi maliyeti ölçülüp yazılacak.
5. Ayrı bir dağıtımda denenecek.
