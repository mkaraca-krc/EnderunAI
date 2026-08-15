"use client";

import { useMemo, useState } from "react";

import type {
  ScheduleActivity,
  ScheduleDependency,
} from "@/services/project-schedule.service";

/**
 * İş programı Gantt şeması.
 *
 * Elle çizilen SVG; hazır bir Gantt kütüphanesi eklenmedi. Kütüphaneler
 * kendi tasarım dillerini getirir ve bu ekranın kurumsal kimlikle
 * (turkuaz #18797c) tutarlı kalması gerekiyordu.
 *
 * Çizilen dört şey birbirinden AYRI okunmalı:
 *   - baseline (kilitli referans) ince gri çubuk
 *   - planlanan (bugünkü plan) ana çubuk
 *   - gerçekleşen ana çubuğun içindeki dolgu
 *   - tahmini gecikme, plan bitişinden sonraki kesikli uzantı
 * Üçünü tek çubukta göstermek "plan mı gerçek mi" sorusunu
 * cevapsız bırakırdı.
 */

const ROW_HEIGHT = 38;
const BAR_HEIGHT = 16;
const HEADER_HEIGHT = 44;
const LABEL_WIDTH = 280;

const ZOOM = {
  gun: { label: "Gün", width: 22 },
  hafta: { label: "Hafta", width: 9 },
  ay: { label: "Ay", width: 3.5 },
} as const;

type ZoomKey = keyof typeof ZOOM;

const MS_PER_DAY = 86_400_000;

/** ISO tarihi (yyyy-MM-dd) gün numarasına çevirir; saat/zaman dilimi girmez. */
function dayNumber(iso: string): number {
  const [year, month, day] = iso.slice(0, 10).split("-").map(Number);
  return Math.floor(Date.UTC(year, month - 1, day) / MS_PER_DAY);
}

function fromDayNumber(value: number): Date {
  return new Date(value * MS_PER_DAY);
}

function formatDate(iso?: string | null) {
  return iso ? iso.slice(0, 10).split("-").reverse().join(".") : "—";
}

const MONTHS = [
  "Oca", "Şub", "Mar", "Nis", "May", "Haz",
  "Tem", "Ağu", "Eyl", "Eki", "Kas", "Ara",
];

type GanttChartProps = {
  activities: ScheduleActivity[];
  dependencies: ScheduleDependency[];
  workWeek: number;
  holidays: string[];
  deadline?: string | null;
  asOf: string;
  selectedId?: string | null;
  onSelect?: (activityId: string) => void;
};

export default function GanttChart({
  activities,
  dependencies,
  workWeek,
  holidays,
  deadline,
  asOf,
  selectedId,
  onSelect,
}: GanttChartProps) {
  const [zoom, setZoom] = useState<ZoomKey>("hafta");

  const holidaySet = useMemo(
    () => new Set(holidays.map((x) => x.slice(0, 10))),
    [holidays]
  );

  const model = useMemo(() => {
    if (activities.length === 0) return null;

    const marks: number[] = [dayNumber(asOf)];

    for (const activity of activities) {
      marks.push(dayNumber(activity.plannedStart));
      marks.push(dayNumber(activity.plannedEnd));
      if (activity.baselineStart) marks.push(dayNumber(activity.baselineStart));
      if (activity.baselineEnd) marks.push(dayNumber(activity.baselineEnd));
      if (activity.forecastFinish) marks.push(dayNumber(activity.forecastFinish));
    }

    if (deadline) marks.push(dayNumber(deadline));

    const start = Math.min(...marks) - 3;
    const end = Math.max(...marks) + 3;

    return { start, end, days: end - start + 1 };
  }, [activities, asOf, deadline]);

  if (!model) {
    return (
      <div className="erp-empty-state">
        <strong>Çizilecek aktivite yok</strong>
        <p>Kısımlardan çubuk oluşturun ya da elle aktivite ekleyin.</p>
      </div>
    );
  }

  const dayWidth = ZOOM[zoom].width;
  const chartWidth = model.days * dayWidth;
  const chartHeight = activities.length * ROW_HEIGHT;
  const rowIndex = new Map(activities.map((x, index) => [x.id, index]));

  const x = (dn: number) => (dn - model.start) * dayWidth;
  const xOf = (iso: string) => x(dayNumber(iso));

  /** Çalışılmayan gün mü — gölgelendirme için. */
  const isOffDay = (dn: number) => {
    const date = fromDayNumber(dn);
    const iso = date.toISOString().slice(0, 10);
    if (holidaySet.has(iso)) return true;

    // Bayrak sırası: Pzt=1 … Paz=64
    const weekday = date.getUTCDay(); // 0 = Pazar
    const flag = weekday === 0 ? 64 : 1 << (weekday - 1);
    return (workWeek & flag) === 0;
  };

  const offDays: number[] = [];
  const monthTicks: { dn: number; label: string }[] = [];

  for (let dn = model.start; dn <= model.end; dn++) {
    if (isOffDay(dn)) offDays.push(dn);

    const date = fromDayNumber(dn);
    if (date.getUTCDate() === 1) {
      monthTicks.push({
        dn,
        label: `${MONTHS[date.getUTCMonth()]} ${date.getUTCFullYear()}`,
      });
    }
  }

  const today = dayNumber(asOf);

  return (
    <div>
      <div
        style={{
          display: "flex",
          gap: 6,
          alignItems: "center",
          marginBottom: 10,
          flexWrap: "wrap",
        }}
      >
        <small style={{ color: "var(--erp-muted)" }}>Ölçek</small>
        {(Object.keys(ZOOM) as ZoomKey[]).map((key) => (
          <button
            key={key}
            type="button"
            className={zoom === key ? "erp-primary-button" : "erp-secondary-button"}
            onClick={() => setZoom(key)}
          >
            {ZOOM[key].label}
          </button>
        ))}

        <span style={{ flex: 1 }} />

        <Legend color="var(--color-chart-1)" text="Planlanan" />
        <Legend color="var(--color-semantic-danger)" text="Kritik yol" />
        <Legend color="var(--erp-primary)" text="Gerçekleşen" />
        <Legend color="var(--erp-border)" text="Baseline" />
        <Legend color="var(--color-semantic-warning)" text="Tahmini gecikme" />
      </div>

      <div style={{ display: "flex", border: "1px solid var(--erp-border)" }}>
        {/* Sol sütun: aktivite adları. Zaman ekseni kayarken sabit kalır. */}
        <div
          style={{
            width: LABEL_WIDTH,
            flex: `0 0 ${LABEL_WIDTH}px`,
            borderRight: "1px solid var(--erp-border)",
            background: "var(--erp-panel)",
          }}
        >
          <div
            style={{
              height: HEADER_HEIGHT,
              borderBottom: "1px solid var(--erp-border)",
              display: "flex",
              alignItems: "center",
              padding: "0 10px",
              fontSize: 12,
              color: "var(--erp-muted)",
            }}
          >
            Aktivite
          </div>

          {activities.map((activity) => (
            <button
              key={activity.id}
              type="button"
              onClick={() => onSelect?.(activity.id)}
              title={activity.name}
              style={{
                height: ROW_HEIGHT,
                width: "100%",
                border: "none",
                borderBottom: "1px solid var(--erp-border)",
                background:
                  selectedId === activity.id ? "var(--color-brand-primary-tint)" : "transparent",
                textAlign: "left",
                padding: activity.parentActivityId
                  ? "0 10px 0 28px"
                  : "0 10px",
                cursor: "pointer",
                display: "flex",
                alignItems: "center",
                gap: 6,
                overflow: "hidden",
              }}
            >
              {activity.isCritical && (
                <span
                  aria-hidden
                  style={{
                    width: 6,
                    height: 6,
                    borderRadius: "50%",
                    background: "var(--color-semantic-danger)",
                    flex: "0 0 auto",
                  }}
                />
              )}
              <span
                style={{
                  fontSize: 13,
                  fontWeight: activity.parentActivityId ? 400 : 600,
                  whiteSpace: "nowrap",
                  overflow: "hidden",
                  textOverflow: "ellipsis",
                }}
              >
                {activity.name}
              </span>
            </button>
          ))}
        </div>

        {/* Zaman ekseni — yalnızca bu kısım yatay kayar. */}
        <div style={{ overflowX: "auto", flex: 1 }}>
          <svg
            width={Math.max(chartWidth, 400)}
            height={HEADER_HEIGHT + chartHeight}
            role="img"
            aria-label="İş programı Gantt şeması"
          >
            <defs>
              <marker
                id="gantt-arrow"
                markerWidth="6"
                markerHeight="6"
                refX="5"
                refY="3"
                orient="auto"
              >
                <path d="M0,0 L6,3 L0,6 Z" fill="var(--erp-muted)" />
              </marker>
              <marker
                id="gantt-arrow-critical"
                markerWidth="6"
                markerHeight="6"
                refX="5"
                refY="3"
                orient="auto"
              >
                <path d="M0,0 L6,3 L0,6 Z" fill="var(--color-semantic-danger)" />
              </marker>
            </defs>

            {/* Çalışılmayan günler */}
            {offDays.map((dn) => (
              <rect
                key={`off-${dn}`}
                x={x(dn)}
                y={HEADER_HEIGHT}
                width={dayWidth}
                height={chartHeight}
                fill="var(--color-surface-bg)"
              />
            ))}

            {/* Ay başlıkları */}
            <rect
              x={0}
              y={0}
              width={Math.max(chartWidth, 400)}
              height={HEADER_HEIGHT}
              fill="var(--color-surface-card)"
            />
            {monthTicks.map((tick) => (
              <g key={`m-${tick.dn}`}>
                <line
                  x1={x(tick.dn)}
                  y1={0}
                  x2={x(tick.dn)}
                  y2={HEADER_HEIGHT + chartHeight}
                  stroke="var(--erp-border)"
                />
                <text
                  x={x(tick.dn) + 4}
                  y={18}
                  fontSize={11}
                  fill="var(--erp-muted)"
                >
                  {tick.label}
                </text>
              </g>
            ))}
            <line
              x1={0}
              y1={HEADER_HEIGHT}
              x2={Math.max(chartWidth, 400)}
              y2={HEADER_HEIGHT}
              stroke="var(--erp-border)"
            />

            {/* Satır ayraçları */}
            {activities.map((activity, index) => (
              <line
                key={`r-${activity.id}`}
                x1={0}
                y1={HEADER_HEIGHT + (index + 1) * ROW_HEIGHT}
                x2={Math.max(chartWidth, 400)}
                y2={HEADER_HEIGHT + (index + 1) * ROW_HEIGHT}
                stroke="var(--erp-border)"
              />
            ))}

            {/* Bağımlılık okları */}
            {dependencies.map((dependency) => {
              const from = rowIndex.get(dependency.predecessorActivityId);
              const to = rowIndex.get(dependency.successorActivityId);

              if (from === undefined || to === undefined) return null;

              const predecessor = activities[from];
              const successor = activities[to];

              const critical = predecessor.isCritical && successor.isCritical;

              // Bitiş → başlangıç dışındaki türlerde de aynı gösterim
              // kullanılıyor; ok sadece "bu ikisi bağlı" der, türü
              // listede yazıyor.
              const x1 = xOf(predecessor.plannedEnd) + dayWidth;
              const y1 = HEADER_HEIGHT + from * ROW_HEIGHT + ROW_HEIGHT / 2;
              const x2 = xOf(successor.plannedStart);
              const y2 = HEADER_HEIGHT + to * ROW_HEIGHT + ROW_HEIGHT / 2;
              const mid = Math.max(x1 + 6, x2 - 6);

              return (
                <polyline
                  key={dependency.id}
                  points={`${x1},${y1} ${mid},${y1} ${mid},${y2} ${x2},${y2}`}
                  fill="none"
                  stroke={critical ? "var(--color-semantic-danger)" : "var(--erp-muted)"}
                  strokeWidth={1}
                  markerEnd={`url(#${
                    critical ? "gantt-arrow-critical" : "gantt-arrow"
                  })`}
                />
              );
            })}

            {/* Çubuklar */}
            {activities.map((activity, index) => {
              const top = HEADER_HEIGHT + index * ROW_HEIGHT;
              const barY = top + 8;

              const startX = xOf(activity.plannedStart);
              const width = Math.max(
                dayWidth,
                xOf(activity.plannedEnd) + dayWidth - startX
              );

              const color = activity.isCritical ? "var(--color-semantic-danger)" : "var(--color-chart-1)";
              const progressWidth = (width * Math.min(100, activity.progressRate)) / 100;

              const forecastX =
                activity.forecastFinish &&
                dayNumber(activity.forecastFinish) > dayNumber(activity.plannedEnd)
                  ? xOf(activity.forecastFinish) + dayWidth
                  : null;

              return (
                <g
                  key={activity.id}
                  onClick={() => onSelect?.(activity.id)}
                  style={{ cursor: "pointer" }}
                >
                  <title>
                    {`${activity.name}\n` +
                      `Plan: ${formatDate(activity.plannedStart)} – ${formatDate(activity.plannedEnd)}\n` +
                      `Gerçekleşen: %${activity.progressRate} (${activity.progressSourceName})\n` +
                      `Bolluk: ${activity.totalFloatWorkDays} iş günü` +
                      (forecastX
                        ? `\nTahmini bitiş: ${formatDate(activity.forecastFinish)}`
                        : "")}
                  </title>

                  {/* Tahmini gecikme uzantısı */}
                  {forecastX && (
                    <rect
                      x={startX + width}
                      y={barY + 3}
                      width={Math.max(2, forecastX - startX - width)}
                      height={BAR_HEIGHT - 6}
                      fill="var(--color-semantic-warning)"
                      opacity={0.55}
                      rx={2}
                    />
                  )}

                  {/* Planlanan çubuk */}
                  <rect
                    x={startX}
                    y={barY}
                    width={width}
                    height={BAR_HEIGHT}
                    rx={3}
                    fill={color}
                    opacity={0.28}
                    stroke={color}
                    strokeWidth={selectedId === activity.id ? 2 : 1}
                  />

                  {/* Gerçekleşen dolgu */}
                  {progressWidth > 0 && (
                    <rect
                      x={startX}
                      y={barY}
                      width={progressWidth}
                      height={BAR_HEIGHT}
                      rx={3}
                      fill="var(--erp-primary)"
                      opacity={0.85}
                    />
                  )}

                  {/* Baseline — kilitli referans */}
                  {activity.baselineStart && activity.baselineEnd && (
                    <rect
                      x={xOf(activity.baselineStart)}
                      y={barY + BAR_HEIGHT + 2}
                      width={Math.max(
                        dayWidth,
                        xOf(activity.baselineEnd) +
                          dayWidth -
                          xOf(activity.baselineStart)
                      )}
                      height={4}
                      rx={2}
                      fill="var(--erp-border)"
                    />
                  )}
                </g>
              );
            })}

            {/* Termin çizgisi */}
            {deadline && (
              <g>
                <line
                  x1={xOf(deadline)}
                  y1={HEADER_HEIGHT}
                  x2={xOf(deadline)}
                  y2={HEADER_HEIGHT + chartHeight}
                  stroke="var(--color-semantic-danger)"
                  strokeWidth={1.5}
                  strokeDasharray="2 3"
                />
                <text
                  x={xOf(deadline) + 3}
                  y={HEADER_HEIGHT - 6}
                  fontSize={10}
                  fill="var(--color-semantic-danger)"
                >
                  Termin
                </text>
              </g>
            )}

            {/* Bugün çizgisi */}
            <g>
              <line
                x1={x(today)}
                y1={HEADER_HEIGHT}
                x2={x(today)}
                y2={HEADER_HEIGHT + chartHeight}
                stroke="var(--erp-accent)"
                strokeWidth={1.5}
              />
              <text
                x={x(today) + 3}
                y={HEADER_HEIGHT - 20}
                fontSize={10}
                fill="var(--erp-accent)"
              >
                Bugün
              </text>
            </g>
          </svg>
        </div>
      </div>
    </div>
  );
}

function Legend({ color, text }: { color: string; text: string }) {
  return (
    <span
      style={{
        display: "inline-flex",
        alignItems: "center",
        gap: 5,
        fontSize: 12,
        color: "var(--erp-muted)",
      }}
    >
      <span
        aria-hidden
        style={{
          width: 12,
          height: 8,
          borderRadius: 2,
          background: color,
          display: "inline-block",
        }}
      />
      {text}
    </span>
  );
}
