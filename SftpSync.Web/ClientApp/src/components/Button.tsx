import type { ButtonHTMLAttributes } from "react";
import { cn } from "../lib/cn";

export function Button({
  className,
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement>) {
  return (
    <button
      className={cn(
        "inline-flex h-9 items-center justify-center gap-2 rounded-md border border-border bg-panel px-3 text-sm font-medium text-foreground transition hover:bg-muted focus:outline-none focus:shadow-focus disabled:pointer-events-none disabled:opacity-50",
        className,
      )}
      {...props}
    />
  );
}
