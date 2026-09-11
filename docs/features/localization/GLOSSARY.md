# Spanish SPA terminology

Phase 3 uses neutral, professional Spanish. Complete sentences address the reader as **usted**; action labels use
infinitives. First-person acknowledgements preserve the English speaker (for example, “Entiendo…”). This draft
implements decisions P3.1–P3.7 in [PLAN.md §11.5](PLAN.md#115-phase-3--spanish-in-the-spa); the product owner reviews
wording in the running application after delivery.

## Identity and access

| Source concept | Spanish | Meaning / use |
| --- | --- | --- |
| Organization / organization-specific tenant | Organización | Organization registration, membership and Platform organization management |
| Tenant / context, across tenant types | Contexto | The shared abstraction also contains Personal and Platform tenants |
| Platform | Plataforma | The system administration area; capitalized as the named area |
| Sign in / sign out | Iniciar sesión / Cerrar sesión | Includes the older English “Log in” / “Log out” labels |
| Session | Sesión | An authenticated session; ending a device means ending its session |
| Access | Acceso | Current identity, context and permissions |
| Account / personal account | Cuenta / Cuenta personal | The person's account; not a membership |
| Identity | Identidad | The account identity in operator screens |
| Profile / personal profile | Perfil / Perfil personal | The person's profile data |
| Role / permission | Rol / Permiso | Authorization concepts; exact permission codes remain visible below their labels |
| Member / membership | Miembro / Membresía | Person in an organization / the recorded relationship |
| Owner / administrator | Propietario / Administrador | Only `isSystem` role names are translated |
| Built-in / retired role | Del sistema / Retirado | System role / role no longer available for assignment |
| Grant / manage | Otorgar / Administrar | Permissions / administrative operations |
| Transfer ownership | Transferir la propiedad | Organization ownership, not identity ownership |
| Invitation / recipient | Invitación / Destinatario | Invitation and its addressed recipient |
| Accept / resend / withdraw | Aceptar / Reenviar / Retirar | Invitation actions; cancelled status is “Cancelada” |
| Register / registration | Registrarse (account), Registrar (object) / Registro | Keeps action labels as infinitives |
| Legal name | Razón social | Organization name used in legal registration |
| Full name / display name | Nombre completo / Nombre visible | User-authored values remain unchanged |
| Email / address / inbox | Correo electrónico / Dirección / Bandeja de entrada | “Dirección” keeps the existing short operator column label |
| Password / credential | Contraseña / Credencial | Password UI versus the broader credential concept |
| Reset / set / change password | Restablecer / Establecer / Cambiar contraseña | Distinct existing credential actions |
| Sign-in provider | Proveedor de inicio de sesión | Provider names such as Google stay unchanged |
| Link / unlink provider | Vincular / Desvincular proveedor | Explicit account linking |
| Second factor | Segundo factor | Includes the English MFA-authenticated condition |
| Authenticator | Aplicación de autenticación | The app generating second-factor codes |
| Proof / step up | Verificación / Verificar el segundo factor | Identity proof / the Platform second-factor form |
| Enrollment / shared key | Configuración / Clave compartida | Second-factor setup; the key itself is invariant |
| Recovery code | Código de recuperación | The code itself is invariant |
| Deactivate / reactivate / suspend | Desactivar / Reactivar / Suspender | Voluntary account pause / return / administrative suspension |
| Revoke | Revocar acceso | Removes a Platform administrator's membership access |
| Document / correction / review | Documento / Corrección / Revisión | An operator reviews document corrections |
| Document reissued | Documento reemitido | A reason for a document correction |
| Operator / directory | Operador / Directorio | Administrative user / account directory |
| DNI / CUIT | DNI / CUIT | Invariant document and tax identifiers |

The Platform organization directory and its lifecycle commands target only `TenantType.Organization`. Therefore
`platform.tenants.manage` and `platform_tenant_concurrency_conflict` say **Organización**. The broader `tenant.read`
and `tenant.manage` permissions, active-context summary, shell switcher and context chooser say **Contexto**.
Custom role names stay exactly stored, including `Owner` or
`Administrator`; organization names, user names, provider names, emails, document types/country codes, slugs, IDs,
references, reason codes, audit codes, routes, durations and raw operator data are never translated.

## Status and operations

Status wording follows its subject: tenant/organization, membership and account use feminine labels; MFA uses
masculine labels for “factor”. Separate identity and tenant suspension catalogs retain separate semantics.

| Source concept | Spanish |
| --- | --- |
| PendingConfirmation / Pending | Confirmación pendiente / Pendiente |
| Active / Suspended / Closed | Activa / Suspendida / Cerrada |
| Revoked membership / Accepted invitation / Cancelled invitation | Revocada / Aceptada / Cancelada |
| None / Verified / Active second factor | Ninguno / Verificado / Activo |
| AdministrativelySuspended / SelfDeactivated | Suspendida administrativamente / Desactivada por su propietario |
| PolicyViolation / SecurityIncident | Incumplimiento de políticas / Incidente de seguridad |
| BillingHold / OperatorRequest | Bloqueo por facturación / Solicitud del operador |
| Retention / retention policy / retention period | Conservación / Política de conservación / Período de conservación |
| Hold / place hold / release hold | Bloqueo de eliminación / Establecer bloqueo / Levantar bloqueo |
| Subject identity / reason code / reference | Identidad de la persona / Código de motivo / Referencia |
| Personal data / Real / Synthetic | Datos personales / Reales / Sintéticos |
| Category / trigger / action / evidence | Categoría / Evento de inicio / Acción / Evidencia |
| PersonalProfileNames / PersonalIdentityDocument | Nombres del perfil personal / Documento de identidad personal |
| SessionRecords / AuditEvents | Registros de sesiones / Eventos de auditoría |
| OutboxMessages / OutboxSecrets | Mensajes de salida / Secretos de salida |
| DeliveryEvidence / PlatformMfaMaterial | Evidencia de entrega / Material del segundo factor de la Plataforma |
| RecordCreation / LastActivity / AccountClosure | Creación del registro / Última actividad / Cierre de la cuenta |
| Retain / Anonymise / Erase | Conservar / Anonimizar / Eliminar |
| Audit | Auditoría |
| Slug (column label only) | Identificador de URL |
| Device / last seen | Dispositivo / Última actividad |
| Date unavailable | Fecha no disponible |
| Language / home / navigation | Idioma / Inicio / Navegación |
| Examples / counter / count / increment | Ejemplos / Contador / Recuento / Incrementar |
| Save / cancel / continue / retry / show more | Guardar / Cancelar / Continuar / Volver a intentar / Mostrar más |
| Loading / finishing up | Cargando / Finalizando |
| Deployment | Despliegue |
| Token | Token |

All 29 exact permission labels and the two system roles are reviewable together in
[the Spanish enums catalog](../../../src/Web/ClientApp/src/i18n/locales/es/enums.json). These phrases reuse the
terms above. `English` and `Español` remain self-named in both languages. The product name `Clean Architecture`
also remains unchanged.

## Review notes

All Spanish values are nonempty drafts. Product-owner attention is especially useful for these operational terms:

- **Bloqueo de eliminación / Levantar bloqueo**: chosen to say what a retention hold prevents. Confirm that this
  is the team's preferred wording for a legal hold, including the neutral release acknowledgement.
- **Contexto**: accurate for the broad tenant abstraction, but more technical than “Organización”. Review the
  broad tenant permissions, context summary and chooser alongside the personal-account flow.
- **Desactivada por su propietario**: preserves the voluntary account state without confusing it with an
  administrative suspension. Review the acknowledgement that lifting a suspension may return to this state.
- **Secretos de salida / Material del segundo factor**: deliberate operational terms for retention categories.
  Confirm they are clear to the operators who read the policy.
- **Documento reemitido / Identificador de URL / Token**: accurate drafts that may benefit from terminology
  already familiar to this product's users.

To review, open `/`, choose **Español**, and sign in. Inspect `/identity`, `/roles`, `/members`, `/members/invite`
and `/identity/sessions` with an appropriately authorized account. In a Platform context, inspect `/platform`,
`/platform/identities` and `/platform/retention`. Compare system labels with the unchanged codes, custom names and
operator data. Dates use medium date and short time in the browser's time zone. Phase 4 email/account preferences
and Phase 5 server field-validation messages are separate work; those messages can still appear in English.
