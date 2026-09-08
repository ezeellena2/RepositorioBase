import type { ReactNode } from "react";

export interface Column<T> {
  key: string;
  header: string;
  render: (row: T) => ReactNode;
  /** Columna de acciones o números: se alinea a la derecha. */
  end?: boolean;
}

interface DataTableProps<T> {
  caption: string;
  columns: Column<T>[];
  rows: T[];
  getKey: (row: T) => string;
}

/** Tabla de los directorios de Platform. Scrollea sola en pantallas angostas. */
export function DataTable<T>({ caption, columns, rows, getKey }: DataTableProps<T>) {
  return (
    <div className="table-wrap">
      <table className="table">
        <caption className="table__caption">{caption}</caption>
        <thead>
          <tr>
            {columns.map((column) => (
              <th key={column.key} scope="col" className={column.end ? "is-end" : undefined}>
                {column.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={getKey(row)}>
              {columns.map((column) => (
                <td key={column.key} className={column.end ? "is-end" : undefined}>
                  {column.render(row)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
