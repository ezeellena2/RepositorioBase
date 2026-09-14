```yaml
schema: gentle-ai.verify-result/v1
evidence_revision: sha256:e8972f3494c117f3bf80aea7044e153f29e035cc10d51d3794ed5788ac94a639
verdict: pass_with_warnings
blockers: 0
critical_findings: 0
requirements: 30/30
scenarios: 78/78
test_command: cd src/Web/ClientApp && npx vitest run && npx eslint src/ && npx vite build
test_exit_code: 0
test_output_hash: sha256:2bc09525c9fc913bb0018aa67b06a46b1420b27b808884494057551d408c9087
build_command: dotnet build --configuration Release
build_exit_code: 0
build_output_hash: sha256:543b89be43bfaf317aacc38081e951eec5ca7f958bbbc59516c413d9ebcc64a6
```

## Verification Report

**Change**: offset-pagination-standard
**Version**: N/A (delta specs of 2026-09-13; `spa-pagination-ui` "Mutation outcome survives a failed refresh" amended 2026-09-14, decision C3-V01)
**Mode**: Strict TDD (artifact store hybrid; `openspec/config.yaml` `strict_tdd: true`)
**Tree**: `main` at `f29ca74` (a parallel-chat commit on top of baseline `2716aa6`), `origin/main` `f29ca74`, nothing staged. Verified on 2026-09-14, 14:01–14:28 (-03:00).

Evidence conventions:
- `<scratchpad>` is `C:/Users/ezequ/AppData/Local/Temp/claude/C--Users-ezequ-source-repos-RepositorioBase/44206285-9004-463b-be7a-03b29e719c1b/scratchpad`. Every gate log is `<scratchpad>/verify-*.log` or `verify-*.txt`.
- `evidence_revision` is the sha256 of `<scratchpad>/verify-evidence-manifest.sha256`, which lists the sha256 of 67 evidence files (every `verify-*` log and exit file, plus both LIST-CODES snapshots).
- `test_output_hash` is the sha256 of the concatenated raw logs of the three `test_command` steps (`verify-13.2-vitest.log`, `verify-13.2-eslint.log`, `verify-13.2-vite.log`). `build_output_hash` is the sha256 of `verify-13.3-build-release.log`.
- Paths: `SPA` = `src/Web/ClientApp/src`; `FT` = `tests/Application.FunctionalTests/IdentityAccess`; `UT` = `tests/Application.UnitTests`.

### Completeness
| Metric | Value |
|--------|-------|
| Tasks total | 120 |
| Tasks complete | 120 |
| Tasks incomplete | 0 |

Phase 13 (13.1–13.8) and Delivery (DL.1–DL.6) are procedure steps without checkboxes (decision #1142). This verification ran 13.1–13.8.

### Build & Tests Execution

**Build**: ✅ Passed
```text
$ dotnet build CleanArchitecture.slnx -v minimal      -> exit 0, 0 errors, 2 warnings (ASPIRE010 on AppHost and TestAppHost; they predate the change)
$ dotnet build --configuration Release                -> exit 0, 0 errors, the same 2 ASPIRE010 warnings (verify-13.3-build-release.log)
$ dotnet build src/Web/Web.csproj                     -> exit 0; regenerated src/Web/wwwroot/openapi/v1.json (13.1)
```

**Tests**: ✅ 2,242 passed / ❌ 5 failed (all 5 are recorded baselines in categories CI excludes) / ⚠️ 0 skipped
```text
$ cd src/Web/ClientApp && npx vitest run      -> exit 0, Test Files 46 passed (46), Tests 632 passed (632), 69.79 s
$ cd src/Web/ClientApp && npx eslint src/     -> exit 0, no output
$ cd src/Web/ClientApp && npm run i18n:unused -> exit 0, "No unused keys found" for common, identity and platform
$ cd src/Web/ClientApp && npx vite build      -> exit 0 (only the known chunk-size notice)
$ dotnet test tests/Domain.UnitTests/Domain.UnitTests.csproj --filter "TestCategory!=IndependentDevelopmentReview"                       -> exit 0, 209/209
$ dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter "TestCategory!=IndependentDevelopmentReview"             -> exit 0, 246/246
$ dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "TestCategory!=IndependentDevelopmentReview"  -> exit 0, 754/754 (7 m 26 s)
$ dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter "TestCategory!=IndependentDevelopmentReview" -> exit 0, 370/370 (38 s)
$ dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter "TestCategory=IndependentDevelopmentReview" -> exit 1, 3 failed / 0 passed (baseline: intentional since b9a30d1)
    A_held_first_document_does_not_starve_the_next_eligible_subject; A_held_revoked_session_is_preserved_until_hold_release; Erasing_a_revoked_session_writes_required_policy_evidence
$ dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "TestCategory=IndependentDevelopmentReview"   -> exit 1, 2 failed / 2 passed (baseline, identical to the 1.1 record)
    Ownership_cannot_be_transferred_to_a_self_deactivated_identity_with_an_active_membership; Suspending_a_member_requires_the_recent_primary_proof_retained_by_accepted_C6
$ dotnet test tests/Web.AcceptanceTests/Web.AcceptanceTests.csproj --disable-build-servers -p:UseSharedCompilation=false -p:OpenApiGenerateDocumentsOnBuild=false -> exit 0, 31/31 (1 m 37 s), Spanish smoke included
```
Every run was sequential, with one Aspire-bearing project per process.

**Coverage**: ➖ Not available / threshold: 0%. The SPA has no `@vitest/coverage-*` dependency. `coverlet.collector` is present in the .NET test projects, but it is not wired into CI or into the declared verification commands, so no coverage run was added.

### Gate evidence (tasks.md Phase 13)
| Step | Command | Exit | Result |
|------|---------|------|--------|
| 13.1 processes | CIM process listing (`verify-13.1-prereq.log`) | 0 | No `find.exe`, vitest, testhost, vstest, `dotnet test` or RepositorioBase AppHost process. CPU load 0% (2% before 13.2); 16 logical processors |
| 13.1 Docker | `docker info`, `docker ps` | 0 | Docker 29.3.1, 3 containers running |
| 13.1 Playwright | `artifacts/bin/Web.AcceptanceTests/debug/playwright.ps1`; `%LOCALAPPDATA%/ms-playwright` | 0 | The script exists, and `chromium-1234` and `chromium_headless_shell-1234` are present, so nothing was installed |
| 13.1 OpenAPI | `dotnet build src/Web/Web.csproj` | 0 | `v1.json` was rewritten at 14:01 (236,773 bytes): `"pageNumber"` 21 occurrences, `nextCursor` 0, `cursor` 0, `limit` parameter 0 |
| 13.2 Vitest | `npx vitest run` | 0 | 46/46 files, 632/632 tests. The baseline-timeout rule was used 0 times, because nothing failed |
| 13.2 lint | `npx eslint src/` | 0 | Clean |
| 13.2 unused keys | `npm run i18n:unused` | 0 | No unused keys |
| 13.2 build | `npx vite build` | 0 | Built |
| 13.2 suppressions | `rg -n "eslint-disable" src/Web/ClientApp/src` | 0 | 14 lines, equal to the 11A.11 result: the 9 recorded at 1.1, plus 5 `react-hooks/exhaustive-deps` (`RolesPage.jsx:172`, `MembersPage.jsx:167`, `InviteMemberPage.jsx:149`, `PlatformPanel.jsx:130`, `PlatformIdentitiesPage.jsx:232`). No `i18next` suppression was added |
| 13.3 | `dotnet build CleanArchitecture.slnx -v minimal`; `dotnet build --configuration Release` | 0; 0 | Both build; 0 errors |
| 13.4 | NET-G DT, UT, FT, IT; both IndependentDevelopmentReview categories | 0,0,0,0; 1,1 | 209, 246, 754 and 370 passed. The IT category has exactly the 3 intentional failures. The FT category has 2 failed and 2 passed, identical to the 1.1 record |
| 13.5 | journeys command as written | 0 | 31/31, which is the 31 scenario headings across 6 feature files; `Localization.feature` generates one `TestCase("es")` from `languages.json` `journeys` |
| 13.6 | `git status --short --untracked-files=all -- tests src/Web/ClientApp/src` plus a row comparison (`verify-13.6-compare.log`) | 0 | 82 entries, 42 of them test files, every one a declared row. Moved-from rows 8, 17 and 18 show ` D`. Rows 22 and 33 are unchanged. `DelegatedAdministrationRevalidationTests.cs` is unchanged against HEAD. `git status --short -- tests` lists 17 entries, exactly rows 24–32 and 34–41. RESULT: PASS |
| 13.7 8A.8 | the SPEC search, the reviewed-exclusion search, and the ADR-004 and TRACEABILITY checks | see result | Search 1 matches only `SPEC.md:1072` (C5 history). The exclusion search matches only `TASKS.md:936`, `TASKS.md:940`, `SPEC.md:702` (the sessions statement) and `SPEC.md:1072`. The ADR-004 `cursor` search has no match (exit 1). The status at `:3` is Proposed. Decision 17 (`:36`) names typed bounded offset pages. In TRACEABILITY the retired test name has 0 matches; the renamed test is at `:40` and `:112`, and `:112` names the "seven paging-parameter routes". `usePlatformRead` and "More accounts" have 0 matches |
| 13.7 12.1 | both searches | 0; 0 | Search 1 finds 19 lines, each under a reviewed exclusion: `StackBaselineTests.cs` 7, `problemDetails.js:14` 1, `problemDetails.test.js:131,137,140,141`, `platformClient.test.js:188,189`, `PlatformDirectoryContractTests.cs:70,136`, `ProblemDetailsContractTests.cs:381,384,437`. `web-api-client.ts` has none. Search 2 finds 14 lines, all rate limits or attempt budgets |
| 13.7 12.5 | the three design searches, plus the `ui-composition-rules.md` cursor search | 1,1,1,1 | Nothing found. `:316` still forbids adding pagination, and `:317` preserves real offset `TablePagination` controls |
| 13.7 12.7 | the catalogue, the words and resources, and the forbidden codes | 0; 0; 0; 1 | `problemCodes.json`: the blob `cb1efdcc…720d` is equal at `2716aa6` and now, and the bytes are identical with CR removed. The `errors.json` en/es, `IdentityAccessErrors.cs` and `*.resx` diffs are empty against HEAD and against `2716aa6`. No forbidden code exists |
| 13.7 12.8 | the key search, `i18n:unused`, `catalog.contract.test.js`, the locales diff | 1; 0; 0; 0 | No key call site remains. No unused keys. 17/17. The locales differ from `2716aa6` in 4 files (+8/−18): exactly the five deleted keys, in `en` and `es`, plus comma-only rewrites. `identities.retry` is still "Try again" and "Volver a intentar" |
| 13.7 11.15 | the label-prop search (non-test files) | 1 | No `labelRowsPerPage`, `labelDisplayedRows` or `getItemAriaLabel` prop |
| 13.7 LIST-CODES | the tasks.md `node -e` command, then `diff` against the baseline | 0 | `list-problem-codes.verify.json` is identical to `list-problem-codes.baseline.json` |
| 13.8 static checks | `rg` and `sed` (the output could not be saved to a log: the C: drive was full, see WARNING 6; the results are recorded here) | 0 | Exactly 8 `ToAcceptedHttpResult(context, problems)` callers. The EF helper runs `CountAsync`, then `Skip`/`Take`/`ToListAsync`. `MAX_WALK_PAGES = 100`. `useDirectoryPages` is local to `PlatformPanel.jsx`. The four Domain IDs have no `IComparable`, `CompareTo` or comparison operator (exit 1). The cross-tenant members/invitations read uses `?pageNumber=1&pageSize=25` |

### Spec Compliance Matrix
| Requirement | Scenario | Test | Result |
|-------------|----------|------|--------|
| Offset page parameters | OpenAPI names the offset parameters | `FT/Platform/PlatformDirectoryContractTests.cs > Every_list_route_declares_offset_parameters_and_its_binding_refusal`, `> Each_directory_is_declared_as_a_bounded_get` | ✅ COMPLIANT |
| Offset page parameters | A requested page is served | `FT/Roles/RoleAdministrationTests.cs > A_requested_role_page_carries_its_offset_metadata_and_clamps_the_page_size` (30 roles, page 2: 5 items, totalCount 30) | ✅ COMPLIANT |
| Offset page response | A middle page reports both neighbours | `UT/Common/Models/PaginatedListTests.cs > Computes_total_pages_and_navigation_flags` | ✅ COMPLIANT |
| Offset page response | An empty collection | `PaginatedListTests > An_empty_collection_has_no_pages_and_no_neighbours`; `SPA/api/pagination.test.js > returns an empty collection unchanged` | ✅ COMPLIANT |
| Offset page response | No cursor or envelope member | `FT/Api/ProblemDetailsContractTests.cs > A_list_success_is_its_offset_page_dto_with_no_cursor_or_envelope_member` (a tenant list and a Platform directory) | ✅ COMPLIANT |
| Defaults and clamping within Int32 | Tenant lists default to 25 rows | `FT/Members/AdministrationDirectoryRevalidationTests.cs > R6C_The_next_page_is_executable_and_omits_or_duplicates_no_records` (members/roles/invitations; a first GET with no parameters gives 25 items, pageSize 25, totalCount 26, hasNextPage true; 26-row fixture); `UT/Common/Models/PaginationQueryTests.cs > Defaults_to_first_page_with_twenty_five_items` | ✅ COMPLIANT |
| Defaults and clamping within Int32 | A huge page number is clamped, not a fault | `PlatformDirectoryContractTests > Out_of_range_pages_are_clamped_answered_and_never_logged_as_errors` (handler level: success, 21474836/100, empty); `PaginationQueryTests > A_huge_page_number_is_clamped_so_skip_never_overflows` | ✅ COMPLIANT (handler level; WARNING 1) |
| Defaults and clamping within Int32 | A zero page size returns one row | `PlatformDirectoryContractTests > Out_of_range_pages_are_clamped_answered_and_never_logged_as_errors` ((0,0) gives 1/1 and 1 row); over HTTP, `RoleAdministrationTests > A_requested_role_page_carries_its_offset_metadata_and_clamps_the_page_size` (`pageSize=0`: pageSize 1, 1 item) | ✅ COMPLIANT |
| Past-the-end page | A page past the end is an empty page | `PlatformDirectoryContractTests > A_page_past_the_end_is_an_empty_page_with_the_real_totals_not_a_refusal`; `PaginatedListTests > A_page_past_the_end_keeps_the_real_totals_and_has_no_next_page` | ✅ COMPLIANT (handler and model level; WARNING 1) |
| Past-the-end page | Another tenant's list stays not found | `RoleAdministrationTests > Read_scope_mismatches_are_native_absence_for_real_and_random_tenants`; `FT/Members/MembershipAdministrationTests.cs > Member_and_invitation_read_scope_mismatches_are_native_absence_for_real_and_random_tenants` (offset parameters, `404 not_found`) | ✅ COMPLIANT |
| Deterministic page order | Consecutive pages neither repeat nor skip | `AdministrationDirectoryRevalidationTests > R6C_…` (pages 1 and 2 at size 25 equal `?pageSize=100` in order; no overlap; the union equals the seed) | ✅ COMPLIANT |
| Deterministic page order | Audit stays newest first across pages | `PlatformDirectoryContractTests > The_audit_directory_is_newest_first_and_pages_backwards_in_time` | ✅ COMPLIANT |
| Cursor pagination retired | Platform list queries carry an offset request | `UT/Architecture/PlatformApplicationShapeTests.cs > A_directory_query_carries_only_a_bounded_offset_page` | ✅ COMPLIANT |
| Cursor pagination retired | No cursor-pagination leftovers | 13.7 rerun of 12.1: 19 lines, all under reviewed exclusions | ✅ COMPLIANT (search) |
| Section 4 states the offset contract | IA-REQ-045 describes offset directories | `SPEC.md:246` read; 13.7 8A.8 search 1 finds only `:1072` | ✅ COMPLIANT (document) |
| Section 4 states the offset contract | IA-REQ-038 carries the paging rules | `SPEC.md:203-208` read (default 25, 1–100, clamped, `400 invalid_request`, empty `items` past the end); the no-envelope sentence remains at `:201` | ✅ COMPLIANT (document) |
| Route rows carry the offset shape | Rows and scenario name offset paging | `SPEC.md:313-316`, `:471-472`, `:1040`, `:1044` read; 8A.8 search 1 | ✅ COMPLIANT (document) |
| Route rows carry the offset shape | C5 history is kept with a dated note | `SPEC.md:1072` unchanged, followed by the 2026-09-13 note at `:1073-1075` | ✅ COMPLIANT (document) |
| Evidence and ADR follow the amendment | Only reviewed exclusions remain | 13.7 reviewed-exclusion search: `TASKS.md:936,940`, `SPEC.md:702,1072` | ✅ COMPLIANT (search) |
| Evidence and ADR follow the amendment | The migrated binding test is the cited proof | `TRACEABILITY.md:112` cites the renamed test and the seven routes; the retired name has 0 matches | ✅ COMPLIANT (search) |
| Evidence and ADR follow the amendment | ADR decision 17 names offset pages | ADR-004 `:36` "typed bounded offset page"; `cursor` 0 matches; `:3` Proposed | ✅ COMPLIANT (search) |
| Paging parameter binding refusal | A non-integer page number is refused | `FT/Api/OpenApiContractTests.cs > Every_page_parameter_binding_refusal_is_emitted_only_as_declared` (7 routes; problem+json with code, traceId and status) | ✅ COMPLIANT |
| Paging parameter binding refusal | A beyond-Int32 paging value is refused, not clamped | same test (`pageNumber=2147483648`, `pageSize=2147483648`) | ✅ COMPLIANT |
| Paging parameter binding refusal | Every list route declares the refusal | `PlatformDirectoryContractTests > Every_list_route_declares_offset_parameters_and_its_binding_refusal`; LIST-CODES | ✅ COMPLIANT |
| Declared and emitted codes are unchanged | Declarations do not move | 13.7 LIST-CODES identical to the 1.1 baseline | ✅ COMPLIANT (snapshot) |
| Declared and emitted codes are unchanged | Every Platform directory requires the second factor | `FT/Platform/PlatformDirectoryAccessTests.cs > A_password_only_session_reads_no_directory` (four `recent_mfa_required`); `OpenApiContractTests > Platform_contracts_declare_the_bounded_code_gates_and_the_second_factor_directories` (`401` on all four) | ✅ COMPLIANT (handler and contract; WARNING 1) |
| Declared and emitted codes are unchanged | Out-of-range values never become `validation_failed` | `ProblemDetailsContractTests > Out_of_range_platform_page_sizes_are_clamped_over_http_and_never_refused_as_validation` | ✅ COMPLIANT |
| Pagination adds no error code | Catalogue and words are byte-identical | 13.7 rerun of 12.7; `OpenApiContractTests > Served_problem_codes_and_checked_in_client_catalogue_are_the_same_contract` | ✅ COMPLIANT |
| Pagination adds no error code | No pagination code is invented | 13.7 forbidden-codes search, exit 1 | ✅ COMPLIANT (search) |
| Expected paging outcomes are not errors | Clamped and empty pages log nothing at Error | `PlatformDirectoryContractTests > Out_of_range_pages_are_clamped_answered_and_never_logged_as_errors` | ✅ COMPLIANT |
| Expected paging outcomes are not errors | A binding refusal logs nothing at Error | `OpenApiContractTests > Every_page_parameter_binding_refusal_is_emitted_only_as_declared` | ✅ COMPLIANT |
| Strict client page reader | The retired cursor shape is drift | `SPA/api/problemDetails.test.js > refuses the retired cursor shape even when items are declared`; `SPA/features/platform/platformClient.test.js > %s reports the retired cursor shape as an unreadable response` | ✅ COMPLIANT |
| Strict client page reader | Malformed metadata is drift, not a generic failure | `SPA/features/identity/api/identityClient.test.js > %s reports a page number of 0 as an unreadable response, not as data or a generic failure` | ✅ COMPLIANT |
| Strict client page reader | A well-formed page is returned as sent | `identityClient.test.js > %s returns a well-formed page exactly as it was answered`; `pagination.test.js > returns %s unchanged` | ✅ COMPLIANT |
| Null-safe bounded paging arguments | A null page reads the default first page | `identityClient.test.js > %s reads a null page as the default first page of the tenant %s`; `pagination.test.js > bounds a null page without failing, as the role pickers pass one` | ✅ COMPLIANT |
| Null-safe bounded paging arguments | Out-of-range and non-numeric values are bounded | `pagination.test.js > clamps an out-of-range page number and page size into the contract`, `> reads %s as the default first page` | ✅ COMPLIANT |
| Module-neutral problem modules | No retired import path remains | 13.7 rerun of the 12.5 retired-path search over `SPA` and `.agents/skills`, exit 1 | ✅ COMPLIANT (search) |
| Module-neutral problem modules | The renderer depends on no feature | 13.7 import search: no `features/` import in `api/{problemDetails,apiTransport,pagination}.js`, `components/{ProblemMessage.jsx,problemFields.js}`; `useRead.js`, `useSubmit.js` and `fieldErrors.js` are still in `features/identity` | ✅ COMPLIANT (search) |
| Module-neutral problem modules | Platform renders a problem exactly as before | `SPA/features/platform/PlatformPanel.test.jsx > keeps a displayed directory through a failed page change and retries the page that was asked for` (one alert: "Something went wrong. Try again." and "Reference: trace-refresh"); `> offers a retry for a directory server failure and loads that section afterwards` | ✅ COMPLIANT |
| Field-error helpers have one module-neutral owner | No module outside Identity imports Identity field-error helpers | 13.7 boundary search, exit 1 | ✅ COMPLIANT (search) |
| Field-error helpers have one module-neutral owner | Identity keeps only its registration rules | 13.7 `fieldErrors.js` imports and exports (1 import from `./register/cuit`, 4 exports); importers are exactly `PersonalPages.jsx`, `RegisterOrganizationPage.jsx` and `fieldErrors.test.js` | ✅ COMPLIANT (search) |
| Field-error helpers have one module-neutral owner | A Platform field error still renders on its field | `SPA/features/platform/invitations/MfaRecoveryPage.test.jsx > associates and focuses a recovery-code validation detail without marking the proof password` | ✅ COMPLIANT |
| Error words unchanged by the move | Spanish words survive the move | `SPA/i18n/spanish.test.jsx > keeps a failed language detail in the Spanish general alert and returns focus to that alert`; `SPA/i18n/catalog.contract.test.js` 17/17; the `es/errors.json` diff is empty | ✅ COMPLIANT |
| Error words unchanged by the move | Unclaimed field errors still render | `SPA/components/ProblemMessage.test.jsx > omits case-insensitively claimed errors and links rendered field errors without exposing raw keys` | ✅ COMPLIANT |
| Error words unchanged by the move | No word or resource file changes | 13.7 rerun of 12.7: the `errors.json` en/es and `*.resx` diffs are empty | ✅ COMPLIANT (snapshot) |
| One catalogue, same gates | The served catalogue equals the checked-in one | `OpenApiContractTests > Served_problem_codes_and_checked_in_client_catalogue_are_the_same_contract` | ✅ COMPLIANT |
| One catalogue, same gates | Every code has words in every supported language | `SPA/features/identity/problemCatalogue.contract.test.js > covers the complete API and client vocabulary in %s without orphaned message keys` (en, es), `> keeps transport-only codes out of the API catalogue`, `> reads every supported language, the source included, from the registry for the words gate` | ✅ COMPLIANT |
| One catalogue, same gates | An undeclared fixture pair is refused | `problemCatalogue.contract.test.js > allows MSW fixtures only for status and code pairs declared by the API` (the same guard path; it uses `not_a_problem_code` instead of `invalid_page`) | ✅ COMPLIANT |
| Result stays internal behind one mapper | No Result or envelope crosses HTTP | `ProblemDetailsContractTests > List_create_and_bodyless_successes_carry_no_universal_envelope` | ✅ COMPLIANT |
| Result stays internal behind one mapper | A refusal goes through the writer | `ProblemDetailsContractTests > A_refused_list_read_is_a_problem_document_from_the_shared_writer` | ✅ COMPLIANT |
| Standards documents match the new contract | No standards document names a retired path | 13.7 rerun of 12.5 (retired paths and `features/identity/fieldErrors` in `.agents/skills`), exit 1 | ✅ COMPLIANT (search) |
| Standards documents match the new contract | The composition rules speak of offset controls | 13.7: `ui-composition-rules.md` `cursor` exit 1; `:316` still forbids invented pagination | ✅ COMPLIANT (search) |
| Page control on every collection | Tenant lists reach the 26th record | `SPA/features/identity/roles/RolesPage.test.jsx > reaches the 26th role by page number, and comes back to the first page`; `members/MembersPage.test.jsx > reaches the 26th member by page number`; `invitations/InviteMemberPage.test.jsx > reaches the 26th invitation by page number` | ✅ COMPLIANT |
| Page control on every collection | Changing rows per page restarts at page 1 | `RolesPage.test.jsx > restarts at the first page when the rows per page change` | ✅ COMPLIANT |
| Page control on every collection | Administrators and audit are reachable past page 1 | `PlatformPanel.test.jsx > moves the $title directory to page 2 on its own and shows rows 26 to 30` (Organizations, Administrators, Audit) | ✅ COMPLIANT |
| Page control on every collection | A single page and an empty collection | `RolesPage.test.jsx > offers no way to another page when every role fits on one`, `> keeps the empty state and draws no page control when the organization has no roles` | ✅ COMPLIANT |
| Page control on every collection | The permission catalogue loads once | `RolesPage.test.jsx > asks for the permission catalogue once, however many pages are turned` | ✅ COMPLIANT |
| Page control on every collection | A failed permission catalogue holds the roles back | `RolesPage.test.jsx > holds the roles back when the permission catalogue cannot be reached, and retries only the catalogue` | ✅ COMPLIANT |
| Read states per page | A failed page change keeps the rows and the control | `RolesPage.test.jsx > keeps the rows and the control on the loaded page when the next page cannot be reached` | ✅ COMPLIANT |
| Read states per page | A refused page clears the rows | `RolesPage.test.jsx > clears the rows and explains a refused page in place` | ✅ COMPLIANT |
| Read states per page | Only the last requested page renders | `RolesPage.test.jsx > renders only the last page asked for when clicks outrun the network` | ✅ COMPLIANT |
| Read states per page | PlatformPanel retries the requested page | `PlatformPanel.test.jsx > keeps a displayed directory through a failed page change and retries the page that was asked for` | ✅ COMPLIANT |
| Past-the-end correction | A stale page moves to the real last page | `RolesPage.test.jsx > moves to the real last page when the answered page is past the end, and never calls the list empty`; the `PlatformIdentitiesPage.test.jsx` mirror | ✅ COMPLIANT |
| Past-the-end correction | An empty collection is not corrected | `RolesPage.test.jsx > asks once and keeps the empty state when there is no page to correct` | ✅ COMPLIANT |
| Mutation outcome survives a failed refresh | A failed refresh after a retirement | `RolesPage.test.jsx > reads the page the retirement was made from again, and a failed read never says the retirement failed` | ✅ COMPLIANT |
| Bounded resume walks | A target on page 2 resumes exactly once | `SPA/features/identity/ExternalProofResume.test.jsx > resumes a %s target beyond the first page exactly once with its original draft and version` (members, roles; the only parameters are `pageNumber` and `pageSize`) | ✅ COMPLIANT |
| Bounded resume walks | A failed walk reports and does not execute | `ExternalProofResume.test.jsx > reports a %s search that cannot reach the next page, and executes nothing` | ✅ COMPLIANT |
| Role pickers read the whole catalogue | More than 25 roles are all offered | `MembersPage.test.jsx > offers every role in the editor when the organization has more roles than one page carries`; `InviteMemberPage.test.jsx > offers every role when the organization has more roles than one page carries` (requests exactly `[1, 2]`) | ✅ COMPLIANT |
| Role pickers read the whole catalogue | A single page needs one request | `MembersPage.test.jsx > reads the roles with one request when they fit on one page`; `identityClient.test.js > needs one request for a catalogue that fits on one page` | ✅ COMPLIANT |
| Role pickers read the whole catalogue | A walk that never ends is drift | `InviteMemberPage.test.jsx > reports a role catalogue that never ends as an unreadable answer, with a retry and no role offered`; `pagination.test.js > gives up after 100 pages that all claim a next page, as an unreadable response` | ✅ COMPLIANT |
| Role pickers read the whole catalogue | A failed walk renders like a failed roles read | `InviteMemberPage.test.jsx > reports a failed page of the role catalogue in the roles fieldset, and starts the walk again from page 1` | ✅ COMPLIANT |
| Pagination labels come from the MUI locale | Spanish names reach the control | `spanish.test.jsx > gives the pagination control its Spanish next-page name from the MUI locale`, `> composes MUI locales while retaining the original English visual theme` | ✅ COMPLIANT |
| Pagination labels come from the MUI locale | No screen overrides MUI labels | 13.7 rerun of the 11.15 label-prop search, exit 1 | ✅ COMPLIANT (search) |
| Pagination labels come from the MUI locale | A language without a MUI locale fails the gate | `spanish.test.jsx > maps every and only supported language to explicit MUI locale data` | ✅ COMPLIANT |
| Retired show-more keys removed in every language | Both catalog gates pass without the keys | `npm run i18n:unused` exit 0 (13.2, 13.7); `catalog.contract.test.js` 17/17; the key search, exit 1 | ✅ COMPLIANT |
| Retired show-more keys removed in every language | Only the five deletions change the catalogs | 13.7 locales diff against `2716aa6`; `identities.retry` kept in en and es | ✅ COMPLIANT (snapshot) |
| No new text and invariant paging data | Localization lint stays clean | `npx eslint src/` exit 0; the suppression search shows only 5 added `react-hooks/exhaustive-deps` lines | ✅ COMPLIANT |
| No new text and invariant paging data | Developer text stays English, and the person's words are localized | `pagination.test.js > refuses %s` (English `/offset pagination contract/`), `> rejects malformed page metadata as an unreadable response`; `problemCatalogue.contract.test.js > covers the complete API and client vocabulary in %s…` (non-empty `es` `unreadable_response`); `spanish.test.jsx` (Spanish `ProblemMessage`) | ✅ COMPLIANT (composed proof; WARNING 1) |

**Compliance summary**: 78/78 scenarios compliant (30/30 requirements). No scenario is UNTESTED, FAILING or PARTIAL. Four scenarios rest on handler, model or composed evidence rather than an end-to-end request with the scenario's exact input (WARNING 1).

### Correctness (Static Evidence)
| Requirement | Status | Notes |
|------------|--------|-------|
| Offset page parameters | ✅ Implemented | The seven endpoints bind `int? pageNumber, int? pageSize` through `PaginationQuery.From`; the OpenAPI names are invariant |
| Offset page response | ✅ Implemented | Seven endpoint DTOs with a static `From`; `PaginatedList<T>` computes the flags |
| Defaults and clamping within Int32 | ✅ Implemented | `PaginationQuery` clamps: `MaxPageNumber = int.MaxValue / 100`; `pageSize <= 0` means 1 |
| Past-the-end page | ✅ Implemented | The count and page queries return empty items with the real totals; tenant scope `404` is unchanged |
| Deterministic page order | ✅ Implemented | Ordering by `Id`; audit by `OccurredAt DESC, Id DESC` |
| Cursor pagination retired | ✅ Implemented | Cursor types and helpers deleted; Domain comparison members removed (AD17) |
| Section 4 states the offset contract | ✅ Implemented | `SPEC.md:203-208`, `:246` |
| Route rows carry the offset shape | ✅ Implemented | `SPEC.md:313-316`, `:471-472`, `:1040`, `:1044`, `:1073-1075` |
| Evidence and ADR follow the amendment | ✅ Implemented | `TRACEABILITY.md:22-46`, `:112`, `:118`, `:125`; ADR-004 `:36` |
| Paging parameter binding refusal | ✅ Implemented | `WithBodyBindingFailureCode(invalid_request)` kept on the seven routes |
| Declared and emitted codes are unchanged | ✅ Implemented | LIST-CODES identical |
| Pagination adds no error code | ✅ Implemented | Catalogue blob equal; no new factory or contract |
| Expected paging outcomes are not errors | ✅ Implemented | No logging in the clamp or binding paths |
| Strict client page reader | ✅ Implemented | `readPage` plus `sendPage` classify drift; `nextCursor` stays in `FORBIDDEN_SUCCESS_KEYS` |
| Null-safe bounded paging arguments | ✅ Implemented | `boundedPage`/`paginationSearch`; `identityClient (tenantId, page, options)`, `platformClient (page, options)` |
| Module-neutral problem modules | ✅ Implemented | `src/api/*`, `src/components/ProblemMessage.jsx` |
| Field-error helpers have one module-neutral owner | ✅ Implemented | `src/components/problemFields.js` has 9 exports; `fieldErrors.js` has 4 |
| Error words unchanged by the move | ✅ Implemented | `ProblemMessage` reads `errors:*` through `src/i18n` |
| One catalogue, same gates | ✅ Implemented | `OpenApiContractTests.cs:491` reads `src/api/problemCodes.json` |
| Result stays internal behind one mapper | ✅ Implemented | One added overload, `ToAcceptedHttpResult`, with exactly 8 callers |
| Standards documents match the new contract | ✅ Implemented | `SKILL.md:109`; `error-handling-rules.md:288,328,400,476,594`; `ui-composition-rules.md:317` |
| Page control on every collection | ✅ Implemented | `TablePagination` on the five screens (PlatformPanel through a file-local `useDirectoryPages`) |
| Read states per page | ✅ Implemented | `useRead` page semantics; retry of the requested page |
| Past-the-end correction | ✅ Implemented | A microtask-deferred effect on five screens; nothing is drawn for a past-the-end page |
| Mutation outcome survives a failed refresh | ✅ Implemented | `reloadPage` re-reads the loaded page |
| Bounded resume walks | ✅ Implemented | Walk over `1..totalPages`, skipping the loaded page, with `cancelled` guards |
| Role pickers read the whole catalogue | ✅ Implemented | `listRoleCatalogue` → `readEveryPage` (`MAX_WALK_PAGES = 100`) |
| Pagination labels come from the MUI locale | ✅ Implemented | `themeFor(language)`; no label props |
| Retired show-more keys removed in every language | ✅ Implemented | Five keys removed in en and es |
| No new text and invariant paging data | ✅ Implemented | No key added; developer messages are English |

### Coherence (Design)
| Decision | Followed? | Notes |
|----------|-----------|-------|
| AD1 pagination types in `Application/Common/Models` | ✅ Yes | |
| AD2 EF execution in `Infrastructure/Data/Pagination` | ✅ Yes | |
| AD3 two queries, count then page | ✅ Yes | `CountAsync`, then `Skip/Take/ToListAsync` |
| AD4 clamp within Int32 | ✅ Yes | |
| AD5 keep each route's order with a unique key | ✅ Yes | Proven by R6C order and audit tests |
| AD6 explicit re-wrap | ✅ Yes | |
| AD7 `Query` (Platform) / `Pagination` (tenant) | ✅ Yes | PD-3 shape test |
| AD8 `ToHttpResult` migration, 50 sites, one `ToAcceptedHttpResult` | ✅ Yes | 8 accepted-result callers; FT 754/754 and journeys 31/31 |
| AD9 rely on binding metadata; keep `validation_failed` | ✅ Yes | Rule-10 gap recorded (WARNING 3) |
| AD10 problem-infrastructure location and `problemFields.js` | ✅ Yes | |
| AD11 shared `sendPage` | ✅ Yes | |
| AD12 client bounds mirror the server | ✅ Yes | |
| AD13 `readEveryPage` walk, bound 100 | ✅ Yes | |
| AD14 separate RolesPage catalogue read | ✅ Yes | Recorded deviation: one "Try again" repeats every read that errored (C1 Deviations 1) |
| AD15 page-change problem placement | ✅ Yes | |
| AD16 compile unit 6–8 | ✅ Yes | |
| AD17 remove Domain comparison members if unused | ✅ Yes | Removed; every .NET suite passes |
| AD18 PostgreSQL functional proof for the helper | ✅ Yes | |

Deviations recorded during apply that break no spec (WARNING 5): past-the-end pages draw nothing while correcting, and the correction is deferred to a microtask (C3 Deviations 2 and 3); PlatformPanel uses a file-local `useDirectoryPages`/`DirectoryPagination` (C1 Deviations 5); `identityClient.js` has a private `tenantPage` helper (B2).

### TDD Compliance
| Check | Result | Details |
|-------|--------|---------|
| TDD Evidence reported | ✅ | A "TDD Cycle Evidence" table for every work unit and correction round in `apply-progress.md` (A1, the A1 1.11 closure, A2, A3, A4 plus its correction, B1 plus its correction, B2 plus B2-V03, C1, C2, C3 plus its correction and the C3-V01 closure, C4, D1 plus its correction, the 12.1 closure, and the D1-V04 remediation) |
| All tasks have tests | ✅ | 120/120. Behaviour tasks name their test files. Document and search tasks (8A, 12) record a search RED/GREEN by design; the PD-5 moves are RED-less refactors, proven by searches and runs |
| RED confirmed (tests exist) | ✅ | All 42 declared test-file entries exist at the declared paths, and the new rows 23, 40 and 41 exist (13.6) |
| GREEN confirmed (tests pass) | ✅ | Every listed test file passes in this run: Vitest 632/632, UT 246, FT 754, DT 209, IT 370, journeys 31 |
| Triangulation adequate | ✅ | Multi-case triangulation throughout. Single-case entries are justified: 1.2 and 1.3 (one path each), 11A.3 (one scenario), 8A.7 (one sentence) |
| Safety Net for modified files | ✅ | Safety nets were run before edits. The C1, C2 and C4 nets were red only on the recorded batch B fixture failures. 11.11 had no RED because its premise was false (recorded) |

**TDD Compliance**: 6/6 checks passed

### Test Layer Distribution
| Layer | Tests | Files | Tools |
|-------|-------|-------|-------|
| Unit | 239 | 13 | Vitest 3.2 (mocked `send`/`readOne`, MSW over the real transport, `renderHook`); NUnit 4 + Shouldly |
| Integration | 213 | 11 | Vitest + Testing Library + MSW |
| Functional (HTTP and handler over PostgreSQL) | ≈159 | 14 | NUnit 4 + Shouldly + Aspire PostgreSQL (static count of `[Test]` and `[TestCase]`) |
| E2E | 31 | 6 feature files (not changed; run as proof) | Reqnroll + Playwright |
| **Total** | **≈642** | **44** | |

These are the change's declared test rows (row 22 `fieldErrors.test.js` and row 33 `StackBaselineTests.cs` are unchanged) plus the journeys. All tools are installed.

### Changed File Coverage
Coverage analysis skipped: no coverage tool is detected for the SPA, and .NET `coverlet.collector` is not wired into the declared verification commands. The threshold is 0.

### Assertion Quality
| File | Line | Assertion | Issue | Severity |
|------|------|-----------|-------|----------|
| `FT/Platform/PlatformDirectoryContractTests.cs` | 61-73 | `name.ShouldNotBe("pageNumber" / "pageSize" / "limit" / "cursor")` inside loops over `/api/identity/*` operation parameters | A loop with no non-empty guard. In the served document it evaluates 5 parameter entries (30 identity operations, 5 with parameters), so it is not vacuous today, but it would pass silently if they disappeared | WARNING |
| `SPA/i18n/presentation.test.jsx` | 120-121, 134-135 | `toHaveClass('MuiTypography-body2' / 'MuiTypography-caption')` | CSS class assertion; the lines predate this change (row 14 edited only `:25` and deleted `:116-119`) | WARNING |

Reviewed and not flagged:
- There are no tautologies and no `vi.mock` calls.
- The type-only assertions are paired with value assertions.
- The call-count assertions on the injected `send`/`readOne` (for example `pagination.test.js:107,197`) assert the requests the unit sends, which are its observable output.
- The empty-collection checks have non-empty companions.

**Assertion quality**: 0 CRITICAL, 2 WARNING

### Quality Metrics
**Linter**: ✅ No errors (`npx eslint src/` exit 0; `i18next/no-literal-string` at error severity)
**Type Checker**: ➖ Not available (JavaScript source; `tsconfig.json` has `noEmit` and no script). The .NET builds run with `TreatWarningsAsErrors`: 0 errors.

### Issues Found
**CRITICAL**: None

**WARNING**:
1. **Layered proof for four scenarios.** "A huge page number is clamped, not a fault", "A page past the end is an empty page" and "Every Platform directory requires the second factor" are asserted at handler level (plus the model, the served contract and the generic Result-to-HTTP mapping). No HTTP request sends `pageNumber=2147483647` or a past-the-end page (already recorded at A4-01). "Developer text stays English, and the person's words are localized" is composed: the English developer message, the non-empty `es` `unreadable_response`, and the Spanish `ProblemMessage` path are each asserted, but no test renders `unreadable_response` in `es`. The tasks.md coverage table places these proofs at the same layers.
2. **The Platform administrator invitation neutral `202` has no HTTP-level neutral test.** It is `PlatformEndpoints.cs:304` now (`:309` at `2716aa6`). `PlatformAdministrationTests.cs:33` goes through the handler, and the route is otherwise proven by `ToAcceptedHttpResult` returning the same expression and by the journeys (PlatformOperations passed).
3. **Rule-10 gap.** The four Platform directories still declare `400 validation_failed`, which no list read can emit because out-of-range values are clamped (D15, AD9). The gap is recorded, not closed.
4. **Assertion quality:** two WARNING rows above.
5. **Recorded design deviations** (Coherence), none of which breaks a spec.
6. **Environment.**
   - The C: drive reached 0 bytes free during the final static checks (14:25–14:28), then recovered to 403–588 MB (14:34–14:35). `verify-13.8-static-checks.log` could not be written; its output is recorded in the Gate evidence table. Every gate log had been written before that point, and the manifest covers them.
   - The 13.5 journey harness recreated the persistent `dbserver` PostgreSQL container: it was up for 19 hours at 13.1 and up for 2 minutes after 13.5. Per CLAUDE.md, a recreated container starts with an empty local database.

**SUGGESTION**:
1. Follow-ups from the delivery note: the WhatsApp bot SPEC and SCREENS alignment; `error-handling-rules.md:362`, `:387-389`, `:823`; `error-handling-standards/SKILL.md:17` and `:22` still name `features/*/api/`; the `:594` `fieldError` snippet signature; the `:476` `CONTEXT` naming; a shared page-state hook for the four tenant/identity screens; the thin load margin of the AppRoutes provider sign-in; the PlatformRetentionPage load sensitivity; stale Engram prefaces.
2. `openspec/config.yaml:70-73` (`known_baselines`) and the tasks.md 1.1 record still describe entries 1–4, which left the record after 11A.8 and 11A.10.

### Delivery note (Task 13.8)

#### Summary
Seven list routes moved from cursor to offset pagination: `/api/platform/{organizations,identities,admins,audit}` and `/api/tenants/{tenantId}/{roles,members,invitations}`.
- **Request and response.** They take `pageNumber`/`pageSize` and return `{ items, pageNumber, pageSize, totalCount, totalPages, hasPreviousPage, hasNextPage }` as endpoint-specific DTOs.
- **Shared SPA infrastructure** now lives in `src/api` and `src/components`.
- **Error contract.** No error code was added, renamed or retired.

The verification gates 13.1–13.7 are green, and the verdict is PASS WITH WARNINGS.

#### Error paths (E1–E12)
| Id | Path | Code and status | Where it is shown | Proving test |
|----|------|-----------------|-------------------|--------------|
| E1 | `pageNumber`/`pageSize` out of range (0, negative, 500) | None: clamped, `200` | A normal page; the control shows the effective size | `PaginationQueryTests > Clamps_rather_than_refuses`, `Clamps_page_size_to_one_hundred`; `ProblemDetailsContractTests > Out_of_range_platform_page_sizes_are_clamped_over_http_and_never_refused_as_validation`; `RoleAdministrationTests > A_requested_role_page_carries_its_offset_metadata_and_clamps_the_page_size`; `pagination.test.js > clamps an out-of-range page number and page size into the contract` |
| E2 | Huge `pageNumber` (`int.MaxValue`) | None: clamped to 21,474,836, `200`, no `Error` log | An empty page | `PaginationQueryTests > A_huge_page_number_is_clamped_so_skip_never_overflows`; `PlatformDirectoryContractTests > Out_of_range_pages_are_clamped_answered_and_never_logged_as_errors` (handler level) |
| E3 | Non-integer or beyond-Int32 value | `400 invalid_request` (problem+json with `code`, `traceId` and `status`), declared through `WithBodyBindingFailureCode` | The SPA never sends one (arguments are bounded client-side). If it did, it would be an `ApiProblem` rendered as a refused read | `OpenApiContractTests > Every_page_parameter_binding_refusal_is_emitted_only_as_declared` (7 routes × 4 values); `PlatformDirectoryContractTests > Every_list_route_declares_offset_parameters_and_its_binding_refusal` |
| E4 | `pageNumber` past the last page | None: `200` with empty `items` and the real totals | The screen requests `totalPages` once and adds no text; nothing is drawn while it corrects | `PlatformDirectoryContractTests > A_page_past_the_end_is_an_empty_page_with_the_real_totals_not_a_refusal`; `PaginatedListTests > A_page_past_the_end_keeps_the_real_totals_and_has_no_next_page`; `RolesPage.test.jsx`/`PlatformIdentitiesPage.test.jsx > moves to the real last page…`, `> asks once and keeps the empty state…` |
| E5 | Codes a list read may emit (unchanged) | `401 authentication_required`, `invalid_session` (7 routes); `401 recent_mfa_required` (4 Platform); `403 permission_denied`; `404 not_found` (3 tenant); `400 invalid_request`; `500 internal_server_error` | `401` session loss ends centrally. `recent_mfa_required` opens the Platform second-factor gate. `403`/`404` are refused in place: rows and control cleared, no retry. `500` shows "Something went wrong. Try again." with the reference and "Try again" | LIST-CODES identical to 1.1; `OpenApiContractTests > Platform_contracts_declare_the_bounded_code_gates_and_the_second_factor_directories`; `PlatformDirectoryAccessTests > A_password_only_session_reads_no_directory`; `RoleAdministrationTests > Read_scope_mismatches_are_native_absence_for_real_and_random_tenants`; `MembershipAdministrationTests > Member_and_invitation_read_scope_mismatches_are_native_absence_for_real_and_random_tenants`; `ProblemDetailsContractTests > A_refused_list_read_is_a_problem_document_from_the_shared_writer`; `PlatformPanel.test.jsx > asks for the second factor instead of reading the directories`, `> says why a directory could not be read instead of showing an empty one` |
| E6 | Tempted new paging codes | None added: `invalid_page`, `invalid_page_size`, `invalid_cursor` and `page_out_of_range` do not exist | — | 13.7 12.7 search; `OpenApiContractTests > Served_problem_codes_and_checked_in_client_catalogue_are_the_same_contract` (53 codes); `problemCatalogue.contract.test.js > allows MSW fixtures only for status and code pairs declared by the API` |
| E7 | Drift: retired `{ items, nextCursor }`, a missing member, malformed metadata | Client `unreadable_response`, status `0` | Errored read: `ProblemMessage` "We could not read the answer." (en; `es` catalogue value) with "Try again" | `problemDetails.test.js > refuses the retired cursor shape even when items are declared`; `pagination.test.js > refuses %s`, `> rejects malformed page metadata as an unreadable response`; `identityClient.test.js > %s reports a page number of 0…`; `platformClient.test.js > %s reports malformed page metadata…`, `> %s reports the retired cursor shape…` |
| E8 | Read states on a page change | `errored` (0, 429, ≥500) or `refused` (other 4xx) | Errored: rows stay, with `ProblemMessage` and "Try again" (`common:actions.tryAgain`; `platform:identities.retry` on PlatformIdentitiesPage) requesting the page asked for. Refused: rows and control cleared, no retry | `RolesPage.test.jsx > keeps the rows and the control on the loaded page when the next page cannot be reached`, `> clears the rows and explains a refused page in place`; `PlatformPanel.test.jsx > keeps a displayed directory through a failed page change…`; `InviteMemberPage.test.jsx > keeps the loaded invitations and retries the requested page beside the page control…`; `useRead.test.jsx` |
| E9 | Which page the control shows | — | The last loaded page ("1–25 of 30" stays after a failed change) | `RolesPage.test.jsx > keeps the rows and the control on the loaded page…`; `InviteMemberPage.test.jsx` ("1–25 of 26" stays) |
| E10 | Clicks that outrun responses | — | Only the last requested page renders | `RolesPage.test.jsx`/`PlatformIdentitiesPage.test.jsx > renders only the last page asked for when clicks outrun the network`; `useRead.test.jsx > ignores an older loader that settles after a newer refresh` |
| E11 | A failed refresh after a successful mutation | The read's own code (for example `500`) | A read problem only; the mutation is never reported as failed; a `role="status"` confirmation stays where a screen has one (C3-V01) | `RolesPage.test.jsx > reads the page the retirement was made from again, and a failed read never says the retirement failed`; `PlatformPanel.test.jsx > treats a failed post-suspension refresh as a read error and closes the successful confirmation`, `> keeps a newer invite draft, reports neutral success, and does not turn a failed refresh into invite failure` |
| E12 | Logging | No `Error` record for a clamp, an empty or past-the-end page, or a binding refusal | — | `PlatformDirectoryContractTests > Out_of_range_pages_are_clamped_answered_and_never_logged_as_errors`; `OpenApiContractTests > Every_page_parameter_binding_refusal_is_emitted_only_as_declared` |

Related failure paths:
- **A failed resume walk (D11)** shows the action alert "We could not reach the service." and executes nothing. Proof: `ExternalProofResume.test.jsx > reports a %s search that cannot reach the next page, and executes nothing`.
- **A role-catalogue walk (PD-2)** that exceeds 100 pages is `unreadable_response` in the fieldset, with "Try again". A network error on a page shows the fieldset alert, and "Try again" restarts at page 1. `permission_denied` keeps its refused sentence inside the member editor. Proofs: `InviteMemberPage.test.jsx`, and `MembersPage.test.jsx > does not flash role identifiers and places a catalog refusal inside the member editor`.

#### AD8 neutral 202s
The eight bodyless `202` sites moved to `ToAcceptedHttpResult(context, problems)`:
- organization registration, `Identity.cs:64`;
- personal registration, `PersonalEndpoints.cs:78`;
- invitation registration, `InvitationEndpoints.cs:147`;
- Platform invitation registration, `PlatformInvitationEndpoints.cs:54`;
- password recovery, `PasswordEndpoints.cs:63`;
- reactivation requests, `AccountLifecycleEndpoints.cs:86`;
- bootstrap recovery, `PlatformEndpoints.cs:146`;
- Platform administrator invitation, `PlatformEndpoints.cs:304`.

The success is the same bodyless `202`, and the failure is the same writer call, so the neutral flows stay byte-identical. Antiforgery and early guards keep their order, and sign-in is not migrated. No neutral route is paginated. Proof: the FT gate 754/754, including `RegistrationHttpValidationTests`, `RegistrationTests`, `InvitationHttpContractTests`, `RegisterInvitedUserTests`, `PasswordLifecycleTests`, `IdentityLifecycleTests`, `PlatformInvitationOnboardingTests`, `PlatformBootstrapRecoveryTests`, `SessionTests` and `ProblemDetailsContractTests`; and the journeys 31/31.

#### The `validation_failed` rule-10 gap
Error rule 10 says a route declares nothing it cannot emit. The four Platform directories still declare `400 validation_failed`, but no list read can emit it because out-of-range values are clamped (PD-a). This change records the gap and does not close it (D15, AD9). LIST-CODES confirms the declarations are unchanged.

#### SPEC rows amended
`docs/features/identity-access/SPEC.md` (baseline line numbers in parentheses):
- the IA-REQ-038 offset paging sub-paragraph `:203-208` (after `:201`), with the no-envelope sentence kept;
- IA-REQ-045 `:246` (`:239`);
- the Platform directory rows `:313-316` (`:306-309`), with `recent_mfa_required` only on organizations and identities and the administrators row untyped;
- the scenario `:471-472` (`:464-465`);
- the tenant rows `:1040` (`:1033`) and `:1044` (`:1037`), with no default clause;
- the C5 history `:1072` (`:1065`), unchanged, with a dated note at `:1073-1075`.

`:3` ("Proposed for approval") and the sessions statement `:702` (`:695`) are unchanged.

Also amended:
- **`TRACEABILITY.md`:** the dated evidence paragraph `:22-46`, and rows `:112` (`:86`), `:118` (`:92`) and `:125` (`:99`).
- **ADR-004** decision 17 (`:36`), with its status still Proposed.
- **openspec delta spec** `spa-pagination-ui` "Mutation outcome survives a failed refresh" (amended 2026-09-14, C3-V01).
- **Standards:** `error-handling-standards/SKILL.md:109`; `error-handling-rules.md:288,328,400,476,594`; `ui-composition-rules.md:317-318`.

#### Localization
- **Keys removed:** five, in `en` and `es`:
  - `platform:organizations.more` ("More organizations" / "Mostrar más organizaciones");
  - `platform:identities.more` ("More accounts" / "Mostrar más cuentas");
  - `identity:members.showMore` ("Show more members" / "Mostrar más miembros");
  - `identity:invitations.member.showMore` ("Show more invitations" / "Mostrar más invitaciones");
  - `identity:roles.showMore` ("Show more roles" / "Mostrar más roles").
- **Kept:** `platform:identities.retry` ("Try again" / "Volver a intentar").
- **Keys added:** none.
- **MUI locale labels.** `TablePagination` labels come from the MUI locale that `themeFor(language)` composes: the `enUS` defaults and `esES` ("Filas por página:", "Ir a la página siguiente"). No screen passes a label prop.
- **Label proofs.** `spanish.test.jsx` guards `getItemAriaLabel('next')`, and `labelRowsPerPage` stays proven by `App.localization.test.jsx` and `spanish.test.jsx`.
- **Translations awaiting native review:** none, because no new Spanish copy was added.
- `errors.json` and the `.resx` resources are unchanged.

#### Gates run
| Gate | Result |
|------|--------|
| 13.1 prerequisites | No blocking process; CPU 0%; Docker 29.3.1; Chromium 1234 present; `v1.json` regenerated with `pageNumber` and no `nextCursor` |
| 13.2 `SPA-G` | Vitest 632/632; eslint 0; `i18n:unused` 0; `vite build` 0; 14 `eslint-disable` lines |
| 13.3 builds | Solution exit 0; Release exit 0 |
| 13.4 .NET | DT 209/209; UT 246/246; FT 754/754; IT 370/370; IT review 3 intentional failures; FT review 2 failed / 2 passed (baseline) |
| 13.5 journeys | 31/31, Spanish smoke included |
| 13.6 test-file boundary | PASS: only declared rows; moved-from paths ` D`; `DelegatedAdministrationRevalidationTests.cs` unchanged |
| 13.7 searches and snapshots | 8A.8, 12.1, 12.5, 12.7, 12.8 and 11.15 as expected; LIST-CODES identical |

**Baseline-timeout rule and the A1-01 exception, every use.** Record entries #1–#4 are at or beyond 15 s; #5–#9 are under 15 s.
- **1.1 baseline** (`2716aa6`): SPA-G had 6 failures, the load-sensitive set behind the record.
- **1.11 first run:** 6 failed / 542 passed. #1 timed out alone (15,118 ms), so 1.11 stayed open (A1-01 raised). In correction round 1, #1 passed alone once, at 14,706 ms.
- **1.11 closure** (after decision A1-01): 5 failed / 544 passed.
  - The rule was used 4 times (#2, #3, #4, #7), and each passed alone.
  - A1-01 was used once (#1). It was not load-bearing: #1 also passed alone, at 11,261 ms.
  - The #8 cascade clause was used 0 times.
- **B1 regression run** (not a gate): 4 failed / 572 passed. The rule was used 3 times (#2, #4, #6); A1-01 once (#1, not load-bearing: alone 13,179 ms).
- **B2, C1, C2, C3 regression runs** (not gates): the failures were deterministic batch B fixture reds, and the rule was not used.
- **11A.11** (two runs), **12.9** and **13.2**: 632/632. The rule was used 0 times and A1-01 0 times.

**History of the A1-01 exception.**
- **Timeouts.** The ExternalProofResume roles-target test ("resumes a roles target beyond the first page exactly once with its original draft and version") timed out alone at `2716aa6` (15,107 and 15,937 ms), after Phase 1 (15,118 ms) and at validation (15,059 ms).
- **Decision.** Decision A1-01 (user, 2026-09-13) excused it in intermediate `SPA-G` gates until 11A.8 rewrote it over 26-row fixtures, with no timeout raised. 13.2 excuses neither it nor its cascade.
- **13.2 timings.** In 13.2 the rewritten tests passed outright under full load:
  - roles resume 7,138 ms and members resume 4,527 ms;
  - R6C roles 1,655 ms, members 2,442 ms and invitations 1,926 ms.
- **Entries under 15 s passed:** #5 1,716 ms, #6 2,988 ms, #7 3,364 ms, #8 2,186 ms and #9 2,822 ms.

**Decision D1-V04 (staged waits).** The walk-failure test "reports a %s search that cannot reach the next page, and executes nothing" (`ExternalProofResume.test.jsx`, row 4) waits per journey step: the returned screen's heading, then the page-2 read after return, then the alert. That is up to three 1,000 ms Testing Library waits, where one 1,000 ms wait used to cover the whole return journey. No timeout configuration changed: `testTimeout` stays 15000 ms and no `asyncUtilTimeout` is set. Under full load in 13.2 it passed: members 1,784 ms, roles 2,756 ms. The AppRoutes provider sign-in test passed in 471 ms.

#### Anything unverified
- The Platform administrator invitation neutral bodyless `202` (`PlatformEndpoints.cs:304`; `:309` at baseline) has no HTTP-level neutral test. It is proven only by `ToAcceptedHttpResult` returning the same expression and by the journeys.
- No HTTP request exercises a huge valid `pageNumber` or a past-the-end page. Both are proven at handler and model level.
- No test renders `unreadable_response` in `es`. The words are proven by catalogue parity and the Spanish `ProblemMessage` path.
- Coverage was not measured.
- Every gate ran; none was skipped.

#### User-visible changes
- **D09/PD-1.** Tenant lists (roles, members, invitations) default to 25 rows instead of 100, and `pageSize <= 0` now means 1 instead of 100.
- **PD-6.** PlatformPanel's organizations, administrators and audit directories each gain a `TablePagination`, so rows past page 1 are reachable. The "show more" and "More accounts" controls on the five screens are replaced by `TablePagination`.
- **PD-2.** The MembersPage and InviteMemberPage role pickers offer the full role catalogue, walked page by page (bounded at 100 pages), instead of the first page.

#### Amendments decided during apply
- **B2-V03:** row 5 and 11A.10 cover the shared `pageResponse` helper behind R6A and R6B, with every R6A and R6B assertion kept.
- **A1-01:** the roles-target timeout was excused in intermediate `SPA-G` gates until 11A.8.
- **A1-02:** the 1.1 record was restated.
- **A1-03:** the `problemCatalogue.contract.test.js` registry guard test was accepted (row 19).
- **C3-V01 (option a):** the E11 confirmation clause keeps a `role="status"` confirmation only where a screen shows one, because no paged screen confirms a retire, revoke, reissue or status change (11A.3 and the spec requirement).
- **D1-V01/D1-V02 (option a):** the `pagination.js:57-58` doc comment no longer uses `nextCursor` as its example, and 12.1 exclusion 3 covers the whole 6.2 page-body test.
- **D1-V03:** the 13.8 follow-ups name `error-handling-rules.md:362,387-389,823`.
- **D1-V04:** staged waits in the walk-failure test (above).
- **Decision #1142:** Phase 13 and Delivery are listed without checkboxes, so native sdd-verify can start.

#### Disclosures
- **Batch C native attempt.**
  - It was settled by a call meant as a probe (request-id `offset-pagination-batch-c-settle-probe-1`). Its diagnosis, cleanup-evidence and process-evidence fields read "probe".
  - Its outcome (`passed`) and evidence hash (`sha256:1e07424c…c9b1`, the apply-progress hash after closing 11A.3) are real.
  - The true evidence is in Engram #1131: tasks 11.1–11A.12 complete, `SPA-G` 632/632, no git write, and no process left running.
- **Parallel commit `f29ca74`** ("fix(apphost): forward the Google client and pin the local frontend port", from another chat) changes only `docs/features/identity-access/RUNNING-LOCALLY.md` and `src/AppHost/Program.cs`. No path of this change overlaps it. HEAD and `origin/main` are `f29ca74`, and every gate here ran on it. No failure occurred, so none relates to it.
- **Processes.** 7 orphaned `find.exe` processes from other sessions were stopped with the user's permission before this verification. The 13.1 listing found none.
- **Other disclosures.**
  - During B2 a CLI help check wrote a stray Engram export named `--help` in the repository root. It was deleted, and nothing reached a commit.
  - Plugin hooks requested `Skill(verification)`, `Skill(vercel-functions)` and `Skill(react-best-practices)`. None was invoked: the Skill tool is forbidden for this executor, and none applies.
  - The C: drive filled to 0 bytes during the final static checks (WARNING 6).
  - The journeys recreated the local `dbserver` container.

#### Follow-ups
- `docs/features/whatsapp-bot/SPEC.md:295,304` and `SCREENS.md:53`: align with the offset standard before that feature is implemented (PD-b). `nextCursor` stays forbidden.
- `.agents/skills/error-handling-standards/references/error-handling-rules.md:362`, `:387-389` and `:823` still name the non-existent `features/identity/problemMessages.js` and the old catalogue import.
- `.agents/skills/error-handling-standards/SKILL.md:17` and `:22` still describe the SPA transport as `features/*/api/`.
- The `error-handling-rules.md:594` `fieldError` snippet still shows `(problem, name, rule)`, while the helper is `fieldError(problem, name, t)`.
- The `error-handling-rules.md:476` transport snippet names an identity-specific `CONTEXT` constant in the module-neutral transport.
- A candidate shared page-state hook for the four tenant/identity screens (`requested`, `go`, `retry`, `pastTheEnd`, `reloadPage`), out of scope here.
- The AppRoutes provider sign-in test (`AppRoutes.test.jsx:67`) has a thin load margin; it failed once under load (D1-V04) and passed here in 471 ms.
- The PlatformRetentionPage test once hit 15 s under load; its file passed 33/33 here (63.3 s under full load).
- Stale Engram prefaces: apply-progress parts 1–5 still say "of 6", and part 1's table predates the 12.1 closure. The file is authoritative.

### Verdict
PASS WITH WARNINGS

Every gate 13.1–13.7 is green (Vitest 632/632, the solution and Release builds, DT/UT/FT/IT at baseline, journeys 31/31, test-file boundary PASS, searches and LIST-CODES identical). All 78 scenarios and 30 requirements have passing evidence, and 120/120 tasks are complete. The warnings are recorded gaps and environment notes; none breaks a spec.
