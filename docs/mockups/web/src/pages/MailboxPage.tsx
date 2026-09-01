import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api, type MailMessage } from "../api/client";
import { LinkButton } from "../components/Button";
import { Wordmark } from "../components/Wordmark";

function toRelative(url: string): string {
  try {
    const u = new URL(url);
    return `${u.pathname}${u.search}`;
  } catch {
    return url;
  }
}

function formatDate(iso: string): string {
  return new Intl.DateTimeFormat("es-AR", { hour: "2-digit", minute: "2-digit", day: "2-digit", month: "short" }).format(new Date(iso));
}

function actionLabel(kind: MailMessage["kind"]): string {
  if (kind === "confirm") return "Confirmar mi correo";
  return "Iniciar sesión";
}

function bodyOf(m: MailMessage): string[] {
  if (m.kind === "confirm") {
    return [
      m.preview,
      "El enlace dura 24 horas. Si no creaste una cuenta en Base, podés ignorar este mensaje.",
    ];
  }
  if (m.kind === "already_registered") {
    return [
      m.preview,
      "Si no fuiste vos, no hace falta que hagas nada: nadie puede entrar a tu cuenta sin tu contraseña.",
    ];
  }
  return [m.preview];
}

export function MailboxPage() {
  const [messages, setMessages] = useState<MailMessage[] | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    api.mailbox().then((r) => {
      if (cancelled) return;
      setMessages(r.messages);
      setSelectedId((id) => id ?? r.messages[0]?.id ?? null);
    });
    return () => {
      cancelled = true;
    };
  }, []);

  const selected = messages?.find((m) => m.id === selectedId) ?? null;

  return (
    <div className="shell">
      <header className="topbar">
        <Wordmark to="/iniciar-sesion" />
        <span className="topbar__spacer" />
        <Link to="/iniciar-sesion" className="btn btn--ghost btn--sm">
          Volver al inicio de sesión
        </Link>
      </header>
      <main className="page" style={{ maxWidth: 1080 }}>
        <div className="page__header">
          <h1 className="page__title">Bandeja de correo</h1>
          <p className="page__subtitle">
            Acá aparecen los correos que el sistema enviaría. En el producto real llegan a la casilla de cada persona.
          </p>
        </div>

        <div className="mailbox">
          <section className="card">
            {messages === null ? (
              <div className="empty">Cargando</div>
            ) : messages.length === 0 ? (
              <div className="empty">Todavía no se envió ningún correo.</div>
            ) : (
              <div className="mail-list">
                {messages.map((m) => (
                  <button
                    key={m.id}
                    type="button"
                    className={`mail-list__item${m.id === selectedId ? " mail-list__item--active" : ""}`}
                    onClick={() => setSelectedId(m.id)}
                  >
                    <span className="mail-list__to">
                      <span>{m.to}</span>
                      <span>{formatDate(m.sentAt)}</span>
                    </span>
                    <span className="mail-list__subject">{m.subject}</span>
                    <span className="mail-list__preview">{m.preview}</span>
                  </button>
                ))}
              </div>
            )}
          </section>

          <section className="card">
            {selected ? (
              <article className="mail">
                <h2 className="mail__subject">{selected.subject}</h2>
                <div className="mail__meta">
                  <span>De</span>
                  <span>Base &lt;no-responder@base.app&gt;</span>
                  <span>Para</span>
                  <span>{selected.to}</span>
                </div>
                <div className="mail__body">
                  {bodyOf(selected).map((p, i) => (
                    <p key={i}>{p}</p>
                  ))}
                  {selected.actionUrl && (
                    <div>
                      <LinkButton to={toRelative(selected.actionUrl)}>{actionLabel(selected.kind)}</LinkButton>
                    </div>
                  )}
                  <p className="mail__signature">El equipo de Base</p>
                  <p className="mail__fineprint">
                    Recibiste este mensaje porque alguien usó esta dirección en base.app.
                  </p>
                </div>
              </article>
            ) : (
              <div className="empty">Elegí un correo para verlo.</div>
            )}
          </section>
        </div>
      </main>
    </div>
  );
}
