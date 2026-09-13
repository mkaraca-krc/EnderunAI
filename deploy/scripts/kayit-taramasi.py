#!/usr/bin/env python3
"""
KAYIT/1 — HÜKÜM SATIRLARINI İKİYE AYIRIR: ÖLÇÜLMÜŞ / İDDİA.

═══ NEDEN VAR ═══

Bu hafta ÜÇ yanlış kayıt tesadüfen yakalandı:
  · K10       — "istek atmıyor"                      (yanlış)
  · GÜNLÜK/1  — "sorgu dizgeleri yazılmıyor"         (yanlış)
  · S1        — kod yorumu "zaten isActive ile ayırıyor" (yanlış)

Üçü de bir HÜKÜM'dü ve hiçbirinin yanında ölçüm yoktu. Tesadüfe
bırakılamaz.

═══ KAPSAM AÇIKÇA İLAN EDİLİR (Kural 84) ═══

Bu araç hüküm AVLAR, hüküm KANITLAMAZ. Çıktısı adaylardır; hangisinin
gerçekten yanlış olduğunu ancak ölçüm söyler. Süzgecin kaçırdığı bir
hüküm "yok" demek DEĞİLDİR — bu yüzden her koşuda taranan dosya ve
satır sayısı ile pozitif kontrol sonucu basılır.

═══ TANIMLAR — OPERASYONEL, TARTIŞMAYA AÇIK ═══

HÜKÜM ADAYI  : sistemin davranışı hakkında şimdiki zamanda bir şey
               söyleyen satır (aşağıdaki DAVRANIS kalıpları).
ÖLÇÜLMÜŞ     : aynı satırda ya da ±3 satırda ölçüm izi var
               (sayı+birim, "ölçüldü", dosya:satır, test adı,
               "POZİTİF KONTROL", yüzde, "KIRMIZI/YEŞİL").
İDDİA        : hüküm adayı, ölçüm izi YOK.
"""
import os, re, sys, json

KOK = os.environ.get("REPO_ROOT", "/var/www/enderun-ai")

BELGELER = ["DURUM.md", "docs/DERSLER.md", "docs/CANLI-1.md", "docs/AJANDA-1.md",
            "docs/KURTARMA.md", "docs/SUNUCU-DISI-KOPYA.md", "docs/AJAN-1-TEKLIF.md",
            "docs/MEHMET-BEY-150-153-TALIMAT.md"]

KOD_UZANTILARI = {".cs", ".ts", ".tsx", ".sh", ".py"}
KOD_KOKLERI = ["backend/EnderunAI.Api", "frontend/enderun-ai/app",
               "frontend/enderun-ai/components", "frontend/enderun-ai/lib",
               "deploy/scripts", "scripts"]

# ── DAVRANIŞ İDDİASI KALIPLARI ──────────────────────────────────────
DAVRANIS = re.compile(
    r"\bzaten\b|\basla\b|\bher zaman\b|\bhiçbir\b|\bhiç bir\b|garanti"
    r"|\bdaima\b|\bmutlaka\b"
    r"|\b\w+(iyor|ıyor|uyor|üyor)\b"          # ediyor / yapıyor / tutuyor
    r"|\b\w+(maz|mez)\b",                      # yazmaz / düşmez
    re.IGNORECASE)

# ── ÖLÇÜM İZLERİ ────────────────────────────────────────────────────
OLCUM = re.compile(
    r"ölçüldü|ölçüm|ölçtü|sayıldı|POZİTİF KONTROL|olumsuz kontrol"
    r"|KIRMIZI|YEŞİL|\bmutasyon\b"
    r"|\d+\s*(satır|dosya|tablo|commit|test|kez|sn|dk|MB|GB|kB|bayt|px|%)"
    r"|[A-Za-z0-9_./\-]+\.(cs|ts|tsx|sh|py|md):\d+"
    r"|\bTests\b|\[ÖLÇÜLMEDİ\]",
    re.IGNORECASE)

# ── YAPISAL MUHAFAZA YORUMLARI — HARİÇ ──────────────────────────────
# Bunlar davranış İDDİASI değil, KURAL/GEREKÇE metnidir.
MUHAFAZA = re.compile(
    r"NEDEN VAR|YASAK|KURAL \d|═══|───|eslint|SPDX|Copyright"
    r"|KULLANIM|UYARI:|TODO|FIXME",
    re.IGNORECASE)

# ── ÖNEM — YANLIŞSA NE OLUR ─────────────────────────────────────────
ONEM = [
    (3, re.compile(r"sır|parola|jeton|token|yetki|izin|kimlik|şifre|güvenlik|denetim izi", re.I)),
    (3, re.compile(r"yedek|geri yükle|kurtarma|sil|kayıp|veri kaybı|düşür", re.I)),
    (3, re.compile(r"muhasebe|fiş|tutar|bakiye|maliyet|fatura|ödeme|para", re.I)),
    (2, re.compile(r"göç|migration|şema|veritabanı|yayın|deploy", re.I)),
    (1, re.compile(r".", re.S)),
]
ONEM_ADI = {3: "YÜKSEK", 2: "ORTA", 1: "DÜŞÜK"}

def onem(metin):
    for puan, desen in ONEM:
        if desen.search(metin):
            return puan
    return 1

def yorum_mu(satir, uzanti):
    s = satir.strip()
    if uzanti in {".cs", ".ts", ".tsx"}:
        return s.startswith("//") or s.startswith("*") or s.startswith("/*")
    if uzanti in {".sh", ".py"}:
        return s.startswith("#")
    return False

def tara_belge(yol):
    tam = os.path.join(KOK, yol)
    if not os.path.exists(tam):
        return None
    satirlar = open(tam, encoding="utf-8", errors="replace").read().split("\n")
    kod_bloku = False
    bulgular = []
    for i, s in enumerate(satirlar):
        if s.strip().startswith("```"):
            kod_bloku = not kod_bloku
            continue
        if kod_bloku or len(s.strip()) < 25:
            continue
        if not DAVRANIS.search(s):
            continue
        pencere = "\n".join(satirlar[max(0, i - 3): i + 4])
        olculdu = bool(OLCUM.search(pencere))
        bulgular.append((yol, i + 1, s.strip()[:150], olculdu, onem(s)))
    return bulgular, len(satirlar)

def kod_dosyalari():
    for kok in KOD_KOKLERI:
        tam = os.path.join(KOK, kok)
        for dizin, altlar, dosyalar in os.walk(tam):
            altlar[:] = [a for a in altlar if a not in
                         {"node_modules", "bin", "obj", ".next", "Migrations"}]
            for d in dosyalar:
                if os.path.splitext(d)[1] in KOD_UZANTILARI:
                    yield os.path.join(dizin, d)

def tara_kod():
    bulgular = []
    dosya_sayisi = satir_sayisi = yorum_sayisi = 0
    for tam in kod_dosyalari():
        uz = os.path.splitext(tam)[1]
        try:
            satirlar = open(tam, encoding="utf-8", errors="replace").read().split("\n")
        except OSError:
            continue
        dosya_sayisi += 1
        satir_sayisi += len(satirlar)
        goreli = os.path.relpath(tam, KOK)
        for i, s in enumerate(satirlar):
            if not yorum_mu(s, uz):
                continue
            yorum_sayisi += 1
            g = s.strip().lstrip("/*# ").strip()
            if len(g) < 25 or MUHAFAZA.search(g):
                continue
            if not DAVRANIS.search(g):
                continue
            pencere = "\n".join(satirlar[max(0, i - 3): i + 4])
            olculdu = bool(OLCUM.search(pencere))
            bulgular.append((goreli, i + 1, g[:150], olculdu, onem(g)))
    return bulgular, dosya_sayisi, satir_sayisi, yorum_sayisi

def main():
    print("═══ KAPSAM (Kural 84: taranan ilan edilir) ═══")
    belge_bulgu, belge_satir, belge_dosya = [], 0, 0
    for b in BELGELER:
        sonuc = tara_belge(b)
        if sonuc is None:
            print(f"  ATLANDI (yok): {b}")
            continue
        bulgular, n = sonuc
        belge_bulgu += bulgular
        belge_satir += n
        belge_dosya += 1
    print(f"  belge : {belge_dosya} dosya, {belge_satir} satır")

    kod_bulgu, kd, ks, ky = tara_kod()
    print(f"  kod   : {kd} dosya, {ks} satır, {ky} yorum satırı")

    # ── POZİTİF KONTROL: süzgeç kör mü ──────────────────────────────
    ornek_olculdu = [b for b in belge_bulgu if b[3]]
    ornek_iddia = [b for b in belge_bulgu if not b[3]]
    print(f"  POZİTİF KONTROL: ölçülmüş={len(ornek_olculdu)} iddia={len(ornek_iddia)}"
          f" — ikisi de sıfırdan büyük olmalı: "
          f"{'GEÇTİ' if ornek_olculdu and ornek_iddia else 'ÖLÇEMEDİ'}")

    for ad, kume in (("BELGE", belge_bulgu), ("KOD YORUMU", kod_bulgu)):
        o = sum(1 for b in kume if b[3])
        i = sum(1 for b in kume if not b[3])
        print(f"\n═══ {ad} ═══")
        print(f"  hüküm adayı : {len(kume)}")
        print(f"  ÖLÇÜLMÜŞ    : {o}")
        print(f"  İDDİA       : {i}" + (f"  (%{100*i/len(kume):.0f})" if kume else ""))
        for puan in (3, 2, 1):
            n = sum(1 for b in kume if not b[3] and b[4] == puan)
            print(f"     önem {ONEM_ADI[puan]:<7}: {n}")

    # ── SIRALAMA: MUTLAK NİCELEYİCİ EN TEHLİKELİ SINIF ──────────────
    #
    # "asla / hiçbir / her zaman / zaten / garanti" diyen bir hüküm, tek
    # bir karşı örnekle çöker ve üstüne güvenlik kararı kurulur. Ölçüm
    # izi olmayan böyle bir cümle, listenin en üstüne çıkar.
    MUTLAK = re.compile(r"\basla\b|\bhiçbir\b|\bhiç bir\b|\bher zaman\b"
                        r"|\bzaten\b|garanti|\bdaima\b|\bmutlaka\b", re.I)
    KAPANMIS = re.compile(r"KAPANDI|ÇÖZÜLDÜ|eski|artık değil", re.I)

    def siralama_puani(b):
        _yol, _satir, metin, _o, onem_puani = b
        p = onem_puani * 10
        if MUTLAK.search(metin):
            p += 20
        if KAPANMIS.search(metin):
            p -= 15
        return -p

    print("\n═══ EN ÖNEMLİ İDDİALAR — mutlak niceleyici + yüksek önem, ölçüm izi YOK ═══")
    hepsi = sorted([b for b in (belge_bulgu + kod_bulgu) if not b[3] and b[4] == 3],
                   key=siralama_puani)
    mutlaklar = [b for b in hepsi if MUTLAK.search(b[2])]
    print(f"  (YÜKSEK önemli iddia {len(hepsi)}, bunların {len(mutlaklar)} tanesi mutlak niceleyicili)")
    for yol, satir, metin, _, _ in hepsi[:15]:
        print(f"  {yol}:{satir}\n      {metin}")

    if "--json" in sys.argv:
        json.dump({"belge": belge_bulgu, "kod": kod_bulgu},
                  open("/tmp/kayit-taramasi.json", "w"), ensure_ascii=False)

def kod_mutlak():
    """Yalnız KOD YORUMLARINDAKİ mutlak niceleyicili yüksek-önem iddiaları."""
    kod_bulgu, kd, ks, ky = tara_kod()
    MUTLAK = re.compile(r"\basla\b|\bhiçbir\b|\bhiç bir\b|\bher zaman\b"
                        r"|\bzaten\b|garanti|\bdaima\b", re.I)
    sec = [b for b in kod_bulgu if not b[3] and b[4] == 3 and MUTLAK.search(b[2])]
    print(f"kapsam: {kd} dosya, {ks} satır, {ky} yorum satırı")
    print(f"kod yorumu · YÜKSEK önem · mutlak niceleyici · ölçüm izi YOK: {len(sec)}")
    for yol, satir, metin, _, _ in sec[:20]:
        print(f"  {yol}:{satir}\n      {metin}")

if "--kod-mutlak" in sys.argv:
    kod_mutlak()
else:
    main()
