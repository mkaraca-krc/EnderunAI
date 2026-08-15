import Link from "next/link";
import { money } from "@/lib/format/turkish";

import {
  ProgressPaymentStatus,
  type ProgressPaymentListItem,
} from "@/services/progress-payment.service";

type RecentProgressPaymentsProps = {
  items: ProgressPaymentListItem[];
};


const date = new Intl.DateTimeFormat("tr-TR");

const statusLabels: Record<ProgressPaymentStatus, string> = {
  [ProgressPaymentStatus.Draft]: "Taslak",
  [ProgressPaymentStatus.PendingApproval]: "Onay Bekliyor",
  [ProgressPaymentStatus.Approved]: "Onaylandı",
  [ProgressPaymentStatus.Posted]: "Kesinleşti",
  [ProgressPaymentStatus.Cancelled]: "İptal",
};

const statusClasses: Record<ProgressPaymentStatus, string> = {
  [ProgressPaymentStatus.Draft]: "gray",
  [ProgressPaymentStatus.PendingApproval]: "yellow",
  [ProgressPaymentStatus.Approved]: "blue",
  [ProgressPaymentStatus.Posted]: "green",
  [ProgressPaymentStatus.Cancelled]: "red",
};

export default function RecentProgressPayments({
  items,
}: RecentProgressPaymentsProps) {
  return (
    <div className="erp-panel">
      <div className="erp-panel-header">
        <div>
          <h2>Son Hakedişler</h2>
          <p>En güncel hakediş hareketleri</p>
        </div>

        <Link href="/hakedis">Tümünü Gör</Link>
      </div>

      <div style={{ overflowX: "auto" }}>
        <table className="erp-table">
          <thead>
            <tr>
              <th>Hakediş</th>
              <th>Proje</th>
              <th>Tarih</th>
              <th>Bu Dönem</th>
              <th>Fiyat Farkı</th>
              <th>Net</th>
              <th>Durum</th>
            </tr>
          </thead>

          <tbody>
            {items.length === 0 ? (
              <tr>
                <td colSpan={7}>
                  Henüz hakediş kaydı yok.
                </td>
              </tr>
            ) : (
              items.map((item) => (
                <tr key={item.id}>
                  <td>
                    <Link href={`/hakedis/${item.id}`}>
                      <strong>
                        {item.progressPaymentNumber}
                      </strong>
                    </Link>
                  </td>

                  <td>
                    {item.projectCode}
                    <div>
                      <small>{item.projectName}</small>
                    </div>
                  </td>

                  <td>
                    {date.format(
                      new Date(item.progressPaymentDate)
                    )}
                  </td>

                  <td>{money(item.currentAmount)}</td>

                  <td>
                    {money(
                      item.priceDifferenceAmount
                    )}
                  </td>

                  <td>
                    <strong>
                      {money(item.netPayableAmount)}
                    </strong>
                  </td>

                  <td>
                    <span
                      className={`erp-status ${
                        statusClasses[item.status]
                      }`}
                    >
                      {statusLabels[item.status]}
                    </span>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
