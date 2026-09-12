# Adding a supported language

Add a language only after the business requests it. Stage it as `inProgress`, complete every catalog and resource,
then promote the SPA registry, backend registry, MUI mapping, journey dataset, and database constraints together.
A language is not supported until every gate below is green.

## Quick path

1. Register the language as `inProgress` in both registries.
2. Add and translate the five SPA catalogs and the backend email resource.
3. Prepare the MUI locale mapping and registry-driven journey row.
4. Promote both registries and update all six database language constraints in one change.
5. Generate the EF migration and run the focused and full gates.

Do not add `en-XA` through this workflow. It is generated from English only for local development when the exact
query `?lng=en-XA` is present at SPA startup. That override remains active through client-side navigation until a
reload without the query. It is never a supported or in-progress language, catalog directory, MUI mapping, journey
row, cookie, account preference, backend value, or database value.

## 1. Confirm the language tag and ownership

Use one canonical language tag accepted by `CultureInfo.GetCultureInfo` and `Intl.getCanonicalLocales`. Confirm:

- the business owner and reviewers for the translation;
- whether Material UI publishes locale data for the tag;
- that `en` remains the source language and the deployment default unless a separate decision changes them;
- that the translation platform decision has been revisited if non-developers now need to manage translations.

There is no translation platform today. Repository-owned JSON and RESX files are authoritative.
Runtime matching is case-insensitive, but the SPA always returns and persists the registry's canonical spelling.
An exact registered tag wins before region or script fallback to a registered base language.

## 2. Stage the language as in progress

Add the tag to `inProgress`, not `supported`, in both:

- `src/Web/ClientApp/src/i18n/languages.json`;
- `src/Application/Common/Localization/LocalizationRegistry.cs`.

The registry parity contract must stay green. An in-progress language is never negotiated, selectable, persisted, or
used for delivery. Its catalog gaps are reported without failing supported-language enforcement.

Create these five files, using the English files as the semantic-key source:

```text
src/Web/ClientApp/src/i18n/locales/<language>/common.json
src/Web/ClientApp/src/i18n/locales/<language>/errors.json
src/Web/ClientApp/src/i18n/locales/<language>/enums.json
src/Web/ClientApp/src/i18n/locales/<language>/identity.json
src/Web/ClientApp/src/i18n/locales/<language>/platform.json
```

Add `language.<language>` with the language's autonym to every existing supported `common.json` and to the new
`common.json`. Preserve all interpolation placeholders, rich-text tags, and locale-specific plural categories.
Translate neutral professional copy; never translate user-authored content, codes, routes, identifiers, URLs, or
protocol data.

Add the backend catalog:

```text
src/Infrastructure/Localization/Emails.<language>.resx
```

It must contain every name from `Emails.resx`, with non-empty values and identical numeric placeholders. Keep the
new tag in `inProgress` while translators and reviewers resolve the reported gaps.
Resource discovery recognizes a localized catalog only when its complete suffix matches a supported or in-progress
registry tag case-insensitively; suffix length is not a language-tag contract.

## 3. Prepare promotion data

Material UI and the browser journey are supported-language gates, so add them in the same promotion change rather
than leaving either as extra in-progress data:

- import the locale from `@mui/material/locale` and add the tag to the explicit
  `muiLocaleByLanguage` map in `src/Web/ClientApp/src/theme.jsx`;
- add `{ "language": "<language>" }` to `journeys` in
  `src/Web/ClientApp/src/i18n/languages.json`.

The MUI map keys must equal `supported` exactly. The journey language set must equal `supported` minus `source`,
with unique canonical tags. Reqnroll.ExternalData reads that object-array dataset; do not add language-specific
Gherkin, C# labels, or page methods.

## 4. Promote with database defense in depth

After translation and editorial review are complete, move the tag from `inProgress` to `supported` in both
registries. In the same change, include the MUI mapping and journey row prepared above.

Promotion is not migration-free. Update the exact-supported-language SQL predicate in each current EF model
configuration:

```text
src/Infrastructure/Data/Configurations/IdentityAccess/ApplicationUserConfiguration.cs
src/Infrastructure/Data/Configurations/IdentityAccess/InvitationConfiguration.cs
src/Infrastructure/Data/Configurations/IdentityAccess/OutboxMessageConfiguration.cs
src/Infrastructure/Data/Configurations/IdentityAccess/PendingPersonalIntentConfiguration.cs
src/Infrastructure/Data/Configurations/IdentityAccess/PendingRegistrationIntentConfiguration.cs
src/Infrastructure/Data/Configurations/IdentityAccess/PlatformAdminInvitationConfiguration.cs
```

Then generate and inspect a migration:

```bash
dotnet ef migrations add AddSupportedLanguageConstraints --project src/Infrastructure/Infrastructure.csproj --startup-project src/Web/Web.csproj --output-dir Data/Migrations
```

The migration must replace all six database constraints without weakening nullability or admitting any unregistered
tag. Do not promote the registries without this migration: application validation and database defense in depth must
accept the same supported set.

## 5. Run the gates

Run focused localization checks first:

```bash
cd src/Web/ClientApp
npx vitest run src/i18n/catalog.contract.test.js src/i18n/pseudoLocale.test.js src/i18n/pseudoLanguageOverride.test.jsx src/i18n/spanish.test.jsx
npm run i18n:unused
npm run lint
cd ../../..

dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter "FullyQualifiedName~LocalizationContractTests|FullyQualifiedName~IdentityEmailDeliveryMatrixTests" --disable-build-servers -p:UseSharedCompilation=false
dotnet test tests/Web.AcceptanceTests/Web.AcceptanceTests.csproj --filter "TestCategory=Localization" --disable-build-servers -p:UseSharedCompilation=false -p:OpenApiGenerateDocumentsOnBuild=false
```

Those checks cover registry and catalog parity, problem-code coverage, placeholders and plurals, explicit MUI locale
mapping, generated journey coverage, backend resources, and every email delivery variant in every supported
language.

The unused-key gate intentionally scans only the statically referenced `common`, `identity`, and `platform`
namespaces. `errors` and `enums` use runtime-built keys and remain outside that scanner; their dedicated
problem-code, validation-code, permission, enum, role, and status coverage contracts are authoritative. Do not widen
`preservePatterns` to hide a finding.

Then run build and the complete repository gates:

```bash
dotnet build CleanArchitecture.slnx --disable-build-servers -p:UseSharedCompilation=false

cd src/Web/ClientApp
npm run build
cd ../../..

dotnet test --filter "TestCategory!=IndependentDevelopmentReview" --disable-build-servers -p:UseSharedCompilation=false
git diff --check
```

## Rollback and demotion

Before deployment, revert an incomplete promotion as one unit: supported registries, MUI mapping, journey row,
database model changes, and migration. The translated catalogs may remain staged under `inProgress`.

After deployment or after the tag has been persisted, do not merely remove it from `supported`. First decide how
account preferences, invitation and intent snapshots, and pending outbox delivery languages will be migrated without
changing retry meaning. Ship the data migration and constraint transition before demoting the registries. Keep
catalogs and resources available while retained data can still reference the language.

Removing an abandoned `inProgress` language is safer because it was never persisted or negotiated, but remove its
registry entries and unfinished files together and keep registry parity green.
