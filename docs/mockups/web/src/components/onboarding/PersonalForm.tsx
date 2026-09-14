import { useState, type FormEvent } from "react";
import { Alert, Button, Field } from "../index";
import { api, ApiError, type Me } from "../../lib/api";
import { errorMessage } from "../../lib/session";

const DNI_PATTERN = /^\d{7,8}$/;

interface Errors {
  fullName?: string;
  displayName?: string;
  dni?: string;
}

/**
 * DNI, nombre completo y nombre visible: crea la cuenta personal de la identidad
 * en sesión. Lo usan el último paso de crear cuenta y el alta desde adentro.
 *
 * El DNI va primero porque en el producto va a completar el nombre desde el padrón
 * de AFIP (ARCA). La maqueta todavía no consulta nada: se completa a mano.
 */
export function PersonalForm({ submitLabel, onCreated }: { submitLabel: string; onCreated: (me: Me) => void }) {
  const [fullName, setFullName] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [dni, setDni] = useState("");
  const [errors, setErrors] = useState<Errors>({});
  const [formError, setFormError] = useState("");
  const [saving, setSaving] = useState(false);

  function validate(): Errors {
    const next: Errors = {};
    if (!fullName.trim()) next.fullName = "Escribí tu nombre completo.";
    if (!displayName.trim()) next.displayName = "Escribí cómo querés que te mostremos.";
    if (!DNI_PATTERN.test(dni.replace(/[.\s]/g, ""))) next.dni = "El DNI tiene que tener 7 u 8 dígitos.";
    return next;
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (saving) return;
    const next = validate();
    setErrors(next);
    setFormError("");
    if (Object.keys(next).length > 0) return;

    setSaving(true);
    try {
      onCreated(await api.createPersonal({ fullName: fullName.trim(), displayName: displayName.trim(), dni }));
    } catch (error) {
      if (error instanceof ApiError && error.code === "invalid_dni") setErrors({ dni: error.message });
      else setFormError(errorMessage(error));
      setSaving(false);
    }
  }

  return (
    <form className="form" onSubmit={submit} noValidate>
      {formError && <Alert variant="error">{formError}</Alert>}
      <Field
        label="DNI"
        name="dni"
        inputMode="numeric"
        autoComplete="off"
        autoFocus
        placeholder="30.111.222"
        value={dni}
        onChange={(event) => setDni(event.target.value)}
        error={errors.dni}
        disabled={saving}
      />
      <p className="sim-note">Todavía no: en el producto, el DNI va a completar tu nombre desde el padrón de AFIP.</p>
      <Field
        label="Nombre completo"
        name="fullName"
        autoComplete="name"
        value={fullName}
        onChange={(event) => setFullName(event.target.value)}
        error={errors.fullName}
        disabled={saving}
      />
      <Field
        label="Nombre visible"
        name="displayName"
        autoComplete="nickname"
        value={displayName}
        onChange={(event) => setDisplayName(event.target.value)}
        hint="Así te vamos a mostrar en la app."
        hintMuted
        error={errors.displayName}
        disabled={saving}
      />
      <Button type="submit" loading={saving}>
        {submitLabel}
      </Button>
    </form>
  );
}
