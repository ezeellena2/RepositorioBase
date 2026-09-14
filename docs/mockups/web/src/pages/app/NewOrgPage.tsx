import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { Alert, Button, PageHeader } from "../../components";
import { CompanyForm } from "../../components/onboarding/CompanyForm";
import { useSession } from "../../lib/session";

/** Crear una segunda organización desde una identidad ya autenticada. */
export function NewOrgPage() {
  const navigate = useNavigate();
  const { reload } = useSession();
  const [created, setCreated] = useState<string | null>(null);

  if (created) {
    return (
      <>
        <PageHeader title="Organización creada" description={`${created} ya está disponible en tu selector.`} />
        <Alert variant="success">
          Quedaste como Titular de {created} y el contexto activo ya cambió a esa organización. Podés volver a la anterior
          cuando quieras desde el selector del menú lateral.
        </Alert>
        <div className="panel__actions">
          <Button className="btn--inline" onClick={() => navigate("/app")}>
            Ir al inicio
          </Button>
        </div>
      </>
    );
  }

  return (
    <>
      <PageHeader
        title="Crear organización"
        description="Se crea con tu identidad actual. Vas a quedar como Titular y podés seguir operando con las demás."
      />
      <div className="form--narrow">
        <CompanyForm
          submitLabel="Crear organización"
          onCreated={(next) => {
            setCreated(next.activeOrg?.name ?? "La organización");
            void reload();
          }}
        />
      </div>
    </>
  );
}
