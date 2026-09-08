# Mockups

Maqueta navegable de Identity Access en React y Node. Sirve para evaluar usabilidad y maquetado
de punta a punta. **No es el producto**, no comparte código con `src/` y no debe presentarse como
espejo ni sustituto de la aplicación productiva: la seguridad (MFA, tokens, sesiones, permisos,
auditoría) es una simulación visual con estado local coherente.

```bash
cd docs/mockups
npm install
npm run dev
```

Levanta la API en `http://localhost:3001` y la web en `http://localhost:5173`. La web proxea `/api`
hacia la API. Los datos viven en memoria y se pierden al reiniciar el proceso.

## Panel de demostración

El botón **DEMO**, abajo a la derecha de cualquier pantalla, abre un panel que no forma parte del
producto. Tiene tres solapas:

- **Escenarios**: 24 puntos de partida agrupados por recorrido. Cargar uno reinicia los datos en
  memoria, deja la sesión en el estado que corresponde y navega a la pantalla inicial.
- **Correos**: bandeja simulada con los mensajes de registro, confirmación e invitación. Cada uno
  tiene su enlace utilizable dentro de la maqueta.
- **Estados**: arma fallas para el próximo pedido (error de red, conflicto 409, no encontrado 404),
  revoca la sesión, vence el step-up de MFA y reinicia los datos del mockup.

Platform se abre desde acá a propósito: no se enlaza desde el menú del cliente.

## Recorridos

- **A — Registro, confirmación y sesión**: alta de organización con validación de CUIT, resultado
  neutral, confirmación por enlace, ingreso con error de credenciales y bloqueo 429, sesión vencida,
  y entrada según cantidad de membresías.
- **B — Contexto, permisos y organizaciones**: shell con menú lateral colapsable, selector de
  organización, barra de contexto, aislamiento de permisos entre organizaciones, alta de una segunda
  organización y organización suspendida.
- **C — Invitación y aceptación**: emisión con roles permitidos, alta de una invitada nueva,
  identidad existente, y los casos de invitación vencida, cancelada, usada y aceptación idempotente.
- **D — Platform**: contexto operativo separado, invitación bootstrap con recuperación neutral,
  secuencia de MFA simulada, directorios con paginación por cursor, y operaciones administrativas
  con confirmación y step-up.

## Identidades de ejemplo

Contraseña `1234` en todas.

| Correo | Para qué sirve |
| --- | --- |
| `juan@acme.com` | Dos organizaciones: Titular en Acme S.A. (puede invitar) e Integrante en Distribuidora Sur SRL (no puede) |
| `maria@acme.com` | Una sola organización, permisos limitados |
| `bruno@sur.com` | Titular de varias, una de ellas suspendida |
| `sofia@sinorg.com` | Identidad sin organizaciones |
| `nuevo@sur.com` | Registrada sin confirmar el correo |
| `paula@nueva.com` | Invitada con cuenta sin confirmar |
| `carla@plataforma.com` | Titular de Platform con MFA simulada completa |
| `diego@plataforma.com` | Administradora de Platform sin step-up reciente |
| `elena@plataforma.com` | Invitación bootstrap de Platform vencida |

En la simulación de MFA el código válido es `123456`.

El sistema visual está en `web/src/styles/` y los componentes base en `web/src/components/`.
