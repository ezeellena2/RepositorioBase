# Prompt: el shell de la aplicación

> Pegá todo lo que sigue en un chat nuevo, con este repositorio abierto.
> Está escrito para que se pueda ejecutar sin volver a preguntar nada.

---

## Qué vas a construir

La pantalla que ve una persona **después** de iniciar sesión: el marco permanente de la
aplicación. Sidebar izquierdo con hamburguesa, header con perfil arriba a la derecha,
selector de contexto, y una pantalla de Inicio adentro.

Palabras del dueño del producto, textuales:

> "Una vez que ya estoy adentro de la app, me pone hola, Juan, estás operando con Juan Pérez
> y un combo desplegable que puedo seleccionar para trabajar con Acme S.A. Me gustaría ver
> esta pantalla, como típica pantalla de cualquier aplicación web que existe hoy en día, que
> tiene un menú lateral izquierdo con la hamburguesa para cerrar y abrirlo, y el header en la
> parte superior derecha, la parte del perfil para poder seleccionar. Entonces podemos tener
> ya un pantallazo general de cómo se va a ver la web."

Este diseño ya pasó por un panel de tres propuestas y tres jueces. **Las decisiones de abajo
están tomadas y argumentadas: no las revises, ejecutalas.** Si encontrás un error de hecho,
decilo antes de construir; si es cuestión de gusto, seguí el documento.

---

## Dónde trabajás

Solamente dentro de `docs/mockups/`. Es un sistema de ejemplo — React + Vite + Express con
datos en memoria — que existe para ver cómo se va a ver y funcionar el producto antes de
construirlo en .NET y React de verdad.

**No toques** `src/`, `tests/`, ni nada en la raíz del repositorio. **No hagas commit de
nada** salvo que te lo pidan explícitamente.

Levantar el proyecto:

```bash
cd docs/mockups && npm run install:all && npm run dev
```

API en `:3001`, web en `:5173`. Hay un recorrido guiado con Playwright en `demo.mjs`
(`npm run demo`, o `FAST=1 node demo.mjs` para verificarlo sin ventana).

---

## Leé esto antes de escribir una línea

| Archivo | Por qué |
|---|---|
| `docs/mockups/web/src/styles/tokens.css` | los design tokens que ya existen |
| `docs/mockups/web/src/styles/base.css` | los estilos de auth: el lenguaje visual a continuar |
| `docs/mockups/web/src/components/ui.jsx` | el kit actual: `AuthLayout`, `Card`, `Field`, `Button`, `Alert`, `Spinner` |
| `docs/mockups/web/src/pages/App.jsx` | la pantalla post-login actual — **es la que vas a reemplazar** |
| `docs/mockups/api/src/server.js` | la API de ejemplo y su seed |

Restricciones duras: **CSS plano**. Sin Tailwind, sin librerías de componentes, sin
CSS-in-JS, sin librerías de iconos. Los iconos son SVG inline escritos a mano, `viewBox="0 0 20 20"`,
`stroke-width` 1.6, `stroke="currentColor"`, `fill="none"`.

---

## Contexto de producto

SaaS B2B multitenant argentino: un asistente de WhatsApp sobre módulos de negocio
(facturación contra ARCA, turnos, avisos). Tres actores — operador de plataforma,
administrador de organización, y miembro.

**El problema central de UX.** Una misma persona opera en más de un contexto. Juan Pérez
trabaja en Acme S.A. (empresa, CUIT 30-71234567-1) y además tiene su monotributo
("Juan Pérez", persona física, CUIT 20-33444555-1). Son dos CUIT, dos responsabilidades
fiscales. Si factura con el equivocado, el error es fiscal, no cosmético.

Todo el shell existe para que eso no pase.

---

## Hechos verificados del código actual

Verificados línea por línea. No los des por sentado de otra forma:

- `base.css:16` aplica `background-image: var(--bg-glow)` **al `body`**. El degradado radial
  está anclado a `50% 0%`, o sea justo detrás de donde va el header.
- `base.css:171-172` define `.btn { width: 100% }`. Cualquier botón del shell que no sea de
  ancho completo se va a resolver con un estilo suelto si no lo prevenís.
- `base.css:138` tiene `#f7f9fa` hardcodeado y `base.css:294` tiene `#fafcfc`. Dos grises
  casi iguales, ninguno con nombre.
- `base.css:118, 183, 288` repiten `120ms ease` a mano.
- `.btn-text` (`base.css:211`) sí tiene ancho automático.
- `contextDto` (`server.js:48-56`) devuelve `tenants: [{id, type, name}]` — **sin CUIT y sin
  rol** — y `activeTenant` sin rol y sin permisos.
- Las membresías del seed (`server.js:31-35`) **sí** tienen `role` (`owner` / `member`), pero
  `contextDto` no lo expone.
- `kind()` en `api/src/cuit.js` deriva el tipo del prefijo del CUIT: 20/23/24/25/26/27 →
  persona, 30/33/34 → empresa. **Nunca puede devolver "plataforma".** El registro crea el
  tenant con `type: kind(n)`.

---

## Paso 0 — Higiene, antes de maquetar nada

Cuatro arreglos al CSS que ya existe. Hacelos primero: los cuatro son más caros después.

1. **Mové `background-image: var(--bg-glow)` del `body` a `.auth`.** El glow se diseñó para
   las pantallas de autenticación. Si se queda en el `body`, va a asomar detrás del sidebar
   del shell. Con esto el header puede ser opaco y no hace falta ningún `backdrop-filter`.
2. **Agregá el modificador `.btn.inline { width: auto; height: 38px; padding: 0 16px }`**
   antes de maquetar cualquier pantalla interna.
3. **Unificá los dos grises** en un token `--surface-2: #f8fafb` y usalo en los dos lugares.
4. **Nombrá la duración y la curva**: `--dur: 120ms` y `--ease: cubic-bezier(.2,.7,.3,1)`,
   y reemplazá los tres `120ms ease` escritos a mano.

---

## Paso 1 — La API primero. Sin esto el shell no se puede construir

El DTO actual no alcanza y es el bloqueo real. Sin CUIT por tenant no se puede mostrar el
número en cada fila del selector, que es justo lo que evita elegir mal. Sin rol ni permisos
el shell no puede filtrar la navegación y va a mostrar ítems que revientan al entrar.

### 1.1 El tipo fiscal y el alcance son dos ejes distintos

**No agregues `"plataforma"` como tercer valor de `type`.** `type` es el tipo fiscal derivado
del CUIT y `kind()` no puede producir ese valor jamás. Agregá un campo aparte:

```js
// scope: 'platform' | 'tenant'   — alcance de sistema
// type:  'empresa'  | 'persona'  — tipo fiscal, derivado del CUIT (null si scope es platform)
```

Cualquier regla escrita como `tenant.type === 'plataforma'` está apoyada en un campo que hoy
no puede tener ese valor.

### 1.2 El DTO nuevo

```js
{
  user: { id, name, email },
  activeTenant: {
    id, name, scope, type, cuit,      // cuit ya formateado: "30-71234567-1"
    role,                              // 'owner' | 'admin' | 'member' | 'operator'
    permissions: ['bot.modules.manage', ...],   // permisos EFECTIVOS en este contexto
    degraded: [ { code, severity, text } ]      // severity: 'warn' | 'error'
  },
  tenants: [
    { id, name, scope, type, cuit, role, hasDegraded }   // hasDegraded: boolean
  ]
}
```

`hasDegraded` en la lista es a propósito: permite marcar una falla en un contexto donde la
persona **no** está parada, que es donde más falta hace verla.

### 1.3 El seed

Agregá un tenant de plataforma y un usuario operador. **No lo llames "Plataforma"**: el
wordmark de la aplicación ya dice literalmente "Plataforma" y quedarían dos cosas idénticas a
16px de distancia, justo en la zona donde el diseño quiere distinguir *dónde estoy* de *quién
soy*. Llamalo **"Operaciones"**.

| Usuario | Clave | Contextos | Para qué sirve |
|---|---|---|---|
| `operador@plataforma.com` | `1234` | Operaciones (`scope: platform`, rol `operator`) | ver P1–P5 |
| `juan@acme.com` | `1234` | Acme S.A. (empresa, `owner`) + Juan Pérez (persona, `owner`) | **el caso de los dos sombreros** |
| `maria@acme.com` | `1234` | Acme S.A. (`member`) | el rail flaco: dos ítems y nada más |
| `nuevo@sur.com` | `1234` | Distribuidora Sur (sin confirmar) | no puede entrar |

Permisos por rol, con los nombres del catálogo:

- `operator` → `platform.whatsapp.read`, `platform.bot.capabilities.read`
- `owner` / `admin` → `bot.modules.manage`, `bot.capabilities.manage`, `whatsapp.links.read`
- `member` → ninguno

### 1.4 Datos sembrados, nunca inventados en el front

El Inicio muestra estado de módulos y capacidades. **Esos datos van sembrados en la API**, no
escritos a mano en el JSX. Una maqueta con cifras fabricadas se lee como falsa apenas alguien
pregunta de dónde salen, y la pregunta se hace siempre.

Sembrá, por tenant, algo chico y verosímil: dos o tres módulos con estado
(`vigente` / `sin configurar` / `vence en N días`) y dos o tres códigos de capacidad
habilitados (`emitir_factura`, `sacar_turno`, `consultar_saldo`). Un endpoint
`GET /api/me/resumen` alcanza.

**Nada de porcentajes, tendencias ni "+12%".** No hay fuente para eso.

---

## Paso 2 — Tokens nuevos

Agregalos a `tokens.css`, en el mismo estilo que los existentes:

```css
--header-h: 60px;          /* a 56px por debajo de 768px */
--sidebar-w: 264px;
--sidebar-w-min: 68px;

/* acento por contexto */
--ctx-empresa: var(--primary);
--ctx-empresa-soft: var(--primary-soft);
--ctx-persona: #4c4a8c;         /* índigo apagado */
--ctx-persona-soft: #eceaf4;
--ctx-platform: var(--ink);     /* tinta, no color de marca */
--ctx-platform-soft: #e8ebed;

--ctx-accent: var(--ctx-empresa);          /* reasignado por [data-context] */
--ctx-accent-soft: var(--ctx-empresa-soft);

/* tercer color semántico */
--warn: #8a5a12;
--warn-soft: #fbf1de;

--nav-hover: var(--surface-2);
--shadow-pop: 0 2px 6px rgba(22,33,43,.06), 0 12px 28px -8px rgba(22,33,43,.22);

--z-header: 40;  --z-drawer: 50;  --z-pop: 60;  --z-flash: 70;
```

Tres notas sobre por qué:

- **`--ctx-persona` es índigo y no violeta libre.** Está elegido midiendo la distancia al
  terracota de `--danger` (`#b3412a`): el monotributo no puede leerse como una alerta.
- **`--warn` no existe hoy** y hace falta. `SCREENS.md` define un estado *Degraded* normativo
  que no es error ni éxito. Sin este token, "la credencial vence en 4 días" se pinta con
  `--danger` y se lee como emergencia, o se pinta con `--muted` y desaparece.
- **`--shadow-pop` es distinta de `--shadow-card`.** La existente está calibrada para una
  tarjeta flotando sobre el fondo con glow, y queda difusa e imprecisa en un menú anclado a
  8px de su botón.

El único mecanismo de tematización es un atributo:

```css
.shell[data-context="persona"]  { --ctx-accent: var(--ctx-persona);  --ctx-accent-soft: var(--ctx-persona-soft); }
.shell[data-context="platform"] { --ctx-accent: var(--ctx-platform); --ctx-accent-soft: var(--ctx-platform-soft); }
```

Sin JS de temas y sin duplicar reglas.

### Nombres de clase: en inglés

El código base ya está clasado en inglés con contenido en español: `topbar`, `tenant-row`,
`field`, `panel`, `badge`. **Sostené ese idioma.** `nav-label`, no `nav-rotulo`.
Mezclar es cómo se llega a tener `.tenant-nombre` y `.tenant-name` en el mismo archivo.

---

## Paso 3 — La silueta

**El sidebar es dueño de la esquina superior izquierda. El header vive solamente sobre la
columna de contenido.** No un header de ancho completo por encima del sidebar: es la decisión
más fechada disponible y es lo primero que se ve en una captura de pantalla.

```
┌──────────┬────────────────────────────────┐
│          │  header  (switcher · perfil)   │
│ sidebar  ├────────────────────────────────┤
│          │                                │
│          │  contenido                     │
└──────────┴────────────────────────────────┘
```

`.shell` es un grid de `var(--sidebar-w) 1fr`. El header es `sticky` dentro de la columna de
contenido, `60px`, fondo `var(--surface)`, `border-bottom: 1px solid var(--line)`. Sin sombra
en reposo; al scrollear, `.shell-header.is-scrolled` agrega
`box-shadow: 0 1px 0 var(--line), 0 6px 16px -12px rgba(22,33,43,.28)`.

**Nada de `backdrop-filter`.** Con el glow movido a `.auth` no compra nada visible y cuesta
una capa de composición en cada scroll.

---

## Paso 4 — El selector de contexto (la pieza central)

Va **en el header, a la izquierda**, separado del wordmark por una línea vertical de 1px.
No en el sidebar y no dentro del menú de perfil.

Por qué ahí: por debajo de 1024px el sidebar es un drawer cerrado por defecto, así que un
indicador de contexto adentro del sidebar es, en móvil, un indicador invisible. Y va afuera
del menú de perfil porque el perfil es la persona — una sola, siempre la misma — y el contexto
es el tenant — dos CUIT, dos responsabilidades. Meterlos en el mismo desplegable sugiere que
cambiar de contexto es cambiar de cuenta, y no lo es.

### Cerrado — `.tenant-switch`

Botón de 40px de alto, radio 10px, `border: 1px solid var(--line)`, fondo `#fff`.
Adentro, de izquierda a derecha:

- **`.tenant-sigil`** — cuadrado de 26px, radio 8px, iniciales en 11px/800.
  Coloreado por contexto con `--ctx-accent-soft` de fondo y `--ctx-accent` de texto.
  **Este es el único lugar donde el color del contexto aparece.**
- Dos renglones apilados, `line-height: 1.15`:
  `.tenant-name` en 14px/600 `var(--ink)`, truncado con ellipsis;
  `.tenant-cuit` en 12px `var(--muted)`, con **`font-variant-numeric: tabular-nums`**,
  formato `30-71234567-1`.
- Un chevron de 12px que rota 180° al abrir.

**Forma como código.** El avatar del perfil es un **círculo**; el sigilo del tenant es un
**cuadrado redondeado**. La forma se lee preatentivamente y distingue *persona* de *contexto*
sin depender del color ni del texto.

**Un solo contexto → no hay botón.** Se renderiza `.tenant-badge`: el mismo bloque sin borde,
sin chevron y sin hover. No hay afordancia para algo que no puede pasar.

**Mientras `/api/me` está en vuelo → `.tenant-switch.is-loading`**, con el sigilo y dos barras
de skeleton, y **no es clickeable**. Esto no es pulido, es corrección: ofrecer un cambio de
contexto antes de saber cuáles son los contextos es literalmente cómo alguien elige el CUIT
equivocado.

### Abierto — `.tenant-pop`

Popover anclado al borde izquierdo del botón, 8px abajo, 340px de ancho, `--r-card`,
`border: 1px solid var(--line)`, `box-shadow: var(--shadow-pop)`, entrada de 140ms
(`opacity 0→1`, `translateY -6px→0`).

- Rótulo `.tenant-pop-label`: **"OPERANDO CON"**, 11px/700, `letter-spacing: .08em`, `--muted`.
- Una fila `.tenant-opt` por contexto, 56px, con: sigilo de 30px, nombre en 14px/600, CUIT en
  12px tabular-nums, y a la derecha un `.badge` con el tipo ("Empresa", "Persona física",
  "Operaciones") y debajo el rol en 11px ("Dueño", "Administrador", "Miembro").
- La fila activa lleva fondo `--ctx-accent-soft` y un tilde a la derecha.
- Si un contexto **no activo** tiene `hasDegraded`, su sigilo lleva un punto de 7px con anillo
  de 2px en `var(--surface)`.
- Al pie, separado por 1px: **"Cambiar de contexto cambia el menú y con qué CUIT se factura."**

---

## Paso 5 — Sidebar y navegación filtrada

**La navegación no es un menú: es `SCREENS.md` filtrado dos veces** — primero por el alcance
del contexto activo, después por permiso efectivo por ítem.

`SCREENS.md` dice, sobre el estado *Forbidden*: *"the screen is absent from navigation rather
than present and refusing"*. O sea: **ausente, no deshabilitado.** No hay ítems grises.

| Grupo | Visible cuando | Ítems |
|---|---|---|
| *(sin rótulo)* | siempre | **Inicio** → `/inicio` |
| Plataforma | `scope === 'platform'` | **Canal de WhatsApp** (P1) · **Puesta en marcha** (P2) · **Números vinculados** (P3) — con `platform.whatsapp.read`; **Catálogo de capacidades** (P4) — con `platform.bot.capabilities.read`; **Monitor** (P5) — con `platform.whatsapp.read` |
| Organización | `scope === 'tenant'` **y** algún permiso de administración | **Integraciones** (O1) — `bot.modules.manage`; **Capacidades del asistente** (O3) — `bot.capabilities.manage`; **Miembros y WhatsApp** (O4) — `whatsapp.links.read` |
| Lo mío | `scope === 'tenant'` | **Mi WhatsApp** (M1) — sin permiso, es siempre de uno mismo |

Los grupos Plataforma y Organización son mutuamente excluyentes: nunca conviven.
**Si un grupo se queda sin ítems, no se dibuja tampoco su rótulo.**

### Dónde viven las nueve pantallas que no son ítems de menú

Que un shell derive del inventario obliga a decir dónde cae cada una de las catorce:

- **O2 (credenciales del módulo)** → `/organizacion/integraciones/:modulo/credenciales`.
  Se genera del esquema declarado del módulo; darle entrada propia obligaría a inventar un
  ítem por módulo.
- **S2 (autorización externa)** → `/organizacion/integraciones/:modulo/autorizacion`.
- **M2 (vincular número)** → `/mi/whatsapp/vincular`, la acción "Agregar" de M1.
- **M3 (confirmar acción)** → `/aprobar/:runId`, y **fuera del shell** (ver reglas).
- **S1 (alta desde un reclamo)** → fuera del shell, es pre-sesión.

### El rail flaco

Con `maria@acme.com` la navegación son dos ítems: Inicio y Mi WhatsApp. **Es correcto y no
se rellena.** Una columna de 264px sosteniendo dos filas es exactamente el momento en que
alguien va a proponer meter ítems inventados. La respuesta es no.

Si el filtro deja el contexto con *solamente* Inicio, `.nav-empty` muestra
*"En este contexto todavía no hay nada configurado."* más un enlace para cambiar de contexto.

### El colapsado

- Expandido `264px`, colapsado `68px`, transición `width 180ms var(--ease)`.
- **Sobreviven**: el icono de 20px centrado, la barra de activo de 3×18px en `--ctx-accent`
  pegada al borde izquierdo, y el punto de degradación mudado a la esquina del icono.
- **Desaparecen**: `.nav-label` (con `opacity` y después la clase — **nunca `width: 0`**, el
  texto colapsado a cero se ve roto mientras transiciona) y el rótulo del grupo, que se
  reemplaza por una línea de 1px × 24px centrada.
- **Tooltips**: `.shell.is-min .nav-item::after { content: attr(data-tooltip) }`, a
  `left: calc(100% + 10px)`, con `transition: opacity var(--dur) ease 400ms` para que no
  dispare al pasar de largo. **El `::after` es decorativo**: el nombre accesible sale de un
  `aria-label` real en el `<a>`. Un menú colapsado cuyo único texto es contenido generado por
  CSS es un menú mudo para un lector de pantalla.
- **Persiste** en `localStorage['shell.nav']`, leído con inicialización perezosa de `useState`.
  **Nada de un script inline en `index.html`**: es una SPA renderizada en cliente, no hay HTML
  de servidor que pueda parpadear, y sería una segunda fuente de verdad que hay que reconciliar
  contra React.
- Es preferencia de la persona, no del contexto: no se resetea al cambiar de tenant.

---

## Paso 6 — Header y menú de perfil

De izquierda a derecha: hamburguesa (36×36, `aria-expanded`, `aria-controls`) · wordmark
"Plataforma" · separador de 1px · **selector de contexto** · `flex: 1` · chip de estado ·
perfil.

**`.shell-status`** solo se renderiza cuando hay una condición abierta que afecta al contexto
activo. 28px, radio 999px, 12px/600, con un punto de 6px adelante. Variante `.warn`
(`--warn-soft` / `--warn`) y `.error` (`--danger-soft` / `--danger`). El texto es **la
condición**, nunca la palabra "Error": *"Canal sin verificar"*, *"ARCA vence en 4 días"*.
Es un botón: lleva a P1 en contexto plataforma, a O1 en organización. **Un chip por condición
abierta, no uno por evento.**

**Menú de perfil**, disparado por avatar circular de 30px + nombre (oculto bajo 768px) +
chevron:

1. Bloque de identidad no clickeable: avatar de 36px, nombre en 14px/700, correo en 12px `--muted`.
2. **Mi perfil**
3. **Seguridad y acceso** — con un sufijo de estado: "MFA activo" en `--success` o
   "MFA pendiente" en `--warn`. Está acá y no escondido porque las mutaciones del panel de
   plataforma exigen step-up reciente: un operador que llega a configurar el canal y descubre
   recién ahí que le falta MFA perdió el viaje.
4. **Mis contextos (2)** — no navega: abre el mismo `.tenant-pop` del header y le pasa el foco.
5. *separador*
6. **Cerrar sesión** — `.perfil-item.danger`, sin icono de alarma y sin confirmación.

---

## Paso 7 — Inicio

Cuatro bloques. **Ninguno inventa una capacidad ni una cifra.**

1. **`.home-greeting`** — `h1` "Hola, Juan" en `--font-display` 700, 28px,
   `letter-spacing: -0.02em`, y debajo la fecha larga en rioplatense
   ("lunes 1 de septiembre").

2. **`.home-seal`** — tarjeta de ancho completo con una banda de 4px en `--ctx-accent` sobre
   el borde izquierdo. Sigilo de 44px, nombre del tenant en 20px display, y debajo la línea
   que hace todo el trabajo:

   > **CUIT 30-71234567-1** · Empresa · Tu rol: Dueño

   con el CUIT en tabular-nums y peso 600. A la derecha, `.btn.inline.ghost`
   "Cambiar de contexto", que abre el popover del header (ausente si hay un solo contexto).
   Es la frase que pidió el dueño del producto, escrita en palabras y no solo en un chip.
   Mientras `/api/me` está en vuelo, este bloque es un skeleton de altura fija, no un hueco.

3. **`.home-contexts`** — los contextos de la persona listados uno debajo del otro, con
   sigilo, nombre, tipo y **CUIT completo**, el activo marcado. Solo si tiene más de uno.

   Es la única vez en toda la interfaz donde **los dos CUIT se ven juntos y en reposo**, sin
   abrir ningún menú. `SCREENS.md` dice de M1 que ahí es donde se explica la posibilidad del
   segundo contexto, porque *"a person who works for an organization and also acts for
   themselves needs to understand that these are two links, not one"*. Esta tarjeta enseña
   eso el día uno.

4. **`.home-grid`** — tres columnas ≥1024px, dos entre 768 y 1023, una abajo. Cada tarjeta
   tiene un rótulo de 11px/700 en versalitas, un cuerpo, y **un pie condicional**.

   En contexto de organización o persona:
   - **TU WHATSAPP** → el vínculo propio en este contexto, o el vacío honesto
     ("Todavía no vinculaste un número en este contexto" + botón → M2). Pie: "Ir a Mi WhatsApp".
   - **MÓDULOS** (solo con `bot.modules.manage`) → las filas sembradas, cada una con un punto
     de estado: vigente en `--success`, sin configurar en `--muted`, vence en N días en
     `--warn`. Pie: "Ir a Integraciones".
   - **LO QUE EL ASISTENTE PUEDE HACER ACÁ** → los códigos de capacidad habilitados como chips.
     **El pie "Ajustar capacidades" se renderiza solo con `bot.capabilities.manage`.** Un
     miembro común ve la lista y no ve el pie: es la regla de *Forbidden* aplicada hasta el
     último enlace.

   En contexto de plataforma la grilla se reemplaza entera: **CANAL** (estado de P1 y hora del
   último chequeo), **PUESTA EN MARCHA** ("5 de 7 ítems", con el de suscripción de la
   aplicación destacado aparte porque es el que suele leerse sano sin entregar nada), y
   **ÚLTIMAS 24 HORAS** → P5.

---

## Paso 8 — Responsive

Cuatro cortes.

- **≥1280px** — grid completo. `.shell-content { max-width: 1120px; margin: 0 auto; padding: 28px 32px 72px }`.
  Sidebar sticky con `height: calc(100dvh - var(--header-h))` (`dvh`, no `vh`: en móvil la
  barra del navegador cambia de alto).
- **1024–1279px** — igual, pero sin preferencia guardada el sidebar arranca colapsado.
- **768–1023px** — el sidebar sale del flujo: `position: fixed`, ancho 280px (no 264: los
  targets táctiles suben a 44px), `transform: translateX(-100%)`, con velo
  `rgba(22,33,43,.38)`. Cierra con Escape, con clic en el velo y al navegar. Mientras está
  abierto se atrapa el foco y el `body` va a `overflow: hidden`. **Arranca siempre cerrado.**
- **<768px** — header a 56px, wordmark oculto bajo 480px, `.tenant-switch` toma
  `flex: 1; min-width: 0`.

### El presupuesto de 375px

Es el ancho que hay que resolver bien. El cromo fijo del header es:

```
hamburguesa 40  +  separador 1 + 24 de aire  +  avatar 32  +  padding 16×2  =  ~129px
```

Entonces `.tenant-switch { max-width: calc(100vw - 129px) }`, con `.tenant-name` en
`overflow: hidden; text-overflow: ellipsis` y **`.tenant-cuit { flex: none }`**.

**Cuando hay que sacrificar algo, se sacrifica el nombre, nunca el número.**
"Distribuidora Sur SRL" se lee "Distribuidora Su…" y el CUIT queda entero.

Escribí ese presupuesto como comentario en el CSS. Sin la cuenta escrita, el día que alguien
agregue un elemento al header se va a comer el CUIT en silencio.

En 375px `.tenant-switch.is-compact` invierte los renglones: **el CUIT arriba** en 12px/600
`--ink`, el nombre abajo en 12px `--muted`.

**El popover a 375px no es una hoja inferior con asa.** Es el mismo DOM pasando a
`position: fixed` con `left: 0; right: 0; bottom: 0` y `border-radius: 16px 16px 0 0`. Tres
propiedades. Una hoja con asa de arrastre y animación propia son cuatro caminos de código y
dos trampas de foco para resolver lo mismo.

`@media (prefers-reduced-motion: reduce)` anula el slide del drawer y la transición de
reemplazo del menú.

---

## Paso 9 — El contrato de accesibilidad

Es lo que todas las propuestas dejaron flojo y es el bug más común de este patrón exacto.
Los tres disparadores — hamburguesa, selector de contexto, menú de perfil — comparten contrato:

- `aria-expanded` y `aria-haspopup` en el botón, `aria-controls` apuntando al panel.
- Abre con clic, Enter o Espacio. **Cierra con Escape y con clic afuera.**
- **Al cerrar, el foco vuelve al disparador.**
- Dentro del panel el foco es itinerante (flechas arriba/abajo, Home/End).

**El selector de contexto NO es `role="listbox"`.** Ese rol implica un valor de formulario que
se elige; esto es un conjunto de acciones que cambian el estado de la sesión y reescriben la
navegación. Va como botón con `aria-haspopup="menu"` abriendo un panel con `role="menu"` y
filas `role="menuitemradio"`. Especificar mal un rol ARIA es peor que no especificarlo, porque
alguien lo va a implementar tal cual.

**El cambio de contexto se anuncia con `aria-live="polite"`.** Una franja `.context-flash` de
36px debajo del header — *"Ahora operás con Acme S.A. · CUIT 30-71234567-1"* — que se va sola
a los 4 segundos. Un cambio de responsabilidad fiscal que solo se comunica visualmente deja a
un usuario de lector de pantalla sin saber con qué CUIT quedó operando.

---

## Reglas que no se negocian

1. **El CUIT del contexto activo no puede quedar oculto, truncado ni detrás de una
   interacción en ningún breakpoint.** Es lo último que se cae, y no se cae.

2. **Ausente, no deshabilitado.** El filtro por permiso se aplica **también en el router, con
   redirección** — no con un 403. Una URL guardada en favoritos a `/plataforma/monitor` no
   puede resolver a una pantalla que refuse. Filtrar solo la navegación resuelve la mitad de
   *Forbidden*; la otra mitad es el deep link.

3. **Un solo control de contexto.** El contexto aparece en tres superficies — el selector del
   header, el sello del Inicio, "Mis contextos" del menú de perfil — pero **hay un solo
   control real** y las otras dos invocan ese mismo popover. Escribilo como regla: con tres
   superficies vivas, el segundo que toque el código va a implementar un segundo dropdown.

4. **M3 vive fuera del shell**, en un layout de foco: sin sidebar, sin selector, con el sello
   del tenant y nada más. Si vive adentro, alguien puede cambiar de contexto en medio de una
   aprobación y confirmar bajo el CUIT equivocado — la falla exacta que todo este diseño existe
   para evitar. Y va en **`/aprobar/:runId`**: `/confirmar` ya está tomado en `main.jsx` por la
   confirmación de correo con `?token=`, y reusarla haría que el deep link del asistente caiga
   en la pantalla equivocada.

5. **El color del contexto aparece en un solo lugar: el sigilo.** Más la barra del ítem activo,
   que siempre tiene texto al lado. Nada de cintas de color a todo el ancho ni puntos teñidos
   en el wordmark: repetir el mismo dato en cuatro superficies no lo hace más claro, y una
   banda de color pegada al borde superior se lee como barra de notificación del sistema.
   El trabajo real lo hace el CUIT en texto tabular.

6. **El front no fabrica datos.** Si un número no está en la API, no se muestra.

7. **La animación del cambio de menú arranca cuando la respuesta ya llegó**, con el spinner en
   la fila del popover y no en toda la pantalla. Animar antes de confirmar es mostrar una
   navegación que todavía no es cierta.

---

## Lo que NO hay que construir

Cada uno fue considerado y descartado con razón:

- **Paleta de comandos ⌘K.** Medio día de trabajo — portal, filtrado, teclado, ARIA, salto de
  ruta — para moverse entre cinco o seis destinos que ya están todos visibles en el rail. Si el
  producto pasa de ocho destinos por contexto, se agrega. Hoy no.
- **Campana de notificaciones.** No hay modelo de notificaciones ni endpoint, y `SCREENS.md`
  ya le dio domicilio normativo a las alertas (P5 y P1). Sería una tercera copia del mismo dato
  con menos contexto, y una campana que nunca suena hace que el producto se sienta una cáscara.
- **Breadcrumbs.** Con destinos de un solo nivel, siempre van a decir "Grupo / Página", o sea
  repetir el ítem que el rail ya tiene resaltado a 20cm de distancia.
- **Toast con "Deshacer" al cambiar de contexto.** Presenta un cambio de responsabilidad fiscal
  como una acción reversible de lista, con el mismo gesto que se usa para deshacer un envío de
  correo. Y si hay un formulario a medio llenar, deshacer pierde lo tipeado. El patrón correcto
  es el opuesto: **si hay algo sin guardar, el cambio de contexto pide confirmación antes**;
  si no lo hay, alcanza con el acuse de `.context-flash`.
- **KPIs, porcentajes y tendencias en el Inicio.**
- **Cinta de color de ancho completo.**
- **Header de ancho completo por encima del sidebar.**
- **`backdrop-filter` en el header.**
- **Hoja inferior con asa de arrastre.**
- **Script inline en `index.html`.**

---

## Dos decisiones que tenés que plantear, no resolver solo

1. **Inicio no está en `SCREENS.md`.** Ese documento dice textualmente: *"A screen absent from
   this list is a screen nobody agreed to build."* Construila para la maqueta, pero **decilo
   explícitamente al entregar**: o se agrega al inventario con su fila de estados y su
   incremento, o `/app` redirige según contexto y rol (plataforma → P5, admin → O1,
   miembro → M1). Dejarla sin decidir es deuda de especificación disfrazada de pantalla linda.

2. **Que la navegación cambie de forma entre contextos tiene un costo real de memoria
   muscular** que ninguna animación compensa: el tercer ítem del menú no es el mismo en Acme
   que en el monotributo. Es el precio de la regla de *Forbidden* y hay que asumirlo, pero es
   el punto donde este shell se va a sentir peor que uno genérico. Anotalo.

---

## Cómo verificar antes de entregar

1. `npm run dev` y recorrer a mano con los cuatro usuarios del seed.
2. **Con `juan@acme.com`**: aparece el selector con dos contextos; cambiar de Acme a Juan Pérez
   cambia el CUIT del header, el color del sigilo y — si los permisos difieren — la navegación.
3. **Con `maria@acme.com`**: el rail tiene dos ítems y no hay ninguno gris. Entrar a mano a
   `/organizacion/integraciones` redirige, no muestra un 403.
4. **Con `operador@plataforma.com`**: aparece el grupo Plataforma y desaparece "Lo mío".
5. **A 375px exactos** (DevTools): el CUIT se lee entero en los cuatro cortes, con el drawer
   abierto y cerrado.
6. **Solo con teclado**: Tab hasta la hamburguesa, Enter, Escape, y verificar que el foco
   vuelve. Lo mismo con el selector y con el perfil.
7. **Extendé `demo.mjs`** con los pasos nuevos y corré `FAST=1 node demo.mjs` hasta que pase
   entero. Ojo: los pasos 13 a 18 del recorrido actual usan selectores de la `App.jsx` vieja
   (`getByRole('button', {name: 'Salir'})`, `page.locator('select')`) y **se van a romper** al
   reemplazar esa pantalla. Hay que actualizarlos.

---

## Una última cosa

El dueño del producto ya rechazó un maquetado anterior con estas palabras: *"es horrible el
estilo"*, *"no podés hacer un maquetado de un nene de 5 años"*. La pantalla que rechazó era
literalmente un `h1` más un panel con dos renglones — andá a mirar `App.jsx` para saber
exactamente qué no repetir.

El listón es un producto real de 2026, no una plantilla de 2016. Si no podés defender la
pantalla en una captura, todavía no está.
