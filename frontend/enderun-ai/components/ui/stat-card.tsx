import type { ReactNode } from "react";

interface StatCardProps {
  title: string;
  value: string | number;
  description?: string;
  icon?: ReactNode;
  trend?: {
    value: string;
    direction: "up" | "down" | "neutral";
  };
}

const trendClasses = {
  up: "text-emerald-600",
  down: "text-red-600",
  neutral: "text-slate-500",
};

export function StatCard({
  title,
  value,
  description,
  icon,
  trend,
}: StatCardProps) {
  return (
    <div className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
      <div className="flex items-start justify-between gap-4">
        <div>
          <p className="text-sm font-medium text-slate-500">{title}</p>
          <p className="mt-2 text-2xl font-semibold tracking-tight text-slate-900">
            {value}
          </p>
        </div>

        {icon && (
          <div className="flex h-10 w-10 items-center justify-center rounded-lg bg-slate-100 text-slate-700">
            {icon}
          </div>
        )}
      </div>

      {(description || trend) && (
        <div className="mt-4 flex items-center justify-between gap-3 text-sm">
          {description && <span className="text-slate-500">{description}</span>}

          {trend && (
            <span className={`font-medium ${trendClasses[trend.direction]}`}>
              {trend.value}
            </span>
          )}
        </div>
      )}
    </div>
  );
}
