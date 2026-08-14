"use client";

import { useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { moneyWhole } from "@/lib/format/turkish";

import {
  financeService,
  type FinanceDashboard,
} from "@/services/finance.service";

import {
  projectFinanceService,
  type ProjectFinanceSummary,
} from "@/services/project-finance.service";

import {
  cashFlowService,
  type CashFlowSummary,
} from "@/services/cash-flow.service";

import {
  financeAIService,
  type FinanceAnalysis,
} from "@/services/finance-ai.service";

import {
  supplierBalanceService,
  type SupplierBalanceSummary,
} from "@/services/supplier-balance.service";

import {
  currentAccountService,
  type CurrentAccountSummary,
} from "@/services/current-account.service";



const DATA_PENDING_LABEL = "Veri henüz yok";


export default function FinancePage() {

  const [data,setData] =
    useState<FinanceDashboard | null>(null);

  const [projects,setProjects] =
    useState<ProjectFinanceSummary[]>([]);

  const [cashFlow,setCashFlow] =
    useState<CashFlowSummary | null>(null);

  const [aiAnalysis,setAiAnalysis] =
    useState<FinanceAnalysis | null>(null);

  const [suppliers,setSuppliers] =
    useState<SupplierBalanceSummary[]>([]);

  const [suppliersAvailable,setSuppliersAvailable] =
    useState(true);

  const [suppliersMessage,setSuppliersMessage] =
    useState("");

  const [cari,setCari] =
    useState<CurrentAccountSummary | null>(null);

  const [loading,setLoading] =
    useState(true);

  const [error,setError] =
    useState("");


  async function load(){

    try {

      const [
        result,
        cariResult,
        projectResult,
        cashResult,
        aiResult,
        supplierResult,
      ] = await Promise.all([
        financeService.getDashboard(),
        currentAccountService.getSummary(),
        projectFinanceService.getSummary(),
        cashFlowService.getSummary(),
        financeAIService.analyze(),
        supplierBalanceService.getSummary(),
      ]);

      setData(result);
      setCari(cariResult);
      setProjects(projectResult);
      setCashFlow(cashResult);
      setAiAnalysis(aiResult);
      setSuppliers(supplierResult.items);
      setSuppliersAvailable(supplierResult.available);
      setSuppliersMessage(supplierResult.message ?? "");

    }
    catch(err){

      setError(
        err instanceof Error
          ? err.message
          : "Finans verileri alınamadı."
      );

    }
    finally {

      setLoading(false);

    }

  }


  useEffect(()=>{

    void load();

  },[]);



  const cards = data ? [

    {
      title:"Toplam Sözleşme",
      value:moneyWhole(
        data.totalContractAmount
      ),
      pending: false
    },

    {
      title:"Toplam Hakediş",
      value:moneyWhole(
        data.totalProgressPaymentAmount
      ),
      pending: false
    },

    {
      title:"Fiyat Farkı",
      value:moneyWhole(
        data.totalPriceDifferenceAmount
      ),
      pending: false
    },

    {
      title:"Toplam Kesinti",
      value:moneyWhole(
        data.totalDeductionAmount
      ),
      pending: false
    },

    {
      title:"Net Ödeme",
      value:moneyWhole(
        data.totalNetPayableAmount
      ),
      pending: false
    },

    {
      title:"Aktif Proje",
      value:
        String(data.activeProjectCount),
      pending: false
    },

    {
      title:"Hakediş Sayısı",
      value:
        String(data.progressPaymentCount),
      pending: false
    },


    {
      title:"Toplam Alacak",
      value:
        moneyWhole(
          cari?.totalReceivable ?? 0
        ),
      pending: cari ? !cari.balancesAvailable : false
    },


    {
      title:"Toplam Borç",
      value:
        moneyWhole(
          cari?.totalPayable ?? 0
        ),
      pending: cari ? !cari.balancesAvailable : false
    },


    {
      title:"Net Cari Pozisyon",
      value:
        moneyWhole(
          cari?.netBalance ?? 0
        ),
      pending: cari ? !cari.balancesAvailable : false
    },


    {
      title:"Cari Sayısı",
      value:
        String(
          cari?.accountCount ?? 0
        ),
      pending: false
    },

  ] : [];



  return (

    <ErpShell
      design="redwood"
      title="Finans Merkezi"
      description="Hakediş, fiyat farkı ve nakit görünümü"
    >


      {error && (
        <div className="erp-alert error">
          {error}
        </div>
      )}


      {loading ? (

        <div className="erp-panel">
          Finans verileri yükleniyor...
        </div>

      ) : (

        <>


        <section className="enderun-dashboard-stats">

          {cards.map(card=>(

            <div
              key={card.title}
              className={`enderun-dashboard-stat${
                card.pending ? " is-pending" : ""
              }`}
            >

              <div>

                <span>
                  {card.title}
                </span>

                <strong>
                  {card.pending ? "—" : card.value}
                </strong>

                {card.pending && (
                  <span className="erp-pending-badge">
                    {DATA_PENDING_LABEL}
                  </span>
                )}

              </div>

            </div>

          ))}

        </section>



        <section
          className="erp-panel"
          style={{marginTop:20}}
        >

          <div className="erp-panel-header">

            <div>

              <h2>
                Finans Özeti
              </h2>

              <p>
                Enderun AI finans görünümü
              </p>

            </div>

          </div>


          <div className="erp-detail-grid">

            <div>
              <span>
                Sözleşme Portföyü
              </span>

              <strong>
                {moneyWhole(
                  data?.totalContractAmount ?? 0
                )}
              </strong>
            </div>


            <div>
              <span>
                Hakediş Gerçekleşme
              </span>

              <strong>
                {moneyWhole(
                  data?.totalProgressPaymentAmount ?? 0
                )}
              </strong>
            </div>


            <div>
              <span>
                Fiyat Farkı
              </span>

              <strong>
                {moneyWhole(
                  data?.totalPriceDifferenceAmount ?? 0
                )}
              </strong>
            </div>


            <div>
              <span>
                Net Ödeme
              </span>

              <strong>
                {moneyWhole(
                  data?.totalNetPayableAmount ?? 0
                )}
              </strong>
            </div>

          </div>


        </section>


        <section
          className="erp-panel"
          style={{marginTop:20}}
        >

          <div className="erp-panel-header">
            <div>
              <h2>
                Proje Finans Durumu
              </h2>

              <p>
                Proje bazlı sözleşme, hakediş ve kalan tutarlar
              </p>
            </div>
          </div>


          <div className="erp-project-list">

            {projects.map((project) => (

              <div
                key={project.projectId}
                className="erp-project-list-item"
              >

                <div>
                  <strong>
                    {project.projectName}
                  </strong>

                  <span>
                    {project.projectCode}
                  </span>
                </div>


                <div>
                  <span>
                    Sözleşme
                  </span>

                  <strong>
                    {moneyWhole(
                      project.contractAmount
                    )}
                  </strong>
                </div>


                <div>
                  <span>
                    Hakediş
                  </span>

                  <strong>
                    {moneyWhole(
                      project.progressPaymentAmount
                    )}
                  </strong>
                </div>


                <div>
                  <span>
                    Kalan
                  </span>

                  <strong>
                    {moneyWhole(
                      project.remainingAmount
                    )}
                  </strong>
                </div>

              </div>

            ))}

          </div>

        </section>



        <section
          className="erp-panel"
          style={{marginTop:20}}
        >

          <div className="erp-panel-header">
            <div>
              <h2>
                Nakit Akış Özeti
              </h2>

              <p>
                Gelir, gider ve net nakit durumu
              </p>
            </div>
          </div>


          {cashFlow && !cashFlow.available ? (

            <div className="erp-data-pending-panel">
              {cashFlow.message ||
                "Kasa/banka hareket modülü henüz uygulamaya bağlı değil."}
            </div>

          ) : (

          <div className="erp-detail-grid">

            <div>
              <span>
                Gelen
              </span>

              <strong>
                {moneyWhole(
                  cashFlow?.totalIncome ?? 0
                )}
              </strong>
            </div>


            <div>
              <span>
                Giden
              </span>

              <strong>
                {moneyWhole(
                  cashFlow?.totalExpense ?? 0
                )}
              </strong>
            </div>


            <div>
              <span>
                Net Nakit
              </span>

              <strong>
                {moneyWhole(
                  cashFlow?.netCash ?? 0
                )}
              </strong>
            </div>

          </div>

          )}

        </section>



        <section
          className="erp-panel"
          style={{marginTop:20}}
        >

          <div className="erp-panel-header">
            <div>
              <h2>
                Tedarikçi Borç Durumu
              </h2>

              <p>
                Açık tedarikçi bakiye görünümü
              </p>
            </div>
          </div>


          {!suppliersAvailable ? (

            <div className="erp-data-pending-panel">
              {suppliersMessage ||
                "Tedarikçi bakiyesi için fatura ve ödeme kayıtları henüz uygulamaya bağlı değil."}
            </div>

          ) : suppliers.length === 0 ? (

            <div className="erp-empty-state">
              <p>Henüz tedarikçi bakiye kaydı yok.</p>
            </div>

          ) : (

          <div className="erp-project-list">

            {suppliers.map((supplier) => (

              <div
                key={supplier.supplierId}
                className="erp-project-list-item"
              >

                <div>
                  <strong>
                    {supplier.supplierName}
                  </strong>
                </div>


                <div>
                  <span>
                    Borç
                  </span>

                  <strong>
                    {moneyWhole(
                      supplier.totalDebt
                    )}
                  </strong>
                </div>


                <div>
                  <span>
                    Ödenen
                  </span>

                  <strong>
                    {moneyWhole(
                      supplier.totalPaid
                    )}
                  </strong>
                </div>


                <div>
                  <span>
                    Bakiye
                  </span>

                  <strong>
                    {moneyWhole(
                      supplier.balance
                    )}
                  </strong>
                </div>

              </div>

            ))}

          </div>

          )}

        </section>



        <section
          className="erp-panel"
          style={{marginTop:20}}
        >

          <h2>
            AI Finans Yorumu
          </h2>

          <p>
            {aiAnalysis?.summary ??
              "AI finans analizi hazırlanıyor."}
          </p>


          {aiAnalysis?.warnings &&
            aiAnalysis.warnings.length > 0 && (

            <div
              style={{
                marginTop: 16,
                display: "grid",
                gap: 8,
              }}
            >

              {aiAnalysis.warnings.map(
                (warning, index) => (

                  <div
                    key={index}
                    className="erp-alert warning"
                  >
                    {warning}
                  </div>

                )
              )}

            </div>

          )}


        </section>


        </>

      )}

    </ErpShell>

  );

}
