#!/usr/bin/env bash
#
# HESAP BOYUT BAYRAĞI — UYGULA / GERİ AL (E4, 2026-09-13)
#
# ═══ NE YAPAR ═══
#
# `150` ve `153` (bilanço stok hesapları) için `RequiresProject`
# bayrağını uygulamanın KENDİ UCUNDAN değiştirir. Ham SQL kullanmaz:
# veriyi uygulama değiştirir, böylece doğrulama, eşzamanlılık kontrolü
# (`Surum`) ve satır düzeyi iz (`UpdatedByUserId`) devrede kalır.
#
# ═══ NEDEN (ölçüm) ═══
#
# İlke: proje ve masraf merkezi SONUÇ hesaplarının (6xx/7xx) boyutudur.
# Bilanço hesabında zorunluluk, düşünülmüş bir karar değilse hatadır.
# 150/153'te bu bayrak yüzünden stok→muhasebe hattının DÖRT yolu
# patlıyordu ve canlıda o hesaplara ait tek fiş satırı yoktu.
#
# ═══ GERİ ALMA PLAN DEĞİL KOMUTTUR ═══
#
# "Gerekirse true'ya çeviririz" bir plan değildir. `--geri-al` önceden
# yazıldı ve prova zemininde KOŞULDU; canlıda da aynı komut koşacak.
#
# ═══ KULLANIM ═══
#
#   hesap-bayragi.sh --uygula  --uc http://127.0.0.1:5155 --kullanici X --parola Y
#   hesap-bayragi.sh --geri-al --uc http://127.0.0.1:5155 --kullanici X --parola Y
#
# Parola argümanla verilir ve HİÇBİR YERE yazılmaz; betik günlüğe
# yalnız hesap kodunu ve önce/sonra değerini basar.
#
set -uo pipefail

KIP=""
UC="http://127.0.0.1:5155"
KULLANICI=""
PAROLA=""

while [ $# -gt 0 ]; do
    case "$1" in
        --uygula)    KIP="false"; shift ;;   # RequiresProject = false
        --geri-al)   KIP="true";  shift ;;   # RequiresProject = true
        --uc)        UC="$2"; shift 2 ;;
        --kullanici) KULLANICI="$2"; shift 2 ;;
        --parola)    PAROLA="$2"; shift 2 ;;
        *) echo "bilinmeyen argüman: $1" >&2; exit 64 ;;
    esac
done

[ -n "$KIP" ] || { echo "--uygula ya da --geri-al gerekli" >&2; exit 64; }
[ -n "$KULLANICI" ] && [ -n "$PAROLA" ] || { echo "--kullanici ve --parola gerekli" >&2; exit 64; }

export HESAP_UC="$UC" HESAP_KIP="$KIP" HESAP_KULLANICI="$KULLANICI" HESAP_PAROLA="$PAROLA"

python3 - <<'PY'
import json, os, sys, urllib.request, urllib.error

UC = os.environ["HESAP_UC"]
HEDEF = os.environ["HESAP_KIP"] == "true"
KODLAR = ("150", "153")

def cagir(yontem, yol, jeton=None, govde=None):
    istek = urllib.request.Request(
        UC + yol, method=yontem,
        data=json.dumps(govde).encode() if govde is not None else None)
    istek.add_header("Content-Type", "application/json")
    if jeton:
        istek.add_header("Authorization", "Bearer " + jeton)
    try:
        with urllib.request.urlopen(istek, timeout=60) as yanit:
            metin = yanit.read().decode()
            return yanit.status, (json.loads(metin) if metin.strip().startswith(("{", "[")) else metin)
    except urllib.error.HTTPError as e:
        metin = e.read().decode()
        return e.code, (json.loads(metin) if metin.strip().startswith(("{", "[")) else metin)

durum, govde = cagir("POST", "/api/auth/login",
                     govde={"username": os.environ["HESAP_KULLANICI"],
                            "password": os.environ["HESAP_PAROLA"]})
if durum != 200:
    print(f"[hesap-bayragi] GİRİŞ BAŞARISIZ: HTTP {durum}", file=sys.stderr)
    sys.exit(2)
jeton = govde["token"]

durum, liste = cagir("GET", "/api/accounting-accounts?pageSize=2000", jeton)
if durum != 200:
    print(f"[hesap-bayragi] hesap listesi okunamadı: HTTP {durum}", file=sys.stderr)
    sys.exit(2)

satirlar = liste.get("items") if isinstance(liste, dict) else liste
kimlik = {x["code"]: x["id"] for x in satirlar if x.get("code") in KODLAR}

eksik = [k for k in KODLAR if k not in kimlik]
if eksik:
    print(f"[hesap-bayragi] ÖLÇEMEDİ: hesap bulunamadı: {eksik}", file=sys.stderr)
    sys.exit(3)

hata = 0
for kod in KODLAR:
    durum, mevcut = cagir("GET", f"/api/accounting-accounts/{kimlik[kod]}", jeton)
    if durum != 200:
        print(f"[hesap-bayragi] {kod}: okunamadı HTTP {durum}", file=sys.stderr)
        hata = 1
        continue

    once = mevcut.get("requiresProject")
    if once == HEDEF:
        print(f"[hesap-bayragi] {kod}: zaten {HEDEF} — dokunulmadı")
        continue

    istek = {
        "parentAccountId": mevcut.get("parentAccountId"),
        "name": mevcut.get("name"),
        "surum": mevcut.get("surum") or mevcut.get("updatedAtUtc") or mevcut.get("createdAtUtc"),
        "description": mevcut.get("description"),
        "nature": mevcut.get("nature", 0),
        "isPostingAllowed": mevcut.get("isPostingAllowed", True),
        "requiresProject": HEDEF,
        # MASRAF MERKEZİNE DOKUNULMUYOR: mevcut değer aynen geri yazılır.
        "requiresCostCenter": mevcut.get("requiresCostCenter", False),
        "currencyCode": mevcut.get("currencyCode"),
    }
    durum, yanit = cagir("PUT", f"/api/accounting-accounts/{kimlik[kod]}", jeton, istek)

    durum2, sonrasi = cagir("GET", f"/api/accounting-accounts/{kimlik[kod]}", jeton)
    sonra = sonrasi.get("requiresProject") if durum2 == 200 else "OKUNAMADI"

    print(f"[hesap-bayragi] {kod}: proje {once} → {sonra}  (PUT HTTP {durum})")
    if durum >= 300 or sonra != HEDEF:
        print(f"[hesap-bayragi]   BAŞARISIZ: {str(yanit)[:160]}", file=sys.stderr)
        hata = 1

sys.exit(hata)
PY
