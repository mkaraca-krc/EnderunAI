"use client";

import { useEffect, useState } from "react";
import { useParams } from "next/navigation";

import ErpShell from "@/components/erp/erp-shell";
import { Button } from "@/components/ui";

import {
  deductionRuleService,
  DeductionType,
  DeductionCalculationBase,
  type DeductionRule,
} from "@/services/deduction-rule.service";

import {
  projectService,
} from "@/services/project.service";


const deductionLabels: Record<number,string> = {
  0:"KDV Tevkifatı",
  1:"Damga Vergisi",
  2:"Konaklama",
  3:"Yemek",
  4:"Avans Mahsubu",
  5:"Teminat Kesintisi",
  6:"SGK / Vergi Borcu",
  7:"Ceza",
  8:"Malzeme Kesintisi",
  9:"İşçilik Kesintisi",
  99:"Diğer",
};


const baseLabels: Record<number,string> = {
  0:"Hakediş Tutarı",
  1:"Hakediş + Fiyat Farkı",
  2:"Brüt Ödeme",
  3:"Manuel",
};


export default function ProjectDeductionsPage(){

  const params =
    useParams<{id:string}>();

  const projectId =
    params.id;


  const [project,setProject] =
    useState<any>(null);

  const [rules,setRules] =
    useState<DeductionRule[]>([]);


  const [loading,setLoading] =
    useState(true);

  const [saving,setSaving] =
    useState(false);


  const [form,setForm] = useState({

    deductionType:
      DeductionType.GuaranteeRetention,

    description:
      "Teminat Kesintisi",

    rate:3,

    calculationBase:
      DeductionCalculationBase.CurrentAmountWithPriceDifference,

    isAutomatic:true,

    notes:""

  });


  async function load(){

    setLoading(true);

    try{

      const projectData =
        await projectService.getById(
          projectId
        );

      setProject(projectData);


      const rows =
        await deductionRuleService.getAll({
          projectId
        });


      setRules(rows);

    }
    finally{

      setLoading(false);

    }

  }


  useEffect(()=>{

    if(projectId)
      void load();

  },[projectId]);



  function update(
    key:keyof typeof form,
    value:any
  ){

    setForm(current=>({
      ...current,
      [key]:value
    }));

  }



  async function save(){

    if(!project)
      return;


    setSaving(true);

    try{

      await deductionRuleService.create({

        companyId:
          project.companyId,

        projectId,

        deductionType:
          form.deductionType,

        description:
          form.description,

        rate:
          form.rate,

        calculationBase:
          form.calculationBase,

        isAutomatic:
          form.isAutomatic,

        notes:
          form.notes

      });


      await load();

    }
    finally{

      setSaving(false);

    }

  }



  return (

    <ErpShell
      design="redwood"
      title="Kesinti Politikası"
      description={
        project
          ? project.name
          : "Proje kesinti yönetimi"
      }
    >

      <div className="erp-page-toolbar">
        {/* Kesinti kuralları hakediş tarafında da değiştirilebiliyor;
            listeyi tazelemenin yolu yoktu. */}
        <Button variant="secondary" disabled={loading} onClick={() => void load()}>Yenile</Button>
      </div>

      <div className="erp-form-card">

        <h3>
          Yeni Kesinti Kuralı
        </h3>


        <div className="erp-form-grid">


          <label>

            <span>
              Kesinti Türü
            </span>

            <select
              value={form.deductionType}
              onChange={(e)=>
                update(
                  "deductionType",
                  Number(e.target.value)
                )
              }
            >

              {Object.entries(
                deductionLabels
              ).map(([key,value])=>(

                <option
                  key={key}
                  value={key}
                >
                  {value}
                </option>

              ))}

            </select>

          </label>



          <label>

            <span>
              Açıklama
            </span>

            <input
              className="erp-input"
              value={form.description}
              onChange={(e)=>
                update(
                  "description",
                  e.target.value
                )
              }
            />

          </label>



          <label>

            <span>
              Oran %
            </span>

            <input
              className="erp-input"
              type="number"
              value={form.rate}
              onChange={(e)=>
                update(
                  "rate",
                  Number(e.target.value)
                )
              }
            />

          </label>



          <label>

            <span>
              Matrah
            </span>

            <select
              value={form.calculationBase}
              onChange={(e)=>
                update(
                  "calculationBase",
                  Number(e.target.value)
                )
              }
            >

              {Object.entries(
                baseLabels
              ).map(([key,value])=>(

                <option
                  key={key}
                  value={key}
                >
                  {value}
                </option>

              ))}

            </select>

          </label>


        </div>



        <label className="erp-check">

          <input
            type="checkbox"
            checked={form.isAutomatic}
            onChange={(e)=>
              update(
                "isAutomatic",
                e.target.checked
              )
            }
          />

          Otomatik Uygula

        </label>



        <div className="erp-actions">

          <button
            disabled={saving}
            onClick={()=>void save()}
          >
            {saving
              ?"Kaydediliyor..."
              :"Kural Kaydet"}
          </button>

        </div>

      </div>




      <div
        className="erp-table-card"
        style={{marginTop:16}}
      >

        <div className="erp-toolbar">

          <strong>
            Tanımlı Kesinti Kuralları
          </strong>

        </div>


        <table className="erp-table">

          <thead>

            <tr>
              <th>Tür</th>
              <th>Açıklama</th>
              <th>Oran</th>
              <th>Matrah</th>
              <th>Otomatik</th>
            </tr>

          </thead>


          <tbody>

          {rules.map(rule=>(

            <tr key={rule.id}>

              <td>
                {deductionLabels[
                  rule.deductionType
                ]}
              </td>

              <td>
                {rule.description}
              </td>

              <td>
                %{rule.rate}
              </td>

              <td>
                {baseLabels[
                  rule.calculationBase
                ]}
              </td>

              <td>
                {rule.isAutomatic
                  ?"Evet"
                  :"Hayır"}
              </td>

            </tr>

          ))}


          {rules.length===0 && (

            <tr>
              <td colSpan={5}>
                Kesinti kuralı bulunmuyor.
              </td>
            </tr>

          )}

          </tbody>


        </table>


      </div>


    </ErpShell>

  );

}
