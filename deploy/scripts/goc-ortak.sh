#!/usr/bin/env bash
#
# GÖÇ YOLUNUN ORTAK KATMANI — PROVA VE UYGULAMA AYNI YOLDAN GEÇER.
#
# ═══ NEDEN VAR (2026-09-03, DEPARTMAN/1) ═══
#
# SAHA göçünde `goc-provasi.sh` GEÇTİ, hemen ardından `goc-uygula.sh`
# aynı göçü uygularken DÜŞTÜ. Sebep göç değildi: `dotnet ef` çağrısı
# ÜÇ AYRI YERDE, ÜÇ AYRI ORTAMLA yazılmıştı.
#
#   - prova   : JWT_SECRET veriyordu  + --no-build
#   - uygulama: JWT_SECRET VERMİYORDU + --no-build YOK (yeniden derliyor)
#
# İki ayrışma da provanın sınayamadığı noktadaydı — çünkü fark
# provanın kendisindeydi. "Provanın yeşili, uygulamanın yeşili
# değildir."
#
# İKİNCİ AYRIŞMA DAHA SİNSİYDİ: prova `--no-build` ile diskteki MEVCUT
# ikiliyi doğruluyor, uygulama ise YENİDEN DERLEYİP başka bir ikiliden
# göç uyguluyordu. Kaynak prova ile uygulama arasında değişmişse,
# doğrulanan ikili ile uygulanan ikili AYNI DEĞİLDİ.
#
# ═══ ÇÖZÜM ═══
#
# Tek `ef_kos`, tek ortam, tek bayrak kümesi; derleme BİR KEZ başta
# (`goc_derle`) ve her iki taraf da aynı ikiliyi kullanıyor.
#
# ═══ JWT_SECRET ARTIK YOK ═══
#
# Eskiden gerekiyordu çünkü `HrDbContext`'in tasarım-zamanı fabrikası
# yoktu ve `dotnet ef` uygulamanın Host'unu ayağa kaldırmak zorunda
# kalıyordu. `HrDbContextFactory` yazıldı: göç yolu artık yalnız
# `DB_CONNECTION` istiyor. Bir göçün, hiç kullanmadığı bir uygulama
# sırrının varlığına bağlı olması yapısal bir kusurdu.

REPO_ROOT="${REPO_ROOT:-/var/www/enderun-ai}"
EF_ARACI="${DOTNET_EF:-/root/.dotnet/tools/dotnet-ef}"
GOC_PROJE="${REPO_ROOT}/backend/EnderunAI.Api"
GOC_BAGLAMLAR=(AppDbContext HrDbContext)

# ef_kos <bağlantı> <bağlam> <ef-alt-komutu...>
#
# TEK ORTAM, TEK BAYRAK KÜMESİ. Buraya eklenen her şey prova ve
# uygulama için AYNI ANDA geçerli olur — ayrışma imkânsız.
ef_kos() {
    local baglanti="$1" baglam="$2"
    shift 2

    DB_CONNECTION="$baglanti" \
    ConnectionStrings__DefaultConnection="$baglanti" \
        "$EF_ARACI" "$@" --no-build \
        --project "$GOC_PROJE" \
        --context "$baglam"
}

# goc_derle — İKİLİYİ BİR KEZ ÜRET.
#
# Bundan sonraki bütün `ef_kos` çağrıları `--no-build` ile AYNI ikiliyi
# okur. Derlemeyi `dotnet ef`e bırakmak, her çağrının kendi ikilisini
# üretmesi ve prova ile uygulamanın farklı ikililer görmesi demekti.
goc_derle() {
    dotnet build "${GOC_PROJE}/EnderunAI.Api.csproj" -v q --nologo
}

# goc_onkosul_dogrula <bağlantı>
#
# ═══ YARIDA DÜŞEN GÖÇ, HİÇ BAŞLAMAYAN GÖÇTEN PAHALIDIR ═══
#
# SAHA göçünde tam olarak bu oldu: AppDbContext uygulandı, HrDbContext
# AÇILAMADI, betik çıkış 1 verdi. Şema yarım kalmadı (o bağlamda
# uygulanacak göç yoktu) ama bu ŞANSTI — sıra tersine olsaydı ya da
# HrDbContext'in bekleyen göçü olsaydı, gerçekten yarım kalırdı.
#
# Bu denetim göçe BAŞLAMADAN önce her iki bağlamın da AÇILABİLDİĞİNİ
# kanıtlıyor. Açılamıyorsa hiçbir şeye dokunulmadan durulur.
goc_onkosul_dogrula() {
    local baglanti="$1" baglam eksik=0

    if [ -z "$baglanti" ]; then
        echo "[goc-onkosul] HATA: bağlantı dizesi boş." >&2
        return 1
    fi

    if [ ! -x "$EF_ARACI" ]; then
        echo "[goc-onkosul] HATA: dotnet-ef bulunamadı: $EF_ARACI" >&2
        return 1
    fi

    for baglam in "${GOC_BAGLAMLAR[@]}"; do
        if ef_kos "$baglanti" "$baglam" migrations list --json \
                >/dev/null 2>&1; then
            echo "[goc-onkosul] $baglam: açılabiliyor ✓"
        else
            echo "[goc-onkosul] HATA: $baglam AÇILAMIYOR." >&2
            echo "[goc-onkosul] Göçe BAŞLANMADI — yarıda düşen göç, hiç" >&2
            echo "[goc-onkosul] başlamayan göçten pahalıdır." >&2
            eksik=1
        fi
    done

    return "$eksik"
}
