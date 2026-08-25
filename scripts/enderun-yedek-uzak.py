#!/usr/bin/env python3
"""
ŞİFRELİ YEDEKLERİ SUNUCU DIŞINA KOPYALAR (S3 uyumlu nesne depolama).

BUGÜN KAPALI. `UZAK_YEDEK_ETKIN=evet` olmadan hiçbir şey göndermiyor.
Yurt dışı aktarım KVKK değerlendirmesi bekliyor (Mehmet Karacabey
avukata soracak); karar gelene kadar bayrak kapalı kalacak.

NEDEN VAR: yedekler bugün veritabanıyla AYNI DİSKTE. Disk giderse
ikisi birden gider — şifreli olmaları bunu değiştirmez.

──────────────────────────────────────────────────────────────────
SİLME YETKİSİ YOK — VE OLMAYACAK.

Sunucuya giren biri yedekleri de silebiliyorsa yedek, fidye
yazılımına karşı koruma sağlamaz. Yükleme anahtarı yalnız
`s3:PutObject` (+ `s3:ListBucket`) taşır; `s3:DeleteObject` TAŞIMAZ.

BUNUN SONUCU: ESKİYİ BU BETİK DÜŞÜREMEZ. Saklama (günlük 14,
haftalık 8, aylık 12) kova üzerindeki YAŞAM DÖNGÜSÜ KURALIYLA
uygulanır — sağlayıcı tarafında, sunucunun erişemediği yerde.
Betiğin silebiliyor olması, korumanın kendisini ortadan kaldırırdı.
──────────────────────────────────────────────────────────────────

Sırlar ortam değişkeninde: /etc/enderunai/backup-remote.env
Kaynak kodda erişim anahtarı YOK.
"""

import datetime as dt
import json
import os
import pathlib
import re
import subprocess
import sys

AYAR_DOSYASI = "/etc/enderunai/backup-remote.env"
YEDEK_DIZINI = "/var/backups/enderun"
# Ortam dosyasının yolu SINANABİLİR olsun diye değiştirilebilir.
# Denetim kaydı yolunu canlı kaydı kirletmeden sınamanın başka yolu
# yok: yolu sabitlemek, "kayıt gerçekten yazılıyor mu" sorusunu
# yalnız canlıya yazarak cevaplanabilir kılardı.
ENV_DOSYASI = os.environ.get("ENDERUN_ENV_DOSYASI", "/etc/enderunai/backend.env")
KAYIT = "/var/log/enderun-backup.log"


def gunle(seviye: str, mesaj: str) -> None:
    satir = f"{dt.datetime.now(dt.timezone.utc):%Y-%m-%dT%H:%M:%SZ} [{seviye}] {mesaj}"
    print(satir)
    try:
        with open(KAYIT, "a", encoding="utf-8") as f:
            f.write(satir + "\n")
    except OSError:
        pass


def ayarlari_oku(yol: str) -> dict:
    ayar = {}
    try:
        with open(yol, encoding="utf-8") as f:
            for ham in f:
                ham = ham.strip()
                if not ham or ham.startswith("#") or "=" not in ham:
                    continue
                k, _, v = ham.partition("=")
                ayar[k.strip()] = v.strip().strip('"').strip("'")
    except FileNotFoundError:
        pass
    return ayar


SIRLAR: list[str] = []


def temizle(metin: str) -> str:
    """
    Hata metninden BİLİNEN SIRLARI siler.

    İlk yazılışı "20+ karakterlik alfasayısal diziyi maskele" diyen
    kör bir desendi ve ÖLÇÜMDE rastlantısal davrandı: zararsız bir yol
    parçasını (`kova/2026/08/uploads`) maskeledi, aynı hatanın başka
    biçimini maskelemedi — çünkü alt çizgi diziyi bölüyordu. Kör
    desen, gerçek bir sırrı yakalayacağının güvencesini vermez.

    Şimdi silinen şey BİLİNEN değerler: ayar dosyasından okunan erişim
    ve gizli anahtar. Boto3 hataları erişim anahtarı KİMLİĞİNİ metne
    koyabiliyor ("InvalidAccessKeyId: ... you provided ...").

    Uzunluk sınırı ayrıca duruyor: denetim kaydı hata metni deposu
    değil.
    """
    for sir in SIRLAR:
        if sir:
            metin = metin.replace(sir, "<gizli>")
    return metin[:400]


def denetim_kaydi_yaz(eylem: str, ayrinti: dict) -> None:
    """
    Yükleme başarısızlığını GÜVENLİK KAYDINA yazar.

    Bir hafta yedek gitmediğini fark etmemek, hiç yedek almamaktan
    kötü. Kayıt veritabanına yazılıyor: günlük dosyası dönerse
    kaybolur, denetim kaydı kalır.

    DOSYA ADI VE BOYUT YAZILIR; İÇERİK, ANAHTAR VE UÇ NOKTA KİMLİĞİ
    YAZILMAZ.
    """
    try:
        with open(ENV_DOSYASI, encoding="utf-8") as f:
            icerik = f.read()
    except OSError:
        gunle("ERROR", "Denetim kaydı yazılamadı: ortam dosyası okunamadı.")
        return

    eslesme = re.search(r"^DB_CONNECTION=(.*)$", icerik, re.M)
    if not eslesme:
        gunle("ERROR", "Denetim kaydı yazılamadı: DB_CONNECTION yok.")
        return

    baglanti = eslesme.group(1)
    parola = re.search(r"Password=([^;'\"]*)", baglanti)
    kullanici = re.search(r"Username=([^;'\"]*)", baglanti)
    veritabani = re.search(r"Database=([^;'\"]*)", baglanti)

    if not (parola and kullanici and veritabani):
        gunle("ERROR", "Denetim kaydı yazılamadı: bağlantı dizesi çözülemedi.")
        return

    sql = (
        'INSERT INTO security_audit_events '
        '("Id","Action","EntityType","DetailsJson","OccurredAtUtc") '
        "VALUES (gen_random_uuid(), %(eylem)s, 'Backup', %(ayrinti)s::jsonb, now())"
    )

    ortam = dict(os.environ, PGPASSWORD=parola.group(1))
    try:
        subprocess.run(
            ["psql", "-h", "127.0.0.1", "-U", kullanici.group(1),
             "-d", veritabani.group(1), "-v", "ON_ERROR_STOP=1", "-q", "-c",
             sql.replace("%(eylem)s", f"'{eylem}'")
                .replace("%(ayrinti)s", f"'{json.dumps(ayrinti, ensure_ascii=False)}'")],
            env=ortam, check=True, capture_output=True, timeout=30,
        )
        gunle("INFO", f"Denetim kaydı yazıldı: {eylem}")
    except (subprocess.CalledProcessError, subprocess.TimeoutExpired, OSError) as hata:
        gunle("ERROR", f"Denetim kaydı YAZILAMADI ({eylem}): {temizle(str(hata))}")


def main() -> int:
    ayar = ayarlari_oku(AYAR_DOSYASI)
    etkin = ayar.get("UZAK_YEDEK_ETKIN", "hayir").lower()

    if etkin not in ("evet", "yes", "true", "1"):
        gunle("INFO", "Uzak yedek KAPALI (UZAK_YEDEK_ETKIN != evet) — hiçbir şey gönderilmedi.")
        return 0

    # Sırlar, herhangi bir hata metni üretilmeden ÖNCE kaydediliyor:
    # sonra doldurulursa ilk hata maskelenmeden geçerdi.
    SIRLAR.extend(x for x in (ayar.get("S3_ACCESS_KEY"), ayar.get("S3_SECRET_KEY")) if x)

    for gerekli in ("S3_ENDPOINT", "S3_BUCKET", "S3_ACCESS_KEY", "S3_SECRET_KEY", "S3_REGION"):
        if not ayar.get(gerekli):
            gunle("ERROR", f"Uzak yedek AÇIK ama {gerekli} tanımlı değil — gönderim yapılmadı.")
            denetim_kaydi_yaz("BackupRemoteUploadFailed",
                              {"sebep": "eksik_ayar", "ayar": gerekli})
            return 1

    try:
        import boto3
        from botocore.config import Config
        from botocore.exceptions import BotoCoreError, ClientError
    except ImportError:
        gunle("ERROR", "boto3 yok — gönderim yapılmadı.")
        denetim_kaydi_yaz("BackupRemoteUploadFailed", {"sebep": "boto3_yok"})
        return 1

    # İSTEMCİ KURULUMU DA HATA VEREBİLİR — VE VERDİ.
    #
    # Ölçümde geçersiz bir uç nokta `boto3.client()` çağrısında
    # yakalanmamış ValueError ile BETİĞİ ÇÖKERTTİ: yığın izi ekrana
    # düştü, denetim kaydı HİÇ YAZILMADI. Yani tam da kaçınmak
    # istediğimiz durum — yedek gitmiyor ve kimse görmüyor.
    try:
        istemci = boto3.client(
            "s3",
            endpoint_url=ayar["S3_ENDPOINT"],
            aws_access_key_id=ayar["S3_ACCESS_KEY"],
            aws_secret_access_key=ayar["S3_SECRET_KEY"],
            region_name=ayar["S3_REGION"],
            config=Config(retries={"max_attempts": 3, "mode": "standard"}),
        )
    except Exception as hata:  # noqa: BLE001 — kurulum her türlü hatada kayda düşmeli
        gunle("ERROR", f"S3 istemcisi kurulamadı: {temizle(str(hata))}")
        denetim_kaydi_yaz("BackupRemoteUploadFailed",
                          {"sebep": "istemci_kurulamadi", "hata": temizle(str(hata))})
        return 1

    bugun = dt.datetime.now(dt.timezone.utc).strftime("%Y%m%d")
    dosyalar = sorted(pathlib.Path(YEDEK_DIZINI).glob(f"*_{bugun}_*.gpg"))

    if not dosyalar:
        gunle("ERROR", "Bugüne ait şifreli yedek bulunamadı — gönderim yapılmadı.")
        denetim_kaydi_yaz("BackupRemoteUploadFailed", {"sebep": "dosya_yok", "gun": bugun})
        return 1

    # ŞİFRESİZ DOSYA ASLA GÖNDERİLMEZ. Süzgeç .gpg ile bitenler; yine de
    # açıkça kontrol ediliyor, süzgeç bir gün gevşerse diye.
    dosyalar = [d for d in dosyalar if d.name.endswith(".gpg")]

    basarisiz = []
    gonderilen = 0

    for dosya in dosyalar:
        anahtar = f"{bugun[:4]}/{bugun[4:6]}/{dosya.name}"
        try:
            ek = {}
            if ayar.get("S3_OBJECT_LOCK_GUN"):
                ek["ObjectLockMode"] = ayar.get("S3_OBJECT_LOCK_MODU", "COMPLIANCE")
                ek["ObjectLockRetainUntilDate"] = dt.datetime.now(dt.timezone.utc) + dt.timedelta(
                    days=int(ayar["S3_OBJECT_LOCK_GUN"]))

            with dosya.open("rb") as f:
                istemci.put_object(Bucket=ayar["S3_BUCKET"], Key=anahtar, Body=f, **ek)

            gonderilen += 1
            gunle("INFO", f"Uzağa gönderildi: {anahtar} ({dosya.stat().st_size} bayt)")
        except (ClientError, BotoCoreError, OSError, ValueError) as hata:
            basarisiz.append({"dosya": dosya.name, "hata": temizle(str(hata))})
            gunle("ERROR", f"Uzağa GÖNDERİLEMEDİ: {dosya.name} — {temizle(str(hata))}")

    if basarisiz:
        denetim_kaydi_yaz("BackupRemoteUploadFailed",
                          {"gun": bugun, "gonderilen": gonderilen, "basarisiz": basarisiz})
        return 1

    gunle("INFO", f"Uzak yedek tamam: {gonderilen} dosya.")
    return 0


if __name__ == "__main__":
    # SON KALKAN: beklenmedik her hata da kayda düşsün.
    #
    # Yığın izini ekrana basıp çıkmak, "yedek gitmedi" bilgisini
    # günlük dosyasına hapseder; o dosya döner ve kaybolur.
    try:
        sys.exit(main())
    except SystemExit:
        raise
    except Exception as hata:  # noqa: BLE001
        gunle("ERROR", f"Uzak yedek BEKLENMEDİK hatayla düştü: {temizle(str(hata))}")
        denetim_kaydi_yaz("BackupRemoteUploadFailed",
                          {"sebep": "beklenmedik_hata", "hata": temizle(str(hata))})
        sys.exit(1)
