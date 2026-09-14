import { useState, type FormEvent } from "react";
import { Alert, Button, Field } from "../index";
import { api, ApiError, type Me } from "../../lib/api";
import { format, parse } from "../../lib/cuit";
import { errorMessage } from "../../lib/session";

const CUIT_ERROR = "Revisá el CUIT: tiene que tener 11 dígitos y un dígito verificador válido.";

interface Errors {
  name?: string;
  cuit?: string;
}

/**
 * Razón social y CUIT: crea una empresa con la identidad en sesión como titular.
 * El prefijo del CUIT sólo informa si es de una persona humana o jurídica; las dos
 * son una empresa.
 */
export function CompanyForm({ submitLabel, onCreated }: { submitLabel: string; onCreated: (me: Me) => void }) {
  const [name, setName] = useState("");
  const [cuit, setCuit] = useState("");
  const [errors, setErrors] = useState<Errors>({});
  const [formError, setFormError] = useState("");
  const [saving, setSaving] = useState(false);

  const parsed = parse(cuit);

  function handleCuitBlur() {
    if (!cuit.trim()) return;
    if (parsed) {
      setCuit(format(parsed.normalized));
      setErrors((current) => ({ ...current, cuit: undefined }));
    } else {
      setErrors((current) => ({ ...current, cuit: CUIT_ERROR }));
    }
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (saving) return;
    const next: Errors = {};
    if (!name.trim()) next.name = "Escribí la razón social.";
    if (!parsed) next.cuit = CUIT_ERROR;
    setErrors(next);
    setFormError("");
    if (Object.keys(next).length > 0 || !parsed) return;

    setSaving(true);
    try {
      onCreated(await api.createOrg({ cuit: parsed.normalized, name: name.trim() }));
    } catch (error) {
      if (error instanceof ApiError && error.code === "invalid_cuit") setErrors({ cuit: CUIT_ERROR });
      else setFormError(errorMessage(error));
      setSaving(false);
    }
  }

  return (
    <form className="form" onSubmit={submit} noValidate>
      {formError && <Alert variant="error">{formError}</Alert>}
      <Field
        label="Razón social"
        name="name"
        autoComplete="organization"
        autoFocus
        placeholder="Cooperativa Del Valle"
        value={name}
        onChange={(event) => setName(event.target.value)}
        error={errors.name}
        disabled={saving}
      />
      <Field
        label="CUIT"
        name="cuit"
        inputMode="numeric"
        autoComplete="off"
        placeholder="30-71234567-1"
        value={cuit}
        onChange={(event) => setCuit(event.target.value)}
        onBlur={handleCuitBlur}
        hint={parsed ? (parsed.kind === "persona" ? "CUIT de una persona humana." : "CUIT de una persona jurídica.") : undefined}
        hintMuted
        error={errors.cuit}
        disabled={saving}
      />
      <Button type="submit" loading={saving}>
        {submitLabel}
      </Button>
    </form>
  );
}
