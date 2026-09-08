import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { AuthLayout, Card, Field, Button, Alert } from '../components/ui.jsx';
import { api } from '../lib/api.js';
import { normalize, kind, isValid, format } from '../lib/cuit.js';

const LABELS = {
  persona: 'Nombre y apellido',
  empresa: 'Razón social',
  null: 'Nombre o razón social'
};

export default function Registro() {
  const nav = useNavigate();
  const [cuit, setCuit] = useState('');
  const [name, setName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [errors, setErrors] = useState({});
  const [alert, setAlert] = useState(null);
  const [loading, setLoading] = useState(false);

  const normalized = normalize(cuit);
  const valid = isValid(cuit);
  const tipo = valid ? kind(normalized) : null;
  const derived = valid ? (tipo === 'persona' ? 'Persona física' : 'Empresa') : null;

  function blurCuit() {
    if (valid) {
      setCuit(format(normalized));
      setErrors((p) => ({ ...p, cuit: undefined }));
    } else if (cuit.trim()) {
      setErrors((p) => ({ ...p, cuit: 'Revisá el CUIT: el número no es válido.' }));
    }
  }

  async function submit(e) {
    e.preventDefault();
    const next = {};
    if (!valid) next.cuit = 'Revisá el CUIT: el número no es válido.';
    if (!name.trim()) next.name = 'Escribí el nombre.';
    if (!/^\S+@\S+\.\S+$/.test(email)) next.email = 'Escribí un correo válido.';
    if (password.length < 12) next.password = 'La contraseña necesita 12 caracteres o más.';
    setErrors(next);
    if (Object.keys(next).length) return;

    setAlert(null);
    setLoading(true);
    try {
      await api.register({ cuit: normalized, name: name.trim(), email, password });
      nav('/registro/enviado', { state: { email } });
    } catch (err) {
      if (err.code === 'cuit_taken') setAlert('cuit_taken');
      else if (err.code === 'invalid_cuit') setErrors({ cuit: 'Revisá el CUIT: el número no es válido.' });
      else setAlert(err.message);
    } finally {
      setLoading(false);
    }
  }

  return (
    <AuthLayout>
      <Card title="Creá tu cuenta" lede="Con tu CUIT sabemos si sos persona o empresa.">
        {alert === 'cuit_taken' ? (
          <Alert>
            Ese CUIT ya está registrado. Si es tuyo, <Link to="/login">iniciá sesión</Link>.
          </Alert>
        ) : (
          <Alert>{alert}</Alert>
        )}
        <form onSubmit={submit} noValidate>
          <div className="fields">
            <Field
              id="cuit"
              label="CUIT"
              inputMode="numeric"
              placeholder="20-12345678-9"
              autoFocus
              value={cuit}
              disabled={loading}
              error={errors.cuit}
              derived={derived}
              onChange={(e) => {
                setCuit(e.target.value);
                if (errors.cuit) setErrors((p) => ({ ...p, cuit: undefined }));
              }}
              onBlur={blurCuit}
            />
            <Field
              id="name"
              label={LABELS[tipo ?? 'null']}
              value={name}
              disabled={loading}
              error={errors.name}
              onChange={(e) => setName(e.target.value)}
            />
            <Field
              id="email"
              label="Correo electrónico"
              type="email"
              autoComplete="email"
              value={email}
              disabled={loading}
              error={errors.email}
              onChange={(e) => setEmail(e.target.value)}
            />
            <Field
              id="password"
              label="Contraseña"
              type="password"
              autoComplete="new-password"
              value={password}
              disabled={loading}
              error={errors.password}
              hint="Mínimo 12 caracteres."
              onChange={(e) => setPassword(e.target.value)}
            />
          </div>
          <div className="actions">
            <Button type="submit" loading={loading}>
              Crear cuenta
            </Button>
          </div>
        </form>
        <div className="foot">
          ¿Ya tenés cuenta? <Link to="/login">Iniciá sesión</Link>
        </div>
      </Card>
    </AuthLayout>
  );
}
