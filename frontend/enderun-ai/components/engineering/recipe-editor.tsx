"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { decimal, quantity } from "@/lib/format/turkish";
import {
  EngineeringRecipeDetail,
  RecipeLabor,
  RecipeMachine,
  RecipeMaterial,
  engineeringRecipeService,
} from "@/services/engineering-recipe.service";
import {
  inventoryService,
  type InventoryItemListItem,
} from "@/services/inventory.service";

const laborTypes: Record<number, string> = {
  0: "Usta",
  1: "Yardımcı",
  2: "Teknisyen",
  3: "Mühendis",
  4: "Formen",
  5: "Test Mühendisi",
  99: "Diğer",
};

const emptyMaterial = (): RecipeMaterial => ({
  materialCode: "",
  materialName: "",
  quantity: 1,
  unit: "Adet",
  wastePercent: 0,
  notes: "",
});

const emptyLabor = (): RecipeLabor => ({
  laborType: 0,
  personCount: 1,
  hours: 1,
  notes: "",
});

const emptyMachine = (): RecipeMachine => ({
  machineName: "",
  quantity: 1,
  hours: 1,
  notes: "",
});

type Props = {
  positionId: string;
};

export default function RecipeEditor({ positionId }: Props) {
  const [recipeId, setRecipeId] = useState<string | null>(null);
  const [version, setVersion] = useState(1);
  const [description, setDescription] = useState("");
  const [isDefault, setIsDefault] = useState(true);
  const [materials, setMaterials] = useState<RecipeMaterial[]>([]);

  /**
   * Stok kartları. Reçete malzemesi karta BAĞLANMALI: proje malzeme
   * ihtiyacında depo mevcudu ve açık talep yalnız stok kartı üzerinden
   * düşülebiliyor — kartsız malzeme "eksik" hesabına hiç giremez.
   * Serbest metin alanları kaldırılmadı, çünkü katalogda olmayan
   * malzeme de yazılabilmeli; ama kart seçilince kod/ad/birim karttan
   * doldurulur ve ikisi ayrışmaz.
   */
  const [inventoryItems, setInventoryItems] = useState<InventoryItemListItem[]>(
    []
  );
  const [labors, setLabors] = useState<RecipeLabor[]>([]);
  const [machines, setMachines] = useState<RecipeMachine[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");

  const applyRecipe = useCallback((recipe: EngineeringRecipeDetail) => {
    setRecipeId(recipe.id);
    setVersion(recipe.version);
    setDescription(recipe.description ?? "");
    setIsDefault(recipe.isDefault);
    setMaterials(recipe.materials ?? []);
    setLabors(recipe.labors ?? []);
    setMachines(recipe.machines ?? []);
  }, []);

  const load = useCallback(async () => {
    setLoading(true);
    setError("");

    try {
      const list = await engineeringRecipeService.getByPosition(positionId);

      if (list.length === 0) {
        setRecipeId(null);
        setVersion(1);
        setDescription("");
        setIsDefault(true);
        setMaterials([]);
        setLabors([]);
        setMachines([]);
        return;
      }

      const selected = list.find((x) => x.isDefault) ?? list[0];
      const detail = await engineeringRecipeService.getById(selected.id);
      applyRecipe(detail);
    } catch (err) {
      setError(
        err instanceof Error ? err.message : "Reçete bilgileri yüklenemedi."
      );
    } finally {
      setLoading(false);
    }
  }, [applyRecipe, positionId]);

  useEffect(() => {
    load();
  }, [load]);

  useEffect(() => {
    void (async () => {
      try {
        setInventoryItems(await inventoryService.getItems());
      } catch {
        // Kart listesi gelmezse malzeme yine serbest metinle girilebilir;
        // reçete düzenleme kart listesi yüzünden kilitlenmemeli.
        setInventoryItems([]);
      }
    })();
  }, []);

  const totals = useMemo(() => {
    const laborHours = labors.reduce(
      (sum, item) => sum + Number(item.personCount) * Number(item.hours),
      0
    );

    const machineHours = machines.reduce(
      (sum, item) => sum + Number(item.quantity) * Number(item.hours),
      0
    );

    return {
      materialCount: materials.length,
      laborHours,
      machineHours,
    };
  }, [materials, labors, machines]);

  function updateMaterial(
    index: number,
    field: keyof RecipeMaterial,
    value: string | number
  ) {
    setMaterials((items) =>
      items.map((item, itemIndex) =>
        itemIndex === index ? { ...item, [field]: value } : item
      )
    );
  }

  function updateLabor(
    index: number,
    field: keyof RecipeLabor,
    value: string | number
  ) {
    setLabors((items) =>
      items.map((item, itemIndex) =>
        itemIndex === index ? { ...item, [field]: value } : item
      )
    );
  }

  function updateMachine(
    index: number,
    field: keyof RecipeMachine,
    value: string | number
  ) {
    setMachines((items) =>
      items.map((item, itemIndex) =>
        itemIndex === index ? { ...item, [field]: value } : item
      )
    );
  }

  async function save() {
    setSaving(true);
    setError("");
    setSuccess("");

    try {
      const payload = {
        description: description || null,
        isDefault,
        materials: materials.map((item) => ({
          inventoryItemId: item.inventoryItemId || null,
          materialCode: item.materialCode.trim(),
          materialName: item.materialName.trim(),
          quantity: Number(item.quantity),
          unit: item.unit.trim(),
          wastePercent: Number(item.wastePercent),
          notes: item.notes?.trim() || null,
        })),
        labors: labors.map((item) => ({
          laborType: Number(item.laborType),
          personCount: Number(item.personCount),
          hours: Number(item.hours),
          notes: item.notes?.trim() || null,
        })),
        machines: machines.map((item) => ({
          machineName: item.machineName.trim(),
          quantity: Number(item.quantity),
          hours: Number(item.hours),
          notes: item.notes?.trim() || null,
        })),
      };

      if (recipeId) {
        await engineeringRecipeService.update(recipeId, payload);
        setSuccess("Reçete başarıyla güncellendi.");
      } else {
        await engineeringRecipeService.create(positionId, payload);
        setSuccess("Yeni reçete başarıyla oluşturuldu.");
      }

      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Reçete kaydedilemedi.");
    } finally {
      setSaving(false);
    }
  }

  if (loading) {
    return <div className="erp-loading">Reçete yükleniyor...</div>;
  }

  return (
    <div>
      {error && <div className="erp-alert error">{error}</div>}
      {success && <div className="erp-alert success">{success}</div>}

      <div className="enderun-dashboard-stats">
        <div className="enderun-dashboard-stat">
          <div className="enderun-dashboard-stat-icon">R</div>
          <div>
            <span>Reçete</span>
            <strong>V{version}</strong>
            <small>{recipeId ? "Kayıtlı reçete" : "Yeni reçete"}</small>
          </div>
        </div>

        <div className="enderun-dashboard-stat">
          <div className="enderun-dashboard-stat-icon">▦</div>
          <div>
            <span>Malzeme</span>
            <strong>{totals.materialCount}</strong>
            <small>Reçete kalemi</small>
          </div>
        </div>

        <div className="enderun-dashboard-stat">
          <div className="enderun-dashboard-stat-icon">◷</div>
          <div>
            <span>İşçilik</span>
            <strong>{decimal(totals.laborHours, 2)}</strong>
            <small>Toplam adam/saat</small>
          </div>
        </div>

        <div className="enderun-dashboard-stat">
          <div className="enderun-dashboard-stat-icon">⚙</div>
          <div>
            <span>Makine</span>
            <strong>{decimal(totals.machineHours, 2)}</strong>
            <small>Toplam makine/saat</small>
          </div>
        </div>
      </div>

      <div className="erp-panel" style={{ marginTop: 20 }}>
        <div className="erp-panel-header">
          <div>
            <h2>Reçete Bilgileri</h2>
            <p>Poz için varsayılan mühendislik reçetesi</p>
          </div>

          <label style={{ display: "flex", gap: 8, alignItems: "center" }}>
            <input
              type="checkbox"
              checked={isDefault}
              onChange={(event) => setIsDefault(event.target.checked)}
            />
            Varsayılan reçete
          </label>
        </div>

        <textarea
          className="erp-input"
          rows={3}
          placeholder="Reçete açıklaması..."
          value={description}
          onChange={(event) => setDescription(event.target.value)}
        />
      </div>

      <section className="erp-panel" style={{ marginTop: 20 }}>
        <div className="erp-panel-header">
          <div>
            <h2>Malzemeler</h2>
            <p>Poz uygulamasında kullanılacak malzeme kalemleri</p>
          </div>

          <button
            type="button"
            className="erp-primary-button"
            onClick={() => setMaterials((items) => [...items, emptyMaterial()])}
          >
            + Malzeme Ekle
          </button>
        </div>

        {materials.length === 0 ? (
          <div className="erp-empty-state">
            <strong>Malzeme eklenmemiş</strong>
            <p>Reçeteye ilk malzeme kalemini ekleyin.</p>
          </div>
        ) : (
          <div style={{ overflowX: "auto" }}>
            <table className="erp-table">
              <thead>
                <tr>
                  <th>Stok Kartı</th>
                  <th>Kod</th>
                  <th>Malzeme</th>
                  <th>Miktar</th>
                  <th>Birim</th>
                  <th>Fire %</th>
                  <th>Efektif Miktar</th>
                  <th>Not</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {materials.map((item, index) => (
                  <tr key={index}>
                    <td>
                      <select
                        className="erp-input"
                        value={item.inventoryItemId ?? ""}
                        onChange={(event) => {
                          const selected = inventoryItems.find(
                            (card) => card.id === event.target.value
                          );

                          // Kart seçilince kod/ad/birim KARTTAN gelir.
                          // Elle yazılmaya devam edilseydi reçete ile
                          // stok kartı zamanla ayrışır, ihtiyaç yanlış
                          // malzemeye yazılırdı.
                          setMaterials((current) =>
                            current.map((row, rowIndex) =>
                              rowIndex === index
                                ? {
                                    ...row,
                                    inventoryItemId: selected?.id ?? null,
                                    materialCode: selected?.code ?? row.materialCode,
                                    materialName: selected?.name ?? row.materialName,
                                    unit: selected?.unit ?? row.unit,
                                  }
                                : row
                            )
                          );
                        }}
                      >
                        <option value="">— kart yok —</option>
                        {inventoryItems.map((card) => (
                          <option key={card.id} value={card.id}>
                            {card.code} · {card.name}
                          </option>
                        ))}
                      </select>
                    </td>
                    <td>
                      <input
                        className="erp-input"
                        value={item.materialCode}
                        onChange={(event) =>
                          updateMaterial(index, "materialCode", event.target.value)
                        }
                      />
                    </td>
                    <td>
                      <input
                        className="erp-input"
                        value={item.materialName}
                        onChange={(event) =>
                          updateMaterial(index, "materialName", event.target.value)
                        }
                      />
                    </td>
                    <td>
                      <input
                        className="erp-input"
                        type="number"
                        step="0.0001"
                        value={item.quantity}
                        onChange={(event) =>
                          updateMaterial(index, "quantity", Number(event.target.value))
                        }
                      />
                    </td>
                    <td>
                      <input
                        className="erp-input"
                        value={item.unit}
                        onChange={(event) =>
                          updateMaterial(index, "unit", event.target.value)
                        }
                      />
                    </td>
                    <td>
                      <input
                        className="erp-input"
                        type="number"
                        step="0.01"
                        value={item.wastePercent}
                        onChange={(event) =>
                          updateMaterial(
                            index,
                            "wastePercent",
                            Number(event.target.value)
                          )
                        }
                      />
                    </td>
                    <td>
                      {quantity(
                        Number(item.quantity) *
                          (1 + Number(item.wastePercent) / 100),
                      )}
                    </td>
                    <td>
                      <input
                        className="erp-input"
                        value={item.notes ?? ""}
                        onChange={(event) =>
                          updateMaterial(index, "notes", event.target.value)
                        }
                      />
                    </td>
                    <td>
                      <button
                        type="button"
                        className="erp-secondary-button"
                        onClick={() =>
                          setMaterials((items) =>
                            items.filter((_, itemIndex) => itemIndex !== index)
                          )
                        }
                      >
                        Sil
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className="erp-panel" style={{ marginTop: 20 }}>
        <div className="erp-panel-header">
          <div>
            <h2>İşçilik</h2>
            <p>Personel türü, kişi sayısı ve çalışma süresi</p>
          </div>

          <button
            type="button"
            className="erp-primary-button"
            onClick={() => setLabors((items) => [...items, emptyLabor()])}
          >
            + İşçilik Ekle
          </button>
        </div>

        <div style={{ overflowX: "auto" }}>
          <table className="erp-table">
            <thead>
              <tr>
                <th>İşçilik Türü</th>
                <th>Kişi</th>
                <th>Saat</th>
                <th>Toplam Adam/Saat</th>
                <th>Not</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {labors.map((item, index) => (
                <tr key={index}>
                  <td>
                    <select
                      className="erp-input"
                      value={item.laborType}
                      onChange={(event) =>
                        updateLabor(index, "laborType", Number(event.target.value))
                      }
                    >
                      {Object.entries(laborTypes).map(([value, label]) => (
                        <option key={value} value={value}>
                          {label}
                        </option>
                      ))}
                    </select>
                  </td>
                  <td>
                    <input
                      className="erp-input"
                      type="number"
                      step="0.01"
                      value={item.personCount}
                      onChange={(event) =>
                        updateLabor(index, "personCount", Number(event.target.value))
                      }
                    />
                  </td>
                  <td>
                    <input
                      className="erp-input"
                      type="number"
                      step="0.01"
                      value={item.hours}
                      onChange={(event) =>
                        updateLabor(index, "hours", Number(event.target.value))
                      }
                    />
                  </td>
                  <td>
                    {decimal(Number(item.personCount) * Number(item.hours), 2)}
                  </td>
                  <td>
                    <input
                      className="erp-input"
                      value={item.notes ?? ""}
                      onChange={(event) =>
                        updateLabor(index, "notes", event.target.value)
                      }
                    />
                  </td>
                  <td>
                    <button
                      type="button"
                      className="erp-secondary-button"
                      onClick={() =>
                        setLabors((items) =>
                          items.filter((_, itemIndex) => itemIndex !== index)
                        )
                      }
                    >
                      Sil
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <section className="erp-panel" style={{ marginTop: 20 }}>
        <div className="erp-panel-header">
          <div>
            <h2>Makinalar ve Ekipmanlar</h2>
            <p>Uygulama sırasında kullanılacak makina ve ekipmanlar</p>
          </div>

          <button
            type="button"
            className="erp-primary-button"
            onClick={() => setMachines((items) => [...items, emptyMachine()])}
          >
            + Makina Ekle
          </button>
        </div>

        <div style={{ overflowX: "auto" }}>
          <table className="erp-table">
            <thead>
              <tr>
                <th>Makina / Ekipman</th>
                <th>Adet</th>
                <th>Saat</th>
                <th>Toplam Saat</th>
                <th>Not</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {machines.map((item, index) => (
                <tr key={index}>
                  <td>
                    <input
                      className="erp-input"
                      value={item.machineName}
                      onChange={(event) =>
                        updateMachine(index, "machineName", event.target.value)
                      }
                    />
                  </td>
                  <td>
                    <input
                      className="erp-input"
                      type="number"
                      step="0.01"
                      value={item.quantity}
                      onChange={(event) =>
                        updateMachine(index, "quantity", Number(event.target.value))
                      }
                    />
                  </td>
                  <td>
                    <input
                      className="erp-input"
                      type="number"
                      step="0.01"
                      value={item.hours}
                      onChange={(event) =>
                        updateMachine(index, "hours", Number(event.target.value))
                      }
                    />
                  </td>
                  <td>
                    {decimal(Number(item.quantity) * Number(item.hours), 2)}
                  </td>
                  <td>
                    <input
                      className="erp-input"
                      value={item.notes ?? ""}
                      onChange={(event) =>
                        updateMachine(index, "notes", event.target.value)
                      }
                    />
                  </td>
                  <td>
                    <button
                      type="button"
                      className="erp-secondary-button"
                      onClick={() =>
                        setMachines((items) =>
                          items.filter((_, itemIndex) => itemIndex !== index)
                        )
                      }
                    >
                      Sil
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <div
        style={{
          display: "flex",
          justifyContent: "flex-end",
          marginTop: 24,
        }}
      >
        <button
          type="button"
          className="erp-primary-button"
          onClick={save}
          disabled={saving}
        >
          {saving
            ? "Reçete kaydediliyor..."
            : recipeId
              ? "Reçeteyi Güncelle"
              : "Reçeteyi Oluştur"}
        </button>
      </div>
    </div>
  );
}
