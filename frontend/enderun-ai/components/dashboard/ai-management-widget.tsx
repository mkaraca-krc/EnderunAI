import type { AIAnalysisItem } from "@/services/ai-analysis.service";

type AiManagementWidgetProps = {
  alerts: AIAnalysisItem[];
};

export default function AiManagementWidget({
  alerts,
}: AiManagementWidgetProps) {
  return (
    <div className="erp-panel">
      <div className="erp-panel-header">
        <div>
          <h2>Enderun AI Yönetim Asistanı</h2>
          <p>
            Finans, proje ve operasyon risk analizleri
          </p>
        </div>
      </div>

      {alerts.length === 0 ? (
        <div className="erp-alert success">
          Şu anda kritik bir AI uyarısı bulunmuyor.
        </div>
      ) : (
        <div className="dashboard-ai-alert-list">
          {alerts.map((alert, index) => (
            <div
              key={`${alert.title}-${index}`}
              className="dashboard-ai-alert-card"
            >
              <div className="dashboard-ai-alert-content">
                <span
                  className={`erp-status ${alert.level}`}
                >
                  !
                </span>

                <div>
                  <strong>{alert.title}</strong>

                  <p className="dashboard-ai-alert-message">
                    {alert.module}
                    {" - "}
                    {alert.message}
                  </p>

                  {alert.suggestion && (
                    <p className="dashboard-ai-alert-suggestion">
                      <strong>Öneri:</strong>{" "}
                      {alert.suggestion}
                    </p>
                  )}
                </div>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
