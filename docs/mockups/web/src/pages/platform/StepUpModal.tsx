import { useState } from "react";
import { Alert, Button, Field, Modal } from "../../components";
import { api } from "../../lib/api";
import { errorMessage } from "../../lib/session";

/**
 * Revalidación MFA simulada antes de una mutación sensible. No hay criptografía:
 * el código de la simulación es fijo y está a la vista.
 */
export function StepUpModal({ onClose, onDone }: { onClose: () => void; onDone: () => void }) {
  const [code, setCode] = useState("");
  const [error, setError] = useState("");
  const [working, setWorking] = useState(false);

  async function submit() {
    setWorking(true);
    setError("");
    try {
      await api.stepUp(code.trim());
      onDone();
    } catch (caught) {
      setError(errorMessage(caught));
    } finally {
      setWorking(false);
    }
  }

  return (
    <Modal
      title="Revalidar MFA (simulada)"
      description="La última verificación venció. Volvé a validar para poder ejecutar la operación."
      onClose={onClose}
      footer={
        <>
          <Button variant="ghost" className="btn--inline" onClick={onClose} disabled={working}>
            Cancelar
          </Button>
          <Button className="btn--inline" onClick={() => void submit()} loading={working}>
            Revalidar
          </Button>
        </>
      }
    >
      <div className="form">
        <p className="sim-note">
          <strong>Simulación.</strong> No hay TOTP real: el código válido es <code>123456</code>.
        </p>
        <Field
          label="Código de 6 dígitos"
          value={code}
          onChange={(event) => setCode(event.target.value)}
          inputMode="numeric"
          placeholder="123456"
          autoFocus
        />
        {error && <Alert variant="error">{error}</Alert>}
      </div>
    </Modal>
  );
}
