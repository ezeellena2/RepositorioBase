import { useCallback, useEffect, useState, type FormEvent } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { Alert, AuthLayout, Badge, Button, Card, Field, Modal, StateBlock } from "../components";
import { api, ApiError, type InvitationView } from "../lib/api";
import { INVITATION_STATUS_LABEL, shortDate } from "../lib/format";

/** Pantalla pública de invitación. Cubre alta, confirmación, ingreso y aceptación. */
export function InvitationPage() {
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const token = params.get("token") ?? "";

  const [invitation, setInvitation] = useState<InvitationView | null>(null);
  const [status, setStatus] = useState<"loading" | "ready" | "invalid" | "error">("loading");
  const [message, setMessage] = useState("");

  const [name, setName] = useState("");
  const [password, setPassword] = useState("");
  const [registering, setRegistering] = useState(false);
  const [registered, setRegistered] = useState(false);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [accepting, setAccepting] = useState(false);
  const [result, setResult] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!token) {
      setStatus("invalid");
      setMessage("El enlace no trae ningún código de invitación.");
      return;
    }
    setStatus("loading");
    try {
      setInvitation(await api.invitation(token));
      setStatus("ready");
    } catch (error) {
      if (error instanceof ApiError && error.status === 404) {
        setStatus("invalid");
        setMessage(error.message);
        return;
      }
      setStatus("error");
      setMessage(error instanceof Error ? error.message : "No pudimos leer la invitación.");
    }
  }, [token]);

  useEffect(() => {
    void load();
  }, [load]);

  async function submitRegister(event: FormEvent) {
    event.preventDefault();
    setRegistering(true);
    setMessage("");
    try {
      await api.registerFromInvitation(token, { name: name.trim(), password });
      setRegistered(true);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "No pudimos crear la cuenta.");
    } finally {
      setRegistering(false);
    }
  }

  async function accept() {
    setAccepting(true);
    try {
      const data = await api.acceptInvitation(token);
      setConfirmOpen(false);
      setResult(
        data.alreadyAccepted
          ? "Esta invitación ya estaba aceptada. No se creó ninguna membresía nueva."
          : `Listo: ya integrás ${invitation?.orgName}.`,
      );
      await load();
    } catch (error) {
      setConfirmOpen(false);
      setMessage(error instanceof Error ? error.message : "No pudimos aceptar la invitación.");
    } finally {
      setAccepting(false);
    }
  }

  if (status === "loading") {
    return (
      <AuthLayout>
        <Card title="Invitación">
          <StateBlock kind="loading" title="Leyendo la invitación…" />
        </Card>
      </AuthLayout>
    );
  }

  if (status === "invalid") {
    return (
      <AuthLayout>
        <Card title="Invitación no válida" subtitle={message}>
          <p className="muted">Si te compartieron el enlace por correo, revisá que esté completo.</p>
          <Link className="btn btn--ghost" to="/login">
            Ir al ingreso
          </Link>
        </Card>
      </AuthLayout>
    );
  }

  if (status === "error" || !invitation) {
    return (
      <AuthLayout>
        <Card title="No pudimos leer la invitación" subtitle={message}>
          <Button onClick={() => void load()}>Reintentar</Button>
        </Card>
      </AuthLayout>
    );
  }

  const closed = invitation.status !== "pending";

  return (
    <AuthLayout>
      <Card
        title={`Invitación a ${invitation.orgName}`}
        subtitle={
          <>
            Para {invitation.email} · rol {invitation.roleLabel} ·{" "}
            <Badge tone={invitation.status === "pending" ? "warn" : invitation.status === "accepted" ? "ok" : "danger"}>
              {INVITATION_STATUS_LABEL[invitation.status]}
            </Badge>
          </>
        }
      >
        {result && <Alert variant="success">{result}</Alert>}
        {message && !result && <Alert variant="error">{message}</Alert>}

        {invitation.status === "expired" && (
          <p>
            La invitación venció el {shortDate(invitation.expiresAt)}. Pedile a {invitation.invitedByName ?? "quien te invitó"}{" "}
            que emita una nueva.
          </p>
        )}
        {invitation.status === "cancelled" && (
          <p>La organización canceló esta invitación. Si creés que es un error, pedí que la vuelvan a emitir.</p>
        )}
        {invitation.status === "accepted" && !result && (
          <p>Esta invitación ya fue aceptada. No hace falta hacer nada más: la membresía ya existe.</p>
        )}

        {!closed && invitation.next === "register" && !registered && (
          <form className="form" onSubmit={submitRegister} noValidate>
            <p className="muted">
              Todavía no hay una identidad para {invitation.email}. Creála acá: no se crea ninguna organización, sólo tu
              identidad.
            </p>
            <Field label="Tu nombre" value={name} onChange={(event) => setName(event.target.value)} required />
            <Field
              label="Contraseña"
              type="password"
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              hint="Mínimo 4 caracteres."
              hintMuted
              required
            />
            <Button type="submit" loading={registering}>
              Crear mi identidad
            </Button>
          </form>
        )}

        {!closed && invitation.next === "register" && registered && (
          <Alert variant="success">
            Te mandamos un correo de confirmación. Abrilo desde la bandeja simulada del panel de demostración, confirmá y
            después ingresá para aceptar la invitación.
          </Alert>
        )}

        {!closed && invitation.next === "confirm" && (
          <p>
            Falta confirmar el correo de {invitation.email}. Abrí la bandeja simulada en el panel de demostración, seguí el
            enlace de confirmación y volvé a esta pantalla. La membresía no se activa antes de eso.
          </p>
        )}

        {!closed && invitation.next === "login" && (
          <>
            <p>Ya existe una identidad para {invitation.email}. Ingresá con ella para aceptar la invitación.</p>
            <Link className="btn btn--primary" to={`/login?next=${encodeURIComponent(`/invitacion?token=${token}`)}`}>
              Ingresar
            </Link>
          </>
        )}

        {!closed && invitation.next === "other-identity" && (
          <>
            <p>
              Estás en la sesión de {invitation.viewerEmail}, pero la invitación es para {invitation.email}. No se puede
              aceptar en nombre de otra persona: cerrá sesión e ingresá con esa identidad.
            </p>
            <Button
              variant="ghost"
              onClick={async () => {
                await api.logout();
                navigate(`/login?next=${encodeURIComponent(`/invitacion?token=${token}`)}`);
              }}
            >
              Cerrar sesión
            </Button>
          </>
        )}

        {!closed && invitation.next === "accept" && !result && (
          <>
            <p>
              Vas a sumarte a {invitation.orgName} como {invitation.roleLabel}, con tu identidad {invitation.viewerEmail}.
            </p>
            <Button onClick={() => setConfirmOpen(true)}>Aceptar invitación</Button>
          </>
        )}

        {result && (
          <Button variant="ghost" onClick={() => navigate("/app")}>
            Ir a la aplicación
          </Button>
        )}
      </Card>

      {confirmOpen && (
        <Modal
          title="Aceptar la invitación"
          description={`Vas a integrar ${invitation.orgName} como ${invitation.roleLabel}. Podés operar con esta organización y seguir usando las demás.`}
          onClose={() => setConfirmOpen(false)}
          footer={
            <>
              <Button variant="ghost" className="btn--inline" onClick={() => setConfirmOpen(false)} disabled={accepting}>
                Volver
              </Button>
              <Button className="btn--inline" onClick={() => void accept()} loading={accepting}>
                Aceptar
              </Button>
            </>
          }
        />
      )}
    </AuthLayout>
  );
}
