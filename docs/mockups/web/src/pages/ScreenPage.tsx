interface ScreenPageProps {
  code: string | null;
  title: string;
  note?: string;
}

/** Destino provisorio de una pantalla del inventario. El shell ya la ubica y aplica su permiso. */
export function ScreenPage({ code, title, note }: ScreenPageProps) {
  return (
    <div className="screen">
      <div className="screen-head">
        {code && <span className="screen-code">{code}</span>}
        <h1>{title}</h1>
      </div>
      <div className="screen-placeholder">
        <p>
          {code
            ? `Esta pantalla está en el inventario como ${code}. Se construye en su propio incremento.`
            : "Esta pantalla no está en el inventario de catorce. Se define en su propio incremento."}
        </p>
        {note && <p>{note}</p>}
      </div>
    </div>
  );
}
