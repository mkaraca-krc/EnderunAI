"use client";

import Link from "next/link";
import { useCallback, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { useModuleActions } from "@/lib/auth/module-actions";
import {
  Badge,
  Button,
  Card,
  CardContent,
  CardHeader,
  EmptyState,
  Input,
  Modal,
  Select,
} from "@/components/ui";
import { companyService, type CompanyListItem } from "@/services/company.service";
import {
  VEHICLE_OWNERSHIP_LABELS,
  VEHICLE_TYPE_LABELS,
  vehicleService,
  type SaveVehiclePayload,
  type VehicleListItem,
} from "@/services/vehicle.service";

function emptyForm(companyId: string): SaveVehiclePayload {
  return {
    companyId,
    plateNumber: "",
    type: 0,
    ownership: 0,
    brand: "",
    model: "",
    modelYear: null,
    rentAmount: null,
    rentPeriod: 0,
    rentDueDay: null,
    inspectionDueDate: null,
    insuranceRenewalDate: null,
    cascoRenewalDate: null,
    motorTaxDueDate: null,
    nextMaintenanceDate: null,
  };
}

/**
 * ARAÇ LİSTESİ.
 *
 * Araç kartları ELLE açılır (toplu aktarım yok): araç sayısı elle
 * yönetilebilecek kadar azdır ve plaka hatası pahalıdır.
 *
 * "Yaklaşan yenileme" rozeti SUNUCUDAN geliyor — eşik bildirim
 * motorunun kendi sabitinden okunuyor. Burada hesaplansaydı ikinci bir
 * eşik doğar ve liste "yaklaşıyor" derken bildirim merkezi susardı.
 */
export default function FleetPage() {
  /**
   * Düğme -> uç -> izin (VehiclesController):
   *   POST vehicles -> vehicle.manage
   *
   * BU MODÜLDE YETKİ AYRIMI YOK: yalnız vehicle.view ve vehicle.manage
   * var. Oluşturma/düzenleme/silme ayrımı arayüzde uydurulmadı.
   */
  const actions = useModuleActions("vehicle");

  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [companyId, setCompanyId] = useState("");
  const [vehicles, setVehicles] = useState<VehicleListItem[]>([]);
  const [search, setSearch] = useState("");
  const [ownership, setOwnership] = useState("");

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  const [formOpen, setFormOpen] = useState(false);
  const [form, setForm] = useState<SaveVehiclePayload>(emptyForm(""));
  const [saving, setSaving] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError("");

    try {
      setVehicles(
        await vehicleService.getAll({
          companyId: companyId || undefined,
          ownership: ownership === "" ? undefined : Number(ownership),
          search: search.trim() || undefined,
        })
      );
    } catch (err) {
      setError(err instanceof Error ? err.message : "Araçlar yüklenemedi.");
    } finally {
      setLoading(false);
    }
  }, [companyId, ownership, search]);

  useEffect(() => {
    void (async () => {
      try {
        const data = await companyService.getAll();
        setCompanies(data);

        if (data.length === 1) {
          setCompanyId(data[0].id);
          setForm(emptyForm(data[0].id));
        }
      } catch (err) {
        setError(err instanceof Error ? err.message : "Şirketler yüklenemedi.");
      }
    })();
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  async function submit() {
    setSaving(true);
    setError("");
    setNotice("");

    try {
      const created = await vehicleService.create({
        ...form,
        companyId: form.companyId || companyId,
        modelYear: form.modelYear ? Number(form.modelYear) : null,
        rentAmount:
          form.ownership === 1 && form.rentAmount
            ? Number(form.rentAmount)
            : null,
        rentPeriod: form.ownership === 1 ? Number(form.rentPeriod ?? 0) : null,
        rentDueDay:
          form.ownership === 1 && form.rentDueDay
            ? Number(form.rentDueDay)
            : null,
      });

      setNotice(`${created.plateNumber} plakalı araç eklendi.`);
      setFormOpen(false);
      setForm(emptyForm(companyId));
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Araç eklenemedi.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <ErpShell
      design="redwood"
      title="Filo"
      description="Araç kartları, projelere atama ve yenileme takibi"
    >
      {error && (
        <div className="mb-5 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
          {error}
        </div>
      )}

      {notice && (
        <div className="mb-5 rounded-lg border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-700">
          {notice}
        </div>
      )}

      <div className="mb-6 flex flex-col gap-3 xl:flex-row xl:items-center xl:justify-between">
        <form
          className="flex flex-1 flex-col gap-3 md:flex-row"
          onSubmit={(event) => {
            event.preventDefault();
            void load();
          }}
        >
          <Input
            value={search}
            placeholder="Plaka, marka veya model"
            onChange={(event) => setSearch(event.target.value)}
          />

          <Select
            value={companyId}
            onChange={(event) => setCompanyId(event.target.value)}
            placeholder="Tüm şirketler"
            options={companies.map((x) => ({
              label: `${x.code} · ${x.name}`,
              value: x.id,
            }))}
          />

          <Select
            value={ownership}
            onChange={(event) => setOwnership(event.target.value)}
            placeholder="Öz mal + kiralık"
            options={Object.entries(VEHICLE_OWNERSHIP_LABELS).map(
              ([value, label]) => ({ value, label })
            )}
          />

          <Button type="submit" variant="secondary">
            Ara
          </Button>
        </form>

        {actions.can("manage") && (
          <Button
            onClick={() => {
              setForm(emptyForm(companyId || companies[0]?.id || ""));
              setFormOpen(true);
            }}
          >
            + Yeni Araç
          </Button>
        )}
      </div>

      <Card>
        <CardHeader>
          <h2 className="text-lg font-semibold text-slate-900">Araçlar</h2>
        </CardHeader>

        <CardContent>
          {loading ? (
            <p className="py-8 text-center text-slate-500">Yükleniyor…</p>
          ) : vehicles.length === 0 ? (
            <EmptyState
              title="Araç tanımlı değil"
              description="Filoya araç eklemek için sağ üstteki “Yeni Araç” düğmesini kullanın."
            />
          ) : (
            <div className="overflow-x-auto">
              <table className="min-w-full text-sm">
                <thead>
                  <tr className="bg-slate-100 text-slate-600">
                    <th className="px-3 py-2 text-left">Plaka</th>
                    <th className="px-3 py-2 text-left">Tip</th>
                    <th className="px-3 py-2 text-left">Sahiplik</th>
                    <th className="px-3 py-2 text-left">Güncel yer</th>
                    <th className="px-3 py-2 text-left">Yenileme</th>
                  </tr>
                </thead>

                <tbody>
                  {vehicles.map((vehicle) => (
                    <tr key={vehicle.id} className="border-t border-slate-200">
                      <td className="px-3 py-2">
                        <Link
                          href={`/filo/${vehicle.id}`}
                          className="font-medium text-brand-700 hover:underline"
                        >
                          {vehicle.plateNumber}
                        </Link>
                        {(vehicle.brand || vehicle.model) && (
                          <span className="block text-xs text-slate-500">
                            {[vehicle.brand, vehicle.model]
                              .filter(Boolean)
                              .join(" ")}
                          </span>
                        )}
                      </td>

                      <td className="px-3 py-2">
                        {VEHICLE_TYPE_LABELS[vehicle.type] ?? "—"}
                      </td>

                      <td className="px-3 py-2">
                        <Badge
                          variant={vehicle.ownership === 1 ? "info" : "default"}
                        >
                          {VEHICLE_OWNERSHIP_LABELS[vehicle.ownership]}
                        </Badge>
                      </td>

                      <td className="px-3 py-2">
                        {vehicle.currentAssignment ? (
                          vehicle.currentAssignment.projectId ? (
                            <>
                              <span className="block text-slate-900">
                                {vehicle.currentAssignment.projectCode}
                              </span>
                              <span className="block text-xs text-slate-500">
                                {vehicle.currentAssignment.driverName ??
                                  "sürücü atanmadı"}
                              </span>
                            </>
                          ) : (
                            <span className="text-slate-600">Merkez havuzu</span>
                          )
                        ) : (
                          <span className="text-slate-400">Atanmadı</span>
                        )}
                      </td>

                      <td className="px-3 py-2">
                        {vehicle.dueSoonCount > 0 ? (
                          <Badge variant="warning">
                            {vehicle.dueSoonCount} yaklaşan
                          </Badge>
                        ) : (
                          <span className="text-xs text-slate-400">—</span>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </CardContent>
      </Card>

      <Modal
        open={formOpen}
        onClose={() => setFormOpen(false)}
        title="Yeni Araç"
        size="lg"
      >
        <div className="grid gap-4 md:grid-cols-2">
          <Select
            label="Şirket"
            value={form.companyId}
            onChange={(event) =>
              setForm((x) => ({ ...x, companyId: event.target.value }))
            }
            placeholder="Şirket seçin"
            options={companies.map((x) => ({
              label: `${x.code} · ${x.name}`,
              value: x.id,
            }))}
          />

          <Input
            label="Plaka"
            value={form.plateNumber}
            placeholder="06 ABC 123"
            onChange={(event) =>
              setForm((x) => ({ ...x, plateNumber: event.target.value }))
            }
          />

          <Select
            label="Tip"
            value={String(form.type)}
            onChange={(event) =>
              setForm((x) => ({ ...x, type: Number(event.target.value) }))
            }
            options={Object.entries(VEHICLE_TYPE_LABELS).map(
              ([value, label]) => ({ value, label })
            )}
          />

          <Select
            label="Sahiplik"
            value={String(form.ownership)}
            onChange={(event) =>
              setForm((x) => ({ ...x, ownership: Number(event.target.value) }))
            }
            options={Object.entries(VEHICLE_OWNERSHIP_LABELS).map(
              ([value, label]) => ({ value, label })
            )}
          />

          <Input
            label="Marka"
            value={form.brand ?? ""}
            onChange={(event) =>
              setForm((x) => ({ ...x, brand: event.target.value }))
            }
          />

          <Input
            label="Model"
            value={form.model ?? ""}
            onChange={(event) =>
              setForm((x) => ({ ...x, model: event.target.value }))
            }
          />

          {form.ownership === 1 && (
            <>
              <Input
                label="Kira bedeli"
                type="number"
                value={form.rentAmount ?? ""}
                onChange={(event) =>
                  setForm((x) => ({
                    ...x,
                    rentAmount: Number(event.target.value),
                  }))
                }
              />

              <Input
                label="Kira vadesi (ayın günü)"
                type="number"
                min={1}
                max={31}
                value={form.rentDueDay ?? ""}
                onChange={(event) =>
                  setForm((x) => ({
                    ...x,
                    rentDueDay: Number(event.target.value),
                  }))
                }
              />
            </>
          )}

          {[
            ["Muayene", "inspectionDueDate"],
            ["Sigorta yenileme", "insuranceRenewalDate"],
            ["Kasko yenileme", "cascoRenewalDate"],
            ["MTV son ödeme", "motorTaxDueDate"],
            ["Periyodik bakım", "nextMaintenanceDate"],
          ].map(([label, field]) => (
            <Input
              key={field}
              label={label}
              type="date"
              value={(form[field as keyof SaveVehiclePayload] as string) ?? ""}
              onChange={(event) =>
                setForm((x) => ({ ...x, [field]: event.target.value || null }))
              }
            />
          ))}
        </div>

        <p className="mt-4 text-xs text-slate-500">
          Kiralık araçta kira bedeli zorunludur: bedeli olmayan kira nakit
          akışa düşmez ve araç bedava görünür.
        </p>

        <div className="mt-5 flex justify-end gap-3">
          <Button variant="secondary" onClick={() => setFormOpen(false)}>
            Vazgeç
          </Button>
          {actions.can("manage") && (
            <Button onClick={submit} loading={saving}>
              Kaydet
            </Button>
          )}
        </div>
      </Modal>
    </ErpShell>
  );
}
