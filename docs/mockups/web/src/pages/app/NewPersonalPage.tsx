import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { Alert, Button, PageHeader, StateBlock } from "../../components";
import { PersonalForm } from "../../components/onboarding/PersonalForm";
import { useSession } from "../../lib/session";

/** Agregar la cuenta personal a una identidad que ya opera con empresas. */
export function NewPersonalPage() {
  const navigate = useNavigate();
  const { me, reload } = useSession();
  const [created, setCreated] = useState<string | null>(null);

  if (!me) return null;

  if (created) {
    return (
      <>
        <PageHeader title="Cuenta personal creada" description={`${created} ya está en tu selector.`} />
        <Alert variant="success">
          El contexto activo ya cambió a tu cuenta personal. Podés volver a tus empresas desde el selector del menú lateral.
        </Alert>
        <div className="panel__actions">
          <Button className="btn--inline" onClick={() => navigate("/app")}>
            Ir al inicio
          </Button>
        </div>
      </>
    );
  }

  const existing = me.orgs.find((org) => org.type === "persona");
  if (existing) {
    return (
      <StateBlock
        kind="empty"
        title="Ya tenés una cuenta personal"
        description={`${existing.name} ya está en tu selector. Una identidad tiene a lo sumo una.`}
        action={<Button onClick={() => navigate("/app")}>Ir al inicio</Button>}
      />
    );
  }

  return (
    <>
      <PageHeader
        title="Agregar cuenta personal"
        description="Se agrega a tu identidad actual. Seguís pudiendo operar con tus empresas."
      />
      <div className="form--narrow">
        <PersonalForm
          submitLabel="Crear cuenta personal"
          onCreated={(next) => {
            setCreated(next.activeOrg?.name ?? "Tu cuenta personal");
            void reload();
          }}
        />
      </div>
    </>
  );
}
