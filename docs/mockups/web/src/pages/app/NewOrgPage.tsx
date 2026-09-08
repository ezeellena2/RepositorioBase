import { useState, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import { Alert, Button, Field, PageHeader } from "../../components";
import { api } from "../../lib/api";
import { parse } from "../../lib/cuit";
import { errorMessage, useSession } from "../../lib/session";

/** Crear una segunda organización desde una identidad ya autenticada. */
export function NewOrgPage() {
  const navigate = useNavigate();
  const { reload } = useSession();
  const [cuit, setCuit] = useState("");
  const [name, setName] = useState("");
  const [errors, setErrors] = useState<{ cuit?: string; name?: string }>({});
  const [generalError, setGeneralError] = useState("");
  const [saving, setSaving] = useState(false);
  const [created, setCreated] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    const nextErrors: { cuit?: string; name?: string } = {};
    if (!parse(cuit)) nextErrors.cuit = "El CUIT tiene que tener 11 dígitos y un dígito verificador válido.";
    if (!name.trim()) nextErrors.name = "Escribí el nombre o razón social.";
    setErrors(nextErrors);
    setGeneralError("");
    if (Object.keys(nextErrors).length > 0) return;

    setSaving(true);
    try {
      await api.createOrg({ cuit, name: name.trim() });
      await reload();
      setCreated(name.trim());
    } catch (error) {
      setGeneralError(errorMessage(error));
    } finally {
      setSaving(false);
    }
  }

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
      <form className="form form--narrow" onSubmit={submit} noValidate>
        <Field
          label="CUIT"
          value={cuit}
          onChange={(event) => setCuit(event.target.value)}
          error={errors.cuit}
          hint="11 dígitos. El tipo (Personal o Empresa) se deduce del prefijo."
          hintMuted
          inputMode="numeric"
          placeholder="30-71234567-1"
        />
        <Field
          label="Nombre o razón social"
          value={name}
          onChange={(event) => setName(event.target.value)}
          error={errors.name}
          placeholder="Cooperativa Del Valle"
        />
        {generalError && <Alert variant="error">{generalError}</Alert>}
        <Button type="submit" loading={saving}>
          Crear organización
        </Button>
      </form>
    </>
  );
}
