import type { ReactNode } from "react";

export type Column<Row> = {
  readonly key: string;
  readonly header: string;
  readonly render: (row: Row) => ReactNode;
};

type TableProps<Row> = {
  readonly columns: readonly Column<Row>[];
  readonly rows: readonly Row[];
  readonly rowKey: (row: Row) => string;
};

export function Table<Row>({ columns, rows, rowKey }: TableProps<Row>) {
  return (
    <table className="w-full text-left text-sm">
      <thead className="border-b border-gray-200 dark:border-gray-800">
        <tr>
          {columns.map((column) => (
            <th key={column.key} scope="col" className="px-3 py-2 font-semibold">
              {column.header}
            </th>
          ))}
        </tr>
      </thead>
      <tbody>
        {rows.map((row) => (
          <tr key={rowKey(row)} className="border-b border-gray-100 dark:border-gray-900">
            {columns.map((column) => (
              <td key={column.key} className="px-3 py-2">
                {column.render(row)}
              </td>
            ))}
          </tr>
        ))}
      </tbody>
    </table>
  );
}
