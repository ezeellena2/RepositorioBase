# Localization rules

Ship one semantic contract across the SPA, APIs, background delivery, tests, and CI: system text is selected by
stable key and explicit language; user content and technical data remain untouched.

## Language model

| Term | Meaning |
| --- | --- |
| Source language | `en`, where new copy is authored and existing copy is extracted verbatim. |
| Default language | Deployment-configured fallback used only when resolution finds no supported choice. |
| Supported language | Complete, selectable, negotiated, and enforced by all parity gates. |
| In-progress language | Registered and measured, but never offered or negotiated; missing entries are reported, not enforced. |
| Preferred language | A signed-in account choice, persisted as `PreferredLanguage` and applied across devices. |
| Language snapshot | The immutable language recorded on an invitation or delivery intent when no account preference is available. |
| Catalog | The complete SPA key/value set for one language. |
| Key | A stable identifier for system-owned text; it never derives from the current wording. |
| Namespace | An i18next resource partition named by its catalog JSON filename; qualify cross-namespace keys as `namespace:key`. |

Language chooses words, locale chooses formatting conventions, and time zone chooses the displayed instant. Never
use one as a substitute for another.

## SPA layout and keys

Keep one UI catalog per language behind the `src/i18n` facade, split into actual i18next namespaces:

```text
src/Web/ClientApp/src/i18n/
├── index.js
├── languages.json
├── useFormat.js
└── locales/
    ├── en/
    │   ├── common.json
    │   ├── errors.json
    │   ├── enums.json
    │   ├── identity.json
    │   └── platform.json
    └── es/
        ├── common.json
        ├── errors.json
        ├── enums.json
        ├── identity.json
        └── platform.json
```

`index.js` registers only bundled catalog files under `src/i18n/locales/<language>/*.json` synchronously, under the
namespace named by each filename: `common`, `errors`, `enums`, `identity`, or `platform`. `languages.json` is registry
metadata and is never registered or exposed as a translation namespace. Bind the owning feature namespace in a
component, then use lower-camel dotted local keys. Qualify another namespace with standard i18next `namespace:key`
syntax. Reserve these entries for protocol-backed values:

- `errors:<code>` for API errors;
- `errors:validation.<code>` for validation errors;
- `enums:<type>.<value>` for enum and status labels;
- `enums:permissions.<code>` for permission labels and descriptions;
- `enums:roles.system.<name>` for built-in role names.

Do not translate or pass structural IDs, routes, field names, prop values, stable domain codes, or URLs through
`t()`. Do not use English text as a key.

## React usage

Import the reactive translation API through `src/i18n`; components do not import catalogs or configure i18next.

```jsx
import { Trans, useFormat, useTranslation } from '../../i18n';

const { t } = useTranslation('identity');
const { formatDate, formatNumber } = useFormat();

<Typography component="h1">{t('invite.title')}</Typography>
<Button aria-label={t('invite.submit')}>{t('invite.submit')}</Button>
<Button>{t('common:actions.cancel')}</Button>
<Typography>{t('invite.pendingCount', { count })}</Typography>
<Typography>{formatDate(invitation.expiresAt)}</Typography>
<Typography>{formatNumber(summary.total)}</Typography>
<Trans
  ns="identity"
  i18nKey="terms.agreement"
  components={{ terms: <RouterLink to="/terms" /> }}
/>
```

Plain modules use the facade's translator with qualified keys:

```js
import { t as translate } from '../../i18n';

const conflictMessage = translate('errors:invitation_conflict');
const suspendedLabel = translate('enums:tenantStatus.suspended');
```

Use `Trans` only when markup interrupts a sentence. Keep all words and punctuation in the catalog. Use i18next
plural suffixes, preserve `count` in every form, and provide every plural category each language requires:

`locales/en/identity.json`:

```json
{
  "invite": {
    "pendingCount_one": "{{count, number}} pending invitation",
    "pendingCount_other": "{{count, number}} pending invitations"
  }
}
```

`locales/es/identity.json`:

```json
{
  "invite": {
    "pendingCount_one": "{{count, number}} invitación pendiente",
    "pendingCount_many": "{{count, number}} invitaciones pendientes",
    "pendingCount_other": "{{count, number}} invitaciones pendientes"
  }
}
```

The numeric `count` option selects the locale's plural category. The `number` interpolation formatter changes only
the displayed count, formatting it for the active locale. English requires `one` and `other`; Spanish requires
`one`, `many`, and `other` in the supported Node runtime. Locale categories are not required to match.

`useFormat()` must bind `Intl.DateTimeFormat` and `Intl.NumberFormat` to the active locale explicitly. APIs keep
timestamps in UTC and numbers in invariant machine form; components never rely on the process or browser default.

## Catalog and UI contracts

- During extraction, copy each existing English string into `en` verbatim. Extraction is not permission to edit.
- Require every supported catalog to contain every `en` semantic key with a non-empty value and identical named
  interpolation placeholders. For each pluralized semantic key, every catalog must provide every category its own
  language requires; categories do not need to match across languages. Report the same facts for `inProgress`
  languages without making them selectable.
- Run SPA tests in deterministic `en`. A supported non-English language receives a smoke journey before promotion.
- Keep `appTheme` exported while `themeFor(language)` composes the matching MUI locale.
- Preserve `id`, `name`, `data-testid`, `role`, heading level, `type`, `autoComplete`, `required`, disabled logic,
  and established accessible names. Translation changes another language's value; a copy change updates all languages.

## ASP.NET request culture

Keep backend and SPA registries equal for source, default, supported, and in-progress languages. Resolve each
interactive server request in this order:

1. the ASP.NET culture cookie (`CookieRequestCultureProvider.DefaultCookieName`, `c=<tag>|uic=<tag>`);
2. `Accept-Language`, matching an exact supported tag first and then its base language (`es-AR` → `es`);
3. the configured default language.

Reject unknown and in-progress values at every step. The server never reads the account for per-request language
resolution. After sign-in, the SPA obtains `PreferredLanguage` from the identity context and writes the culture
cookie, so subsequent server requests receive the account preference through that cookie. Persist a signed-in choice
to the account, set the SPA document's `<html lang>` to the resolved language, and keep locale and time-zone selection
separate even when their initial values are derived from the language.

## Backend resources and delivery

Use `Microsoft.Extensions.Localization` with `.resx` resources. The neutral resource is English source; add one
culture resource for every supported non-English language:

```text
src/Infrastructure/Localization/Emails.resx
src/Infrastructure/Localization/Emails.es.resx
```

Use stable semantic resource names such as `Invitation.Subject` and `Invitation.ExpiresAt`. Preserve identical
format placeholders in every resource. At delivery, the handler loads from the database the recipient, their
preferred language or the invitation/intent language snapshot, and the template values. It then resolves delivery
language from the recipient account preference, then the snapshot, then the configured default, and passes the result
as an explicit `CultureInfo`:

```csharp
var culture = CultureInfo.GetCultureInfo(resolvedLanguage);
var template = Emails.ResourceManager.GetString(name, culture)
    ?? throw new InvalidOperationException("The email resource is missing.");
var rendered = string.Format(culture, template, args);
```

The delivery renderer uses that explicit culture for resource lookup and interpolation; it neither reads nor mutates
ambient worker, request, or machine culture. Outbox payloads carry identifiers only—never rendered prose, template
arguments, or personal data (IA-REQ-029). Logs, audits, exceptions, OpenAPI, routes, codes, IDs, and URLs remain
invariant English.

## Required gates

| Gate | Must prove |
| --- | --- |
| Catalog parity | Supported catalogs match `en` semantic keys, contain no empty values, preserve identical named placeholders, and provide every plural category each language itself requires; category sets may differ. |
| Registry parity | SPA and backend source/default/supported/in-progress sets are identical. |
| Problem-code coverage | Every code exposed through `x-problem-codes` has `errors:<code>` in every supported catalog. |
| Backend parity | Every supported culture resource has every source name, non-empty, with matching placeholders. |
| Delivery matrix | Every server-delivered message type renders successfully in every supported language. |
| SPA PR gate | SPA tests run deterministically in `en`, and lint runs on every pull request. |
| Journey gate | Existing journeys stay deterministic in `en`; each supported non-English language has a smoke journey. |

## Add-a-key checklist

- [ ] Classify the value as system text, user content, or invariant technical data.
- [ ] Choose a stable namespace and key; for a protocol value, preserve its code and required key shape.
- [ ] Add the `en` source value, verbatim when extracting existing copy.
- [ ] Add a non-empty value for every supported language; draft neutral professional Spanish and flag uncertain
      wording for native review.
- [ ] Preserve named placeholders and rich-text components; provide every plural form the target language requires.
- [ ] Use `t()`, `Trans`, `useFormat()`, or an explicitly cultured backend resource at the delivery boundary.
- [ ] Preserve accessible and test contracts, then run every applicable gate above.
