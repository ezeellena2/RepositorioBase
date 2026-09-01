import { useState } from "react";
import { AppShell } from "../components/AppShell";
import { Button } from "../components/Button";
import { CheckIcon } from "../components/Icons";
import { useIdentity } from "../lib/identity";

const OPTIONS = [
  { id: "facturacion", title: "Facturación", desc: "Emitir comprobantes y llevar cobranzas." },
  { id: "clientes", title: "Clientes", desc: "Agenda de contactos y seguimiento comercial." },
  { id: "equipo", title: "Equipo", desc: "Invitar personas y asignar roles." },
  { id: "reportes", title: "Reportes", desc: "Indicadores y exportaciones." },
];

export function HomePage() {
  const { context } = useIdentity();
  const [configuring, setConfiguring] = useState(false);
  const [selected, setSelected] = useState<string[]>([]);
  const [saved, setSaved] = useState<string[] | null>(null);

  if (!context?.activeTenant) return null;
  const tenant = context.activeTenant;
  const firstName = context.user.displayName.split(" ")[0];

  function toggle(id: string) {
    setSelected((s) => (s.includes(id) ? s.filter((x) => x !== id) : [...s, id]));
  }

  return (
    <AppShell>
      <main className="page">
        <div className="page__header">
          <h1 className="page__title">Hola, {firstName}</h1>
          <p className="page__subtitle">
            Estás operando en <strong>{tenant.name}</strong>.
          </p>
        </div>

        <div className="grid grid--2">
          <section className="card">
            <div className="card__header">
              <h2 className="card__title">Primeros pasos</h2>
              <p className="card__desc">Tres cosas para dejar tu espacio listo.</p>
            </div>
            <div className="card__body">
              <div className="checklist">
                <div className="checklist__item">
                  <span className="checklist__mark checklist__mark--done">
                    <CheckIcon size={14} />
                  </span>
                  <div className="checklist__body">
                    <div className="checklist__title checklist__title--done">Confirmá tu correo</div>
                  </div>
                </div>
                <div className="checklist__item">
                  <span className="checklist__mark checklist__mark--done">
                    <CheckIcon size={14} />
                  </span>
                  <div className="checklist__body">
                    <div className="checklist__title checklist__title--done">Elegí un espacio para operar</div>
                  </div>
                </div>
                <div className="checklist__item">
                  <span className={`checklist__mark${saved ? " checklist__mark--done" : ""}`}>
                    <CheckIcon size={14} />
                  </span>
                  <div className="checklist__body">
                    <div className={`checklist__title${saved ? " checklist__title--done" : ""}`}>
                      Contanos qué querés hacer con la plataforma
                    </div>
                    {saved ? (
                      <div className="checklist__desc">
                        Activaste {saved.map((id) => OPTIONS.find((o) => o.id === id)?.title).join(", ")}.
                      </div>
                    ) : (
                      <div className="checklist__desc">Lo podés cambiar más adelante.</div>
                    )}
                  </div>
                  {!configuring && !saved && (
                    <Button size="sm" variant="secondary" onClick={() => setConfiguring(true)}>
                      Configurar
                    </Button>
                  )}
                  {saved && (
                    <Button size="sm" variant="ghost" onClick={() => { setConfiguring(true); setSaved(null); }}>
                      Cambiar
                    </Button>
                  )}
                </div>
              </div>

              {configuring && (
                <div>
                  <div className="options">
                    {OPTIONS.map((o) => (
                      <label key={o.id} className={`option${selected.includes(o.id) ? " option--on" : ""}`}>
                        <input type="checkbox" checked={selected.includes(o.id)} onChange={() => toggle(o.id)} />
                        <span>
                          <strong>{o.title}</strong>
                          <span className="option__desc">{o.desc}</span>
                        </span>
                      </label>
                    ))}
                  </div>
                  <div className="actions">
                    <Button size="sm" disabled={selected.length === 0} onClick={() => { setSaved(selected); setConfiguring(false); }}>
                      Guardar
                    </Button>
                    <Button size="sm" variant="ghost" onClick={() => setConfiguring(false)}>
                      Ahora no
                    </Button>
                  </div>
                </div>
              )}
            </div>
          </section>

          <section className="card">
            <div className="card__header">
              <h2 className="card__title">Tu espacio</h2>
            </div>
            <div className="card__body">
              <dl className="dl">
                <dt>Nombre</dt>
                <dd>{tenant.name}</dd>
                <dt>Tipo</dt>
                <dd>{tenant.type === "Personal" ? "Espacio personal" : "Organización"}</dd>
                <dt>CUIT</dt>
                <dd>{tenant.cuit}</dd>
                <dt>Tu rol</dt>
                <dd>{tenant.role}</dd>
                <dt>Permisos</dt>
                <dd style={{ fontWeight: 450, color: "var(--color-text-muted)" }}>{context.permissions.join(", ")}</dd>
              </dl>
            </div>
          </section>
        </div>
      </main>
    </AppShell>
  );
}
