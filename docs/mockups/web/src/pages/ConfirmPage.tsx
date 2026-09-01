import { useEffect, useRef, useState } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import { AuthLayout, Button, Card, Spinner } from "../components";
import { api } from "../lib/api";

type Status = "working" | "done" | "invalid" | "missing";

interface ConfirmState {
  confirmed?: boolean;
}

export function ConfirmPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const token = new URLSearchParams(location.search).get("token");
  // Al confirmar, el token sale de la URL y el resultado queda en el state del
  // historial (history.replaceState). Así una recarga sigue mostrando "Listo".
  const alreadyConfirmed = Boolean((location.state as ConfirmState | null)?.confirmed);

  const [status, setStatus] = useState<Status>(() => {
    if (alreadyConfirmed) return "done";
    return token ? "working" : "missing";
  });
  const started = useRef(false);

  useEffect(() => {
    if (!token || started.current) return;
    started.current = true;
    api
      .confirm(token)
      .then(() => {
        setStatus("done");
        navigate("/confirmar", { replace: true, state: { confirmed: true } satisfies ConfirmState });
      })
      .catch(() => setStatus("invalid"));
  }, [token, navigate]);

  if (status === "working") {
    return (
      <AuthLayout>
        <Card title="Confirmando tu correo">
          <div className="card__spinner">
            <Spinner />
          </div>
        </Card>
      </AuthLayout>
    );
  }

  if (status === "done") {
    return (
      <AuthLayout>
        <Card title="Listo" subtitle="Tu correo quedó confirmado. Ya podés iniciar sesión.">
          <Button onClick={() => navigate("/login")}>Iniciar sesión</Button>
        </Card>
      </AuthLayout>
    );
  }

  if (status === "invalid") {
    return (
      <AuthLayout>
        <Card
          title="Este enlace no sirve"
          subtitle="Puede haber vencido o ya haberse usado. Si todavía no confirmaste, registrate de nuevo con el mismo correo."
        >
          <Button variant="ghost" onClick={() => navigate("/registro")}>
            Ir al registro
          </Button>
        </Card>
      </AuthLayout>
    );
  }

  return (
    <AuthLayout>
      <Card title="Falta el enlace" subtitle="Abrí esta página desde el enlace que te mandamos por correo.">
        <Button variant="ghost" onClick={() => navigate("/login")}>
          Volver
        </Button>
      </Card>
    </AuthLayout>
  );
}
