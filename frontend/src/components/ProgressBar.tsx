const PERCENT_MAX = 100;

type ProgressBarProps = {
  readonly value: number;
  readonly label: string;
};

export function ProgressBar({ value, label }: ProgressBarProps) {
  const fraction = Number.isNaN(value) ? 0 : Math.min(Math.max(value, 0), 1);
  const percent = Math.round(fraction * PERCENT_MAX);

  return (
    <div
      role="progressbar"
      aria-label={label}
      aria-valuemin={0}
      aria-valuemax={PERCENT_MAX}
      aria-valuenow={percent}
      className="h-2 w-full overflow-hidden rounded-full bg-gray-200 dark:bg-gray-800"
    >
      <div className="h-full bg-blue-600 dark:bg-blue-500" style={{ width: `${percent}%` }} />
    </div>
  );
}
