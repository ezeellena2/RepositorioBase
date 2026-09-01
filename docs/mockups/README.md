# Sistema de ejemplo

Prototipo navegable de cómo se ve y cómo funciona el producto. No es el proyecto real: no comparte código con `src/` y no tiene base de datos. Sirve como referencia visual y funcional para construir la versión definitiva (.NET + React).

## Cómo levantarlo

```bash
cd docs/mockups
npm install
npm run dev
```

Abre la web en `http://localhost:5173` y la API en `http://localhost:4000`. La web proxea `/api` hacia la API.

## Estructura

| Carpeta | Qué es |
|---|---|
| `web/` | Vite + React + TypeScript. CSS propio, sin librerías de UI. |
| `api/` | Node + Express. Datos en memoria; se reinician al reiniciar el proceso o desde "Reiniciar datos". |

## Qué muestra hoy

1. Inicio de sesión.
2. Registro con CUIT, nombre, correo y contraseña. El tipo de cuenta (persona física o empresa) se deduce del prefijo del CUIT y no se pregunta.
3. Pantalla "Revisá tu correo" tras registrarse. Hasta que confirma, la persona no puede iniciar sesión.
4. Bandeja de correo de demostración (`/correo`) donde aparece el correo con el enlace de confirmación.
5. Confirmación del correo y vuelta al inicio de sesión.
6. Elección de espacio cuando la cuenta pertenece a más de una organización o tiene espacio personal más organizaciones. Con un solo espacio entra directo.
7. Inicio con cambio de espacio desde el menú superior y cierre de sesión.

## Cuentas de prueba

| Correo | Contraseña | Espacios |
|---|---|---|
| `ana@ejemplo.com` | `Demo1234` | Espacio personal, Estudio Pereyra y Asociados, Distribuidora del Litoral S.A. |
| `martin@ejemplo.com` | `Demo1234` | Solo espacio personal |

Para probar el registro completo se puede usar cualquier CUIT válido, por ejemplo `20-12345678-6` (persona) o `30-70123456-8` (empresa).

## Endpoints de la API de ejemplo

| Método y ruta | Resultado |
|---|---|
| `POST /api/identity/register` | `202` neutral; envía correo de confirmación o aviso de cuenta existente |
| `POST /api/identity/resend-confirmation` | `202` |
| `POST /api/identity/confirm-email` | `204`; errores `invalid_token`, `expired_token` |
| `POST /api/identity/sessions` | `204` + cookie; `401 invalid_credentials`, `403 email_not_confirmed` |
| `DELETE /api/identity/sessions/current` | `204` |
| `GET /api/identity/context` | Usuario, espacio activo, espacios disponibles, permisos |
| `PUT /api/identity/context/tenant` | Cambia el espacio activo |
| `GET /api/demo/mailbox` | Correos enviados (solo demostración) |
| `POST /api/demo/reset` | Vuelve a los datos iniciales |

Los errores se devuelven como `application/problem+json` con un `code` estable, siguiendo el contrato descrito en `docs/features/identity-access/SPEC.md`.
