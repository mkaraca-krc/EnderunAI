# Enderun ERP — depo çalışma notları

## Hızır kullanım kılavuzu bilgi bankası (ÖNEMLİ BAKIM KURALI)

Hızır asistanı, kullanıcıya "bu işlemi nereden yaparım" sorularını
`backend/EnderunAI.Api/Data/Seeds/hizir-knowledge-base.json` dosyasındaki
modül/sayfa haritasına bakarak cevaplıyor.

**Yeni bir modül, sayfa veya paket tamamlandığında bu dosyaya o modülün
sayfaları eklenmelidir.** Eklenmezse Hızır yeni ekranları tarif edemez ve
kullanıcıya "bulamadım" der — özellikle sahadaki roller için asıl değer
buradan geliyor.

Her sayfa kaydında:

- `path` — gerçek rota (`components/erp/erp-shell.tsx` menü ağacıyla aynı)
- `permission` — sayfayı açmak için gereken izin anahtarı; Hızır bu izne
  sahip olmayan kullanıcıya sayfayı **hiç göstermez**
- `purpose` — sayfanın ne işe yaradığı, tek cümle
- `steps` — kullanıcının izleyeceği adımlar, menüden başlayarak

Kılavuz dosyası değiştiğinde ayrıca `HizirPermissionTests` içindeki
"izinsiz sayfayı tarif etmesin" testlerinin hâlâ geçtiği doğrulanmalı.

## Yerleşik tuzaklar

- **Soft-delete ve benzersiz indeks:** `AuditSaveChangesInterceptor` her
  `Remove()` çağrısını `IsDeleted = true`'ya çeviriyor. Bu yüzden
  soft-delete edilebilen bir varlıkta benzersiz indeks tanımlarken
  `HasFilter("\"IsDeleted\" = false")` kullanılmalı; aksi halde silinen
  kayıt aynı anahtarın tekrar kullanılmasını kalıcı olarak engelliyor.
- **Seed add-only:** `DatabaseSeeder` mevcut satırların üzerine yazmaz.
  Sonradan eklenen bir ayar alanının mevcut kayıtlarda dolması gerekiyorsa
  `FillIfMissingAsync` deseniyle yalnızca NULL alanlar tamamlanmalı.
- **hr_* tabloları `HrDbContext`'e ait** ve `AppDbContext` modeline
  girmiyor; bu tablolara kolon eklerken migration elle yazılıyor
  (bkz. `20260803125421_AddPayrollCalculationColumns`).
- **csproj seed kopyalama:** `Content Include` değil `Content Update`
  kullanılmalı; Web SDK zaten `.json` dosyalarını gloluyor ve `Include`
  `NETSDK1022` hatası veriyor.

## Süreç

- Migration öncesi `/usr/local/bin/enderun-backup.sh` ile yedek alınır;
  migration `dotnet ef database update --context AppDbContext` ile elle
  uygulanır (safe-deploy bilinçli olarak otomatik uygulamıyor).
- Yayın yalnızca `deploy/scripts/safe-deploy.sh` ile yapılır.
- Commit mesajları, kod yorumları ve arayüz metinleri Türkçe; emoji yok.
