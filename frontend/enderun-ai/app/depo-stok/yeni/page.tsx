"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";

import ErpShell from "@/components/erp/erp-shell";
import {
  inventoryService,
  type CompanyOption,
  type CreateInventoryItemRequest,
  type InventoryItemType,
} from "@/services/inventory.service";

const initialForm: CreateInventoryItemRequest = {
  companyId: "",
  code: "",
  name: "",
  category: "",
  brand: "",
  model: "",
  unit: "Adet",
  barcode: "",
  minimumStock: 0,
  maximumStock: 0,
  type: 0,
};

const UNITS = ["Adet", "Metre", "Kg", "Takım", "Kutu", "Paket", "Rulo"];

export default function CreateInventoryItemPage() {
  const router = useRouter();

  const [form, setForm] = useState<CreateInventoryItemRequest>(initialForm);

  // Ayrı metin durumu: boş bırakmak "bilinmiyor" demek, sıfır demek
  // değil. Sayı durumunda tutulsaydı boş alan sıfıra düşer ve
  // malzemenin bakır içermediği iddia edilmiş olurdu.
  const [copperKgPerUnit, setCopperKgPerUnit] = useState("");
  const [companies, setCompanies] = useState<CompanyOption[]>([]);
  const [loadingCompanies, setLoadingCompanies] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    void (async () => {
      try {
        const data = await inventoryService.getCompanies();
        setCompanies(data);

        if (data.length === 1) {
          setForm((current) => ({ ...current, companyId: data[0].id }));
        }
      } catch (err) {
        setError(
          err instanceof Error ? err.message : "Şirket listesi yüklenemedi."
        );
      } finally {
        setLoadingCompanies(false);
      }
    })();
  }, []);

  function update<K extends keyof CreateInventoryItemRequest>(
    key: K,
    value: CreateInventoryItemRequest[K]
  ) {
    setForm((current) => ({ ...current, [key]: value }));
  }

  const validationErrors: string[] = [];
  if (!form.companyId) validationErrors.push("Şirket seçin.");
  if (!form.code.trim()) validationErrors.push("Malzeme kodu girin.");
  if (!form.name.trim()) validationErrors.push("Malzeme adı girin.");
  if (!form.unit.trim()) validationErrors.push("Birim seçin.");
  if (
    form.maximumStock > 0 &&
    form.minimumStock > 0 &&
    form.maximumStock < form.minimumStock
  ) {
    validationErrors.push("Maksimum stok minimumdan küçük olamaz.");
  }

  async function submit(event: React.FormEvent) {
    event.preventDefault();

    // Buton pasif bırakılmıyor; eksik varsa ne eksik olduğu yazılıyor.
    if (validationErrors.length > 0) {
      setError(validationErrors.join(" "));
      return;
    }

    setSaving(true);
    setError("");

    try {
      await inventoryService.createItem({
        ...form,
        code: form.code.trim(),
        name: form.name.trim(),
        unit: form.unit.trim(),
        // Boş bırakılan katsayı sıfır değil "bilinmiyor" demek: sıfır
        // yazmak, bakır içermediğini iddia etmek olurdu.
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
      description="Şirket genelinde kullanılacak malzeme veya demirbaş kartı"
    >
      {error && <div className="erp-alert error">{error}</div>}

      <form className="erp-form-card" onSubmit={submit}>
        <div className="erp-form-header">
          <h2>Kart Bilgileri</h2>
          <p>
            Kod ve ad zorunlu. Minimum stok girilirse kalem, seviyenin altına
            düştüğünde panelde ve Hızır brifinginde kritik olarak görünür.
          </p>
        </div>

        <div className="erp-form-grid">
          <label>
            <span>Şirket *</span>
            <select
              value={form.companyId}
              onChange={(event) => update("companyId", event.target.value)}
              disabled={loadingCompanies}
            >
              <option value="">
                {loadingCompanies ? "Yükleniyor..." : "Şirket seçin"}
              </option>
              {companies.map((company) => (
                <option key={company.id} value={company.id}>
                  {company.name}
                </option>
              ))}
            </select>
          </label>

          <label>
            <span>Malzeme Tipi *</span>
            <select
              value={form.type}
              onChange={(event) =>
                update("type", Number(event.target.value) as InventoryItemType)
              }
            >
              <option value={0}>Stok malzemesi</option>
              <option value={1}>Sarf malzemesi</option>
              <option value={2}>Demirbaş</option>
            </select>
          </label>

          <label>
            <span>Malzeme Kodu *</span>
            <input
              type="text"
              value={form.code}
              onChange={(event) => update("code", event.target.value)}
              placeholder="Örn. ELK-KBL-0001"
            />
          </label>

          <label>
            <span>Malzeme Adı *</span>
            <input
              type="text"
              value={form.name}
              onChange={(event) => update("name", event.target.value)}
              placeholder="Örn. NYY 5x10 mm² kablo"
            />
          </label>

          <label>
            <span>Kategori</span>
            <input
              type="text"
              value={form.category}
              onChange={(event) => update("category", event.target.value)}
              placeholder="Örn. Enerji kabloları"
            />
          </label>

          <label>
            <span>Birim *</span>
            <select
              value={form.unit}
              onChange={(event) => update("unit", event.target.value)}
            >
              {UNITS.map((unit) => (
                <option key={unit} value={unit}>
                  {unit}
                </option>
              ))}
            </select>
          </label>

          <label>
            <span>Marka</span>
            <input
              type="text"
              value={form.brand}
              onChange={(event) => update("brand", event.target.value)}
              placeholder="Örn. Öznur"
            />
          </label>

          <label>
            <span>Model</span>
            <input
              type="text"
              value={form.model}
              onChange={(event) => update("model", event.target.value)}
              placeholder="Model veya üretici kodu"
            />
          </label>

          <label>
            <span>Barkod</span>
            <input
              type="text"
              value={form.barcode}
              onChange={(event) => update("barcode", event.target.value)}
              placeholder="Barkod numarası"
            />
          </label>

          <label>
            <span>Minimum Stok</span>
            <input
              type="number"
              min="0"
              step="0.01"
              value={form.minimumStock}
              onChange={(event) =>
                update("minimumStock", Number(event.target.value))
              }
            />
            <small>0 bırakılırsa kritik stok uyarısı üretilmez.</small>
          </label>

          <label>
            <span>Bakır Katsayısı (kg/birim)</span>
            <input
              type="number"
              min="0"
              step="0.0001"
              value={copperKgPerUnit}
              onChange={(event) => setCopperKgPerUnit(event.target.value)}
              placeholder="Örn. 0,0675"
            />
            <small>
              Birim başına bakır miktarı. Bakır maruziyeti raporu yalnızca bu
              alandan beslenir; boş bırakılan malzeme emtia riskine hiç
              girmez.
            </small>
          </label>

          <label>
            <span>Maksimum Stok</span>
            <input
              type="number"
              min="0"
              step="0.01"
              value={form.maximumStock}
              onChange={(event) =>
                update("maximumStock", Number(event.target.value))
              }
            />
          </label>
        </div>

        <div className="erp-form-actions">
          <button
            type="button"
            className="erp-secondary-button"
            onClick={() => router.push("/depo-stok")}
          >
            Vazgeç
          </button>

          <button type="submit" className="erp-primary-button" disabled={saving}>
            {saving ? "Kaydediliyor..." : "Malzeme Kartını Oluştur"}
          </button>
        </div>
      </form>
    </ErpShell>
  );
}
