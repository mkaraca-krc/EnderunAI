"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useCallback, useEffect, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import { money } from "@/lib/format/turkish";
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
import { projectService, type ProjectListItem } from "@/services/project.service";
import {
  VEHICLE_FUEL_LABELS,
  VEHICLE_OWNERSHIP_LABELS,
  VEHICLE_RENT_PERIOD_LABELS,
  VEHICLE_TYPE_LABELS,
  vehicleService,
  type VehicleDetail,
  type VehicleExpenseList,
} from "@/services/vehicle.service";

function formatDate(value?: string | null) {
  return value ? new Date(value).toLocaleDateString("tr-TR") : "—";
}

/**
 * ARAÇ KARTI: bilgiler, atama geçmişi, masraf dökümü ve yaklaşan
 * yenilemeler.
 *
 * MASRAF DÖKÜMÜ FİLTRELENMİŞ GÖRÜNÜMDÜR — ayrı bir toplama kaynağı
 * değil. Aynı kayıtlar gider merkezi raporunda da bir kez sayılıyor;
 * burada ikinci bir defter tutulsaydı aynı masraf iki kez görünürdü.
 *
 * Elden ödenen kalemler ek ödeme yetkisi olmayan kullanıcıya HİÇ
 * gelmez; toplam yalnız görünenlerden oluşur ve kaç kalemin gizlendiği
 * ayrıca yazılır (tutarı değil, sayısı).
 */
export default function VehicleDetailPage() {
  const params = useParams<{ id: string }>();
  const vehicleId = params.id;

  const [vehicle, setVehicle] = useState<VehicleDetail | null>(null);
  const [expenses, setExpenses] = useState<VehicleExpenseList | null>(null);
  const [projects, setProjects] = useState<ProjectListItem[]>([]);

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  const [assignOpen, setAssignOpen] = useState(false);
  const [assignProjectId, setAssignProjectId] = useState("");
  const [assignStart, setAssignStart] = useState(
    new Date().toISOString().slice(0, 10)
  );
  const [assignNotes, setAssignNotes] = useState("");
  const [saving, setSaving] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError("");

    try {
      const [detail, expenseList] = await Promise.all([
        vehicleService.getById(vehicleId),
        vehicleService.getExpenses(vehicleId),
      ]);

      setVehicle(detail);
      setExpenses(expenseList);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Araç yüklenemedi.");
    } finally {
      setLoading(false);
    }
  }, [vehicleId]);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    void (async () => {
      try {
        setProjects(await projectService.getAll());
      } catch {
        setProjects([]);
      }
    })();
  }, []);

  async function assign() {
    setSaving(true);
    setError("");
    setNotice("");

    try {
      await vehicleService.assign(vehicleId, {
        // Proje seçilmezse araç MERKEZ HAVUZUNA alınır — ayrı bir
        // "merkez" seçeneği yok, boş proje merkez demek.
        projectId: assignProjectId || null,
        startDate: assignStart,
        notes: assignNotes.trim() || null,
      });

      setNotice(
        assignProjectId
          ? "Araç projeye atandı; önceki atama kapatıldı."
          : "Araç merkez havuzuna alındı."
      );

      setAssignOpen(false);
      setAssignNotes("");
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Atama yapılamadı.");
    } finally {
      setSaving(false);
    }
  }

  const renewals: [string, string | null | undefined][] = vehicle
    ? [
        ["Muayene", vehicle.inspectionDueDate],
        ["Sigorta yenileme", vehicle.insuranceRenewalDate],
        ["Kasko yenileme", vehicle.cascoRenewalDate],
        ["MTV son ödeme", vehicle.motorTaxDueDate],
        ["Periyodik bakım", vehicle.nextMaintenanceDate],
      ]
    : [];

  return (
    <ErpShell
      design="redwood"
      title={vehicle ? vehicle.plateNumber : "Araç"}
      description="Araç bilgileri, atama geçmişi ve masraf dökümü"
    >
      <div className="mb-5 flex items-center justify-between gap-3">
        <Link href="/filo" className="text-sm text-brand-700 hover:underline">
          ← Filo
        </Link>

        <div className="flex items-center gap-3">
          {/* Masraf kalemleri ve atamalar başka ekranlardan işleniyor. */}
          <Button
            variant="secondary"
            disabled={loading}
            onClick={() => void load()}
          >
            Yenile
          </Button>

          <Button onClick={() => setAssignOpen(true)} disabled={!vehicle}>
            Atama Yap
          </Button>
        </div>
      </div>

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

      {loading ? (
        <p className="py-8 text-center text-slate-500">Yükleniyor…</p>
      ) : !vehicle ? (
        <EmptyState
          title="Araç bulunamadı"
          description="Kayıt silinmiş ya da başka bir şirkete ait olabilir."
        />
      ) : (
        <div className="grid gap-6 xl:grid-cols-3">
          <Card className="xl:col-span-2">
            <CardHeader>
              <h2 className="text-lg font-semibold text-slate-900">Bilgiler</h2>
            </CardHeader>

            <CardContent>
              <dl className="grid gap-4 md:grid-cols-2">
                {[
                  ["Tip", VEHICLE_TYPE_LABELS[vehicle.type]],
                  ["Sahiplik", VEHICLE_OWNERSHIP_LABELS[vehicle.ownership]],
                  ["Marka / model", [vehicle.brand, vehicle.model].filter(Boolean).join(" ") || "—"],
                  ["Model yılı", vehicle.modelYear ?? "—"],
                  ["Şase no", vehicle.chassisNumber || "—"],
                  [
                    "Yakıt",
                    vehicle.fuelType !== null && vehicle.fuelType !== undefined
                      ? VEHICLE_FUEL_LABELS[vehicle.fuelType]
                      : "—",
                  ],
                ].map(([label, value]) => (
                  <div key={String(label)}>
                    <dt className="text-xs text-slate-500">{label}</dt>
                    <dd className="text-sm text-slate-900">{value}</dd>
                  </div>
                ))}

                {vehicle.ownership === 1 && (
                  <>
                    <div>
                      <dt className="text-xs text-slate-500">Kiralayan</dt>
                      <dd className="text-sm text-slate-900">
                        {vehicle.lessorTitle || "—"}
                      </dd>
                    </div>

                    <div>
                      <dt className="text-xs text-slate-500">Kira</dt>
                      <dd className="text-sm text-slate-900">
                        {vehicle.rentAmount ? money(vehicle.rentAmount) : "—"}
                        {vehicle.rentPeriod !== null &&
                        vehicle.rentPeriod !== undefined
                          ? ` · ${VEHICLE_RENT_PERIOD_LABELS[vehicle.rentPeriod]}`
                          : ""}
                        {vehicle.rentDueDay ? ` · ayın ${vehicle.rentDueDay}'i` : ""}
                      </dd>
                    </div>
                  </>
                )}

                {vehicle.ownership === 0 && vehicle.purchaseCost != null && (
                  <div>
                    <dt className="text-xs text-slate-500">Alış</dt>
                    <dd className="text-sm text-slate-900">
                      {money(vehicle.purchaseCost)} ·{" "}
                      {formatDate(vehicle.purchaseDate)}
                      <span className="mt-1 block text-xs text-slate-500">
                        Amortisman hesaplanmaz; yalnız cari masraflar yansır.
                      </span>
                    </dd>
                  </div>
                )}
              </dl>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <h2 className="text-lg font-semibold text-slate-900">
                Yaklaşan yenilemeler
              </h2>
            </CardHeader>

            <CardContent>
              <ul className="space-y-3 text-sm">
                {renewals.map(([label, value]) => (
                  <li key={label} className="flex justify-between gap-3">
                    <span className="text-slate-600">{label}</span>
                    <span className="text-slate-900">{formatDate(value)}</span>
                  </li>
                ))}
              </ul>

              <p className="mt-4 text-xs text-slate-500">
                Tarihler yaklaştığında bildirim merkezinde hatırlatma açılır;
                tarih ileri alınınca hatırlatma kendiliğinden kapanır.
              </p>
            </CardContent>
          </Card>

          <Card className="xl:col-span-2">
            <CardHeader>
              <div className="flex items-baseline justify-between gap-3">
                <h2 className="text-lg font-semibold text-slate-900">
                  Masraf dökümü
                </h2>
                {expenses && (
                  <span className="text-sm font-semibold text-slate-900">
                    {money(expenses.total)}
                  </span>
                )}
              </div>
            </CardHeader>

            <CardContent>
              {!expenses || expenses.items.length === 0 ? (
                <EmptyState
                  title="Masraf kaydı yok"
                  description="Araç masrafı gider kaydından girilir ve orada araç işaretlenir."
                />
              ) : (
                <div className="overflow-x-auto">
                  <table className="min-w-full text-sm">
                    <thead>
                      <tr className="bg-slate-100 text-slate-600">
                        <th className="px-3 py-2 text-left">Tarih</th>
                        <th className="px-3 py-2 text-left">Açıklama</th>
                        <th className="px-3 py-2 text-left">Kategori</th>
                        <th className="px-3 py-2 text-left">Merkez</th>
                        <th className="px-3 py-2 text-right">Tutar</th>
                      </tr>
                    </thead>

                    <tbody>
                      {expenses.items.map((item) => (
                        <tr key={item.id} className="border-t border-slate-200">
                          <td className="px-3 py-2 text-slate-600">
                            {formatDate(item.expenseDate)}
                          </td>
                          <td className="px-3 py-2 text-slate-900">
                            {item.description}
                          </td>
                          <td className="px-3 py-2 text-slate-600">
                            {item.categoryName}
                          </td>
                          <td className="px-3 py-2 text-slate-600">
                            {item.projectCode ?? item.branchName ?? "—"}
                          </td>
                          <td className="px-3 py-2 text-right tabular-nums">
                            {money(item.amount)}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}

              {expenses && expenses.hiddenCount > 0 && (
                <p className="mt-3 text-xs text-amber-700">
                  {expenses.hiddenCount} kalem elden/faturasız olduğu için
                  gizlendi; toplam yalnız görünen kalemlerden oluşuyor.
                </p>
              )}
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <h2 className="text-lg font-semibold text-slate-900">
                Atama geçmişi
              </h2>
            </CardHeader>

            <CardContent>
              {vehicle.assignments.length === 0 ? (
                <EmptyState
                  title="Atama yok"
                  description="Araç henüz bir projeye ya da merkeze atanmadı."
                />
              ) : (
                <ul className="space-y-3 text-sm">
                  {vehicle.assignments.map((assignment) => (
                    <li
                      key={assignment.id}
                      className="border-b border-slate-100 pb-3 last:border-0"
                    >
                      <div className="flex items-center justify-between gap-2">
                        <span className="font-medium text-slate-900">
                          {assignment.projectCode ?? "Merkez havuzu"}
                        </span>

                        {assignment.endDate === null && (
                          <Badge variant="success">Açık</Badge>
                        )}
                      </div>

                      <span className="block text-xs text-slate-500">
                        {formatDate(assignment.startDate)} —{" "}
                        {assignment.endDate
                          ? formatDate(assignment.endDate)
                          : "devam ediyor"}
                      </span>

                      {assignment.driverName && (
                        <span className="block text-xs text-slate-500">
                          Sürücü: {assignment.driverName}
                        </span>
                      )}
                    </li>
                  ))}
                </ul>
              )}
            </CardContent>
          </Card>
        </div>
      )}

      <Modal
        open={assignOpen}
        onClose={() => setAssignOpen(false)}
        title="Araç Ataması"
      >
        <div className="space-y-4">
          <Select
            label="Proje"
            value={assignProjectId}
            onChange={(event) => setAssignProjectId(event.target.value)}
            placeholder="Merkez havuzu (proje seçilmedi)"
            options={projects.map((x) => ({
              label: `${x.code} · ${x.name}`,
              value: x.id,
            }))}
          />

          <Input
            label="Başlangıç"
            type="date"
            value={assignStart}
            onChange={(event) => setAssignStart(event.target.value)}
          />

          <Input
            label="Not"
            value={assignNotes}
            onChange={(event) => setAssignNotes(event.target.value)}
          />

          <p className="text-xs text-slate-500">
            Açık atama varsa bu tarihte kapatılır; geçmiş atamalar silinmez —
            masraf yansıtması “o tarihte araç neredeydi” diye soruyor.
          </p>

          <div className="flex justify-end gap-3">
            <Button variant="secondary" onClick={() => setAssignOpen(false)}>
              Vazgeç
            </Button>
            <Button onClick={assign} loading={saving}>
              Ata
            </Button>
          </div>
        </div>
      </Modal>
    </ErpShell>
  );
}
