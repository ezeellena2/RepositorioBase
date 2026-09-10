---
name: localization-standards
description: "Trigger: any change that adds or changes text a person reads (SPA, email, bot) or adds an API error code, enum value, permission or status. Apply the repository's cross-project localization standard."
license: Apache-2.0
metadata:
  author: repository-maintainers
  version: "1.0"
---

## Activation Contract

Load for any change that adds or changes human-readable SPA, email, or bot text, or adds an API error code,
enum value, permission, or status. Read the reference before editing.

## Hard Rules

1. Make the server speak invariant codes; make the SPA translate them.
2. Make the delivery owner translate server-sent text with an explicit recipient culture.
3. Treat `en` as source. Complete every supported language in the same change; never expose `inProgress` languages.
4. Keep language, locale, and time zone distinct.
5. Never translate user-authored content; always translate system-owned content by key.
6. Keep logs, audits, outbox payloads, codes, identifiers, URLs, developer exceptions, and OpenAPI invariant English.
7. Add a language as catalog, resource, and registry data; promote it to supported only when complete.

## Decision Gates

| Change | Required action |
| --- | --- |
| Human-readable text | Add the `en` value and every supported-language value in the same change. |
| API error | Add `errors:<code>` in every supported language. |
| Validation code | Add `errors:validation.<code>` in every supported language. |
| Enum or status | Add `enums:<type>.<value>` in every supported language. |
| Permission | Add `enums:permissions.<code>` in every supported language. |
| Built-in role | Add `enums:roles.system.<name>` in every supported language. |
| Server-sent text | Add backend resources for every supported culture and pass culture explicitly. |
| Tempted to return display text | Return a code; let the SPA translate it. |
| Spanish is uncertain | Draft neutral professional Spanish and flag native review; never leave it empty. |
| English copy is bad | Make a separate copy change in every supported language. |

## Execution Steps

1. Classify each changed value as system text, user content, or invariant technical data.
2. Add or preserve the stable key/code, then update all required catalogs or resources.
3. Preserve placeholders, plurals, accessible names, and explicit formatting/culture boundaries.
4. Run the applicable parity, placeholder, lint, test, rendering, and journey gates from the reference.

## Output Contract

Report keys/codes added, languages and resources updated, explicit culture/formatting behavior, checks run, and
any translation awaiting native review.

## References

- [Localization rules](references/localization-rules.md) — layouts, key conventions, examples, server culture,
  checklists, and gates.
