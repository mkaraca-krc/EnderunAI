#!/usr/bin/env python3
"""HIZLI SIR TARAYICI — PUSH EDİLECEK COMMIT ARALIĞI.

═══ NEDEN ARALIK, NEDEN UÇ NOKTA FARKI DEĞİL ═══

İlk tasarım `git diff <uzak>..<yerel>` ile UÇ NOKTALAR arasındaki farkı
tarayacaktı. O tasarım şu vakayı KAÇIRIR:

    commit 1: dosyaya sır eklendi
    commit 2: dosya silindi

Uç noktalar arasında dosya yok — fark boş. Ama sır GEÇMİŞE YAZILDI ve
oradan silinemez. Sır geçmişe COMMIT ANINDA girer, uç noktada değil.

Bu yüzden her commit tek tek geziliyor ve her dosyanın O COMMIT'TEKİ
hâli okunuyor.

═══ NEDEN AYRI TARAYICI ═══

`SecretInSourceGuardTests` tüm depoyu tarıyor ama `dotnet test` arka
ucu yeniden derlediği için 278 saniye sürüyor. Push kancasında bu,
SSH bağlantısının uzak uç tarafından kapatılmasına yol açtı.

Bu tarayıcı aynı işi push edilen ARALIK için saniyeler içinde yapıyor.
Kapsamlar farklı, LİSTE AYNI: ikisi de `deploy/bekci/uretim-sir-adlari.txt`
okuyor.

═══ NEDEN BU KAPI PUSH ÖNCESİNDE ═══

Sır bekçisinin kaçırdığı şey GERİ ALINAMAZ — geçmişe yazılır. Diğer
kapıların kaçırdığı bir sonraki turda yakalanır. Asimetri, kapının
maliyetini haklı çıkarıyor.

KULLANIM:
    sir-tara.py <uzak_sha> <yerel_sha>
    sir-tara.py --commitler <sha> [<sha> ...]
"""

import os
import re
import subprocess
import sys

KOK = os.environ.get("REPO_ROOT", "/var/www/enderun-ai")
ADLAR_DOSYASI = os.path.join(KOK, "deploy", "bekci", "uretim-sir-adlari.txt")
ORTAM_DOSYASI = "/etc/enderunai/backend.env"

# ═══ ÜST SINIR — SESSİZ KISALTMA YOK ═══
#
# Ölçüldü (2026-09-04): son 14 günde günlük commit 2-14; deponun
# tarihindeki en yoğun gün 94 commit (toplu bir gün). Sınır 50
# olduğunda tüm geçmişte YALNIZ BİR GÜN onu aşardı — ve o gün zaten
# tam tarama hak eden bir gün.
#
# Sınır aşılınca sessizce kısaltılmıyor: KIRMIZI veriliyor ve tam
# tarama isteniyor. Kısaltmak, taranmadığı hâlde "tarandı" demek olurdu.
UST_SINIR = 50

# İKİLİ UZANTILAR — metin olarak okunamaz.
IKILI = {
    ".png", ".jpg", ".jpeg", ".gif", ".ico", ".pdf", ".zip", ".gz",
    ".dll", ".exe", ".so", ".woff", ".woff2", ".ttf", ".eot",
}


def git(*args, ikili=False):
    r = subprocess.run(
        ["git", *args], cwd=KOK, capture_output=True,
        text=not ikili, errors="replace" if not ikili else None)
    return r.returncode, r.stdout if r.returncode == 0 else ""


def sir_adlari():
    """Tek listeden zorunlu sır adlarını okur."""
    zorunlu = []
    if not os.path.exists(ADLAR_DOSYASI):
        return None
    for satir in open(ADLAR_DOSYASI, encoding="utf-8"):
        s = satir.strip()
        if not s or s.startswith("#"):
            continue
        parcalar = s.split(None, 2)
        if len(parcalar) >= 2 and parcalar[0] == "zorunlu":
            zorunlu.append(parcalar[1])
    return zorunlu


def uretim_sirlari(adlar):
    """Ortam dosyasından DEĞERLERİ okur. Hiçbir yere basılmaz."""
    ham = {}
    if not os.path.exists(ORTAM_DOSYASI):
        return {}
    try:
        icerik = open(ORTAM_DOSYASI, encoding="utf-8", errors="replace").read()
    except PermissionError:
        return {}

    for satir in icerik.split("\n"):
        s = satir.strip()
        if not s or s.startswith("#") or "=" not in s:
            continue
        ad, _, deger = s.partition("=")
        deger = deger.strip().strip('"').strip("'")
        if deger:
            ham[ad.strip()] = deger

    sonuc = {}
    for ad in adlar:
        if ad == "DB_PAROLASI":
            m = re.search(r"Password=([^;]+)", ham.get("DB_CONNECTION", ""))
            if m:
                sonuc[ad] = m.group(1)
        elif ad in ham:
            sonuc[ad] = ham[ad]
    return sonuc


def commitler(argv):
    if argv and argv[0] == "--commitler":
        return argv[1:]

    if len(argv) < 2:
        print("[sir-tara] KULLANIM: sir-tara.py <uzak_sha> <yerel_sha>", file=sys.stderr)
        sys.exit(2)

    uzak, yerel = argv[0], argv[1]

    # İLK PUSH: uzak taraf 0000... — aralık yok, push edilen tüm
    # commit'ler gezilir. Aynı döngü, farklı başlangıç.
    if set(uzak) <= {"0"}:
        kod, cikti = git("rev-list", yerel, "--not", "--all")
        if kod != 0:
            kod, cikti = git("rev-list", yerel)
    else:
        kod, cikti = git("rev-list", f"{uzak}..{yerel}")

    if kod != 0:
        print("[sir-tara] Aralık çözülemedi.", file=sys.stderr)
        sys.exit(2)

    return [x for x in cikti.split("\n") if x.strip()]


def main():
    adlar = sir_adlari()
    if adlar is None:
        print(f"[sir-tara] HATA: sır adları listesi yok: {ADLAR_DOSYASI}", file=sys.stderr)
        return 2
    if not adlar:
        print("[sir-tara] HATA: listede zorunlu sır yok; tarama hiçbir şey sınamaz.", file=sys.stderr)
        return 2

    sirlar = uretim_sirlari(adlar)
    if not sirlar:
        # SESSİZ ATLAMA YOK — ama bu ortamda (ör. CI) sırlar okunamaz.
        print("[sir-tara] ATLANDI: üretim ortam dosyası okunamadı "
              f"({ORTAM_DOSYASI}). Bu ortamda gerçek sır kontrolü YAPILAMADI.")
        return 0

    eksik = [a for a in adlar if a not in sirlar]
    if eksik:
        print("[sir-tara] KONTROL EDİLEMEDİ — ortamda bulunamayan sırlar: "
              + ", ".join(eksik), file=sys.stderr)
        return 1

    shalar = commitler(sys.argv[1:])
    if not shalar:
        print("[sir-tara] Taranacak commit yok.")
        return 0

    if len(shalar) > UST_SINIR:
        print(f"[sir-tara] ARALIK ÇOK BÜYÜK: {len(shalar)} commit "
              f"(üst sınır {UST_SINIR}).", file=sys.stderr)
        print("[sir-tara] Sessizce kısaltılmadı. TAM TARAMA GEREKLİ: "
              "yayın turundaki sır bekçisi bu aralığı kapsar.", file=sys.stderr)
        return 1

    bulgular = []
    taranan = 0

    for sha in shalar:
        kod, cikti = git("diff-tree", "--no-commit-id", "--name-only", "-r", sha)
        if kod != 0:
            continue

        for yol in [y for y in cikti.split("\n") if y.strip()]:
            if os.path.splitext(yol)[1].lower() in IKILI:
                continue

            # O COMMIT'TEKİ hâli. Dosya o commit'te SİLİNDİYSE `git show`
            # düşer ve atlanır — sır zaten eklendiği commit'te taranmış
            # olur.
            kod2, icerik = git("show", f"{sha}:{yol}")
            if kod2 != 0:
                continue

            taranan += 1
            for i, satir in enumerate(icerik.split("\n"), start=1):
                for ad, deger in sirlar.items():
                    if deger in satir:
                        # YALNIZ AD VE KONUM — DEĞER ASLA.
                        bulgular.append(f"{sha[:8]} · {yol}:{i} → GERÇEK ÜRETİM SIRRI: {ad}")

    if bulgular:
        print("╔════════════════════════════════════════════════════╗", file=sys.stderr)
        print("║  GERÇEK ÜRETİM SIRRI COMMIT'LENMİŞ                 ║", file=sys.stderr)
        print("╚════════════════════════════════════════════════════╝", file=sys.stderr)
        for b in bulgular:
            print("  " + b, file=sys.stderr)
        print("", file=sys.stderr)
        print("Bu bir 'sır benzeri dizgi' DEĞİL: değer canlıdakiyle birebir aynı.",
              file=sys.stderr)
        print("PUSH EDİLİRSE GERİ ALINAMAZ — sır geçmişe yazılır ve silinemez.",
              file=sys.stderr)
        print("Commit'i düzeltin (ör. `git rebase -i`) ve sırrı ortam "
              "değişkenine taşıyın.", file=sys.stderr)
        return 1

    print(f"[sir-tara] Temiz: {len(shalar)} commit, {taranan} dosya sürümü tarandı.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
