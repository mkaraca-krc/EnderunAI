"use client";

import { useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";

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


const money = new Intl.NumberFormat(
  "tr-TR",
  {
    style: "currency",
    currency: "TRY",
    maximumFractionDigits: 0,
  }
);


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
      setSuppliers(supplierResult);

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
      value:money.format(
        data.totalContractAmount
      )
    },

    {
      title:"Toplam Hakediş",
      value:money.format(
        data.totalProgressPaymentAmount
      )
    },

    {
      title:"Fiyat Farkı",
      value:money.format(
        data.totalPriceDifferenceAmount
      )
    },

    {
      title:"Toplam Kesinti",
      value:money.format(
        data.totalDeductionAmount
      )
    },

    {
      title:"Net Ödeme",
      value:money.format(
        data.totalNetPayableAmount
      )
    },

    {
      title:"Aktif Proje",
      value:
        String(data.activeProjectCount)
    },

    {
      title:"Hakediş Sayısı",
      value:
        String(data.progressPaymentCount)
    },


    {
      title:"Toplam Alacak",
      value:
        money.format(
          cari?.totalReceivable ?? 0
        )
    },


    {
      title:"Toplam Borç",
      value:
        money.format(
          cari?.totalPayable ?? 0
        )
    },


    {
      title:"Net Cari Pozisyon",
      value:
        money.format(
          cari?.netBalance ?? 0
        )
    },


    {
      title:"Cari Sayısı",
      value:
        String(
          cari?.accountCount ?? 0
        )
    },

  ] : [];



  return (

    <ErpShell
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
              className="enderun-dashboard-stat"
            >

              <div>

                <span>
                  {card.title}
                </span>

                <strong>
                  {card.value}
                </strong>

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
                {money.format(
                  data?.totalContractAmount ?? 0
                )}
              </strong>
            </div>


            <div>
              <span>
                Hakediş Gerçekleşme
              </span>

              <strong>
                {money.format(
                  data?.totalProgressPaymentAmount ?? 0
                )}
              </strong>
            </div>


            <div>
              <span>
                Fiyat Farkı
              </span>

              <strong>
                {money.format(
                  data?.totalPriceDifferenceAmount ?? 0
                )}
              </strong>
            </div>


            <div>
              <span>
                Net Ödeme
              </span>

              <strong>
                {money.format(
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
                    {money.format(
                      project.contractAmount
                    )}
                  </strong>
                </div>


                <div>
                  <span>
                    Hakediş
                  </span>

                  <strong>
                    {money.format(
                      project.progressPaymentAmount
                    )}
                  </strong>
                </div>


                <div>
                  <span>
                    Kalan
                  </span>

                  <strong>
                    {money.format(
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


          <div className="erp-detail-grid">

            <div>
              <span>
                Gelen
              </span>

              <strong>
                {money.format(
                  cashFlow?.totalIncome ?? 0
                )}
              </strong>
            </div>


            <div>
              <span>
                Giden
              </span>

              <strong>
                {money.format(
                  cashFlow?.totalExpense ?? 0
                )}
              </strong>
            </div>


            <div>
              <span>
                Net Nakit
              </span>

              <strong>
                {money.format(
                  cashFlow?.netCash ?? 0
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
                Tedarikçi Borç Durumu
              </h2>

              <p>
                Açık tedarikçi bakiye görünümü
              </p>
            </div>
          </div>


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
                    {money.format(
                      supplier.totalDebt
                    )}
                  </strong>
                </div>


                <div>
                  <span>
                    Ödenen
                  </span>

                  <strong>
                    {money.format(
                      supplier.totalPaid
                    )}
                  </strong>
                </div>


                <div>
                  <span>
                    Bakiye
                  </span>

                  <strong>
                    {money.format(
                      supplier.balance
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
