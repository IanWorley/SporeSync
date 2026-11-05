import type { ReactNode } from "react";

export type BadgeTone = "neutral" | "info" | "success" | "warning" | "danger";

const TONE_CLASSES: Record<BadgeTone, string> = {
  neutral: "bg-gray-100 text-gray-800 dark:bg-gray-800 dark:text-gray-200",
  info: "bg-blue-100 text-blue-800 dark:bg-blue-900 dark:text-blue-200",
  success: "bg-green-100 text-green-800 dark:bg-green-900 dark:text-green-200",
  warning: "bg-amber-100 text-amber-800 dark:bg-amber-900 dark:text-amber-200",
  danger: "bg-red-100 text-red-800 dark:bg-red-900 dark:text-red-200",
};

type BadgeProps = {
  readonly tone: BadgeTone;
  readonly children: ReactNode;
};

export function Badge({ tone, children }: BadgeProps) {
  return (
    <span
      className={`inline-block rounded-full px-2 py-0.5 text-xs font-medium ${TONE_CLASSES[tone]}`}
    >
      {children}
    </span>
  );
}
