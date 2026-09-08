import { useCallback, useEffect, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { Alert, AuthLayout, Badge, Button, Card, StateBlock } from "../../components";
import { api, ApiError, type InvitationView } from "../../lib/api";
import { INVITATION_STATUS_LABEL } from "../../lib/format";
import { errorMessage } from "../../lib/session";

/**
 * Invitación bootstrap de Platform. Si venció o falló, la recuperación es
 * neutral: no se elige email ni identidad y la respuesta es siempre la misma.
 */
export function PlatformInvitationPage() {
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const token = params.get("token") ?? "";

  const [invitation, setInvitation] = useState<InvitationView | null>(null);
  const [status, setStatus] = useState<"loading" | "ready" | "invalid">("loading");
  const [message, setMessage] = useState("");
  const [recovered, setRecovered] = useState("");
  const [working, setWorking] = useState(false);

  const load = useCallback(async () => {
    if (!token) {
      setStatus("invalid");
      setMessage("El enlace no trae ningún código de invitación.");
      return;
    }
    try {
      setInvitation(await api.invitation(token));
      setStatus("ready");
    } catch (error) {
      setStatus("invalid");
      setMessage(error instanceof ApiError ? error.message : "No pudimos leer la invitación.");
    }
  }, [token]);

  useEffect(() => {
    void load();
  }, [load]);

  async function recover() {
    setWorking(true);
    try {
      const data = await api.bootstrapRecover();
      setRecovered(data.message);
    } catch (error) {
      setRecovered(errorMessage(error));
    } finally {
      setWorking(false);
    }
  }

  async function accept() {
    setWorking(true);
    setMessage("");
    try {
      await api.acceptPlatformInvitation(token);
      navigate("/platform/mfa", { replace: true });
    } catch (error) {
      setMessage(errorMessage(error));
    } finally {
      setWorking(false);
    }
  }

  if (status === "loading") {
    return (
      <AuthLayout>
        <Card title="Invitación de Platform">
          <StateBlock kind="loading" title="Leyendo la invitación…" />
        </Card>
      </AuthLayout>
    );
  }

  const closed = status === "invalid" || (invitation && invitation.status !== "pending");

  return (
    <AuthLayout>
      <Card
        title="Invitación para administrar Platform"
        subtitle={
          invitation ? (
            <>
              Para {invitation.email} ·{" "}
              <Badge tone={invitation.status === "pending" ? "warn" : "danger"}>
                {INVITATION_STATUS_LABEL[invitation.status]}
              </Badge>
            </>
          ) : (
            message
          )
        }
      >
        {message && invitation && <Alert variant="error">{message}</Alert>}

        {invitation && invitation.status === "pending" && (
          <>
            <p>
              Al aceptar vas a tener que completar la secuencia de MFA simulada. La membresía de Platform no se activa
              antes de eso.
            </p>
            {invitation.next === "login" || invitation.next === "register" ? (
              <>
                <p className="muted">Primero ingresá con la identidad {invitation.email}.</p>
                <Link className="btn btn--primary" to={`/login?next=${encodeURIComponent(`/platform/invitacion?token=${token}`)}`}>
                  Ingresar
                </Link>
              </>
            ) : (
              <Button onClick={() => void accept()} loading={working}>
                Aceptar y continuar
              </Button>
            )}
          </>
        )}

        {closed && (
          <>
            <p>
              {status === "invalid"
                ? "El enlace no es válido."
                : "Esta invitación bootstrap ya no está vigente."}{" "}
              Podés pedir que se reintente el alta: la respuesta es siempre la misma y no informa si existe una identidad
              asociada.
            </p>
            {recovered ? (
              <Alert variant="info">{recovered}</Alert>
            ) : (
              <Button variant="ghost" onClick={() => void recover()} loading={working}>
                Pedir recuperación
              </Button>
            )}
          </>
        )}
      </Card>
    </AuthLayout>
  );
}
