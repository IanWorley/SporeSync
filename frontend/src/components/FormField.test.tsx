import { render, screen } from "@testing-library/react";
import { expect, test } from "vite-plus/test";
import { FormField } from "./FormField";

test("links the label and the error to the input", () => {
  render(
    <FormField label="Port" error="Must be a number">
      {(control) => <input {...control} />}
    </FormField>,
  );

  const input = screen.getByRole("textbox", { name: "Port", description: "Must be a number" });
  expect(input.getAttribute("aria-invalid")).toBe("true");
});

test("marks the input valid when there is no error", () => {
  render(<FormField label="Port">{(control) => <input {...control} />}</FormField>);

  const input = screen.getByRole("textbox", { name: "Port", description: "" });
  expect(input.getAttribute("aria-invalid")).toBe("false");
});
