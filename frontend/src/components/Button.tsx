import type { ComponentProps } from "react";

export type ButtonVariant = "primary" | "secondary" | "danger";

const VARIANT_CLASSES: Record<ButtonVariant, string> = {
  primary: "bg-blue-600 text-white hover:bg-blue-700 dark:bg-blue-500 dark:hover:bg-blue-400",
  secondary:
    "border border-gray-300 bg-white text-gray-900 hover:bg-gray-100 dark:border-gray-700 dark:bg-gray-900 dark:text-gray-100 dark:hover:bg-gray-800",
  danger: "bg-red-600 text-white hover:bg-red-700 dark:bg-red-500 dark:hover:bg-red-400",
};

type ButtonProps = Omit<ComponentProps<"button">, "className" | "type"> & {
  readonly type: "button" | "submit" | "reset";
  readonly variant: ButtonVariant;
};

export function Button({ type, variant, ...props }: ButtonProps) {
  return (
    <button
      // oxlint-disable-next-line react/button-has-type -- ButtonProps types `type` as one of the three valid values.
      type={type}
      className={`rounded px-3 py-1.5 text-sm font-medium disabled:opacity-50 ${VARIANT_CLASSES[variant]}`}
      {...props}
    />
  );
}
