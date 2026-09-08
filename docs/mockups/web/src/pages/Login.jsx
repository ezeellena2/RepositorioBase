import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { AuthLayout, Card, Field, Button, Alert } from '../components/ui.jsx';
import { api } from '../lib/api.js';

export default function Login() {
  const nav = useNavigate();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [errors, setErrors] = useState({});
  const [alert, setAlert] = useState(null);
  const [loading, setLoading] = useState(false);

  async function submit(e) {
    e.preventDefault();
    const next = {};
    if (!/^\S+@\S+\.\S+$/.test(email)) next.email = 'Escribí un correo válido.';
    if (!password) next.password = 'Escribí tu contraseña.';
    setErrors(next);
    if (Object.keys(next).length) return;

    setAlert(null);
    setLoading(true);
    try {
      await api.login({ email, password });
      nav('/app');
    } catch (err) {
      setAlert(err.message);
      setPassword('');
      document.getElementById('password')?.focus();
    } finally {
      setLoading(false);
    }
  }

  return (
    <AuthLayout>
      <Card title="Iniciá sesión">
        <Alert>{alert}</Alert>
        <form onSubmit={submit} noValidate>
          <div className="fields">
            <Field
              id="email"
              label="Correo electrónico"
              type="email"
              autoComplete="username"
              autoFocus
              value={email}
              disabled={loading}
              error={errors.email}
              onChange={(e) => setEmail(e.target.value)}
            />
            <Field
              id="password"
              label="Contraseña"
              type="password"
              autoComplete="current-password"
              value={password}
              disabled={loading}
              error={errors.password}
              onChange={(e) => setPassword(e.target.value)}
            />
          </div>
          <div className="actions">
            <Button type="submit" loading={loading}>
              Entrar
            </Button>
          </div>
        </form>
        <div className="foot">
          ¿No tenés cuenta? <Link to="/registro">Registrate</Link>
        </div>
      </Card>
    </AuthLayout>
  );
}
