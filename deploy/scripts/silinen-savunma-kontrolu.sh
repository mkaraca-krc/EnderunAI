#!/usr/bin/env bash
#
# SİLİNEN SAVUNMA KONTROLÜ
#
# NEDEN VAR: `2d90c946` (MERKEZ/1) merkez kuralını ortak metoda taşırken
# POST gövdesini METİN ARALIĞIYLA kesti ve aralıkta duran ATAMA
# DOĞRULAMASINI da götürdü. 26 satır sessizce silindi, canlıya çıktı ve
# 2965 test, dört cırcır, kapsam tabanı — hiçbiri görmedi. Sebep: silinen
# kod testsizdi.
#
# NE YAPAR: bir commit'in (ya da çalışma ağacının) SİLDİĞİ satırlara
# bakar. Savunma şekilli olanları OLDUĞU GİBİ ekrana basar ve commit
# mesajında beyan edilmelerini ister.
#
# NE YAPMAZ: taban çizgisi tutmaz, sayı saymaz, hiçbir şeyi engellemez.
# Sayım tabanı ÖNERİLDİ VE REDDEDİLDİ — yanlış katmanda ölçüyordu:
# bugün olan şey bir sayının düşmesi değil, bir DEĞİŞİKLİĞİN
# amaçladığından fazlasını götürmesiydi. Doğru yer diff'in kendisi.
# Ayrıca toplamı sabit tutan bir silme (başka yerde +2, burada -2) sayım
# tabanından sessizce geçerdi ve en tehlikeli silme tam olarak odur.
#
# ═══ DÜRÜST SINIR ═══
# BU KONTROL SİLMEYE KARŞI KORUR, ETKİSİZLEŞTİRMEYE KARŞI DEĞİL.
# `if (false && ...)` yazan bir değişiklik hiçbir satır silmez ve bu
# kontrolden sessizce geçer — sonda A3'te tam olarak bu denendi ve
# kontrol görmezdi. Etkisizleştirmeye karşı tek koruma, savunmayı
# sınayan TESTTİR. Bu kontrol yalnızca testsiz savunmanın sessizce yok
# olmasını GÜRÜLTÜLÜ hâle getirir.
#
# ÖLÇÜM: son 50 commit'e geriye dönük koşuldu — 2 alarm (%4), 0 yanlış
# alarm. `2d90c946` yakalanıyor; yakalanmasaydı kontrol yanlış yazılmış
# olurdu.
#
# KULLANIM:
#   silinen-savunma-kontrolu.sh              # çalışma ağacı (commit öncesi)
#   silinen-savunma-kontrolu.sh <commit>     # tek commit — UYARI, çıkış 0
#   silinen-savunma-kontrolu.sh <commit> --kapi   # BEYAN ARAR, yoksa çıkış 1
#
# İKİ KİP, VE SEBEBİ:
#
# Birincil yer ELLE ÇAĞRIDIR: commit'ten önce koşulur, kişi sildiğini
# görür ve kararını orada verir. O kipte çıkış her zaman 0.
#
# safe-deploy içinde ise `--kapi` ile çağrılır ve BEYAN ARAR. İlk
# yazımım orada da yalnız günlüğe basıyordu; Mehmet düzeltti ve gerekçe
# benim kendi cümlemdi: "ölçüm, ancak okunabildiği yerde ölçümdür."
# Otomatik bir yayın turunda günlüğe basılan uyarı OKUNMAZ — o hâliyle
# kontrol safe-deploy içinde süs olurdu.
#
# KAPI "SİLME YASAK" DEMİYOR, "SİLDİĞİNİ SÖYLE" DİYOR. Meşru taşımalar
# engellenmiyor: maliyeti commit mesajına bir cümle.
#
# BEYAN BİÇİMİ: commit mesajında satır başında `SAVUNMA-BEYAN:` geçmesi.
# Satır başı seçildi çünkü gövde metninde tesadüfen oluşması zor ve
# `git log --format=%B | grep '^SAVUNMA-BEYAN:'` ile kesin okunuyor.
#
# ═══ BU KONTROL KENDİ HATASIYLA DOĞDU ═══
# safe-deploy'a bağlarken `$SCRIPT_DIR` yazdım; o değişken safe-deploy'da
# TANIMLI DEĞİL. `bash -n` "sözdizimi geçerli" dedi ve beni yanılttı:
# BASH -N SÖZDİZİMİNİ DENETLER, TANIMLILIĞI DENETLEMEZ. Sözdizimi
# kontrolü bir çalıştırma kontrolü değildir. Yol `${REPO_ROOT}` ile
# düzeltildi ve safe-deploy'un çağırdığı BİÇİMDE denenerek doğrulandı.

set -uo pipefail

HEDEF="${1:-}"
KIP="${2:-uyari}"

DESEN='return[[:space:]]+(BadRequest|Forbid|Unauthorized|NotFound)[[:space:]]*\(|throw[[:space:]]+new[[:space:]]+[A-Za-z]|if[[:space:]]*\([[:space:]]*![[:space:]]*await[[:space:]]|\[RequirePermission'

if [ -n "$HEDEF" ]; then
    KAYNAK="$(git show "$HEDEF" --unified=0 -- '*.cs' '*.ts' '*.tsx' 2>/dev/null)"
    BASLIK="commit $HEDEF"
else
    KAYNAK="$(git diff --unified=0 -- '*.cs' '*.ts' '*.tsx' 2>/dev/null; \
              git diff --cached --unified=0 -- '*.cs' '*.ts' '*.tsx' 2>/dev/null)"
    BASLIK="çalışma ağacı"
fi

# Silinen satırlar: '-' ile başlayan ama '---' olmayan.
# Yorum satırları elenir: yorumda geçen bir `throw` savunma değildir.
BULGU="$(printf '%s\n' "$KAYNAK" \
    | grep '^-' | grep -v '^---' | sed 's/^-//' \
    | grep -vE '^[[:space:]]*(//|\*|/\*)' \
    | grep -E "$DESEN" || true)"

if [ -z "$BULGU" ]; then
    echo "[silinen-savunma] $BASLIK: savunma şekilli satır silinmemiş."
    exit 0
fi

# Kapı kipinde beyan aranır. Beyan commit mesajında, satır başında.
BEYAN=""
if [ "$KIP" = "--kapi" ] && [ -n "$HEDEF" ]; then
    BEYAN="$(git log --format=%B -1 "$HEDEF" 2>/dev/null \
             | grep -E '^SAVUNMA-BEYAN:' || true)"
fi

SAYI="$(printf '%s\n' "$BULGU" | grep -c . || true)"

cat <<UYARI

╔══════════════════════════════════════════════════════════════════╗
║  SİLİNEN SAVUNMA SATIRLARI — $BASLIK
╚══════════════════════════════════════════════════════════════════╝

Bu değişiklik $SAYI savunma şekilli satır SİLİYOR:

UYARI

printf '%s\n' "$BULGU" | sed 's/^/    /'

cat <<'UYARI'

SORU: bunların her biri KASITLI mı?

  - Kasıtlıysa commit mesajında BEYAN ET (neyin yerine geçtiğini yaz).
  - Kasıtlı değilse KESİM YANLIŞ (Kural 72).

Hatırlatma: bu kontrol SİLMEYE karşı korur, ETKİSİZLEŞTİRMEYE karşı
değil. Bir savunmayı `if (false && ...)` ile kapatan değişiklik buradan
sessizce geçer. Tek gerçek koruma, savunmayı sınayan testtir.

UYARI

if [ "$KIP" != "--kapi" ]; then
    exit 0
fi

if [ -n "$BEYAN" ]; then
    echo "BEYAN BULUNDU — geçildi:"
    printf '%s\n' "$BEYAN" | sed 's/^/    /'
    echo
    exit 0
fi

cat <<'DURDU'
╔══════════════════════════════════════════════════════════════════╗
║  YAYIN DURDU — BEYAN YOK                                         ║
╚══════════════════════════════════════════════════════════════════╝

Commit mesajında satır başında `SAVUNMA-BEYAN:` bulunamadı.

Bu kapı "silme yasak" demiyor, "sildiğini söyle" diyor. Yukarıdaki
satırların her biri kasıtlıysa commit mesajına şu biçimde bir satır
ekleyin ve neyin yerine geçtiğini yazın:

    SAVUNMA-BEYAN: <n> savunma satırı silindi — <neyin yerine geçtiği>

Kasıtlı değilse kesim yanlıştır (Kural 72).

DURDU

exit 1
