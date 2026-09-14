# Entrar y crear cuenta — diseño del flujo

**Estado:** aprobado para prototipo (2026-09-14). Se evalúa en la maqueta de `docs/mockups` antes de llevarlo a la
SPA y a la SPEC de Identity Access.

## Por qué

Hoy el producto tiene las piezas pero no el flujo. El ingreso con Google existe sólo en `/login`; quien entra así por
primera vez queda con sesión, sin contexto y sin nada que lo lleve a completar sus datos. `/personal/register` vuelve
a pedir email y contraseña a quien ya tiene sesión y falla con un error genérico. Y la elección entre cuenta personal
y de empresa, que la SPEC pide explícita, no aparece en el camino de Google.

## Decisiones

1. **El tipo se elige sólo al crear cuenta.** «Entrar» va directo a Google o a email y contraseña. «Crear cuenta»
   pregunta primero *¿Personal o Empresa?*. Una persona es una sola cuenta por email que puede tener un contexto
   personal y varias empresas, así que quien vuelve no elige nada: si tiene varios contextos, elige adentro.
2. **Con email, primero el código y después la contraseña.** Email → código de 6 dígitos en la misma pantalla →
   contraseña → datos. El código se manda a la dirección exista o no una cuenta, y la pantalla dice lo mismo en los
   dos casos. Recién después de que el código prueba la dirección, el sistema puede decir la verdad sobre la cuenta.
3. **Si el email ya tiene cuenta, se pide su contraseña y se sigue.** Un código solo nunca abre una cuenta existente.
   Si la cuenta entra sólo con Google, en vez de la contraseña se ofrece «Continuar con Google». En los dos casos lo
   que se completa después se agrega a la cuenta existente.
4. **Con Google no hay paso de verificación**: Google ya verificó la dirección. Una cuenta de Google cuyo email ya
   pertenece a una cuenta con contraseña no se vincula sola (BR-ID-005/006): se explica y se manda a entrar con la
   contraseña.
5. **Quien tiene sesión y ningún contexto** ve «Terminá de configurar tu cuenta»: la elección de tipo y los datos,
   sin volver a autenticarse. Cubre al que entró con Google por primera vez y al que abandonó en la mitad.
6. **El CUIT o el DNI se piden primero, siempre.** Es el dato del que se van a derivar los demás: cada vez que se
   pida uno, se va a consultar el padrón de AFIP (ARCA) para completar los campos que correspondan. Aplica al
   último paso de crear cuenta y a las altas desde adentro (nueva empresa, agregar cuenta personal). **Todavía no
   se implementa** — ver [Pendiente: padrón de AFIP](#pendiente-padrón-de-afip).

## Recorrido

```text
Entrar ──────► Google │ email + contraseña ──► adentro
                                              (sin contexto: «Terminá de configurar»)
                                              (varios: elige personal o empresa)

Crear cuenta ► ¿Personal o Empresa?
   ├─ Google ──► cuenta nueva o ya vinculada ──► datos del tipo elegido
   │             email de una cuenta con contraseña sin vincular ──► «entrá con tu contraseña»
   └─ Email ───► código de 6 dígitos
                   ├─ nuevo ──────► contraseña ──► datos
                   ├─ ya existe ──► su contraseña ──► datos
                   └─ solo Google ► Continuar con Google ──► datos

Datos: Personal = DNI, nombre completo, nombre visible · Empresa = CUIT, razón social
```

Pasos visibles: **Tipo · Acceso · Verificación · Datos**.

## Casos límite

| Caso | Respuesta |
|---|---|
| Código incorrecto | Se rechaza y se dice cuántos intentos quedan |
| Quinto intento incorrecto | El código se cierra; hay que pedir otro |
| Código vencido (10 minutos) | Se dice que venció y se ofrece reenviar |
| Reenviar antes de 30 segundos | Se espera; se dice cuánto falta |
| Contraseña de la cuenta existente incorrecta | Mismo bloqueo por intentos que «Entrar» |
| Ya tiene contexto personal y elige Personal | Se le dice que ya la tiene y se ofrece ir a ella |
| DNI ya registrado, o ya tiene contexto personal al crear | Una sola respuesta que no dice cuál de los dos pasó |
| CUIT ya registrado | Se dice que ese CUIT ya está registrado |

## Alcance del prototipo

Se construye en `docs/mockups` (web React + API simulada en memoria). El código llega a la bandeja simulada del
panel DEMO, la pantalla de Google es una simulación rotulada, y cada caso límite tiene un escenario de un clic.
`/login` y `/registro` redirigen a `/entrar` y `/crear-cuenta`.

Fuera de alcance: recuperar la contraseña, vincular Google desde la cuenta, y cualquier cambio en `src/`.

## Pendiente: padrón de AFIP

Decidido, no implementado. Cuando se escriba un CUIT o un DNI, se consulta el padrón de AFIP (ARCA) y se completan
los campos que el padrón conoce: la razón social para un CUIT, el nombre completo para un DNI. El nombre visible
sigue siendo elección de la persona.

Lo que hay que resolver antes de construirlo:

- **Datos personales reales.** Consultar un padrón con un DNI es tratar datos personales reales, y la SPEC mantiene
  `Personal` sobre datos sintéticos hasta que un responsable apruebe finalidad y alcance (IA-REQ-056). La consulta
  queda bloqueada por esa aprobación.
- **Qué pasa cuando el padrón no responde o no encuentra el número.** El alta no puede depender de un servicio
  externo: los campos se completan a mano y la consulta es una ayuda, no un requisito.
- **Si lo que trae el padrón se puede editar** o queda fijo, y qué se guarda como fuente del dato.
- **Enumeración.** Una pantalla pública que devuelve un nombre a partir de un DNI permite averiguar a quién
  pertenece un documento. La consulta va detrás de una sesión o de la dirección ya probada, con presupuesto de
  intentos, y nunca en un paso anónimo.
- **Credenciales y servicio.** Qué servicio del padrón se usa, con qué certificado, y dónde se configura por
  despliegue, fuera del repositorio.

## Impacto previsto en el producto (no se implementa acá)

- El código por mail es la misma clase de prueba que el enlace de hoy — un secreto de un solo uso entregado a la
  dirección (IA-REQ-048) — pero corto: necesita presupuesto de intentos por desafío y por dirección.
- Cambia el orden del registro de IA-REQ-003/005: la prueba de la dirección pasa a ser el primer paso, no el último.
- El retorno de Google con `signed_in` lleva a «Terminá de configurar» cuando la identidad no tiene contexto.
- Cada cambio de texto va en todos los idiomas y cada código de error nace con su mensaje y su test.
