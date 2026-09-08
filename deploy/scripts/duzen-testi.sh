#!/usr/bin/env bash
#
# DÜZEN TESTİ RIG'İ — GERÇEK TARAYICI, GERÇEK YIĞIN
# ═══════════════════════════════════════════════════════════════════
#
# NEDEN VAR (MESAJ/3, 2026-09-07): "composer görünen alanın içinde mi"
# sorusu grep ile cevaplanamaz (Kural 70). jsdom da cevaplayamaz —
# DÜZEN MOTORU YOK, `getBoundingClientRect` her zaman sıfır döner.
# Bu yüzden gerçek bir tarayıcı gerekiyordu.
#
# ═══ NEDEN AYRI BİR YIĞIN ═══
#
# Ölçüm oturum açmış bir kullanıcı gerektiriyor; /mesajlar oturumsuz
# /login'e düşüyor (ölçüldü). İki seçenek vardı:
#
#   (a) CANLI yığında oturum açmak → üretimde test kullanıcısı
#       yaratmak demekti. YAPILMADI.
#   (b) enderun_ai_test üzerinde İKİNCİ bir yığın → yapılan bu.
#
# ═══ SIR YOK ═══
#
# Parola her koşuda rastgele üretiliyor ve yalnız bu betiğin süreç
# ağacında yaşıyor. Depoya, günlüğe, commit'e hiçbir şey yazılmıyor.
# Arka uç `SEED_ADMIN_USERNAME`/`SEED_ADMIN_PASSWORD` görünce açılışta
# kullanıcıyı kendisi kuruyor (DatabaseSeeder).
#
# ═══ FAİL-CLOSED ═══
#
# Yığın ayağa kalkmazsa test ATLANMAZ, KIRMIZI verir. "Ölçemedim"
# ile "ölçtüm, sorun yok" aynı görünmemeli.
set -euo pipefail

KOK="/var/www/enderun-ai"
ON_YUZ="${KOK}/frontend/enderun-ai"
ARKA_PORT=5156
ON_PORT=3001
# Vekil: uretimde nginx-in yaptigi /api/hubs ayrimini yapiyor.
VEKIL_PORT=3002
KULLANICI="duzen-testi"

log() { echo "[duzen-testi] $*"; }
oldu() { log "HATA: $*" >&2; exit 1; }

# ─── Test veritabanı bağlantısı — canlıdan TÜRETİLİYOR, canlıya
#     DOKUNULMUYOR. Ad `enderun_ai_test` olarak sabitleniyor ve
#     doğrulanıyor: yanlış veritabanına açılmak veri kaybı demekti.
[ -f /etc/enderunai/backend.env ] || oldu "backend.env yok."
CANLI="$(grep -E '^DB_CONNECTION=' /etc/enderunai/backend.env | sed -E "s/^DB_CONNECTION=//" | tr -d "'\"")"
[ -n "$CANLI" ] || oldu "DB_CONNECTION okunamadı."
TEST_BAGLANTI="${CANLI//Database=enderun_ai;/Database=enderun_ai_test;}"

case "$TEST_BAGLANTI" in
  *"Database=enderun_ai_test;"*) : ;;
  *) oldu "Hedef veritabanı enderun_ai_test DEĞİL. Durduruldu." ;;
esac

PAROLA="$(head -c 24 /dev/urandom | base64 | tr -d '/+=' | head -c 24)Aa1!"

ARKA_PID=""; ON_PID=""; VEKIL_PID=""

# ─── SÜREÇ GRUBUYLA ÖLDÜRÜLÜYOR — ÖLÇÜLMÜŞ BİR TUZAK.
#
# `npx next start` bir SARMALAYICI: onu öldürmek altındaki Next
# sunucusunu öldürmüyor. İlk yazımda öyleydi ve bir sonraki koşu
# `EADDRINUSE: 3001` alıyordu — ama testler yine de KOŞUYORDU, çünkü
# eski sunucu hâlâ cevap veriyordu. Yani test, ÖLÇMEK İSTEDİĞİ
# sürümü değil, önceki koşunun sürümünü ölçüyordu. Sessiz ve
# tamamen yanıltıcı.
#
# Kural 78'in aynı sınıfı: sarmalayıcıyı durdurmak süreç ağacını
# durdurmaz. `setsid` ile her biri kendi süreç grubunda başlıyor ve
# grubun tamamı (`kill -PGID`) sonlandırılıyor.
temizle() {
  [ -n "$ARKA_PID" ] && kill -TERM -- "-${ARKA_PID}" 2>/dev/null || true
  [ -n "$ON_PID" ] && kill -TERM -- "-${ON_PID}" 2>/dev/null || true
  [ -n "$VEKIL_PID" ] && kill -TERM -- "-${VEKIL_PID}" 2>/dev/null || true
  sleep 1
  [ -n "$ARKA_PID" ] && kill -KILL -- "-${ARKA_PID}" 2>/dev/null || true
  [ -n "$ON_PID" ] && kill -KILL -- "-${ON_PID}" 2>/dev/null || true
  [ -n "$VEKIL_PID" ] && kill -KILL -- "-${VEKIL_PID}" 2>/dev/null || true
  wait 2>/dev/null || true
}
trap temizle EXIT

# ─── PORTLAR BOŞ MU — FAIL-CLOSED.
#
# Dolu bir port sessizce ESKİ sunucuya bağlanmak demek. Ölçmek
# istediğimiz sürümü ölçmediğimiz bir koşu, hiç koşmamaktan kötüdür.
for port in "$ARKA_PORT" "$ON_PORT" "$VEKIL_PORT"; do
  if ss -ltn "sport = :${port}" 2>/dev/null | grep -q LISTEN; then
    oldu "Port ${port} DOLU. Önceki bir koşu kalmış olabilir; " \
         "süreci bulup durdurun (ss -ltnp sport = :${port})."
  fi
done

# ─── ESKİ TEST KULLANICISI SİLİNİYOR — ÖLÇÜLMÜŞ BİR TUZAK.
#
# `SeedAdminUserAsync` kullanıcı ZATEN VARSA parolayı GÜNCELLEMİYOR
# (erken dönüyor). Parola her koşuda rastgele üretildiği için ikinci
# koşu birinci koşunun parolasıyla karşılaşıyor ve giriş sessizce
# başarısız oluyordu: Playwright /login'de kalıyor, hata "buton
# tıklanamadı" gibi görünüyordu — asıl sebep parola uyuşmazlığıydı.
#
# Kullanıcı her koşudan ÖNCE siliniyor; tohumlayıcı yenisini kuruyor.
log "Önceki koşunun test kullanıcısı siliniyor..."
sudo -u postgres psql -q -d enderun_ai_test -c \
  "DELETE FROM users WHERE \"Username\" IN ('${KULLANICI}', 'duzen-karsi-taraf', 'duzen-kisitli');" >/dev/null

# ═══ ARKA UÇ AYRI DİZİNE PUBLISH EDİLİYOR ═══
#
# ÖLÇÜLEN TUZAK: rig arka ucu CANLININ `publish/` dizininden
# çalıştırıyordu — yani en son DAĞITILAN derlemeden. Çalışma
# ağacındaki arka uç değişiklikleri rig'de HİÇ görünmüyordu.
#
# Bir kez tam olarak bunu yaşadım: matris ucundaki grants süzmesini
# yazdım, testi koştum, düştü. Sebep düzeltmenin yanlış olması
# değil, rig'in eski ikiliyi çalıştırmasıydı. Ön yüzde `.next` için
# çözdüğüm sorunun arka uç hâli.
#
# `SONDA_PUBLISH` verilirse o kullanılır (seed sondası bunu yapıyor);
# yoksa çalışma ağacından TAZE publish alınır.
ARKA_PUBLISH="${SONDA_PUBLISH:-${ON_YUZ}/../../publish-duzen}"

if [ -n "${SONDA_PUBLISH:-}" ]; then
  log "Arka uç publish DIŞARIDAN verildi: ${SONDA_PUBLISH}"
elif [ "${DERLEME_ATLA:-hayir}" = "evet" ]; then
  log "Arka uç publish ATLANDI (DERLEME_ATLA=evet) — mevcut kullanılacak."
  [ -f "${ARKA_PUBLISH}/EnderunAI.Api.dll" ] \
    || oldu "Atlanacak publish yok: ${ARKA_PUBLISH}"
else
  log "Arka uç publish ediliyor (ayrı dizin, canlının publish/ dizinine DOKUNULMUYOR)..."
  #
  # ═══ ÜÇ SONUÇ: ÖLÇTÜ+GEÇTİ / ÖLÇTÜ+DÜŞTÜ / ÖLÇEMEDİ (Kural 67) ═══
  #
  # ÖLÇÜLEN KUSUR (2026-09-08): rig, yayının backend testleri koşarken
  # başlatıldı. `derleme-kos.sh` tek örnek kapısıyla çıkış 75 verdi
  # ("ZATEN KOŞAN BİR DERLEME VAR"). Rig bunu "Arka uç publish
  # başarısız" diye raporladı ve KIRMIZI verdi.
  #
  # O kırmızı ürüne ait DEĞİLDİ — test hiç koşmadı. Bir sonraki okuyan
  # (ben de olabilirim) kırmızıyı ürüne yazardı. "Kırmızı, bulgunun
  # değil ENGELİN işareti olabilir" ayrımı burada araca konuyor.
  #
  # 75 = derleme-kos.sh'nin "başkası koşuyor" kodu; ondan ayırt
  # ediliyor. Gerçek derleme hatası hâlâ DÜŞTÜ olarak raporlanıyor.
  DERLEME_BELLEK_TAVANI="${DERLEME_BELLEK_TAVANI:-7200M}" \
    "${KOK}/scripts/derleme-kos.sh" dotnet publish "${KOK}/backend/EnderunAI.Api" \
      -c Release -o "$ARKA_PUBLISH" --nologo -v q > /tmp/duzen-publish.log 2>&1
  publish_kodu=$?

  if [ "$publish_kodu" = "75" ]; then
    log "ÖLÇEMEDİ: derleme koşucusu meşgul (başka bir derleme sürüyor)."
    log "ÖLÇEMEDİ: rig hiçbir şey ölçmedi — bu bir ÜRÜN BULGUSU DEĞİLDİR."
    log "ÖLÇEMEDİ: koşan iş bitince tekrar çalıştırın."
    exit 3
  fi

  if [ "$publish_kodu" != "0" ]; then
    tail -20 /tmp/duzen-publish.log >&2
    oldu "Arka uç publish başarısız (çıkış ${publish_kodu})."
  fi
  log "Arka uç publish hazır."
fi

# FAIL-CLOSED: canlının publish dizini ASLA kullanılmasın.
case "$ARKA_PUBLISH" in
  "${KOK}/publish"|"${KOK}/publish/") oldu "Rig canlının publish/ dizinini kullanamaz." ;;
esac

# RIG'İN KENDİ DİSK KÖKÜ (SIZINTI/1).
#
# Rig arka ucu `enderun_ai_test` ile koşuyor ama yazma kökleri sabit
# kodluydu: yüklediği her dosya CANLI `uploads/` ve `project-files/`
# dizinlerine düşüyordu. Rig'in canlı `.next` dizinine yazmasıyla
# aynı sınıf — orada da çözüm ayrı dizin olmuştu.
#
# Fail-closed kapı (YazmaKokleri) test veritabanına bağlı bir sürecin
# canlı köke bakmasını reddediyor; bu satırlar olmadan rig HİÇ
# AÇILMAZ. Kapıyı gevşetmiyoruz, rig'e kendi kökünü veriyoruz.
RIG_DISK="$(mktemp -d /tmp/duzen-rig-disk-XXXXXX)"
log "Rig disk kökü: ${RIG_DISK} (canlı dizinlere DOKUNULMUYOR)"

log "Arka uç ${ARKA_PORT} portunda açılıyor (enderun_ai_test)..."
DB_CONNECTION="$TEST_BAGLANTI" \
Uploads__Root="${RIG_DISK}/uploads" \
EInvoice__ArchivePath="${RIG_DISK}/e-fatura" \
Storage__ProjectFilesRoot="${RIG_DISK}/project-files" \
JWT_SECRET="duzen-testi-jwt-$(head -c 16 /dev/urandom | base64 | tr -d '/+=')" \
SEED_ADMIN_USERNAME="$KULLANICI" \
SEED_ADMIN_PASSWORD="$PAROLA" \
SEED_ADMIN_FULLNAME="Duzen Testi" \
ASPNETCORE_URLS="http://127.0.0.1:${ARKA_PORT}" \
ASPNETCORE_ENVIRONMENT="Production" \
  setsid dotnet "${ARKA_PUBLISH}/EnderunAI.Api.dll" > /tmp/duzen-arka.log 2>&1 &
ARKA_PID=$!

# ÖLÇÜLDÜ: boş bir enderun_ai_test'te tohumlama 120 sn'yi aşıyor
# (kategoriler, roller, izinler, hesap planı). 60 döngü yetmedi ve
# rig "arka uç açılmadı" diye düştü — arka uç ayaktaydı, HENÜZ
# TOHUMLUYORDU. Sınır ölçüme göre 300 sn'ye çekildi.
for i in $(seq 1 150); do
  curl -sf -m 2 "http://127.0.0.1:${ARKA_PORT}/api/health" >/dev/null 2>&1 && break
  [ "$i" -eq 150 ] && { tail -20 /tmp/duzen-arka.log >&2; oldu "Arka uç ${ARKA_PORT} 300 sn'de açılmadı."; }
  sleep 2
done
log "Arka uç hazır."

# ─── İKİNCİ KULLANICI + KONUŞMA.
#
# Composer YALNIZ bir konuşma seçiliyken render ediliyor (ölçüldü:
# `secili` yoksa "Soldan bir konuşma seçin" boş durumu çıkıyor).
# Ölçmek istediğimiz şey composer olduğu için konuşma ŞART.
#
# Parola karması tohumlanan kullanıcıdan kopyalanıyor: ikinci kullanıcı
# giriş yapmayacak, yalnız karşı taraf olarak duracak.
# DİKKAT — HEREDOC ALINTISIZ: ${KULLANICI} genişlemesi gerektiği için
# <<SQL alıntılanmadı. Sonuç: içerideki ters tırnak ve $ KABUK
# tarafından çalıştırılır. Yorumlarda ters tırnak kullanıldı ve
# "command not found" hataları üretti (ölçüldü). SQL bloğunda ters
# tırnak KULLANILMAZ.
log "Konuşma tohumlanıyor..."
# ON_ERROR_STOP ŞART — ÖLÇÜLMÜŞ FAIL-OPEN.
#
# psql, SQL hatasında bile 0 döner. Tohumlama tamamen başarısız
# olduğu hâlde rig "Konuşma hazır" deyip devam etti ve testler
# "konuşma bulunamadı" ile düştü: gerçek sebep (NOT NULL ihlali)
# hiç görünmedi. Bir kapının sessizce açık kalması, hiç olmamasından
# kötüdür.
sudo -u postgres psql -q -v ON_ERROR_STOP=1 -d enderun_ai_test <<SQL
DO \$\$
DECLARE
  v_ben uuid; v_o uuid; v_sirket uuid; v_konusma uuid; v_konusma2 uuid; v_kisitli uuid; v_personel_a uuid; v_personel_b uuid; v_simdi timestamptz := now();
BEGIN
  SELECT "Id" INTO v_ben FROM users WHERE "Username" = '${KULLANICI}';
  IF v_ben IS NULL THEN RAISE EXCEPTION 'Tohumlanan kullanıcı yok'; END IF;

  -- VERİ KAPSAMI — ÖLÇÜLMÜŞ EKSİK.
  -- SeedAdminUserAsync kullanıcıyı ve rolünü kuruyor ama UserDataScope
  -- kaydı AÇMIYOR. Kapsam kaydı olmayan kullanıcı konuşma listesinde
  -- hiçbir şey görmüyordu: ekran "Henüz konuşmanız yok" diyordu ve
  -- test composer'ı hiç bulamıyordu. Sebep üyelik değil, kapsamdı.
  DELETE FROM user_data_scopes WHERE "UserId" = v_ben;
  INSERT INTO user_data_scopes
    ("Id","UserId","ScopeType","IsActive","IsDeleted","CreatedAtUtc")
  VALUES (gen_random_uuid(), v_ben, 0, true, false, v_simdi);

  DELETE FROM users WHERE "Username" IN ('duzen-karsi-taraf', 'duzen-kisitli');
  DELETE FROM personnel WHERE "EmployeeNumber" IN ('DZN-A','DZN-B');
  INSERT INTO users
    ("Id","Username","FullName","PasswordHash","PasswordSalt","IsActive","WorkHoursExempt","CreatedAtUtc")
  SELECT gen_random_uuid(), 'duzen-karsi-taraf', 'Karsi Taraf',
         "PasswordHash", "PasswordSalt", true, true, v_simdi
  FROM users WHERE "Id" = v_ben
  RETURNING "Id" INTO v_o;

  SELECT "Id" INTO v_sirket FROM companies LIMIT 1;
  IF v_sirket IS NULL THEN RAISE EXCEPTION 'Sirket yok'; END IF;

  INSERT INTO conversations
    ("Id","CompanyId","Type","IsArchived","IsActive","IsDeleted","CreatedAtUtc","LastMessageAtUtc")
  VALUES (gen_random_uuid(), v_sirket, 0, false, true, false, v_simdi, v_simdi)
  RETURNING "Id" INTO v_konusma;

  INSERT INTO conversation_members
    ("Id","ConversationId","UserId","JoinedAtUtc","IsActive","IsDeleted","CreatedAtUtc")
  VALUES (gen_random_uuid(), v_konusma, v_ben, v_simdi, true, false, v_simdi),
         (gen_random_uuid(), v_konusma, v_o,   v_simdi, true, false, v_simdi);

  -- ÇOK MESAJ: az mesajla akış kısa kalır ve taşma HİÇ oluşmaz;
  -- test o zaman kırık düzende de yeşil verirdi.
  INSERT INTO messages
    ("Id","ConversationId","CompanyId","SenderUserId","Body","EditCount","IsActive","IsDeleted","CreatedAtUtc")
  SELECT gen_random_uuid(), v_konusma, v_sirket, v_ben,
         'Duzen testi mesaji ' || g || ' - satirin uzun olmasi icin biraz metin.',
         0, true, false, v_simdi
  FROM generate_series(1, 40) g;

  -- İKİNCİ KONUŞMA — SES TESTİNİN "BAŞKA KONUŞMA AÇIK" AYAĞI İÇİN.
  -- Tek konuşmayla "ekranda etkin konuşma" ile "başka konuşma"
  -- ayrımı ÖLÇÜLEMEZDİ.
  INSERT INTO conversations
    ("Id","CompanyId","Type","IsArchived","IsActive","IsDeleted","CreatedAtUtc","LastMessageAtUtc")
  VALUES (gen_random_uuid(), v_sirket, 0, false, true, false, v_simdi, v_simdi)
  RETURNING "Id" INTO v_konusma2;

  INSERT INTO conversation_members
    ("Id","ConversationId","UserId","JoinedAtUtc","IsActive","IsDeleted","CreatedAtUtc")
  VALUES (gen_random_uuid(), v_konusma2, v_ben, v_simdi, true, false, v_simdi),
         (gen_random_uuid(), v_konusma2, v_o,   v_simdi, true, false, v_simdi);

  INSERT INTO messages
    ("Id","ConversationId","CompanyId","SenderUserId","Body","EditCount","IsActive","IsDeleted","CreatedAtUtc")
  VALUES (gen_random_uuid(), v_konusma2, v_sirket, v_o, 'Ikinci konusma', 0, true, false, v_simdi);

  -- KARŞI TARAFA DA KAPSAM: ses testi onun adına mesaj gönderiyor.
  DELETE FROM user_data_scopes WHERE "UserId" = v_o;
  INSERT INTO user_data_scopes
    ("Id","UserId","ScopeType","IsActive","IsDeleted","CreatedAtUtc")
  VALUES (gen_random_uuid(), v_o, 0, true, false, v_simdi);

  -- KARŞI TARAFA ROL: mesaj gönderebilmesi için mesajlar.send lazım.
  DELETE FROM user_roles WHERE "UserId" = v_o;
  INSERT INTO user_roles ("UserId","RoleId")
  SELECT v_o, ur."RoleId"
  FROM user_roles ur WHERE ur."UserId" = v_ben;

  -- ═══ KISITLI KULLANICI (YETKI/1 OLCUMU) ═══
  --
  -- Rolu tam yetkili ama KISISEL DENY kaydi olan bir kullanici.
  -- Canlida uakkaya'nin durumu bu: 19 kisit kayitli, cozucude
  -- dogru okunuyor (olculdu) ama ekran acilmaya devam ediyor.
  -- Kusurun yasadigi durumu rig'de birebir kuruyoruz (Kural 81).
  INSERT INTO users
    ("Id","Username","FullName","PasswordHash","PasswordSalt","IsActive","WorkHoursExempt","CreatedAtUtc")
  SELECT gen_random_uuid(), 'duzen-kisitli', 'Kisitli Kullanici',
         "PasswordHash", "PasswordSalt", true, true, v_simdi
  FROM users WHERE "Id" = v_ben
  RETURNING "Id" INTO v_kisitli;

  INSERT INTO user_roles ("UserId","RoleId")
  SELECT v_kisitli, ur."RoleId" FROM user_roles ur WHERE ur."UserId" = v_ben;

  INSERT INTO user_data_scopes
    ("Id","UserId","ScopeType","IsActive","IsDeleted","CreatedAtUtc")
  VALUES (gen_random_uuid(), v_kisitli, 0, true, false, v_simdi);

  -- ═══ PERSONEL KAYITLARI (ISG/BENIM SIZINTI OLCUMU) ═══
  --
  -- /isg/benim ucu personel kimligini OTURUMDAN aliyor. "Baskasinin
  -- kaydini gosterebiliyor mu" sorusu ancak IKI kullanicinin da
  -- personel kaydi VARSA olculebilir; kayit yoksa ikisi de 404 alir
  -- ve olcum hicbir sey kanitlamaz (Kural 48).
  INSERT INTO personnel
    ("Id","CompanyId","EmployeeNumber","FirstName","LastName","Status","IsActive","IsDeleted","CreatedAtUtc")
  VALUES (gen_random_uuid(), v_sirket, 'DZN-A', 'Duzen', 'BirinciKisi', 1, true, false, v_simdi)
  RETURNING "Id" INTO v_personel_a;

  INSERT INTO personnel
    ("Id","CompanyId","EmployeeNumber","FirstName","LastName","Status","IsActive","IsDeleted","CreatedAtUtc")
  VALUES (gen_random_uuid(), v_sirket, 'DZN-B', 'Duzen', 'IkinciKisi', 1, true, false, v_simdi)
  RETURNING "Id" INTO v_personel_b;

  UPDATE users SET "PersonnelId" = v_personel_a WHERE "Id" = v_ben;
  UPDATE users SET "PersonnelId" = v_personel_b WHERE "Id" = v_o;

  -- DENY = Effect 2. Ucu de EKRANA baglı izinler.
  INSERT INTO user_permission_overrides
    ("Id","UserId","PermissionId","Effect","CreatedAtUtc")
  SELECT gen_random_uuid(), v_kisitli, p."Id", 2, v_simdi
  FROM permissions p
  WHERE p."Key" IN ('dashboard.view','accounting.view','tasks.view');

  -- ═══ TERCİH TOHUMU — KURAL 81'İN TERCİH HÂLİ ═══
  --
  -- ÖLÇÜLEN BOŞLUK (2026-09-08): PL1 kusuru YALNIZ lastConversationId
  -- DOLUYSA tetikleniyor. Taze kullanıcıda o alan null olur, panel
  -- seçimsiz açılır ve kusur HİÇ ÜREMEZ — test yeşil verir ve YALAN
  -- SÖYLER.
  --
  -- Mehmet'in canlı hesabında alan doluydu (Uğur AKKAYA konuşması) ve
  -- kusuru o yüzden gördü. Rig üretime benzemezse kusuru göremez;
  -- benzerlik yalnız ağ ve dizin değil, VERİ ve TERCİH için de geçerli.
  DELETE FROM user_ui_preferences WHERE "UserId" = v_ben;

  -- FavoritePaths NOT NULL ve varsayılanı yok: ilk denemede
  -- verilmedi ve tohum düştü. ON_ERROR_STOP sayesinde sessizce
  -- geçmedi — o kapının varlık sebebi tam olarak buydu.
  INSERT INTO user_ui_preferences
    ("Id","UserId","SidebarCollapsed","FavoritePaths","IsActive","IsDeleted",
     "CreatedAtUtc","LastConversationId","MessagePanelOpen","MessageSoundMuted")
  VALUES (gen_random_uuid(), v_ben, false, ARRAY[]::text[], true, false,
          v_simdi, v_konusma, false, false);
END \$\$;
SQL
log "Konuşma hazır."

# ─── ÖN YÜZ DERLENİYOR — SESSİZ ESKİ YAPI TUZAĞI.
#
# `next start` DERLENMİŞ `.next` dizinini servis eder. Derlemeden
# koşulursa rig, değiştirdiğimiz CSS'i değil ÖNCEKİ yapıyı ölçer ve
# "düzeltme işe yaramadı" ya da daha kötüsü "işe yaradı" der.
# Ölçtüğünü sandığın şeyle gerçekte ölçtüğün şeyin ayrışması.
#
# `--derleme-atla` yalnız yapının taze olduğu BİLİNDİĞİNDE kullanılır.
cd "$ON_YUZ"

# ═══ AYRI YAPI DİZİNİ — CANLIYI EZMEME KAPISI ═══
#
# ÖLÇÜLEN OLAY (2026-09-07): bu betik `npm run build`i CANLININ
# SERVİS ETTİĞİ dizinde koşuyordu. Canlı Next süreci 20:33:39'da
# başlamıştı; rig 20:50:40'ta `.next`i altından değiştirdi. Çalışan
# sunucu ESKİ manifest'i tutuyor, diskteki dosyalar YENİ — parça
# 404'leri ve bozuk render.
#
# Kullanıcıya "yetki matrisi bozuldu" ve "/dokumanlar açılmıyor" diye
# görünen şey buydu. Kod değil, dizin çakışmasıydı. Test düzeneğinin
# canlıyı bozması, testin kendisinden daha pahalıya mal oldu.
export NEXT_DIST_DIR=".next-duzen"

# FAIL-CLOSED: dizin canlınınkiyle aynıysa HİÇ BAŞLAMA.
if [ "$NEXT_DIST_DIR" = ".next" ] || [ -z "$NEXT_DIST_DIR" ]; then
  oldu "Yapı dizini canlınınkiyle aynı (.next). Rig canlıyı ezerdi."
fi

# Canlı servis bu dizini mi servis ediyor — ölç, varsayma.
CANLI_DIZIN="$(systemctl show enderunai-frontend -p WorkingDirectory --value 2>/dev/null)"
if [ "$CANLI_DIZIN" = "$ON_YUZ" ] && [ "$NEXT_DIST_DIR" = ".next" ]; then
  oldu "Canlı ön yüz bu dizinden servis ediyor ve yapı dizini ayrılmamış."
fi

if [ "${DERLEME_ATLA:-hayir}" = "evet" ]; then
  log "Ön yüz derlemesi ATLANDI (DERLEME_ATLA=evet)."
else
  log "Ön yüz derleniyor..."
  npm run build > /tmp/duzen-derleme.log 2>&1 \
    || { tail -30 /tmp/duzen-derleme.log >&2; oldu "Ön yüz derlemesi başarısız."; }
  log "Ön yüz derlemesi bitti."
fi

log "Ön yüz ${ON_PORT} portunda açılıyor..."
BACKEND_API_URL="http://127.0.0.1:${ARKA_PORT}" PORT="${ON_PORT}" \
  setsid env NEXT_DIST_DIR="$NEXT_DIST_DIR" npx next start -p "${ON_PORT}" > /tmp/duzen-on.log 2>&1 &
ON_PID=$!

for i in $(seq 1 60); do
  curl -sf -m 2 -o /dev/null "http://127.0.0.1:${ON_PORT}/login" && break
  [ "$i" -eq 60 ] && { tail -20 /tmp/duzen-on.log >&2; oldu "Ön yüz ${ON_PORT} açılmadı."; }
  sleep 2
done
log "Ön yüz hazır."

log "Vekil ${VEKIL_PORT} portunda aciliyor (uretimdeki nginx-in karsiligi)..."
VEKIL_PORT="$VEKIL_PORT" VEKIL_NEXT_PORT="$ON_PORT" VEKIL_ARKA_PORT="$ARKA_PORT" \
  setsid node "${KOK}/deploy/scripts/duzen-vekil.mjs" > /tmp/duzen-vekil.log 2>&1 &
VEKIL_PID=$!

for i in $(seq 1 30); do
  curl -sf -m 2 -o /dev/null "http://127.0.0.1:${VEKIL_PORT}/login" && break
  [ "$i" -eq 30 ] && { cat /tmp/duzen-vekil.log >&2; oldu "Vekil acilmadi."; }
  sleep 1
done
log "Vekil hazir."

log "Playwright koşuyor..."
DUZEN_URL="http://127.0.0.1:${VEKIL_PORT}" \
DUZEN_KULLANICI="$KULLANICI" \
DUZEN_PAROLA="$PAROLA" \
DUZEN_KARSI_KULLANICI="duzen-karsi-taraf" \
DUZEN_KISITLI_KULLANICI="duzen-kisitli" \
  npx playwright test --config=playwright.config.ts "$@"
