"use client";

import Link from "next/link";
import { FormEvent, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import ErpShell from "@/components/erp/erp-shell";
import { companyService } from "@/services/company.service";
import { engineeringPositionCreateService } from "@/services/engineering-position.service";

type CompanyItem = {
  id: string;
  code: string;
  name: string;
};

const disciplines = [
  [0, "Genel"],
  [1, "Elektrik"],
  [2, "Orta Gerilim"],
  [3, "Zayıf Akım"],
  [4, "Veri Merkezi"],
  [5, "Fiber"],
  [6, "Mekanik"],
  [7, "İnşaat"],
];

/**
 * Poz kaynağı BACKEND'DE İKİ DEĞERLİ: Official = 0, Enderun = 1.
 *
 * Burada bir dönem beş seçenek vardı (Enderun/ÇŞİDB/MSB/TEDAŞ/Özel):
 * 2/3/4 backend'de tanımsız olduğu için "Geçersiz poz kaynağı" 400
 * dönüyordu, "Enderun"(0) seçilince kod alanı gizlendiği hâlde backend
 * resmî pozda kodu zorunlu tuttuğu için o seçim de hep hata veriyordu.
 * Çalışan tek seçenek "ÇŞİDB"(1) idi ve aslında ŞİRKETE ÖZEL poz
 * yaratıyordu. Kurum bilgisi kaynak değil, ayrı bir alandır.
 */
const sources = [
  [0, "Resmî kurum kitabı (ÇŞB, TEDAŞ, MSB…)"],
  [1, "Şirkete özel poz"],
];

export default function NewEngineeringPositionPage() {
  const router = useRouter();

  const [companies, setCompanies] = useState<CompanyItem[]>([]);
  const [companyId, setCompanyId] = useState("");
  const [source, setSource] = useState("0");
  const [discipline, setDiscipline] = useState("1");
  const [code, setCode] = useState("");
  const [name, setName] = useState("");
  const [unit, setUnit] = useState("Adet");
  const [officialInstitution, setOfficialInstitution] = useState("");
  const [officialCode, setOfficialCode] = useState("");
  const [category, setCategory] = useState("");
  const [description, setDescription] = useState("");
  const [technicalSpecification, setTechnicalSpecification] = useState("");
  const [searchKeywords, setSearchKeywords] = useState("");
  const [laborHours, setLaborHours] = useState("0");
  const [helperHours, setHelperHours] = useState("0");
  const [machineHours, setMachineHours] = useState("0");

  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    async function loadCompanies() {
      try {
        const result = (await companyService.getAll()) as CompanyItem[];
        setCompanies(result);

        if (result.length > 0) {
          setCompanyId(result[0].id);
        }
      } catch (err) {
        setError(
          err instanceof Error ? err.message : "Şirketler yüklenemedi."
        );
      } finally {
        setLoading(false);
      }
    }

    loadCompanies();
  }, []);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setSaving(true);
    setError("");

    try {
      const result = await engineeringPositionCreateService.create({
        companyId,
        // Resmî pozda kod ZORUNLU (kitaptaki poz numarası); şirkete
        // özel pozda kod backend'de şirket serisinden üretilir.
        code: Number(source) === 1 ? null : code.trim() || null,
        name: name.trim(),
        unit: unit.trim(),
        source: Number(source),
        discipline: Number(discipline),
        officialInstitution: officialInstitution.trim() || null,
        officialCode: officialCode.trim() || null,
        category: category.trim() || null,
        description: description.trim() || null,
        technicalSpecification: technicalSpecification.trim() || null,
        searchKeywords: searchKeywords.trim() || null,
        defaultLaborHours: Number(laborHours || 0),
        defaultHelperHours: Number(helperHours || 0),
        defaultMachineHours: Number(machineHours || 0),
      });

      router.push(`/muhendislik/pozlar/${result.id}`);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Poz oluşturulamadı.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <ErpShell
      design="redwood"
      title="Yeni Mühendislik Pozu"
      description="Poz kütüphanesine yeni teknik kayıt oluşturun"
    >
      {error && <div className="erp-alert error">{error}</div>}

      <section className="enderun-dashboard-hero">
        <div>
          <span className="enderun-dashboard-kicker">
            MÜHENDİSLİK MERKEZİ
          </span>
          <h2>Yeni poz oluştur</h2>
          <p>
            Poz bilgilerini, disiplinini, adam/saat değerlerini ve teknik
            açıklamalarını tanımlayın.
          </p>
        </div>

        <div className="enderun-dashboard-hero-actions">
          <Link
            href="/muhendislik/pozlar"
            className="erp-secondary-button"
          >
            ← Poz Listesi
          </Link>
        </div>
      </section>

      <form className="erp-panel" onSubmit={submit}>
        <div className="erp-panel-header">
          <div>
            <h2>Temel Bilgiler</h2>
            <p>Pozun kimlik ve sınıflandırma bilgileri</p>
          </div>
        </div>

        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(3, minmax(0, 1fr))",
            gap: 16,
          }}
        >
          <label>
            <span>Şirket</span>
            <select
              className="erp-input"
              value={companyId}
              onChange={(event) => setCompanyId(event.target.value)}
              disabled={loading}
              required
            >
              <option value="">Şirket seçin</option>
              {companies.map((company) => (
                <option value={company.id} key={company.id}>
                  {company.code} · {company.name}
                </option>
              ))}
            </select>
          </label>

          <label>
            <span>Poz Kaynağı</span>
            <select
              className="erp-input"
              value={source}
              onChange={(event) => setSource(event.target.value)}
            >
              {sources.map(([value, label]) => (
                <option value={value} key={value}>
                  {label}
                </option>
              ))}
            </select>
          </label>

          <label>
            <span>Disiplin</span>
            <select
              className="erp-input"
              value={discipline}
              onChange={(event) => setDiscipline(event.target.value)}
            >
              {disciplines.map(([value, label]) => (
                <option value={value} key={value}>
                  {label}
                </option>
              ))}
            </select>
          </label>

          {Number(source) === 0 && (
            <label>
              <span>Poz Kodu</span>
              <input
                className="erp-input"
                value={code}
                onChange={(event) => setCode(event.target.value)}
                placeholder="35.140.2535"
                required
              />
            </label>
          )}

          <label>
            <span>Poz Adı</span>
            <input
              className="erp-input"
              value={name}
              onChange={(event) => setName(event.target.value)}
              required
            />
          </label>

          <label>
            <span>Birim</span>
            <input
              className="erp-input"
              value={unit}
              onChange={(event) => setUnit(event.target.value)}
              required
            />
          </label>

          <label>
            <span>Kategori</span>
            <input
              className="erp-input"
              value={category}
              onChange={(event) => setCategory(event.target.value)}
              placeholder="Kablo, pano, zayıf akım..."
            />
          </label>

          <label>
            <span>Resmî Kurum</span>
            <input
              className="erp-input"
              value={officialInstitution}
              onChange={(event) =>
                setOfficialInstitution(event.target.value)
              }
            />
          </label>

          <label>
            <span>Resmî Poz Kodu</span>
            <input
              className="erp-input"
              value={officialCode}
              onChange={(event) => setOfficialCode(event.target.value)}
            />
          </label>

          <label>
            <span>Usta Adam/Saat</span>
            <input
              type="number"
              step="0.01"
              min="0"
              className="erp-input"
              value={laborHours}
              onChange={(event) => setLaborHours(event.target.value)}
            />
          </label>

          <label>
            <span>Yardımcı Adam/Saat</span>
            <input
              type="number"
              step="0.01"
              min="0"
              className="erp-input"
              value={helperHours}
              onChange={(event) => setHelperHours(event.target.value)}
            />
          </label>

          <label>
            <span>Makine Saati</span>
            <input
              type="number"
              step="0.01"
              min="0"
              className="erp-input"
              value={machineHours}
              onChange={(event) => setMachineHours(event.target.value)}
            />
          </label>
        </div>

        <div style={{ marginTop: 20 }}>
          <label>
            <span>Arama Anahtar Kelimeleri</span>
            <input
              className="erp-input"
              value={searchKeywords}
              onChange={(event) => setSearchKeywords(event.target.value)}
              placeholder="kablo, n2xh, halojensiz, enerji..."
            />
          </label>
        </div>

        <div style={{ marginTop: 20 }}>
          <label>
            <span>Açıklama</span>
            <textarea
              className="erp-input"
              rows={4}
              value={description}
              onChange={(event) => setDescription(event.target.value)}
            />
          </label>
        </div>

        <div style={{ marginTop: 20 }}>
          <label>
            <span>Teknik Şartname</span>
            <textarea
              className="erp-input"
              rows={7}
              value={technicalSpecification}
              onChange={(event) =>
                setTechnicalSpecification(event.target.value)
              }
            />
          </label>
        </div>

        <div
          style={{
            display: "flex",
            justifyContent: "flex-end",
            gap: 12,
            marginTop: 24,
          }}
        >
          <Link
            href="/muhendislik/pozlar"
            className="erp-secondary-button"
          >
            İptal
          </Link>

          <button
            type="submit"
            className="erp-primary-button"
            disabled={saving || loading || !companyId}
          >
            {saving ? "Poz oluşturuluyor..." : "Pozu Oluştur"}
          </button>
        </div>
      </form>
    </ErpShell>
  );
}
