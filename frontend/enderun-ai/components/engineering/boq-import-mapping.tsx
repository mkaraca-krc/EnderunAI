"use client";

import {
  BoqSectionRule,
  type BoqImportMapping,
  type BoqSpreadsheetInspection,
} from "@/services/project-boq.service";

type Props = {
  inspection: BoqSpreadsheetInspection;
  mapping: BoqImportMapping;
  onChange: (mapping: BoqImportMapping) => void;
  /** Sayfa ya da başlık satırı değişince dosya yeniden okunur. */
  onReinspect: (sheetName: string, headerRow: number) => void;
  disabled?: boolean;
};

const sectionRules = [
  {
    value: BoqSectionRule.EmptyUnit,
    label: "Birimi boş olan satır kısım başlığıdır",
    hint:
      "Gerçek icmallerde en güvenilir kural. Başlık satırında ara toplamlar " +
      "sayı sütunlarına yazıldığı için 'sayı var mı' bakmak yanıltıcıdır.",
  },
  {
    value: BoqSectionRule.CodeEndsWithDot,
    label: "Poz no noktayla bitiyorsa kısım başlığıdır (01.)",
    hint: "Kodlama düzeni tutarlıysa kullanın.",
  },
  {
    value: BoqSectionRule.SectionColumn,
    label: "Ayrı bir kısım sütunu var",
    hint: "ENDERUN şablonunun düzeni: kısım sütunu dolu, poz no boş.",
  },
];

function guessColumn(headers: string[], include: string[], exclude: string[] = []) {
  const index = headers.findIndex((header) => {
    const value = header.toLocaleLowerCase("tr-TR");

    return (
      include.some((needle) => value.includes(needle)) &&
      !exclude.some((needle) => value.includes(needle))
    );
  });

  return index >= 0 ? index + 1 : 0;
}

/**
 * Başlık adlarından makul bir ilk eşleme önerir. Öneri sessizce
 * uygulanmaz: kullanıcı ekranda görüp değiştirir. Yanlış eşlenmiş bir
 * sütun icmali bin kat şişirebilir.
 */
export function guessMapping(
  inspection: BoqSpreadsheetInspection
): BoqImportMapping {
  const headers = inspection.headers;

  return {
    sheetName: inspection.sheetName,
    headerRowIndex: inspection.headerRowIndex,
    codeColumn: guessColumn(headers, ["poz", "kod"]),
    descriptionColumn: guessColumn(headers, ["açıklama", "aciklama", "tanım", "tanim"]),
    // "Malzeme Birim Fiyatı" da "birim" içeriyor; fiyat sütunları elenir.
    unitColumn: guessColumn(headers, ["birim", "ölçü", "olcu"], ["fiyat"]),
    quantityColumn: guessColumn(headers, ["miktar", "keşif", "kesif", "metraj"]),
    // "KAPSAM" SÜTUNLARI ELENİR. Gerçek icmallerde fiyat sütunlarından
    // önce "Ana Malzeme Kapsamı" / "İşçilik Kapsamı" gibi METİN
    // sütunları geliyor ("Yüklenici" yazar). Bunlar da "malzeme" ve
    // "işçilik" kelimesini içerdiği için ilk eşleşmeyi kapıyor ve
    // tahmin fiyat yerine metin sütununu seçiyordu; onaylanırsa
    // dosyadaki HER satır "birim fiyat okunamadı" hatası veriyordu.
    // NATURA icmalinde ölçüldü: düzeltmeden önce 2/9 sütun yanlış.
    materialColumn: guessColumn(headers, ["malzeme"], ["kapsam"]),
    laborColumn: guessColumn(headers, ["işçilik", "iscilik", "montaj"], ["kapsam"]),
    // "gg" da aranıyor: ENDERUN'un KENDİ şablonu "GG&K B.F." yazıyor
    // ve nokta içermediği için "g.g" kalıbına takılmıyordu — kendi
    // şablonumuzda bile genel gider sütunu eşlenmemiş geliyordu.
    // Regresyon testinde yakalandı.
    overheadColumn: guessColumn(headers, ["g.g", "gg", "genel", "kar", "kâr"]),
    sectionColumn: guessColumn(headers, ["kısım", "kisim"]) || null,
    totalColumn: guessColumn(headers, ["tutar"]) || null,
    sectionRule: BoqSectionRule.EmptyUnit,
  };
}

function ColumnSelect({
  label,
  value,
  headers,
  optional,
  hint,
  disabled,
  onChange,
}: {
  label: string;
  value: number;
  headers: string[];
  optional?: boolean;
  hint?: string;
  disabled?: boolean;
  onChange: (value: number) => void;
}) {
  return (
    <label>
      <span>
        {label}
        {optional ? " (ops.)" : " *"}
      </span>
      <select
        className="erp-input"
        value={value}
        disabled={disabled}
        onChange={(event) => onChange(Number(event.target.value))}
      >
        <option value={0}>— eşlenmedi —</option>
        {headers.map((header, index) => (
          <option key={index} value={index + 1}>
            {index + 1}. {header || "(başlıksız)"}
          </option>
        ))}
      </select>
      {hint && <small style={{ display: "block" }}>{hint}</small>}
    </label>
  );
}

/**
 * İcmal Excel'inin sütun eşleme ekranı.
 *
 * Sütun düzeni varsayılmıyor: her müşteri kendi icmal düzeniyle geliyor
 * ve düzen projeden projeye değişiyor.
 */
export default function BoqImportMappingPanel({
  inspection,
  mapping,
  onChange,
  onReinspect,
  disabled,
}: Props) {
  const headers = inspection.headers;

  function set(patch: Partial<BoqImportMapping>) {
    onChange({ ...mapping, ...patch });
  }

  return (
    <div className="erp-mt">
      <h3>Sütun Eşleme</h3>
      <p>
        Sayfa <strong>{inspection.sheetName}</strong> · {inspection.totalRowCount}{" "}
        veri satırı. Başlıklardan bir öneri dolduruldu; yanlışsa değiştirin.
      </p>

      <div style={{ display: "flex", gap: 12, flexWrap: "wrap" }}>
        {inspection.sheetNames.length > 1 && (
          <label>
            <span>Sayfa</span>
            <select
              className="erp-input"
              value={inspection.sheetName}
              disabled={disabled}
              onChange={(event) =>
                onReinspect(event.target.value, inspection.headerRowIndex)
              }
            >
              {inspection.sheetNames.map((name) => (
                <option key={name} value={name}>
                  {name}
                </option>
              ))}
            </select>
          </label>
        )}

        <label>
          <span>Başlık satırı</span>
          <input
            className="erp-input"
            type="number"
            min={1}
            max={50}
            value={inspection.headerRowIndex}
            disabled={disabled}
            style={{ width: 90 }}
            onChange={(event) =>
              onReinspect(inspection.sheetName, Number(event.target.value))
            }
          />
        </label>
      </div>

      <div
        style={{
          display: "grid",
          gridTemplateColumns: "repeat(4, minmax(0, 1fr))",
          gap: 12,
          marginTop: 12,
        }}
      >
        <ColumnSelect
          label="Poz no"
          value={mapping.codeColumn}
          headers={headers}
          disabled={disabled}
          onChange={(v) => set({ codeColumn: v })}
        />
        <ColumnSelect
          label="Tanım"
          value={mapping.descriptionColumn}
          headers={headers}
          disabled={disabled}
          onChange={(v) => set({ descriptionColumn: v })}
        />
        <ColumnSelect
          label="Birim"
          value={mapping.unitColumn}
          headers={headers}
          disabled={disabled}
          onChange={(v) => set({ unitColumn: v })}
        />
        <ColumnSelect
          label="Miktar"
          value={mapping.quantityColumn}
          headers={headers}
          disabled={disabled}
          onChange={(v) => set({ quantityColumn: v })}
        />
        <ColumnSelect
          label="Malzeme B.F."
          value={mapping.materialColumn}
          headers={headers}
          disabled={disabled}
          onChange={(v) => set({ materialColumn: v })}
        />
        <ColumnSelect
          label="İşçilik B.F."
          value={mapping.laborColumn}
          headers={headers}
          disabled={disabled}
          onChange={(v) => set({ laborColumn: v })}
        />
        <ColumnSelect
          label="GG & Kâr B.F."
          value={mapping.overheadColumn}
          headers={headers}
          disabled={disabled}
          onChange={(v) => set({ overheadColumn: v })}
        />
        <ColumnSelect
          label="Tutar (doğrulama)"
          value={mapping.totalColumn ?? 0}
          headers={headers}
          optional
          hint="Veri olarak kullanılmaz; belirsiz sayıları çözer ve tutmayan satırı hata yapar."
          disabled={disabled}
          onChange={(v) => set({ totalColumn: v || null })}
        />

        {mapping.sectionRule === BoqSectionRule.SectionColumn && (
          <ColumnSelect
            label="Kısım sütunu"
            value={mapping.sectionColumn ?? 0}
            headers={headers}
            disabled={disabled}
            onChange={(v) => set({ sectionColumn: v || null })}
          />
        )}
      </div>

      <div style={{ marginTop: 12 }}>
        <span className="erp-stat-label">Kısım başlığı nasıl tanınsın?</span>

        {sectionRules.map((rule) => (
          <label key={rule.value} style={{ display: "block", marginTop: 4 }}>
            <input
              type="radio"
              checked={mapping.sectionRule === rule.value}
              disabled={disabled}
              onChange={() => set({ sectionRule: rule.value })}
            />{" "}
            {rule.label}
            <small style={{ display: "block", marginLeft: 20 }}>{rule.hint}</small>
          </label>
        ))}
      </div>

      {inspection.sampleRows.length > 0 && (
        <div className="erp-table-wrap erp-mt">
          <table className="erp-table">
            <thead>
              <tr>
                {headers.map((header, index) => (
                  <th key={index}>
                    {index + 1}. {header || "(başlıksız)"}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {inspection.sampleRows.map((row, index) => (
                <tr key={index}>
                  {headers.map((_, column) => (
                    <td key={column}>
                      <small>{row[column] ?? ""}</small>
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
