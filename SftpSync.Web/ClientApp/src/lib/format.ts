const byteUnits = ["B", "KB", "MB", "GB", "TB"];

export function formatBytes(value: number | null | undefined) {
  if (value === null || value === undefined) {
    return "0 B";
  }

  if (value < 1) {
    return "0 B";
  }

  const exponent = Math.min(Math.floor(Math.log(value) / Math.log(1024)), byteUnits.length - 1);
  const amount = value / 1024 ** exponent;
  return `${amount >= 10 || exponent === 0 ? amount.toFixed(0) : amount.toFixed(1)} ${byteUnits[exponent]}`;
}

export function formatRate(value: number | null | undefined) {
  if (!value) {
    return "0 B/s";
  }

  return `${formatBytes(value)}/s`;
}

export function formatLocalDateTime(value: string | null | undefined) {
  if (!value) {
    return "Not available";
  }

  return new Intl.DateTimeFormat(undefined, {
    dateStyle: "medium",
    timeStyle: "medium"
  }).format(new Date(value));
}

export function formatRelativeTime(value: string | null | undefined, now = new Date()) {
  if (!value) {
    return "Not available";
  }

  const seconds = Math.round((new Date(value).getTime() - now.getTime()) / 1000);
  const divisions: Array<[Intl.RelativeTimeFormatUnit, number]> = [
    ["year", 60 * 60 * 24 * 365],
    ["month", 60 * 60 * 24 * 30],
    ["day", 60 * 60 * 24],
    ["hour", 60 * 60],
    ["minute", 60],
    ["second", 1]
  ];

  const formatter = new Intl.RelativeTimeFormat(undefined, { numeric: "auto" });
  const [unit, amount] = divisions.find(([, amount]) => Math.abs(seconds) >= amount) ?? ["second", 1];
  return formatter.format(Math.round(seconds / amount), unit);
}
