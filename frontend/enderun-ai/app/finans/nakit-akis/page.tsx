"use client";

import { useCallback, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import {
  cashFlowService,
  type CashFlowForecast,
  type CashFlowItem,
} from "@/services/cash-flow.service";
import { companyService, type CompanyListItem } from "@/services/company.service";
import { projectService, type ProjectListItem } from "@/services/project.service";

const money = new Intl.NumberFormat("tr-TR", {
  style: "currency",
  currency: "TRY",
});

const dateFormat = new Intl.DateTimeFormat("tr-TR");

function ItemTable({
  title,
  emptyText,
  items,
}: {
  title: string;
  emptyText: string;
  items: CashFlowItem[];
}) {
  return (
    <div className="erp-table-card">
      <div className="erp-table-header">
        <h2>{title}</h2>
        <strong>{money.format(items.reduce((sum, item) => sum + item.amount, 0))}</strong>
      </div>

      {items.length === 0 ? (
        <div className="erp-empty-state">
          <strong>Kayıt yok</strong>
          <p>{emptyText}</p>
        </div>
      ) : (
        <div className="erp-table-wrap">
          <table className="erp-table">
            <thead>
              <tr>
                <th>Vade</th>
                <th>Tür</th>
                <th>Referans</th>
                <th>Cari</th>
                <th>Proje</th>
                <th style={{ textAlign: "right" }}>Tutar</th>
              </tr>
            </thead>
            <tbody>
              {items.map((item) => (
                <tr key={`${item.kind}-${item.sourceId}`}>
                  <td>
                    {dateFormat.format(new Date(item.expectedDate))}
                    <small>
                      {item.isOverdue
                        ? `${Math.abs(item.daysToDue)} gün gecikmiş`
                        : `${item.daysToDue} gün`}
                    </small>
                  </td>
                  <td>
                    <span className={`erp-status ${item.isOverdue ? "red" : "blue"}`}>
                      {item.kindName}
                    </span>
                  </td>
                  <td>
                    <strong>{item.reference}</strong>
                    <small>{item.title}</small>
                  </td>
                  <td>{item.currentAccountTitle ?? "—"}</td>
                  <td>{item.projectCode ?? "—"}</td>
                  <td style={{ textAlign: "right" }}>
                    <strong>{money.format(item.amount)}</strong>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

export default function CashFlowPage() {
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [companyId, setCompanyId] = useState("");
  const [projects, setProjects] = useState<ProjectListItem[]>([]);
  const [projectId, setProjectId] = useState("");

  const [forecast, setForecast] = useState<CashFlowForecast | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const loadCompanies = useCallback(async () => {
    try {
      const result = await companyService.getAll();
      setCompanies(result);
      setCompanyId((current) => current || result[0]?.id || "");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Şirketler alınamadı.");
    }
  }, []);

  const loadProjects = useCallback(async () => {
    if (!companyId) return;

    try {
      setProjects(await projectService.getAll(companyId));
    } catch {
      setProjects([]);
    }
  }, [companyId]);

  const loadForecast = useCallback(async () => {
    if (!companyId) {
      setForecast(null);
      setLoading(false);
      return;
    }

    setLoading(true);
    setError("");

    try {
      setForecast(
        await cashFlowService.getForecast({
          companyId,
          projectId: projectId || undefined,
        })
      );
    } catch (err) {
      setForecast(null);
      setError(err instanceof Error ? err.message : "Nakit akışı alınamadı.");
    } finally {
      setLoading(false);
    }
  }, [companyId, projectId]);

  useEffect(() => {
    void loadCompanies();
  }, [loadCompanies]);

  useEffect(() => {
    void loadProjects();
  }, [loadProjects]);

  useEffect(() => {
    void loadForecast();
  }, [loadForecast]);

  return (
    <ErpShell
      title="Nakit Akışı"
      description="Vade bazlı beklenen tahsilatlar ve ödemeler — 30 / 60 / 90 gün"
    >
      <div className="erp-page-toolbar">
        <div>
          {forecast && (
            <>
              <strong>Mevcut kasa/banka: {money.format(forecast.currentCashBalance)}</strong>
              <small style={{ display: "block", marginTop: "4px" }}>
                {dateFormat.format(new Date(forecast.asOfDate))} itibarıyla
              </small>
            </>
          )}
        </div>

        <div style={{ display: "flex", gap: "8px", flexWrap: "wrap" }}>
          <select value={companyId} onChange={(e) => setCompanyId(e.target.value)}>
            {companies.map((company) => (
              <option key={company.id} value={company.id}>
                {company.name}
              </option>
            ))}
          </select>

          <select value={projectId} onChange={(e) => setProjectId(e.target.value)}>
            <option value="">Tüm projeler</option>
            {projects.map((project) => (
              <option key={project.id} value={project.id}>
                {project.code} — {project.name}
              </option>
            ))}
          </select>
        </div>
      </div>

      {error && <div className="erp-alert error">{error}</div>}

      {loading ? (
        <div className="erp-loading">Nakit akışı hesaplanıyor...</div>
      ) : !forecast ? (
        <div className="erp-empty-state">
          <strong>Veri yok</strong>
          <p>Seçili şirket için nakit akışı hesaplanamadı.</p>
        </div>
      ) : (
        <>
          {(forecast.overdueInflowAmount > 0 || forecast.overdueOutflowAmount > 0) && (
            <div className="erp-alert">
              Vadesi geçmiş: {money.format(forecast.overdueInflowAmount)} beklenen tahsilat,{" "}
              {money.format(forecast.overdueOutflowAmount)} beklenen ödeme. Bu tutarlar
              aşağıdaki 30/60/90 gün projeksiyonuna dahil edilmedi.
            </div>
          )}

          <div className="erp-table-card" style={{ marginBottom: "16px" }}>
            <div className="erp-table-header">
              <h2>Önümüzdeki Dönem Projeksiyonu</h2>
            </div>

            <div className="erp-table-wrap">
              <table className="erp-table">
                <thead>
                  <tr>
                    <th>Dönem</th>
                    <th style={{ textAlign: "right" }}>Girecek</th>
                    <th style={{ textAlign: "right" }}>Çıkacak</th>
                    <th style={{ textAlign: "right" }}>Net</th>
                    <th style={{ textAlign: "right" }}>Tahmini Bakiye</th>
                  </tr>
                </thead>
                <tbody>
                  <tr>
                    <td>
                      <strong>Bugün</strong>
                    </td>
                    <td style={{ textAlign: "right" }}>—</td>
                    <td style={{ textAlign: "right" }}>—</td>
                    <td style={{ textAlign: "right" }}>—</td>
                    <td style={{ textAlign: "right" }}>
                      <strong>{money.format(forecast.currentCashBalance)}</strong>
                    </td>
                  </tr>
                  {forecast.buckets.map((bucket) => (
                    <tr key={bucket.days}>
                      <td>
                        <strong>{bucket.label}</strong>
                      </td>
                      <td style={{ textAlign: "right" }}>
                        {money.format(bucket.inflowAmount)}
                      </td>
                      <td style={{ textAlign: "right" }}>
                        {money.format(bucket.outflowAmount)}
                      </td>
                      <td style={{ textAlign: "right" }}>
                        <span
                          className={`erp-status ${bucket.netAmount >= 0 ? "green" : "red"}`}
                        >
                          {money.format(bucket.netAmount)}
                        </span>
                      </td>
                      <td style={{ textAlign: "right" }}>
                        <strong>{money.format(bucket.projectedBalance)}</strong>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>

          <div style={{ display: "grid", gap: "16px" }}>
            <ItemTable
              title="Beklenen Tahsilatlar"
              emptyText="Portföyde/bankada bekleyen çek ya da tahsil edilmemiş hakediş yok."
              items={forecast.inflows}
            />

            <ItemTable
              title="Beklenen Ödemeler"
              emptyText="Vadesi bekleyen verilen çek ya da ödenmemiş tedarikçi faturası yok."
              items={forecast.outflows}
            />
          </div>
        </>
      )}
    </ErpShell>
  );
}
