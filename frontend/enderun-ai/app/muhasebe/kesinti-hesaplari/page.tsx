"use client";

import { useCallback, useEffect, useMemo, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";
import {
  Badge,
  Button,
  Card,
  CardContent,
  CardHeader,
  EmptyState,
  Input,
  Select,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui";
import {
  accountingAccountService,
  type AccountingAccountListItem,
} from "@/services/accounting-account.service";
import { companyService, type CompanyListItem } from "@/services/company.service";
import { useModuleActions } from "@/lib/auth/module-actions";
import {
  hakedisDeductionAccountService,
  type DeductionAccountMapping,
} from "@/services/hakedis-deduction-account.service";

/**
 * Hakediş kesinti türlerinin muhasebe hesap eşlemesi.
 *
 * NEDEN GEREKLİ: eşleme yapılmadığında bütün kesintiler finans
 * ayarındaki GENEL kesinti hesabına düşüyor — barter, yemek,
 * konaklama ve İSG kesintileri tek hesapta toplanıyor ve muhasebe
 * bunları ayırt edemiyor. Uç aylardır hazırdı ama ekranı olmadığı
 * için eşleme hiç yapılamıyordu.
 *
 * EŞLEME ZORUNLU DEĞİL ve ekran bunu açıkça söylüyor: boş satır bir
 * eksiklik değil, "genel hesaba düşsün" demektir. Aksi hâlde
 * kullanıcı her tür için gereksiz hesap açar.
 *
 * KAYIT TOPLUCA: uç PUT ile son durumu alıyor, kısmi güncelleme yok.
 * O yüzden ekran taslağı yerel tutuyor ve tek "Kaydet" ile
 * gönderiyor; satır satır kaydetmek, gönderilmeyen satırların
 * silinmesine yol açardı.
 */

function messageOf(error: unknown) {
  return error instanceof Error ? error.message : "İşlem tamamlanamadı.";
}

/** Yalnızca fiş kesilebilen hesaplar eşlenebilir. */
function isSelectable(account: AccountingAccountListItem) {
  return account.isActive && account.isPostingAllowed;
}

export default function DeductionAccountsPage() {
  /*
   * PUT hakedis-deduction-accounts -> accounting.manage
   * Kesinti hesabı eşlemesi defteri etkiliyor.
   */
  const actions = useModuleActions("accounting");
  const canManage = actions.can("manage");

  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [companyId, setCompanyId] = useState("");

  const [accounts, setAccounts] = useState<AccountingAccountListItem[]>([]);
  const [rows, setRows] = useState<DeductionAccountMapping[]>([]);

  /** Yerel taslak: tür → seçilen hesap ve not. */
  const [draft, setDraft] = useState<
    Record<number, { accountId: string; notes: string }>
  >({});

  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  useEffect(() => {
    void (async () => {
      try {
        const list = await companyService.getAll();
        setCompanies(list);

        const first = list.find((x) => x.isActive !== false) ?? list[0];
        if (first) setCompanyId((current) => current || first.id);
      } catch (err) {
        setError(messageOf(err));
      }
    })();
  }, []);

  const load = useCallback(async () => {
    if (!companyId) return;

    setLoading(true);
    setError("");
    setNotice("");

    try {
      const [mappings, accountList] = await Promise.all([
        hakedisDeductionAccountService.get(companyId),
        accountingAccountService.getAll({ companyId, isActive: true }),
      ]);

      setRows(mappings);
      setAccounts(accountList.filter(isSelectable));

      setDraft(
        Object.fromEntries(
          mappings.map((row) => [
            row.deductionType,
            {
              accountId: row.accountingAccountId ?? "",
              notes: row.notes ?? "",
            },
          ])
        )
      );
    } catch (err) {
      setError(messageOf(err));
      setRows([]);
    } finally {
      setLoading(false);
    }
  }, [companyId]);

  useEffect(() => {
    void (async () => {
      await load();
    })();
  }, [load]);

  const accountOptions = useMemo(
    () => [
      { label: "— Genel kesinti hesabına düşsün —", value: "" },
      ...accounts.map((account) => ({
        label: `${account.code} · ${account.name}`,
        value: account.id,
      })),
    ],
    [accounts]
  );

  /** Kaydedilmemiş değişiklik var mı — kullanıcı sayfadan çıkmadan bilmeli. */
  const dirty = useMemo(
    () =>
      rows.some((row) => {
        const current = draft[row.deductionType];
        if (!current) return false;

        return (
          current.accountId !== (row.accountingAccountId ?? "") ||
          current.notes.trim() !== (row.notes ?? "").trim()
        );
      }),
    [rows, draft]
  );

  const mappedCount = useMemo(
    () => Object.values(draft).filter((item) => item.accountId).length,
    [draft]
  );

  function update(type: number, patch: { accountId?: string; notes?: string }) {
    setDraft((current) => ({
      ...current,
      [type]: {
        accountId: patch.accountId ?? current[type]?.accountId ?? "",
        notes: patch.notes ?? current[type]?.notes ?? "",
      },
    }));
    setNotice("");
  }

  async function save() {
    setSaving(true);
    setError("");
    setNotice("");

    try {
      await hakedisDeductionAccountService.replace(
        companyId,
        rows.map((row) => {
          const current = draft[row.deductionType];

          return {
            deductionType: row.deductionType,
            accountingAccountId: current?.accountId ? current.accountId : null,
            notes: current?.notes.trim() ? current.notes.trim() : null,
          };
        })
      );

      setNotice("Kesinti hesapları kaydedildi.");
      await load();
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setSaving(false);
    }
  }

  return (
    <ErpShell
      design="redwood"
      title="Hakediş Kesinti Hesapları"
      description="Kesinti türlerini muhasebe hesaplarına eşler; eşlenmeyen tür genel kesinti hesabına düşer."
    >
      <div className="space-y-6">
        <Card>
          <CardContent className="flex flex-wrap items-end gap-4">
            <div className="min-w-56">
              <Select
                label="Şirket"
                value={companyId}
                onChange={(event) => setCompanyId(event.target.value)}
                options={companies.map((company) => ({
                  label: company.name,
                  value: company.id,
                }))}
              />
            </div>

            <Button
              variant="secondary"
              onClick={() => void load()}
              disabled={loading || !companyId}
            >
              {loading ? "Yükleniyor..." : "Yenile"}
            </Button>

            {canManage && actions.can("manage") && (
              <Button onClick={() => void save()} disabled={saving || !dirty}>
                {saving ? "Kaydediliyor..." : "Kaydet"}
              </Button>
            )}
          </CardContent>
        </Card>

        {error && (
          <div className="rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
            {error}
          </div>
        )}

        {notice && (
          <div className="rounded-lg border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-800">
            {notice}
          </div>
        )}

        {!canManage && (
          <div className="rounded-lg border border-slate-200 bg-slate-50 px-4 py-3 text-sm text-slate-600">
            Eşlemeyi görüntülüyorsunuz. Değiştirmek için muhasebe yönetim
            yetkisi gerekir.
          </div>
        )}

        {dirty && canManage && (
          <div className="rounded-lg border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
            Kaydedilmemiş değişiklik var.
          </div>
        )}

        <div className="rounded-lg border border-slate-200 bg-slate-50 px-4 py-3 text-sm text-slate-600">
          Eşleme zorunlu değil. Hesap seçilmeyen tür, finans ayarındaki{" "}
          <strong>genel kesinti hesabına</strong> düşer — boş satır bir
          eksiklik değildir. Listede yalnızca fiş kesilebilen aktif hesaplar
          görünür.
        </div>

        {rows.length === 0 && !loading ? (
          <EmptyState
            title="Kesinti türü yok"
            description="Şirket seçin ya da yenileyin."
          />
        ) : (
          <Card>
            <CardHeader>
              <div className="flex items-center gap-3">
                <h2 className="text-sm font-semibold text-slate-900">
                  Kesinti türü → muhasebe hesabı
                </h2>
                <Badge variant={mappedCount > 0 ? "info" : "default"}>
                  {mappedCount} / {rows.length} eşlendi
                </Badge>
              </div>
            </CardHeader>

            <CardContent className="p-0 overflow-x-auto">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Kesinti türü</TableHead>
                    <TableHead>Muhasebe hesabı</TableHead>
                    <TableHead>Not</TableHead>
                    <TableHead>Durum</TableHead>
                  </TableRow>
                </TableHeader>

                <TableBody>
                  {rows.map((row) => {
                    const current = draft[row.deductionType];
                    const selected = current?.accountId ?? "";

                    return (
                      <TableRow key={row.deductionType}>
                        <TableCell className="font-medium">{row.name}</TableCell>

                        <TableCell className="min-w-72">
                          <Select
                            value={selected}
                            disabled={!canManage}
                            onChange={(event) =>
                              update(row.deductionType, {
                                accountId: event.target.value,
                              })
                            }
                            options={accountOptions}
                          />
                        </TableCell>

                        <TableCell className="min-w-56">
                          <Input
                            value={current?.notes ?? ""}
                            disabled={!canManage}
                            placeholder="İsteğe bağlı"
                            onChange={(event) =>
                              update(row.deductionType, {
                                notes: event.target.value,
                              })
                            }
                          />
                        </TableCell>

                        <TableCell>
                          {selected ? (
                            <Badge variant="success">Eşlendi</Badge>
                          ) : (
                            <Badge>Genel hesap</Badge>
                          )}
                        </TableCell>
                      </TableRow>
                    );
                  })}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
        )}
      </div>
    </ErpShell>
  );
}
