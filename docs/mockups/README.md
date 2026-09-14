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

- **Escenarios**: puntos de partida agrupados por recorrido. Cargar uno reinicia los datos en
  memoria, deja la sesión en el estado que corresponde y navega a la pantalla inicial.
- **Correos**: bandeja simulada con los códigos para crear cuenta y los mensajes de confirmación e
  invitación. Los que traen enlace se abren dentro de la maqueta.
- **Estados**: arma fallas para el próximo pedido (error de red, conflicto 409, no encontrado 404),
  revoca la sesión, vence el step-up de MFA y reinicia los datos del mockup.

Platform se abre desde acá a propósito: no se enlaza desde el menú del cliente.

## Entrar y crear cuenta

El flujo que se está evaluando antes de llevarlo al producto. Las decisiones y sus porqués están en
[el documento de diseño](../superpowers/specs/2026-09-14-entrar-crear-cuenta-design.md).

- **Entrar** (`/entrar`) no pregunta el tipo de cuenta: Google, o email y contraseña. Quien entra sin
  ningún contexto ve «Terminá de configurar tu cuenta» (`/configurar`).
- **Crear cuenta** (`/crear-cuenta`) pregunta primero *¿Personal o Empresa?*, después Google o email.
  Con email llega un código de 6 dígitos a DEMO › Correos; recién con el código verificado se dice si
  la dirección ya tenía cuenta. Una cuenta nueva elige contraseña; una existente pone la suya; una
  que entra sólo con Google sigue con Google. El último paso pide primero el DNI y después nombre
  completo y nombre visible, o primero el CUIT y después la razón social.
- El CUIT o el DNI van primero porque en el producto van a completar el resto desde el padrón de AFIP.
  La maqueta todavía no consulta el padrón: los campos se completan a mano.
- El código vence a los 10 minutos, admite 5 intentos y se puede reenviar cada 30 segundos.
- `/login` y `/registro` redirigen a las rutas nuevas.

La pantalla de Google es una simulación rotulada. Ofrece tres cuentas y dice qué pasa con cada una:

| Cuenta de Google | Qué pasa |
| --- | --- |
| `ana@gmail.com` | Vinculada: entra. Ya tiene cuenta personal |
| `juan@acme.com` | El email tiene cuenta con contraseña y ningún Google vinculado: no entra |
| `nadia.romero@gmail.com` | Nueva: se crea la cuenta, sin contexto |

«Usar otra cuenta» acepta cualquier email. No están en la maqueta: recuperar la contraseña y
vincular Google desde adentro de la cuenta.

## Recorridos

- **A — Entrar, crear cuenta y sesión**: crear cuenta personal o de empresa con Google o con código
  por correo, email que ya tiene cuenta, cuenta que entra sólo con Google, código incorrecto, agotado
  y vencido, Google con el email de una cuenta con contraseña, DNI ya registrado, ingreso con error
  de credenciales y bloqueo 429, sesión vencida, y entrada según cantidad de contextos.
- **B — Contexto, permisos y organizaciones**: shell con menú lateral colapsable, selector de
  organización, barra de contexto, aislamiento de permisos entre organizaciones, alta de una segunda
  organización y organización suspendida.
- **C — Invitación y aceptación**: emisión con roles permitidos, alta de una invitada nueva,
  identidad existente, y los casos de invitación vencida, cancelada, usada y aceptación idempotente.
- **D — Platform**: contexto operativo separado, invitación bootstrap con recuperación neutral,
  secuencia de MFA simulada, directorios con paginación por cursor, y operaciones administrativas
  con confirmación y step-up.
- **D — Ciclo de vida de cuentas**: estados de cuenta, detención con razón de un conjunto cerrado,
  levantamiento de la suspensión hacia el estado anterior (con reconocimiento cuando la baja fue
  propia), precondición de estado con conflicto 409, protección del último titular, e ingreso
  bloqueado de una cuenta detenida.
- **D — Retención**: política del despliegue, despliegue sin política, retenciones legales con
  recibo de una sola vez, levantamiento idempotente y confirmación, y separación entre leer y
  gestionar.

## Identidades de ejemplo

Contraseña `1234` en todas, menos `ana@gmail.com`, que entra sólo con Google.

| Correo | Para qué sirve |
| --- | --- |
| `juan@acme.com` | Dos organizaciones: Titular en Acme S.A. (puede invitar) e Integrante en Distribuidora Sur SRL (no puede) |
| `maria@acme.com` | Una sola organización, permisos limitados |
| `bruno@sur.com` | Titular de varias, una de ellas suspendida |
| `sofia@sinorg.com` | Identidad sin contexto: ni cuenta personal ni empresa |
| `ana@gmail.com` | Sólo Google, con cuenta personal (DNI 30.111.222, ya registrado) |
| `nuevo@sur.com` | Registrada sin confirmar el correo |
| `paula@nueva.com` | Invitada con cuenta sin confirmar |
| `carla@plataforma.com` | Titular de Platform con MFA simulada completa |
| `diego@plataforma.com` | Administradora de Platform sin step-up reciente |
| `elena@plataforma.com` | Invitación bootstrap de Platform vencida |
| `lucia@acme.com` | Cuenta activa cualquiera: sirve para detenerla y ver el efecto |
| `tomas@sur.com` | Cuenta detenida por Platform: reactivarla la devuelve a activa |
| `vera@cuyo.com` | Se dio de baja sola y después la detuvieron: reactivarla pide reconocerlo |
| `hugo@valle.com` | Baja propia, con una retención legal en pie |
| `cerrada@ejemplo.com` | Cuenta cerrada: estado terminal, sin transiciones ni retenciones |

En la simulación de MFA el código válido es `123456`.

## Permisos de Platform

Leer y cambiar son permisos separados, y la pantalla no ofrece lo que el servidor rechazaría:

| Rol | Permisos |
| --- | --- |
| Titular (`carla@plataforma.com`) | Ver identidades, detener y reactivar cuentas, ver la política de retención, poner y levantar retenciones |
| Administradora (`diego@plataforma.com`) | Ver identidades y ver la política de retención |

Detener una cuenta, reactivarla, poner una retención y levantarla piden además un step-up de MFA
vigente. La revalidación nunca completa la operación que interrumpió: hay que volver a pedirla.

## Lo que Platform no puede hacer con una cuenta

- No hay borrado a pedido: el borrado lo ejecuta el mantenimiento según la política, sin ruta ni
  permiso detrás. Lo que se puede es leer las reglas y detener un borrado con una retención legal.
- La razón de una detención pertenece a un conjunto cerrado, queda solamente en auditoría y no se
  devuelve en ninguna respuesta.
- Una cuenta cerrada es terminal: no hay transición de vuelta.
- Reactivar no devuelve las sesiones que la detención terminó.
- Nada lista las retenciones: el identificador se muestra una sola vez, al ponerla.

El sistema visual está en `web/src/styles/` y los componentes base en `web/src/components/`.
