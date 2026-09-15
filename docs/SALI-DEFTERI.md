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
