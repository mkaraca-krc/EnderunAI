"use client";

import { useCallback, useEffect, useState } from "react";

import {
  Badge,
  Button,
  Card,
  CardContent,
  CardHeader,
  EmptyState,
  Input,
  Modal,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui";
import { usePermissions } from "@/lib/use-permissions";
import {
  manufacturerPriceListService,
  type CreateManufacturerPriceListItem,
  type ManufacturerPriceList,
} from "@/services/manufacturer-price-list.service";

/**
 * Üretici fiyat listeleri: listeleme ve yeni liste açma.
 *
 * Uçlar hazırdı ama ekran yalnızca ürün ARAMASI yapıyordu — hangi
 * listelerin var olduğu, hangisinin süresinin dolduğu ve yeni liste
 * açma hiçbir yerden görünmüyordu.
 *
 * "GEÇERLİ LİSTE" TANIMI UÇTA: aktif olmak + son geçerlilik tarihi
 * bugünden küçük olmamak. `activeOnly` süzgeci uca bırakıldı;
 * istemcide tarih karşılaştırması yapılsaydı aynı kural iki yerde
 * dururdu ve zaman dilimi farkıyla ayrışırdı.
 *
 * Yetki: listeleme `engineering.view`, oluşturma `engineering.manage`.
 */

function messageOf(error: unknown) {
  return error instanceof Error ? error.message : "İşlem tamamlanamadı.";
}

function formatDate(value: string | null | undefined) {
  if (!value) return "—";

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "—";

  return date.toLocaleDateString("tr-TR");
}

/** Boş ürün satırı — uç en az bir ürün istiyor. */
function emptyItem(): CreateManufacturerPriceListItem & { key: string } {
  return {
    key: crypto.randomUUID(),
    productCode: "",
    productDescription: "",
    unit: "",
    listPrice: 0,
    category: "",
    brand: "",
    model: "",
  };
}

export default function PriceListManager({
  companyId,
}: {
  companyId: string;
}) {
  const { has } = usePermissions();
  const canManage = has("engineering.manage");

  const [lists, setLists] = useState<ManufacturerPriceList[]>([]);
  const [includeExpired, setIncludeExpired] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  const [formOpen, setFormOpen] = useState(false);
  const [saving, setSaving] = useState(false);
  const [formError, setFormError] = useState("");

  const [header, setHeader] = useState({
    manufacturerName: "",
    listName: "",
    listDate: new Date().toISOString().slice(0, 10),
    validUntil: "",
    currency: "TRY",
  });

  const [items, setItems] = useState([emptyItem()]);

  const load = useCallback(async () => {
    if (!companyId) {
      setLists([]);
      return;
    }

    setLoading(true);
    setError("");

    try {
      setLists(
        await manufacturerPriceListService.getAll({
          companyId,
          activeOnly: !includeExpired,
        })
      );
    } catch (err) {
      setError(messageOf(err));
      setLists([]);
    } finally {
      setLoading(false);
    }
  }, [companyId, includeExpired]);

  useEffect(() => {
    void (async () => {
      await load();
    })();
  }, [load]);

  function resetForm() {
    setHeader({
      manufacturerName: "",
      listName: "",
      listDate: new Date().toISOString().slice(0, 10),
      validUntil: "",
      currency: "TRY",
    });
    setItems([emptyItem()]);
    setFormError("");
  }

  async function save() {
    if (saving) return;

    setSaving(true);
    setFormError("");

    try {
      const result = await manufacturerPriceListService.create({
        companyId,
        manufacturerName: header.manufacturerName,
        listName: header.listName,
        listDate: header.listDate,
        validUntil: header.validUntil || null,
        currency: header.currency,
        items: items.map((item) => ({
          productCode: item.productCode,
          productDescription: item.productDescription,
          unit: item.unit,
          listPrice: Number(item.listPrice) || 0,
          category: item.category?.trim() || null,
          brand: item.brand?.trim() || null,
          model: item.model?.trim() || null,
        })),
      });

      setNotice(result.message);
      setFormOpen(false);
      resetForm();
      await load();
    } catch (err) {
      // Doğrulama uçta; mesajı olduğu gibi gösteriyoruz ve form
      // AÇIK kalıyor — kullanıcı girdiklerini kaybetmemeli.
      setFormError(messageOf(err));
    } finally {
      setSaving(false);
    }
  }

  function updateItem(
    key: string,
    field: keyof CreateManufacturerPriceListItem,
    value: string
  ) {
    setItems((current) =>
      current.map((item) =>
        item.key === key
          ? {
              ...item,
              [field]: field === "listPrice" ? Number(value) || 0 : value,
            }
          : item
      )
    );
  }

  return (
    <Card className="mb-6">
      <CardHeader>
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <h2 className="text-lg font-semibold text-slate-900">
              Üretici Fiyat Listeleri
            </h2>
            <p className="mt-1 text-sm text-slate-500">
              Aramanın beslendiği listeler. Süresi dolanlar varsayılan
              olarak gizlidir.
            </p>
          </div>

          <div className="flex items-center gap-3">
            <label className="flex items-center gap-2 text-sm text-slate-600">
              <input
                type="checkbox"
                checked={includeExpired}
                onChange={(event) => setIncludeExpired(event.target.checked)}
                className="h-4 w-4 rounded border-slate-300"
              />
              Süresi dolanlar dahil
            </label>

            {canManage && (
              <Button
                onClick={() => {
                  resetForm();
                  setFormOpen(true);
                }}
                disabled={!companyId}
              >
                + Yeni Liste
              </Button>
            )}
          </div>
        </div>
      </CardHeader>

      <CardContent>
        {error && (
          <div className="mb-4 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
            {error}
          </div>
        )}

        {notice && (
          <div className="mb-4 rounded-lg border border-emerald-200 bg-emerald-50 px-3 py-2 text-sm text-emerald-800">
            {notice}
          </div>
        )}

        {loading ? (
          <div className="py-8 text-center text-sm text-slate-500">
            Yükleniyor...
          </div>
        ) : lists.length === 0 ? (
          <EmptyState
            title="Fiyat listesi yok"
            description={
              companyId
                ? "Bu şirkette geçerli üretici fiyat listesi bulunmuyor."
                : "Önce şirket seçin."
            }
          />
        ) : (
          <div className="overflow-x-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Üretici</TableHead>
                  <TableHead>Liste</TableHead>
                  <TableHead>Liste tarihi</TableHead>
                  <TableHead>Geçerlilik</TableHead>
                  <TableHead>Para birimi</TableHead>
                  <TableHead className="text-right">Ürün</TableHead>
                  <TableHead>Durum</TableHead>
                </TableRow>
              </TableHeader>

              <TableBody>
                {lists.map((list) => (
                  <TableRow key={list.id}>
                    <TableCell className="font-medium">
                      {list.manufacturerName}
                    </TableCell>
                    <TableCell>{list.listName}</TableCell>
                    <TableCell>{formatDate(list.listDate)}</TableCell>
                    <TableCell>{formatDate(list.validUntil)}</TableCell>
                    <TableCell>{list.currency}</TableCell>
                    <TableCell className="text-right tabular-nums">
                      {list.itemCount}
                    </TableCell>
                    <TableCell>
                      {list.isActive ? (
                        <Badge variant="success">Aktif</Badge>
                      ) : (
                        <Badge>Pasif</Badge>
                      )}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </CardContent>

      <Modal
        open={formOpen}
        title="Yeni üretici fiyat listesi"
        description="Listede en az bir ürün bulunmalıdır."
        size="lg"
        busy={saving}
        onClose={() => setFormOpen(false)}
        footer={
          <div className="flex justify-end gap-2">
            <Button
              variant="secondary"
              onClick={() => setFormOpen(false)}
              disabled={saving}
            >
              Vazgeç
            </Button>
            <Button onClick={() => void save()} disabled={saving}>
              {saving ? "Kaydediliyor..." : "Kaydet"}
            </Button>
          </div>
        }
      >
        <div className="space-y-4">
          {formError && (
            <div className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
              {formError}
            </div>
          )}

          <div className="grid gap-3 sm:grid-cols-2">
            <Input
              label="Üretici"
              value={header.manufacturerName}
              onChange={(event) =>
                setHeader((h) => ({ ...h, manufacturerName: event.target.value }))
              }
            />
            <Input
              label="Liste adı"
              value={header.listName}
              onChange={(event) =>
                setHeader((h) => ({ ...h, listName: event.target.value }))
              }
            />
            <Input
              label="Liste tarihi"
              type="date"
              value={header.listDate}
              onChange={(event) =>
                setHeader((h) => ({ ...h, listDate: event.target.value }))
              }
            />
            <Input
              label="Geçerlilik sonu"
              type="date"
              helperText="Boş bırakılırsa süresiz"
              value={header.validUntil}
              onChange={(event) =>
                setHeader((h) => ({ ...h, validUntil: event.target.value }))
              }
            />
            <Input
              label="Para birimi"
              value={header.currency}
              onChange={(event) =>
                setHeader((h) => ({ ...h, currency: event.target.value }))
              }
            />
          </div>

          <div>
            <div className="mb-2 flex items-center justify-between">
              <h3 className="text-sm font-semibold text-slate-900">Ürünler</h3>
              <Button
                variant="secondary"
                size="sm"
                onClick={() => setItems((current) => [...current, emptyItem()])}
              >
                + Satır
              </Button>
            </div>

            <div className="space-y-2">
              {items.map((item) => (
                <div
                  key={item.key}
                  className="grid gap-2 rounded-lg border border-slate-200 p-2 sm:grid-cols-5"
                >
                  <Input
                    placeholder="Ürün kodu"
                    value={item.productCode}
                    onChange={(event) =>
                      updateItem(item.key, "productCode", event.target.value)
                    }
                  />
                  <Input
                    placeholder="Açıklama"
                    value={item.productDescription}
                    onChange={(event) =>
                      updateItem(
                        item.key,
                        "productDescription",
                        event.target.value
                      )
                    }
                  />
                  <Input
                    placeholder="Birim"
                    value={item.unit}
                    onChange={(event) =>
                      updateItem(item.key, "unit", event.target.value)
                    }
                  />
                  <Input
                    placeholder="Liste fiyatı"
                    type="number"
                    step="0.0001"
                    value={String(item.listPrice)}
                    onChange={(event) =>
                      updateItem(item.key, "listPrice", event.target.value)
                    }
                  />

                  <div className="flex items-center justify-end">
                    <Button
                      variant="ghost"
                      size="sm"
                      disabled={items.length === 1}
                      onClick={() =>
                        setItems((current) =>
                          current.filter((x) => x.key !== item.key)
                        )
                      }
                    >
                      Sil
                    </Button>
                  </div>
                </div>
              ))}
            </div>
          </div>
        </div>
      </Modal>
    </Card>
  );
}
