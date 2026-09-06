#!/usr/bin/env bash
#
# KUTU/1 AYRIŞMA KONTROLÜ — İKİ KOPYA, TEK DOĞRULUK.
#
# ═══ NEDEN VAR ═══
#
# KUTU/1 betikleri iki yerde duruyor: canlıda (`/usr/local/bin`,
# `/etc/systemd/system`) ve depoda (`deploy/kutu/`). Bu, bu kod
# tabanının en sık hatasının aynısı — aynı şeyin ikinci kopyası.
# Göç betiklerinde, merkez kuralında, parola uzunluğunda hep bu oldu.
#
# OTOMATİK EŞİTLEME İSTENMEDİ (Mehmet, 2026-09-06): dağıtım canlı
# sistem kabuğuna dokunur, ayrı bir karar konusudur. İstenen şey
# EŞİTLEME DEĞİL, AYRIŞMANIN GÖRÜNMESİ: *"Sessiz ayrışma olmasın;
# hangisinin doğru olduğuna ben karar veririm."*
#
# ═══ NEDEN RAPOR DEĞİL DE DÜŞÜYOR ═══
#
# Yalnız uyarı basan bir kontrol, gürültüye karışır ve bir süre sonra
# okunmaz. Kapı düşerse karar VERİLMEK ZORUNDA kalır — istenen de
# buydu. Betik hiçbir dosyayı KENDİLİĞİNDEN kopyalamaz; hangisinin
# doğru olduğunu söylemez, yalnız farkı gösterir.
#
# KULLANIM:
#   ayrisma-kontrolu.sh          # fark varsa 1 ile düşer
#   ayrisma-kontrolu.sh --fark   # farkların içeriğini de basar

set -uo pipefail

DEPO="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
FARK_GOSTER=0
[ "${1:-}" = "--fark" ] && FARK_GOSTER=1

# EŞLEŞME LİSTESİ — "depo alt yolu|canlı yol"
ESLESMELER=(
  "bin/cc-oturum.sh|/usr/local/bin/cc-oturum.sh"
  "bin/cc-baslat.sh|/usr/local/bin/cc-baslat.sh"
  "bin/cc-devir.sh|/usr/local/bin/cc-devir.sh"
  "bin/enderun-rapor-yaz.sh|/usr/local/bin/enderun-rapor-yaz.sh"
  "bin/enderun-uyari.sh|/usr/local/bin/enderun-uyari.sh"
  "systemd/cc-oturum.service|/etc/systemd/system/cc-oturum.service"
  "systemd/cc-devir.service|/etc/systemd/system/cc-devir.service"
  "systemd/cc-devir.timer|/etc/systemd/system/cc-devir.timer"
  "systemd/enderun-rapor.service|/etc/systemd/system/enderun-rapor.service"
  "systemd/enderun-rapor.timer|/etc/systemd/system/enderun-rapor.timer"
  "systemd/enderun-uyari@.service|/etc/systemd/system/enderun-uyari@.service"

  # ── KUTU/1 DIŞI, AMA AYNI SINIF ───────────────────────────────
  #
  # Bunlar KUTU/1'den önce vardı ve aynı ikili-kopya deseninde
  # duruyorlar. 2026-09-06'da ÖLÇÜLDÜ: yedisi de canlı kopyasıyla
  # birebir aynı. Aynı olmaları bir tesadüf ve bugüne kadar hiçbir
  # şey onu korumuyordu — listeye alınmalarının sebebi bu.
  #
  # Yollar deponun köküne göre; `../..` bu betiğin durduğu
  # deploy/kutu dizininden çıkar.
  "../../scripts/enderun-backup.sh|/usr/local/bin/enderun-backup.sh"
  "../../scripts/enderun-geri-yukleme-tatbikati.sh|/usr/local/bin/enderun-geri-yukleme-tatbikati.sh"
  "../../ops/systemd/enderun-backup.service|/etc/systemd/system/enderun-backup.service"
  "../../ops/systemd/enderun-backup.timer|/etc/systemd/system/enderun-backup.timer"
  "../../ops/systemd/enderun-geri-yukleme-tatbikati.service|/etc/systemd/system/enderun-geri-yukleme-tatbikati.service"
  "../../ops/systemd/enderun-geri-yukleme-tatbikati.timer|/etc/systemd/system/enderun-geri-yukleme-tatbikati.timer"
  "../../ops/systemd/smtp-port-watch.service|/etc/systemd/system/smtp-port-watch.service"
  "../../ops/systemd/smtp-port-watch.timer|/etc/systemd/system/smtp-port-watch.timer"
)

ayrisan=0
sayilan=0

for es in "${ESLESMELER[@]}"; do
    depo_yolu="${DEPO}/${es%%|*}"
    canli_yolu="${es#*|}"
    sayilan=$((sayilan + 1))

    if [ ! -f "$depo_yolu" ]; then
        echo "[kutu-ayrisma] DEPODA YOK : ${es%%|*}"
        ayrisan=$((ayrisan + 1))
        continue
    fi

    if [ ! -f "$canli_yolu" ]; then
        echo "[kutu-ayrisma] CANLIDA YOK: ${canli_yolu}"
        ayrisan=$((ayrisan + 1))
        continue
    fi

    if ! cmp -s "$depo_yolu" "$canli_yolu"; then
        echo "[kutu-ayrisma] AYRIŞMA   : ${es%%|*}"
        echo "                depo  : ${depo_yolu}"
        echo "                canlı : ${canli_yolu}"
        [ "$FARK_GOSTER" = "1" ] && diff -u "$canli_yolu" "$depo_yolu" | head -40
        ayrisan=$((ayrisan + 1))
    fi
done

# POZİTİF KONTROL — LİSTE BOŞALIRSA KAPI SESSİZCE YEŞİL OLURDU.
# "Hiç ayrışma yok" ile "hiç dosya bakılmadı" aynı çıktıyı verir;
# ayıran tek şey sayımdır (Kural 48).
if [ "$sayilan" -lt 15 ]; then
    echo "[kutu-ayrisma] POZİTİF KONTROL DÜŞTÜ: yalnız ${sayilan} dosya bakıldı." >&2
    echo "[kutu-ayrisma] Liste boşalmış olabilir; bu hâlde kapı hiçbir şey ölçmüyor." >&2
    exit 1
fi

if [ "$ayrisan" -gt 0 ]; then
    echo "[kutu-ayrisma] ${sayilan} dosyadan ${ayrisan} tanesi AYRIŞMIŞ." >&2
    echo "[kutu-ayrisma] Hangisinin doğru olduğuna karar verilmeli — bu betik" >&2
    echo "[kutu-ayrisma] kendiliğinden kopyalamaz. Farkı görmek için: --fark" >&2
    exit 1
fi

echo "[kutu-ayrisma] ${sayilan} dosya, ayrışma yok."
