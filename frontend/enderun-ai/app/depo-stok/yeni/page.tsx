"use client";

import { useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";

import ErpShell from "@/components/erp/erp-shell";
import {
  inventoryService,
  type CompanyOption,
  type InventoryCategory,
  type InventoryItemType,
} from "@/services/inventory.service";
import { projectService, type ProjectListItem } from "@/services/project.service";

/**
 * STOK KARTI AÇMA — KATEGORİ GÜDÜMLÜ (S2).
 *
 * KULLANICI KOD VE AD YAZMAZ:
 *   • Kod tam otomatik sıra (100001…). Kod bir kimliktir, tanım değil;
 *     kullanıcının onu düşünmesi, ezberlemesi ya da bilmesi gerekmez.
 *   • Ad, STANDART kategoride seçilen özelliklerden üretilir. Elle
 *     yazılan ad aynı malzemeyi üç farklı isimle açtırır ("Kablo Tavası
 *     200", "200lük kablo tavası", "KABLO TAVASI 200 MM") ve stok
 *     üçe bölünür.
 *
 * SERBEST kategorilerde (dekoratif aydınlatma, özel imalat) ad elle
 * yazılır ve mükerrer engeli uygulanmaz — her ürün tekildir.
 */
export default function CreateInventoryItemPage() {
  const router = useRouter();

  const [companies, setCompanies] = useState<CompanyOption[]>([]);
  const [categories, setCategories] = useState<InventoryCategory[]>([]);
  const [loading, setLoading] = useState(true);

  const [companyId, setCompanyId] = useState("");

  /*
   * PROJE BAĞI ve TEDARİK TİPİ (S9). SERBEST kategorilerde asıl anlamlı
   * olan ikisi: özel imal edilen ürün bir işe aittir ve stokta
   * bulundurulmaz. Bu yüzden SERBEST seçilince varsayılanlar oraya
   * kayıyor — kullanıcı isterse değiştirir.
   */
  const [projectId, setProjectId] = useState("");
  const [supplyKind, setSupplyKind] = useState("0");
  const [projects, setProjects] = useState<ProjectListItem[]>([]);
  const [categoryId, setCategoryId] = useState("");
  const [unit, setUnit] = useState("");
  const [freeName, setFreeName] = useState("");

  /** Özellik kodu → seçilen seçenek kimliği. */
  const [selection, setSelection] = useState<Record<string, string>>({});

  const [brand, setBrand] = useState("");
  const [model, setModel] = useState("");
  const [barcode, setBarcode] = useState("");
  const [type, setType] = useState<InventoryItemType>(0);
  const [vatRate, setVatRate] = useState("");
  const [description, setDescription] = useState("");

  // Ayrı metin durumu: boş bırakmak "bilinmiyor" demek, sıfır değil.
  // Sayı durumunda tutulsaydı boş alan sıfıra düşer ve malzemenin
  // bakır içermediği iddia edilmiş olurdu.
  const [copperKgPerUnit, setCopperKgPerUnit] = useState("");

  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    void (async () => {
      try {
        const [companyData, categoryData] = await Promise.all([
          inventoryService.getCompanies(),
          inventoryService.getCategories(),
        ]);

        setCompanies(companyData);
        setCategories(categoryData);

        if (companyData.length === 1) setCompanyId(companyData[0].id);
      } catch (err) {
        setError(err instanceof Error ? err.message : "Veriler yüklenemedi.");
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  const category = categories.find((x) => x.id === categoryId) ?? null;
  const isFree = category?.kind === 1;

  useEffect(() => {
    if (!companyId) {
      setProjects([]);
      return;
    }

    let active = true;

    void projectService
      .getAll(companyId)
      .then((list) => {
        if (active) setProjects(list);
      })
      .catch(() => {
        if (active) setProjects([]);
      });

    return () => {
      active = false;
    };
  }, [companyId]);

  /*
   * SERBEST KATEGORİDE VARSAYILAN "ÖZEL İMALAT". Dekoratif aydınlatma ve
   * özel imalat kategorilerinde ürün tekildir ve stokta bulundurulmaz;
   * varsayılanı "stoklu" bırakmak, kullanıcıyı her seferinde düzeltmeye
   * zorlar ve unutulduğunda kart yanlış tipte doğardı.
   */
  useEffect(() => {
    setSupplyKind(isFree ? "1" : "0");
    if (!isFree) setProjectId("");
  }, [isFree]);

  // Kategori değişince seçim ve birim sıfırlanır: önceki kategorinin
  // özellikleri yeni kategoride anlamsız, birimi de izinli olmayabilir.
  function changeCategory(nextId: string) {
    setCategoryId(nextId);
    setSelection({});
    setFreeName("");

    const next = categories.find((x) => x.id === nextId);
    setUnit(next && next.units.length === 1 ? next.units[0] : "");
  }

  /**
   * AD ÖNİZLEMESİ — sunucudakiyle aynı kuralı izler: kategori adı +
   * özellik gösterimleri, ÖZELLİK SIRASINA göre (seçim sırasına değil).
   */
  const namePreview = useMemo(() => {
    if (!category || isFree) return "";

    const parts = category.attributes
      .slice()
      .sort((a, b) => a.sortOrder - b.sortOrder)
      .map((attribute) => {
        const optionId = selection[attribute.code];
        const option = attribute.options.find((x) => x.id === optionId);
        return option?.display ?? "";
      })
      .filter((x) => x.length > 0);

    return [category.name, ...parts].join(" ");
  }, [category, isFree, selection]);

  const missing = useMemo(() => {
    if (!category || isFree) return [];

    return category.attributes
      .filter((attribute) => attribute.isRequired && !selection[attribute.code])
      .map((attribute) => attribute.name);
  }, [category, isFree, selection]);

  const validation: string[] = [];
  if (!companyId) validation.push("Şirket seçin.");
  if (!categoryId) validation.push("Kategori seçin.");
  if (categoryId && !unit) validation.push("Birim seçin.");
  if (isFree && !freeName.trim()) validation.push("Malzeme adını yazın.");
  if (missing.length > 0)
    validation.push(`Şu özellikleri seçin: ${missing.join(", ")}`);

  async function submit(event: React.FormEvent) {
    event.preventDefault();

    if (validation.length > 0) return;

    setSaving(true);
    setError("");

    try {
      await inventoryService.createItem({
        companyId,
        categoryId,
        unit,
        optionIds: isFree ? [] : Object.values(selection),
        name: isFree ? freeName.trim() : undefined,
        brand: brand.trim() || undefined,
        model: model.trim() || undefined,
        barcode: barcode.trim() || undefined,
        type,
        projectId: projectId || null,
        supplyKind: Number(supplyKind) as 0 | 1 | 2,
        vatRate: vatRate.trim() === "" ? null : Number(vatRate),
        description: description.trim() || null,
        copperKgPerUnit:
          copperKgPerUnit.trim() === "" ? null : Number(copperKgPerUnit),
      });

      router.push("/depo-stok");
      router.refresh();
    } catch (err) {
      setError(
        err instanceof Error ? err.message : "Malzeme kartı oluşturulamadı."
      );
    } finally {
      setSaving(false);
    }
  }

  return (
    <ErpShell
      design="redwood"
      title="Yeni Malzeme Kartı"
      description="Kategori seçin, özellikleri işaretleyin — kod ve ad otomatik oluşur"
    >
      {error && <div className="erp-alert error">{error}</div>}

      <form className="erp-form-card" onSubmit={submit}>
        <div className="erp-form-grid">
          <label>
            <span>Şirket *</span>
            <select
              required
              value={companyId}
              onChange={(event) => setCompanyId(event.target.value)}
              disabled={loading}
            >
              <option value="">Seçin</option>
              {companies.map((company) => (
                <option key={company.id} value={company.id}>
                  {company.name}
                </option>
              ))}
            </select>
          </label>

          <label>
            <span>Kategori *</span>
            <select
              required
              value={categoryId}
              onChange={(event) => changeCategory(event.target.value)}
              disabled={loading}
            >
              <option value="">Seçin</option>
              {categories.map((option) => (
                <option key={option.id} value={option.id}>
                  {option.name}
                  {option.kind === 1 ? " (serbest)" : ""}
                </option>
              ))}
            </select>
          </label>

          {category && (
            <label>
              <span>Birim *</span>
              <select
                required
                value={unit}
                onChange={(event) => setUnit(event.target.value)}
                /* Tek birimli kategoride seçim yok — zaten sabit. */
                disabled={category.units.length === 1}
              >
                {category.units.length !== 1 && <option value="">Seçin</option>}
                {category.units.map((option) => (
                  <option key={option} value={option}>
                    {option}
                  </option>
                ))}
              </select>
              <small>Kart açıldıktan sonra birim değişmez.</small>
            </label>
          )}
        </div>

        {category && !isFree && category.attributes.length > 0 && (
          <>
            <h2>Özellikler</h2>

            <div className="erp-form-grid">
              {category.attributes
                .slice()
                .sort((a, b) => a.sortOrder - b.sortOrder)
                .map((attribute) => (
                  <label key={attribute.id}>
                    <span>
                      {attribute.name}
                      {attribute.isRequired ? " *" : ""}
                    </span>

                    <select
                      value={selection[attribute.code] ?? ""}
                      onChange={(event) =>
                        setSelection((current) => ({
                          ...current,
                          [attribute.code]: event.target.value,
                        }))
                      }
                    >
                      <option value="">Seçin</option>
                      {attribute.options.map((option) => (
                        <option key={option.id} value={option.id}>
                          {option.display}
                        </option>
                      ))}
                    </select>
                  </label>
                ))}
            </div>

            {namePreview && (
              <div className="erp-panel rw-panel-highlight">
                <strong>Oluşacak ad:</strong> {namePreview}
                <small style={{ display: "block" }}>
                  Ad özelliklerden üretilir; elle yazılmaz. Aynı özellik
                  kombinasyonu ikinci kez kart olamaz.
                </small>
              </div>
            )}
          </>
        )}

        {isFree && (
          <label className="span-2">
            <span>Malzeme Adı *</span>
            <input
              required
              value={freeName}
              onChange={(event) => setFreeName(event.target.value)}
              placeholder="Lento Sarkıt 3'lü Siyah Gold"
            />
            <small>
              Serbest kategori: ad elle yazılır ve mükerrer engeli
              uygulanmaz — her ürün tekildir.
            </small>
          </label>
        )}

        <h2>Diğer bilgiler</h2>

        <div className="erp-form-grid">
          <label>
            <span>Marka</span>
            <input value={brand} onChange={(e) => setBrand(e.target.value)} />
          </label>

          <label>
            <span>Model</span>
            <input value={model} onChange={(e) => setModel(e.target.value)} />
          </label>

          <label>
            <span>Barkod</span>
            <input value={barcode} onChange={(e) => setBarcode(e.target.value)} />
          </label>

          {/*
            * ASGARİ/AZAMİ BURADA SORULMUYOR (S8): eşik depoya ait, karta
            * değil. Kart açılırken hangi depoda ne kadar bulundurulacağı
            * henüz belli olmaz; tanım /depo-stok/stok-seviyeleri
            * ekranından yapılır.
            */}

          <label>
            <span>Tedarik Tipi</span>
            <select
              value={supplyKind}
              onChange={(e) => setSupplyKind(e.target.value)}
            >
              <option value="0">Stoklu</option>
              <option value="1">Özel imalat</option>
              <option value="2">Sipariş üzerine</option>
            </select>
            <small>
              Asgari/azami seviye takibi yalnız <strong>stoklu</strong>
              kartlarda tanımlanabilir.
            </small>
          </label>

          <label>
            <span>Proje Bağı</span>
            <select
              value={projectId}
              onChange={(e) => setProjectId(e.target.value)}
              disabled={!companyId}
            >
              <option value="">Bağsız (katalog malzemesi)</option>
              {projects.map((project) => (
                <option key={project.id} value={project.id}>
                  {project.name}
                </option>
              ))}
            </select>
            <small>
              <strong>Bağlayıcıdır:</strong> bağı olan kart başka bir işe
              çıkarılamaz ve satılamaz.
            </small>
          </label>

          <label>
            <span>Tip</span>
            <select
              value={type}
              onChange={(e) => setType(Number(e.target.value) as InventoryItemType)}
            >
              <option value={0}>Malzeme</option>
              <option value={1}>Ekipman</option>
              <option value={2}>Sarf</option>
              <option value={3}>Yedek Parça</option>
            </select>
          </label>

          <label>
            <span>KDV Oranı (%)</span>
            <input
              type="number"
              min="0"
              max="100"
              step="0.01"
              value={vatRate}
              onChange={(e) => setVatRate(e.target.value)}
            />
          </label>

          <label>
            <span>Birim Başına Bakır (kg)</span>
            <input
              type="number"
              min="0"
              step="0.0001"
              value={copperKgPerUnit}
              onChange={(e) => setCopperKgPerUnit(e.target.value)}
            />
            <small>Boş bırakmak &quot;bilinmiyor&quot; demek, sıfır değil.</small>
          </label>

          <label className="span-2">
            <span>Açıklama</span>
            <input
              value={description}
              onChange={(e) => setDescription(e.target.value)}
            />
          </label>
        </div>

        {validation.length > 0 && (
          <div className="erp-alert warning">
            <ul>
              {validation.map((message) => (
                <li key={message}>{message}</li>
              ))}
            </ul>
          </div>
        )}

        <div className="erp-actions">
          <button
            type="submit"
            className="erp-primary-button"
            disabled={saving || loading || validation.length > 0}
          >
            {saving ? "Kaydediliyor…" : "Kartı Oluştur"}
          </button>
        </div>
      </form>
    </ErpShell>
  );
}
