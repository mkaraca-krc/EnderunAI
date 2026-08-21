"use client";

import { useEffect, useState } from "react";

import {
  COST_CENTER_KIND,
  costCenterService,
  type CostCenterOption,
} from "@/services/cost-center.service";

/**
 * MASRAF MERKEZİ SEÇİCİ — ortak bileşen.
 *
 * Finansal ve gider doğuran her kayıtta seçilen şey aslında masraf
 * merkezidir: "Merkez" ya da bir proje. Ekranlar bunu iki ayrı alanda
 * soruyordu ve kullanıcı proje listesinde Merkez'i arayıp bulamıyordu.
 *
 * MERKEZ EN ÜSTTE VE AYRIK: projelerden ince bir çizgiyle ayrılmış bir
 * grupta duruyor. Listenin ortasında kaybolsaydı sorun aynen sürerdi.
 *
 * KAPALI PROJE: uç zaten süzüyor; yalnız mevcut kayıtta seçili olan
 * kapalı proje geliyor ve etiketinde bunu söylüyor — kullanıcı eski
 * kaydı açtığında merkezini boş görmemeli.
 */
export function CostCenterSelect({
  companyId,
  value,
  onChange,
  includeProjectId,
  required = false,
  disabled = false,
  emptyLabel = "Seçin",
  id,
}: {
  companyId?: string;
  /** Seçili seçeneğin anahtarı (`kind:code`). */
  value: string;
  onChange: (option: CostCenterOption | undefined) => void;
  /** Mevcut kayıtta seçili proje — kapalı olsa bile listeye katılır. */
  includeProjectId?: string | null;
  required?: boolean;
  disabled?: boolean;
  /** Zorunlu olmayan kullanımda boş seçeneğin etiketi (süzgeçte "Tümü"). */
  emptyLabel?: string;
  id?: string;
}) {
  const [options, setOptions] = useState<CostCenterOption[]>([]);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    let active = true;
    setLoading(true);

    void costCenterService
      .getOptions({
        companyId: companyId || undefined,
        includeProjectId: includeProjectId || undefined,
      })
      .then((list) => {
        if (active) setOptions(list);
      })
      .catch(() => {
        if (active) setOptions([]);
      })
      .finally(() => {
        if (active) setLoading(false);
      });

    return () => {
      active = false;
    };
  }, [companyId, includeProjectId]);

  const centers = options.filter((x) => x.kind === COST_CENTER_KIND.Center);
  const projects = options.filter((x) => x.kind === COST_CENTER_KIND.Project);

  /*
   * ZORUNLU ALANDA VARSAYILAN MERKEZ — VE HAYALET SEÇİM YOK.
   *
   * Zorunluyken boş seçenek basılmıyor; tarayıcı o hâlde listenin ilk
   * satırını GÖSTERİR ama üst bileşenin durumu boş kalır. Kullanıcı
   * "Merkez yazıyor" diye kaydeder, kayıt masraf merkezsiz gider.
   * Burada seçim üst bileşene BİLDİRİLİYOR; gördüğü şey gerçekten
   * seçili oluyor.
   */
  useEffect(() => {
    if (!required || value || options.length === 0) return;

    const fallback = centers[0] ?? options[0];
    if (fallback) onChange(fallback);
    // onChange üst bileşende her renderda yeniden kuruluyor olabilir;
    // bağımlılığa alınsaydı sonsuz döngü doğardı.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [required, value, options]);

  return (
    <select
      id={id}
      className="erp-input"
      value={value}
      disabled={disabled || loading}
      onChange={(event) =>
        onChange(options.find((x) => optionKey(x) === event.target.value))
      }
    >
      {/*
        ZORUNLUYSA BOŞ SEÇENEK YOK: çekte masraf merkezi zorunlu ve
        varsayılan Merkez. Boş seçenek bırakmak, kullanıcının farkında
        olmadan atlamasına kapı açardı.
      */}
      {!required && <option value="">{emptyLabel}</option>}

      {centers.length > 0 && (
        <optgroup label="Merkez">
          {centers.map((option) => (
            <option key={optionKey(option)} value={optionKey(option)}>
              {option.label}
            </option>
          ))}
        </optgroup>
      )}

      {projects.length > 0 && (
        <optgroup label="Projeler">
          {projects.map((option) => (
            <option key={optionKey(option)} value={optionKey(option)}>
              {option.label}
              {option.isClosed ? " (kapalı)" : ""}
            </option>
          ))}
        </optgroup>
      )}
    </select>
  );
}

/** Seçeneğin kararlı anahtarı — tip ve kod birlikte. */
export function optionKey(option: CostCenterOption): string {
  return `${option.kind}:${option.projectId ?? option.code}`;
}
