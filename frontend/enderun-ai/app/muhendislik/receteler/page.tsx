"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";
import ErpShell from "@/components/erp/erp-shell";
import { decimal, whole } from "@/lib/format/turkish";
import { Button } from "@/components/ui";
import {
  EngineeringRecipeCoverage,
  EngineeringRecipeListItem,
  engineeringRecipeService,
} from "@/services/engineering-recipe.service";

type RecipeRow = EngineeringRecipeListItem & {
  discipline: number;
  unit: string;
};

const disciplineLabels: Record<number, string> = {
  0: "Genel",
  1: "Elektrik",
  2: "Orta Gerilim",
  3: "Zayıf Akım",
  4: "Veri Merkezi",
  5: "Fiber",
  6: "Mekanik",
  7: "İnşaat",
};

export default function EngineeringRecipesPage() {
  const [recipes, setRecipes] = useState<RecipeRow[]>([]);
  const [coverage, setCoverage] = useState<EngineeringRecipeCoverage | null>(
    null
  );
  const [search, setSearch] = useState("");
  const [discipline, setDiscipline] = useState("");
  const [onlyDefault, setOnlyDefault] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const load = useCallback(async () => {
    setLoading(true);
    setError("");

    try {
      // TEK ÇAĞRI: eskiden önce pozlar çekilip HER POZ İÇİN ayrı reçete
      // isteği atılıyordu. Poz ucu 100 kayıt döndürdüğü için ekran
      // 23.500 pozun ancak ilk yüzünü tarıyor, buna karşılık 100 istek
      // yapıyordu — hem yavaş hem eksik.
      const [recipeItems, coverageResult] = await Promise.all([
        engineeringRecipeService.getAll({
          search: search.trim() || undefined,
          discipline: discipline === "" ? undefined : Number(discipline),
          onlyDefault: onlyDefault || undefined,
          take: 500,
        }),
        engineeringRecipeService.getCoverage(),
      ]);

      setRecipes(
        recipeItems.map((recipe) => ({
          ...recipe,
          discipline: recipe.discipline ?? 0,
          unit: recipe.unit ?? "",
        }))
      );

      setCoverage(coverageResult);
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Mühendislik reçeteleri yüklenemedi."
      );
    } finally {
      setLoading(false);
    }
  }, [search, discipline, onlyDefault]);

  useEffect(() => {
    load();
  }, [load]);

  // Filtreleme SUNUCUDA yapılıyor (arama, disiplin, yalnız varsayılan).
  // İkinci bir istemci filtresi, sunucunun döndürmediği kayıtları
  // süzüyormuş gibi görünür ve kullanıcıya eksik listeyi tam sanki
  // gösterirdi.
  const filteredRecipes = recipes;

  const totalLaborHours = useMemo(
    () =>
      filteredRecipes.reduce(
        (sum, recipe) => sum + Number(recipe.totalLaborHours || 0),
        0
      ),
    [filteredRecipes]
  );

  return (
    <ErpShell
      design="redwood"
      title="Mühendislik Reçeteleri"
      description="Pozlara bağlı malzeme, işçilik ve makine analizleri"
    >
      {error && <div className="erp-alert error">{error}</div>}

      <section className="enderun-dashboard-hero">
        <div>
          <span className="enderun-dashboard-kicker">
            RECIPE ENGINE
          </span>

          <h2>Mühendislik reçeteleri</h2>

          <p>
            Pozların malzeme, işçilik, fire, makine ve adam/saat
            analizlerini tek merkezden yönetin.
          </p>
        </div>

        <div className="enderun-dashboard-hero-actions">
          <Link href="/muhendislik" className="erp-secondary-button">
            Mühendislik Merkezi
          </Link>

          <Link
            href="/muhendislik/pozlar"
            className="erp-primary-button"
          >
            Poz Kütüphanesini Aç
          </Link>
        </div>
      </section>

      <div className="enderun-dashboard-stats">
        <div className="enderun-dashboard-stat">
          <div className="enderun-dashboard-stat-icon">⚙</div>
          <div>
            <span>Toplam Reçete</span>
            <strong>{loading ? "…" : recipes.length}</strong>
            <small>Kayıtlı reçete versiyonları</small>
          </div>
        </div>

        <div className="enderun-dashboard-stat">
          <div className="enderun-dashboard-stat-icon">✓</div>
          <div>
            <span>Varsayılan Reçete</span>
            <strong>
              {loading
                ? "…"
                : recipes.filter((item) => item.isDefault).length}
            </strong>
            <small>Tekliflerde kullanılacak reçeteler</small>
          </div>
        </div>

        <div className="enderun-dashboard-stat">
          <div className="enderun-dashboard-stat-icon">!</div>
          <div>
            <span>Reçetesiz Poz</span>
            <strong>
              {loading || !coverage
                ? "…"
                : whole(coverage.positionsWithoutRecipe)}
            </strong>
            <small>Analizi henüz oluşturulmayan pozlar</small>
          </div>
        </div>

        <div className="enderun-dashboard-stat">
          <div className="enderun-dashboard-stat-icon">◷</div>
          <div>
            <span>Toplam Adam/Saat</span>
            <strong>
              {loading
                ? "…"
                : decimal(totalLaborHours, 2)}
            </strong>
            <small>Filtrelenen reçete toplamı</small>
          </div>
        </div>
      </div>

      <section className="erp-panel">
        <div className="erp-panel-header">
          <div>
            <h2>Reçete Listesi</h2>
            <p>Poz, disiplin ve reçete durumuna göre filtreleyin</p>
          </div>

          <Button variant="secondary" disabled={loading} onClick={load}>Yenile</Button>
        </div>

        <div
          style={{
            display: "grid",
            gridTemplateColumns: "minmax(280px, 1fr) 220px auto auto",
            gap: 12,
            marginBottom: 20,
          }}
        >
          <input
            className="erp-input"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder="Poz kodu, açıklama veya reçete notu ara..."
          />

          <select
            className="erp-input"
            value={discipline}
            onChange={(event) => setDiscipline(event.target.value)}
          >
            <option value="">Tüm disiplinler</option>

            {Object.entries(disciplineLabels).map(([value, label]) => (
              <option value={value} key={value}>
                {label}
              </option>
            ))}
          </select>

          <label
            className="erp-secondary-button"
            style={{ display: "flex", gap: 8, alignItems: "center" }}
          >
            <input
              type="checkbox"
              checked={onlyDefault}
              onChange={(event) => setOnlyDefault(event.target.checked)}
            />
            Yalnız varsayılanlar
          </label>

          <button
            type="button"
            className="erp-secondary-button"
            onClick={() => {
              setSearch("");
              setDiscipline("");
              setOnlyDefault(false);
            }}
          >
            Temizle
          </button>
        </div>

        {loading ? (
          <div className="erp-loading">
            Mühendislik reçeteleri yükleniyor...
          </div>
        ) : filteredRecipes.length === 0 ? (
          <div className="erp-empty-state">
            <div className="enderun-empty-symbol">⚙</div>
            <strong>Reçete bulunamadı</strong>
            <p>
              Poz Kütüphanesinden bir poz açarak ilk reçeteyi oluşturun.
            </p>

            <Link
              href="/muhendislik/pozlar"
              className="erp-primary-button"
            >
              Poz Kütüphanesine Git
            </Link>
          </div>
        ) : (
          <div style={{ overflowX: "auto" }}>
            <table className="erp-table">
              <thead>
                <tr>
                  <th>Poz</th>
                  <th>Açıklama</th>
                  <th>Disiplin</th>
                  <th>Versiyon</th>
                  <th>Malzeme</th>
                  <th>İşçilik</th>
                  <th>Makine</th>
                  <th>Adam/Saat</th>
                  <th>Durum</th>
                  <th />
                </tr>
              </thead>

              <tbody>
                {filteredRecipes.map((recipe) => (
                  <tr key={recipe.id}>
                    <td>
                      <strong>{recipe.positionCode}</strong>
                      <small style={{ display: "block" }}>
                        {recipe.unit}
                      </small>
                    </td>

                    <td>
                      <strong>{recipe.positionName}</strong>
                      <small style={{ display: "block" }}>
                        {recipe.description || "Açıklama bulunmuyor"}
                      </small>
                    </td>

                    <td>
                      {disciplineLabels[recipe.discipline] ??
                        `Disiplin ${recipe.discipline}`}
                    </td>

                    <td>V{recipe.version}</td>
                    <td>{recipe.materialCount}</td>
                    <td>{recipe.laborCount}</td>
                    <td>{recipe.machineCount}</td>

                    <td>
                      {decimal(Number(recipe.totalLaborHours), 2)}
                    </td>

                    <td>
                      <span
                        className={
                          recipe.isDefault
                            ? "erp-status green"
                            : "erp-status"
                        }
                      >
                        {recipe.isDefault
                          ? "Varsayılan"
                          : "Alternatif"}
                      </span>
                    </td>

                    <td>
                      <Link
                        href={`/muhendislik/pozlar/${recipe.engineeringPositionId}`}
                        className="erp-row-link"
                      >
                        Düzenle →
                      </Link>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      {coverage && coverage.positionsWithoutRecipe > 0 && (
        <section className="erp-panel" style={{ marginTop: 20 }}>
          <div className="erp-panel-header">
            <div>
              <h2>Reçetesi Olmayan Pozlar</h2>
              <p>
                {whole(coverage.positionsWithoutRecipe)} pozun
                varsayılan reçetesi yok — bu pozlar proje malzeme ihtiyacına
                sıfır katkı verir, ihtiyaç listesinde ayrıca uyarı olarak
                görünür.
              </p>
            </div>

            <Link href="/muhendislik/receteler/ice-aktar" className="erp-button">
              Toplu Reçete Aktar
            </Link>
          </div>
        </section>
      )}

    </ErpShell>
  );
}
