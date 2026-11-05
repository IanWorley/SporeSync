import { render, screen } from "@testing-library/react";
import { expect, test } from "vite-plus/test";
import { type Column, Table } from "./Table";

type RemoteFile = { readonly path: string; readonly size: number };

const COLUMNS: readonly Column<RemoteFile>[] = [
  { key: "name", header: "Name", render: (file) => file.path },
  { key: "size", header: "Size", render: (file) => file.size },
];

test("renders a header row and one row per item", () => {
  const files = [
    { path: "a.mkv", size: 10 },
    { path: "b.mkv", size: 20 },
  ];

  render(<Table columns={COLUMNS} rows={files} rowKey={(file) => file.path} />);

  const rows = screen
    .getAllByRole("row")
    .map((row) => [...row.children].map((cell) => cell.textContent));
  expect(rows).toEqual([
    ["Name", "Size"],
    ["a.mkv", "10"],
    ["b.mkv", "20"],
  ]);
});
