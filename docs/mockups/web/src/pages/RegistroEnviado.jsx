import { Navigate, useLocation, useNavigate } from 'react-router-dom';
import { AuthLayout, Card, Button } from '../components/ui.jsx';

export default function RegistroEnviado() {
  const nav = useNavigate();
  const { state } = useLocation();
  if (!state?.email) return <Navigate to="/registro" replace />;

  return (
    <AuthLayout>
      <Card
        title="Revisá tu correo"
        lede={`Si ${state.email} no estaba registrado, te enviamos un enlace para confirmarlo. Vence en 24 horas.`}
      >
        <div className="actions">
          <Button variant="ghost" onClick={() => nav('/login')}>
            Volver a iniciar sesión
          </Button>
        </div>
      </Card>
    </AuthLayout>
  );
}
