import Link from "next/link";

export type DashboardActivity = {
  id: string;
  type: "progress-payment" | "purchase-order" | "goods-receipt" | "stock-movement";
  title: string;
  description: string;
  documentNumber: string;
  activityDate: string;
  href: string;
  statusLabel: string;
  statusTone: "gray" | "yellow" | "blue" | "green" | "red";
};

type RecentActivitiesWidgetProps = {
  activities: DashboardActivity[];
};

const dateTime = new Intl.DateTimeFormat("tr-TR", {
  dateStyle: "short",
  timeStyle: "short",
});

const typeLabels: Record<DashboardActivity["type"], string> = {
  "progress-payment": "Hakediş",
  "purchase-order": "Satın Alma",
  "goods-receipt": "Mal Kabul",
  "stock-movement": "Stok Hareketi",
};

export default function RecentActivitiesWidget({
  activities,
}: RecentActivitiesWidgetProps) {
  return (
    <section className="erp-panel dashboard-recent-activities">
      <div className="erp-panel-header">
        <div>
          <h2>Son İşlemler</h2>
          <p>Hakediş, sipariş ve mal kabul hareketleri</p>
        </div>
      </div>

      {activities.length === 0 ? (
        <div className="erp-empty-state">
          Henüz işlem kaydı bulunmuyor.
        </div>
      ) : (
        <div className="dashboard-activity-list">
          {activities.map((activity) => (
            <Link
              href={activity.href}
              className="dashboard-activity-item"
              key={`${activity.type}-${activity.id}`}
            >
              <div className={`dashboard-activity-icon ${activity.type}`}>
                {activity.type === "progress-payment"
                  ? "H"
                  : activity.type === "purchase-order"
                    ? "S"
                    : activity.type === "goods-receipt"
                      ? "M"
                      : "D"}
              </div>

              <div className="dashboard-activity-content">
                <div className="dashboard-activity-heading">
                  <strong>{activity.title}</strong>
                  <span>{typeLabels[activity.type]}</span>
                </div>

                <p>{activity.description}</p>

                <small>
                  {activity.documentNumber} ·{" "}
                  {dateTime.format(new Date(activity.activityDate))}
                </small>
              </div>

              <span className={`erp-status ${activity.statusTone}`}>
                {activity.statusLabel}
              </span>
            </Link>
          ))}
        </div>
      )}
    </section>
  );
}
