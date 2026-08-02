import Link from "next/link";

type NotificationTone =
  | "critical"
  | "warning"
  | "neutral"
  | "positive";

type NotificationItem = {
  title: string;
  description: string;
  value: number;
  href: string;
  tone: NotificationTone;
};

type NotificationCenterWidgetProps = {
  pendingProgressPayments: number;
  openPurchaseRequests: number;
  openRfqs: number;
  openOrders: number;
  criticalStock: number;
  riskyProjects: number;
  pendingAccessRequests?: number;
  kesifProjects?: number;
};

export default function NotificationCenterWidget({
  pendingProgressPayments,
  openPurchaseRequests,
  openRfqs,
  openOrders,
  criticalStock,
  riskyProjects,
  pendingAccessRequests = 0,
  kesifProjects = 0,
}: NotificationCenterWidgetProps) {
  const items: NotificationItem[] = [
    {
      title: "Bekleyen Erişim Talebi",
      description:
        "Mesai saati dışı erişim isteyen kullanıcılar",
      value: pendingAccessRequests,
      href: "/sistem-yonetimi/erisim-talepleri",
      tone:
        pendingAccessRequests > 0
          ? "warning"
          : "positive",
    },
    {
      title: "Keşif Aşamasındaki Projeler",
      description:
        "Henüz sözleşme/işveren netleşmemiş projeler",
      value: kesifProjects,
      href: "/projeler",
      tone: kesifProjects > 0 ? "neutral" : "positive",
    },
    {
      title: "Onay Bekleyen Hakediş",
      description:
        "Yönetici onayı bekleyen hakediş kayıtları",
      value: pendingProgressPayments,
      href: "/hakedis",
      tone:
        pendingProgressPayments > 0
          ? "warning"
          : "positive",
    },
    {
      title: "Riskli Proje",
      description:
        "Kırmızı sağlık durumundaki projeler",
      value: riskyProjects,
      href: "/projeler",
      tone:
        riskyProjects > 0
          ? "critical"
          : "positive",
    },
    {
      title: "Kritik Stok",
      description:
        "Minimum stok seviyesinde veya altında",
      value: criticalStock,
      href: "/depo-stok",
      tone:
        criticalStock > 0
          ? "critical"
          : "positive",
    },
    {
      title: "Açık Satın Alma Talebi",
      description:
        "İşlem bekleyen satın alma talepleri",
      value: openPurchaseRequests,
      href: "/satin-alma",
      tone:
        openPurchaseRequests > 0
          ? "warning"
          : "positive",
    },
    {
      title: "Devam Eden RFQ",
      description:
        "Teklif toplama süreci devam eden RFQ",
      value: openRfqs,
      href: "/satin-alma/rfq",
      tone:
        openRfqs > 0
          ? "neutral"
          : "positive",
    },
    {
      title: "Açık Sipariş",
      description:
        "Teslim veya kapanış bekleyen siparişler",
      value: openOrders,
      href: "/satin-alma/siparis",
      tone:
        openOrders > 0
          ? "neutral"
          : "positive",
    },
  ];

  const activeItems = items.filter(
    (item) => item.value > 0
  );

  const totalPending = activeItems.reduce(
    (sum, item) => sum + item.value,
    0
  );

  return (
    <section className="erp-panel dashboard-notification-widget">
      <div className="erp-panel-header">
        <div>
          <h2>Bekleyen İşler ve Bildirimler</h2>
          <p>
            Yönetici müdahalesi gerektiren kayıtlar
          </p>
        </div>

        <span
          className={`erp-status ${
            totalPending > 0 ? "yellow" : "green"
          }`}
        >
          {totalPending} kayıt
        </span>
      </div>

      {activeItems.length === 0 ? (
        <div className="erp-alert success">
          Şu anda bekleyen kritik işlem bulunmuyor.
        </div>
      ) : (
        <div className="dashboard-notification-list">
          {activeItems.map((item) => (
            <Link
              className={`dashboard-notification-item ${item.tone}`}
              href={item.href}
              key={item.title}
            >
              <span className="dashboard-notification-indicator" />

              <div className="dashboard-notification-content">
                <strong>{item.title}</strong>
                <small>{item.description}</small>
              </div>

              <span className="dashboard-notification-value">
                {item.value}
              </span>
            </Link>
          ))}
        </div>
      )}
    </section>
  );
}
