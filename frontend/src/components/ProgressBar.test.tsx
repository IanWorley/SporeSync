import { render, screen } from "@testing-library/react";
import { expect, test } from "vite-plus/test";
import { ProgressBar } from "./ProgressBar";

test.each([
  { value: 0.25, percent: "25" },
  { value: -0.5, percent: "0" },
  { value: 1.5, percent: "100" },
  { value: Number.NaN, percent: "0" },
])("reports $value as $percent percent", ({ value, percent }) => {
  render(<ProgressBar value={value} label="movie.mkv" />);

  const bar = screen.getByRole("progressbar", { name: "movie.mkv" });
  expect(bar.getAttribute("aria-valuenow")).toBe(percent);
  expect(bar.getAttribute("aria-valuemin")).toBe("0");
  expect(bar.getAttribute("aria-valuemax")).toBe("100");
});
