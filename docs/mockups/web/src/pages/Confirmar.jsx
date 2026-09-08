import { useEffect, useRef, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { AuthLayout, Card, Button, Spinner } from '../components/ui.jsx';
import { api } from '../lib/api.js';

export default function Confirmar() {
  const nav = useNavigate();
  const [params] = useSearchParams();
  const token = params.get('token');
  const [state, setState] = useState(token ? 'loading' : 'missing');
  const ran = useRef(false);

  useEffect(() => {
    if (!token || ran.current) return;
    ran.current = true;
    api
      .confirm(token)
      .then(() => {
        setState('ok');
        window.history.replaceState(null, '', '/confirmar');
      })
      .catch(() => setState('invalid'));
  }, [token]);

  if (state === 'loading')
    return (
      <AuthLayout>
        <Card title="Confirmando tu correo">
          <Spinner dark />
        </Card>
      </AuthLayout>
    );

  if (state === 'ok')
    return (
      <AuthLayout>
        <Card title="Listo" lede="Tu correo quedó confirmado. Ya podés iniciar sesión.">
          <div className="actions">
            <Button onClick={() => nav('/login')}>Iniciar sesión</Button>
          </div>
        </Card>
      </AuthLayout>
    );

  if (state === 'invalid')
    return (
      <AuthLayout>
        <Card
          title="Este enlace no sirve"
          lede="Puede haber vencido o ya haberse usado. Si todavía no confirmaste, registrate de nuevo con el mismo correo."
        >
          <div className="actions">
            <Button variant="ghost" onClick={() => nav('/registro')}>
              Ir al registro
            </Button>
          </div>
        </Card>
      </AuthLayout>
    );

  return (
    <AuthLayout>
      <Card title="Falta el enlace" lede="Abrí esta página desde el enlace que te mandamos por correo.">
        <div className="actions">
          <Button variant="ghost" onClick={() => nav('/login')}>
            Volver
          </Button>
        </div>
      </Card>
    </AuthLayout>
  );
}
