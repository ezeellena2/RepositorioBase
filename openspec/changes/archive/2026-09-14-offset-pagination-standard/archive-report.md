# Archive Report: offset-pagination-standard

- **Change:** `offset-pagination-standard`
- **Archived on:** 2026-09-14
- **Archived to:** `openspec/changes/archive/2026-09-14-offset-pagination-standard/`
- **Artifact store:** hybrid (this file, plus Engram topic `sdd/offset-pagination-standard/archive-report`)
- **Archive status:** success. This is a full archive: no partial archive and no stale-checkbox reconciliation. Verification was PASS WITH WARNINGS, with 0 CRITICAL findings.
- **Skill resolution:** paths-injected. The phase ran `sdd-archive` with the `_shared` conventions, `engineering-standards`, `error-handling-standards` and `localization-standards`.
- **Executor boundary:** no git write, no sub-agent, no Skill tool. The archive wrote only under `openspec/`, and under the scratchpad for one read-only listing.

This report is the terminal record of the SDD cycle. It describes the change **at close**. Statements taken from
intermediate snapshots (`apply-progress`, `verify-report`) are attributed to their source and time; section 7 ranks
the sources and corrects every stale claim found.

## 1. Outcome

The seven list routes moved from cursor to offset pagination, and the code change reached `origin/main` in one commit:
`1335d8f751bdc7277aecffea85f3d321d549fc31`, "feat(pagination): replace cursor pagination with the offset standard".

- **Specs.** The four delta specs became the first main specs of the repository.
- **Archive.** The change folder moved into the archive, and every copy and move was proven byte-identical by an empty `diff -r`.
- **Still to commit.** `openspec/` itself is not committed yet. The orchestrator commits it separately (section 13).

## 2. Archive readiness

Native status was refreshed by this phase, read-only:

```text
gentle-ai sdd-status offset-pagination-standard --cwd "C:/Users/ezequ/source/repos/RepositorioBase" --json   -> exit 0
```

| Field | Value |
| --- | --- |
| `schemaName` / `schemaVersion` | `gentle-ai.sdd-status` / 2 |
| `artifactStore` | `hybrid` |
| `artifacts` | proposal, specs, design, tasks, applyProgress, verifyReport: all `done` |
| `taskProgress` | total 120, completed 120, pending 0, `allComplete` true |
| `dependencies` | proposal, specs, design, tasks, apply and verify `all_done`; **archive `ready`** |
| `applyState` | `all_done` |
| `actionContext` | `repo-local`; `allowedEditRoots` = the repository root (every archive operation stayed inside it) |
| `relationships` | all empty |
| `remediationState.required` | false |
| **`nextRecommended`** | **`archive`** |
| **`blockedReasons`** | **`[]`** |
| `notes` | `[]` |

Readiness history (launch prompt, 2026-09-14; the ledger is not re-read by this phase):

- The first archive run on 2026-09-14 stopped at readiness and changed nothing. Verify attempt #6 had passed, but it exceeded its changed-line budget (455 against 200; the orchestrator had sized the budget too small for the verify report). That set `decision_required` and a `blocked(maintainer_decision)` reason.
- The user then authorized a maintainer reset. The orchestrator ran `gentle-ai sdd-attempt reset` with:
  - `--expected-revision sha256:2375a2e7c4b3416cd27b29dc48a1412fb455808bb03fd6cb4c7dad921a4df52b`;
  - `--request-id offset-pagination-verify-budget-reset-1`;
  - `--actor "Ezequiel Ellena"`;
  - the default objective relation.
- The ledger is now at revision `sha256:0a621a69e4c3cbe8e961b2f798896680abea71c404d116bbb315a894a911d4bd`, with `decision_required` false. The status above corroborates the effect: `blockedReasons` is empty and archive is `ready`.

## 3. Gates

| Gate | Evidence | Result |
| --- | --- | --- |
| Task Completion Gate | `tasks.md`: 0 lines match `- [ ]` (Grep count before the move; `grep -c` on the archived copy), and 120 match `- [x]`. Phase 13 (13.1–13.8) and Delivery (DL.1–DL.6) are plain procedure steps without checkboxes, per user decision #1142 | PASS |
| CRITICAL gate | `verify-report.md`: "**CRITICAL**: None" (`:280`); "Assertion quality: 0 CRITICAL" (`:273`); verdict PASS WITH WARNINGS (`:453`); 78/78 scenarios and 30/30 requirements compliant (`:8`, `:175`); envelope `critical_findings: 0`, `blockers: 0` | PASS |
| Required artifacts | proposal, four specs, design and tasks are present, as are exploration, apply-progress and verify-report | PASS: nothing missing |
| `rules.archive`: "Warn before merging destructive deltas" | `openspec/specs/` held only `.gitkeep`, so no main spec existed to merge into. None of the four delta specs has an `ADDED`, `MODIFIED`, `REMOVED` or `RENAMED` section heading (count 0 each); each is a full spec. No requirement was removed or replaced | No destructive delta; no warning needed |
| Action context | `repo-local`; no `workspace-planning` mode | PASS |

## 4. Spec sync (Step 2)

Every domain used the SKILL's "If Main Spec Does NOT Exist" block exactly. It copies into a `mktemp` file in the target
folder, runs `diff -r` from source to temp, then runs `mv` onto `spec.md`. Only `{domain}` differs between the four
runs. Each run was wrapped as `( <block> ); echo "BLOCK_EXIT=$?"` from the repository root, with a header `echo`
before it.

| Domain | Action | Details | Bytes | sha256 |
| --- | --- | --- | --- | --- |
| `api-offset-pagination` | Created | 9 requirements added, 0 modified, 0 removed (21 scenarios) | 14,172 | `bced2f97cafb0fab063b767964cc9ccb3a5ae7fe70c079bca48883feed949aa2` |
| `list-read-error-contract` | Created | 6 added, 0 modified, 0 removed (15 scenarios) | 9,544 | `21b5915027dd39072072b59f2a6924dca17f691612de87ea29cb27cc6797fb9b` |
| `shared-problem-infrastructure` | Created | 6 added, 0 modified, 0 removed (16 scenarios) | 11,595 | `9e4789d55cb70b9daf7610bde79d46f07aa2a1f4ad0a3dfe4b6f65a2422d9fe7` |
| `spa-pagination-ui` | Created | 9 added, 0 modified, 0 removed (26 scenarios) | 16,637 | `a2b4df007b4f397bb16ce173fecc9c243cdb885ed18c8a81d4f73eee18823728` |
| **Total** | | **30 requirements, 78 scenarios**, equal to verify-report's 30/30 and 78/78 | | |

Verbatim output of the four runs. In each, the block's own `diff -r` printed nothing:

```text
=== spec sync: api-offset-pagination (Main Spec Does NOT Exist block) ===
BLOCK_EXIT=0
```

```text
=== spec sync: list-read-error-contract (Main Spec Does NOT Exist block) ===
BLOCK_EXIT=0
```

```text
=== spec sync: shared-problem-infrastructure (Main Spec Does NOT Exist block) ===
BLOCK_EXIT=0
```

```text
=== spec sync: spa-pagination-ui (Main Spec Does NOT Exist block) ===
BLOCK_EXIT=0
```

Post-sync readback (read-only, before the move):

```text
--- leftover temp files ---
none
--- post-sync diff -r change specs vs main specs (per domain) ---
api-offset-pagination: identical (exit 0)
list-read-error-contract: identical (exit 0)
shared-problem-infrastructure: identical (exit 0)
spa-pagination-ui: identical (exit 0)
```

The `spa-pagination-ui` main spec carries the 2026-09-14 amendment of "Mutation outcome survives a failed refresh"
(decision C3-V01, #1130), because the delta file was copied as it stood at archive time.

## 5. Move to archive (Step 3)

The SKILL's move block ran exactly, as one shell transaction wrapped as `( <block> ) 2>&1; echo "BLOCK_EXIT=$?"`, with:

- `source="openspec/changes/offset-pagination-standard"`;
- `destination="openspec/changes/archive/2026-09-14-offset-pagination-standard"`.

The destination did not exist beforehand (checked: "destination absent").

Verbatim output:

```text
=== Step 3 move block (SKILL, exact) ===
fatal: source directory is empty, source=openspec/changes/offset-pagination-standard, destination=openspec/changes/archive/2026-09-14-offset-pagination-standard
BLOCK_EXIT=0
```

What happened:

1. **`git mv` failed**, as expected. `openspec/` is untracked (`git status --porcelain -- openspec` gives `?? openspec/`), so git sees no tracked content under the source.
2. **The fallback guard passed.** The source still existed, and `diff -r "$snapshot_root/source" "$source"` printed nothing, so the source had not changed since the recursive snapshot.
3. **Plain `mv`** moved the folder, and the source was confirmed absent.
4. **The mandatory readback passed.** `diff -r "$snapshot_root/source" "$destination"` printed nothing, and the block exited 0.
5. **Cleanup.** The EXIT trap removed the snapshot; no `sdd-archive.*` folder is left in `$TMPDIR`.

Post-move readback (read-only):

```text
--- active change folder present? ---
absent
--- archived files (bytes) ---
296033 .../apply-progress.md
 56074 .../design.md
 50184 .../exploration.md
 22373 .../proposal.md
 14172 .../specs/api-offset-pagination/spec.md
  9544 .../specs/list-read-error-contract/spec.md
 11595 .../specs/shared-problem-infrastructure/spec.md
 16637 .../specs/spa-pagination-ui/spec.md
 82712 .../tasks.md
 61827 .../verify-report.md
621151 total
--- unchecked tasks in archived tasks.md ---
0
--- checked tasks in archived tasks.md ---
120
```

- Every size equals the pre-move `wc -c` of the same file. The archived spec hashes equal the pre-copy hashes in section 4.
- This `archive-report.md` was written afterwards, as a new additive file. It is excluded from the comparison, because it did not exist in the source.

## 6. Step 4 checklist

- [x] **Main specs updated correctly.** Four created; each is byte-identical to its delta (empty `diff -r`, equal sha256).
- [x] **Change folder moved to archive.** `openspec/changes/archive/2026-09-14-offset-pagination-standard/`.
- [x] **Archive contains all artifacts.** proposal.md, specs/ (four domains), design.md and tasks.md, plus exploration.md, apply-progress.md and verify-report.md.
- [x] **Archived `tasks.md` has no unchecked implementation task.** 0 `- [ ]`; 120 `- [x]`. No reconciliation was needed.
- [x] **Active changes directory no longer has this change.** `openspec/changes/` holds only `archive/`.
- [x] **Verbatim `diff -r` readback output is included and empty.** Sections 4 and 5.

## 7. Final state at close

### 7.1 Source ranking

The ranking follows the SKILL's Final-State Authority:

1. The persisted tasks artifact: 120/120 complete, with Phase 13 and Delivery as plain steps.
2. The orchestrator's final-state facts in the archive launch prompt (2026-09-14). Where this phase could corroborate them with read-only repository evidence, the evidence is cited.
3. `verify-report` (Engram #1143/#1144; verified 2026-09-14 14:01–14:28 -03:00) and `apply-progress` (Engram #1065, #1066, #1079, #1087, #1100, #1133, #1140; 2026-09-13 to 2026-09-14). These are intermediate snapshots: valid history, never final state on their own.

### 7.2 Delivery (DL.1–DL.6, 2026-09-14)

Delivery ran after sdd-verify returned PASS WITH WARNINGS, with the user's explicit authorization.

| Step | Final state (launch prompt) | Repository corroboration (read-only, this phase) |
| --- | --- | --- |
| DL.1 | Started after verify passed, with DL.0 settled: one commit (#1063); `openspec/` separately after archive (#1064) | — |
| DL.2 | The Native Checking Contract returned `next_transition` stop, `reason_code` `rdd_disabled`: receipt-driven development is off, so delivery followed ordinary repository policy | Not observable from git |
| DL.3 | Did not apply: no scoped correction changed any byte, so the committed bytes are the verified bytes | Not observable from git |
| DL.4 | 124 change paths staged one by one, giving 117 index entries including 7 renames. No parallel-chat path and no `openspec/` path was staged | `git diff-tree -r -M 1335d8f`: 117 entries, of which 7 renamed, 8 added, 102 modified and 0 deleted. That equals design.md's "Create 8 · Move 7 · Modify 102 · Delete 0", and 117 + 7 rename sources = 124 paths. No `openspec/` path, and no `AGENTS.md`, `CLAUDE.md`, `.codex/` or module-separation-plan path. The only `.agents` paths are the three D20 standards documents |
| DL.5 | Commit `1335d8f751bdc7277aecffea85f3d321d549fc31`, "feat(pagination): replace cursor pagination with the offset standard", with a BREAKING CHANGE footer and no AI attribution; 117 files (+3015/−1227) | `git show --shortstat`: 117 files changed, 3015 insertions, 1227 deletions. The body ends with "BREAKING CHANGE: the list routes no longer accept limit or cursor and no longer return nextCursor; clients send pageNumber and pageSize and read the offset page metadata." It has no `Co-Authored-By` or other attribution line. Author Ezequiel Ellena; parent `f29ca74` |
| DL.6 | `git push origin main` fast-forwarded `f29ca74..1335d8f`; HEAD equals `origin/main` | `git rev-parse HEAD origin/main`: both `1335d8f751bdc7277aecffea85f3d321d549fc31`. `git rev-list --left-right --count origin/main...HEAD`: `0 0` |

The seven renames are the problem-infrastructure moves:

- `features/identity/api/{apiTransport.js,apiTransport.test.js,problemDetails.js,problemDetails.test.js}` to `src/api/`;
- `features/identity/problemCodes.json` to `src/api/`;
- `features/identity/ProblemMessage.{jsx,test.jsx}` to `src/components/`.

`f29ca74`, "fix(apphost): forward the Google client and pin the local frontend port", is an earlier commit from a
parallel chat. It is not part of this change: it touches only `docs/features/identity-access/RUNNING-LOCALLY.md` and
`src/AppHost/Program.cs`.

### 7.3 Verification evidence (final numbers)

These numbers come from the launch prompt, and match verify-report #1143/#1144.

| Gate | Result |
| --- | --- |
| SPA-G | Vitest 632/632 (46 files); `npx eslint src/`, `npm run i18n:unused` and `npx vite build` each exit 0 |
| .NET | Domain.UnitTests 209, Application.UnitTests 246, Application.FunctionalTests 754 and Infrastructure.IntegrationTests 370, all passing |
| `IndependentDevelopmentReview` categories | Equal to baseline. IT: the 3 intentional retention failures, open since `b9a30d1`. FT: 2 failed / 2 passed, identical to the 1.1 record |
| Journeys | 31/31, including the Spanish (`es`) smoke |
| Builds | `dotnet build CleanArchitecture.slnx` and `dotnet build --configuration Release` exit 0, with only the 2 pre-existing `ASPIRE010` warnings |
| Test-file boundary (13.6) | PASS: only declared rows changed |
| Searches and snapshots (13.7) | 8A.8, 12.1, 12.5, 12.7, 12.8 and 11.15 as expected; `LIST-CODES` identical to the 1.1 baseline |
| Baseline-timeout rule and A1-01 exception at 13.2 | 0 uses. The rewritten slow tests passed outright |

The 6 warnings and the suggestions stand as recorded in verify-report #1143 (see 7.6 on how the suggestions are counted):

1. Four scenarios rest on layered proof: handler, model or composed evidence.
2. The Platform administrator invitation's neutral `202` has no HTTP-level neutral test.
3. The `validation_failed` rule-10 gap is recorded, not closed.
4. Two assertion-quality warnings.
5. Design deviations recorded during apply, none of which breaks a spec.
6. Environment: the C: drive filled, and the `dbserver` container was recreated.

### 7.4 Native attempt ledger

- **Verify attempt #6 and the maintainer reset:** see section 2.
- **Batch C attempt:**
  - It was settled by an orchestrator call meant as a probe (request-id `offset-pagination-batch-c-settle-probe-1`), so its diagnosis, cleanup-evidence and process-evidence fields read "probe".
  - Its outcome (`passed`) and evidence hash (the apply-progress hash after closing 11A.3) are real.
  - The true evidence is Engram #1131: tasks 11.1–11A.12 complete, SPA-G 632/632, no git write, and no process left running.
  - A settled objective cannot be re-settled, so the disclosure is the record.

### 7.5 Stale snapshot claims, corrected

| Snapshot claim | Source and time | Final state | Where it closed |
| --- | --- | --- | --- |
| 12.1 pending on `pagination.js:58` | apply-progress part 1 #1065 (cumulative table) and the prefaces of parts 1, 2, 4 and 5 (2026-09-13 to 14) | 12.1 is `[x]` in the archived tasks.md | Decision D1-V01/V02 option (a), #1137 (2026-09-14): the doc comment was reworded and exclusion 3 widened. Closure recorded in part 6, #1133 |
| 1.11 open, awaiting A1-01; A1-03 awaiting a decision | apply-progress part 1 #1065 (A1 issues) and part 2 #1066 (A3 and A4 issues), 2026-09-13 | 1.11 is `[x]`; the A1-03 guard test was accepted | #1067 and #1068 (2026-09-13); "1.11 closure" in #1065 |
| 11A.3 unmarked, awaiting C3-V01 | apply-progress part 5 #1100 (C3 correction round 1 and C4), 2026-09-14 | 11A.3 is `[x]`; the E11 confirmation clause is amended in tasks and spec | #1130 (2026-09-14); closure section in #1100 |
| D1-V04 open for 13.2 (load-sensitive AppRoutes and ExternalProofResume tests) | apply-progress part 6 #1133 and part 7 #1140 | Q1 remediated with staged waits (#1139, #1140). Q2: the orphaned `find.exe` processes were stopped with the user's permission (#1141). Q3 was conditional on the AppRoutes test failing on a quiet machine; it passed at 13.2 (471 ms, verify-report #1144), so it never triggered. The thin load margin stays a follow-up | #1139, #1140, #1141; verify-report 13.2 |
| "120 of 134 checked" | apply-progress parts 6 and 7 | 120/120: after #1142 the 14 Phase 13 and Delivery steps are plain steps. Native status reports total 120 | #1142 (2026-09-14) |
| Apply-progress parts 1–5 say "Part N of 6" | Engram prefaces #1065, #1066, #1079, #1087, #1100 | Seven parts exist (#1140), and the file is authoritative | Not corrected (follow-up 7) |
| HEAD and `origin/main` at `f29ca74`, nothing staged; "the user commits only after sdd-verify passes" | verify-report #1143 (Tree line, 14:01–14:28); design.md Migration / Rollout | Delivered: HEAD = `origin/main` = `1335d8f` | Section 7.2 |
| C: drive at 403–588 MB after reaching 0 bytes | verify-report #1143, WARNING 6 (14:25–14:35) | Recovered: 41 GB free later (launch prompt); 116 GB free when this phase ran `df -h /c` | Environment recovered |
| Delivery open questions left as `- [ ]` bullets | design.md "Open Questions" (2026-09-13) | Settled: one commit (#1063); `openspec/` in a separate post-archive commit (#1064). These bullets are design questions, not tasks, and the archived design.md keeps them as written | DL.0, 2026-09-13 |
| Success criteria left as `- [ ]` bullets | proposal.md "Success Criteria" (2026-09-13) | Met at verification (7.3). One criterion was corrected at sdd-tasks: the Task 8A Step 2 search cannot return empty, because the C5 history (`SPEC.md:1072`, `:1065` at baseline) is kept by D17/PD-4. These bullets are proposal criteria, not tasks | tasks.md "Design corrections found at sdd-tasks"; verify-report 13.7 |
| "The Engram tasks topics #1033/#1034 still hold the pre-answer text" | #1064 (2026-09-13 14:10) | #1034 now carries the DL.0 answers (revision 23) | #1034 |
| `known_baselines` and the 1.1 record still list entries 1–4 | `openspec/config.yaml:70-73`; tasks.md 1.1 record | Entries 1–4 left the record after 11A.8 and 11A.10; the rewritten tests passed outright at 13.2 | Not corrected (follow-up 8). The archive does not edit archived artifacts or `config.yaml` |

### 7.6 Recorded discrepancies

Neither discrepancy is resolved silently in either direction.

- **Suggestion count.**
  - The launch prompt says "the 6 warnings and 8 suggestions stand as recorded in verify-report".
  - verify-report #1143 lists 2 numbered SUGGESTION entries. The first holds 9 semicolon-separated follow-ups, and the second holds the `config.yaml:70-73` / 1.1-record item, so 10 items in all.
  - The launch prompt's follow-up list (fact 7) groups the same content into 8 bullets: it pairs the `:594` and `:476` snippet drift, and the AppRoutes margin with the slow PlatformRetentionPage test.
  - No item is added or dropped between the two sources; they differ only in grouping. Section 14 uses the launch prompt's grouping.
- **DL.4 path count.**
  - "124 change paths" is the launch prompt's staging count. Git corroborates 117 index entries with 7 renames, which reconciles to 124 paths.
  - The one-path-at-a-time staging procedure itself is not observable from the repository and rests on the launch prompt.

## 8. What shipped

The contract is in `openspec/specs/`, and the evidence is in verify-report #1143/#1144.

- **API.** `GET /api/platform/{organizations,identities,admins,audit}` and `GET /api/tenants/{tenantId}/{roles,members,invitations}`:
  - take optional `pageNumber`/`pageSize`, clamped within Int32: default 25, range 1–100, and `pageSize <= 0` means 1;
  - answer endpoint-specific DTOs `{ items, pageNumber, pageSize, totalCount, totalPages, hasPreviousPage, hasNextPage }`;
  - answer a page past the end with `200` and empty `items`;
  - each order by a unique key; audit is newest first.
- **Retired.** The cursor types and helpers are gone, and so are the Domain ID comparison members (AD17).
- **Layers.**
  - Application: `PaginationQuery` and `PaginatedList<T>` in `Application/Common/Models`.
  - Infrastructure: `ToPaginatedListAsync` in `Infrastructure/Data/Pagination`.
  - Web: `ResultHttpExtensions` stays the only Result-to-HTTP mapper, with the new `ToAcceptedHttpResult` (50 AD8 sites migrated, byte-identical answers).
- **SPA.**
  - Module-neutral problem infrastructure: `src/api/{problemDetails,apiTransport,problemCodes.json}`, `src/components/ProblemMessage.jsx` and `src/components/problemFields.js`. Platform no longer imports field-error helpers from `features/identity` (PD-5).
  - Paging helpers in `src/api/pagination.js`: `readPage`, `sendPage`, `readEveryPage` with a 100-page bound, and null-safe bounded arguments.
  - A page-based `useRead`.
  - MUI `TablePagination` on RolesPage, MembersPage, InviteMemberPage, PlatformIdentitiesPage and PlatformPanel's organizations, administrators and audit, with per-page read states, past-the-end correction, bounded resume walks and whole role catalogues.
- **Documents.**
  - identity-access `SPEC.md`: the IA-REQ-038 sub-paragraph `:203-208`, IA-REQ-045 `:246`, rows `:313-316`, `:1040`, `:1044`, the scenario `:471-472`, and the C5 history note `:1073-1075`.
  - `TRACEABILITY.md`: `:22-46`, `:112`, `:118`, `:125`.
  - ADR-004 decision 17 (`:36`); the ADR stays Proposed.
  - The error-handling and UI-composition standards: `SKILL.md:109`, `error-handling-rules.md:288,328,400,476,594` and `ui-composition-rules.md:317-318`.
- **User-visible.**
  - Tenant lists default to 25 rows instead of 100, and `pageSize <= 0` means 1 (D09/PD-1).
  - Page controls replace the "show more" buttons, and PlatformPanel's administrators and audit are reachable past page 1 (PD-6).
  - The role pickers offer the full catalogue (PD-2).
- **Rollback.** `git revert 1335d8f`: a single commit, so the E7-coupled API and SPA revert together. No data migration exists, so no database step is needed.

## 9. Error-handling outcomes

This extends the existing architecture: `Result`/`ApplicationError` internally, the single RFC 9457 writer, and the
strict client reader. **No error code was added, renamed or retired.**
- `problemCodes.json` has the same blob (`cb1efdcc…720d`) at `2716aa6` and after the change.
- `errors.json` in `en` and `es`, `IdentityAccessErrors.cs` and the `*.resx` resources have empty diffs.
- None of `invalid_page`, `invalid_page_size`, `invalid_cursor` or `page_out_of_range` exists.

| Id | Path | Code and status | Where it is shown | Proof |
| --- | --- | --- | --- | --- |
| E1 | `pageNumber`/`pageSize` out of range | none; clamped, `200` | A normal page; the control shows the effective size | `PaginationQueryTests`; `ProblemDetailsContractTests.Out_of_range_platform_page_sizes_are_clamped_over_http_and_never_refused_as_validation`; `RoleAdministrationTests.A_requested_role_page_carries_its_offset_metadata_and_clamps_the_page_size`; `pagination.test.js` |
| E2 | `pageNumber` = `int.MaxValue` | none; clamped to 21,474,836, `200`, no `Error` log | An empty page | `PaginationQueryTests`; `PlatformDirectoryContractTests.Out_of_range_pages_are_clamped_answered_and_never_logged_as_errors` (handler level) |
| E3 | Non-integer or beyond-Int32 value | `400 invalid_request`, problem+json, declared through `WithBodyBindingFailureCode` | The SPA never sends one (bounded client-side) | `OpenApiContractTests.Every_page_parameter_binding_refusal_is_emitted_only_as_declared` (7 routes × 4 values); `PlatformDirectoryContractTests.Every_list_route_declares_offset_parameters_and_its_binding_refusal` |
| E4 | Page past the end | none; `200` with empty `items` and the real totals | The screen requests `totalPages` once, adds no text and draws nothing while it corrects | `PlatformDirectoryContractTests.A_page_past_the_end_is_an_empty_page_with_the_real_totals_not_a_refusal`; `PaginatedListTests`; `RolesPage.test.jsx`, `PlatformIdentitiesPage.test.jsx` |
| E5 | Codes a list read emits (unchanged) | `401 authentication_required`/`invalid_session` (7 routes); `401 recent_mfa_required` (4 Platform); `403 permission_denied`; `404 not_found` (3 tenant); `400 invalid_request`; `500 internal_server_error` | Session loss ends centrally. `recent_mfa_required` opens the Platform second-factor gate. `403`/`404` are refused in place, with rows and control cleared and no retry. `500` shows the generic sentence, "Reference: <traceId>" and "Try again" | `LIST-CODES` identical to baseline; `OpenApiContractTests`; `PlatformDirectoryAccessTests`; the `RoleAdministrationTests` and `MembershipAdministrationTests` scope-mismatch tests; `ProblemDetailsContractTests.A_refused_list_read_is_a_problem_document_from_the_shared_writer` |
| E6 | Tempted paging codes | none added | — | 12.7 search; `OpenApiContractTests.Served_problem_codes_and_checked_in_client_catalogue_are_the_same_contract`; the MSW `problem()` guard |
| E7 | Drift: retired `{ items, nextCursor }`, a missing member, malformed metadata | Client `unreadable_response`, status `0` | Errored read with "We could not read the answer." (`en`; `es` catalogue value) and "Try again" | `problemDetails.test.js`; `pagination.test.js`; `identityClient.test.js`; `platformClient.test.js` |
| E8 | Page-change read states | `errored` (0, 429, ≥500) or `refused` (other 4xx) | Errored keeps the rows and retries the requested page (`common:actions.tryAgain`; `platform:identities.retry` on PlatformIdentitiesPage). Refused clears the rows | `RolesPage.test.jsx`; `PlatformPanel.test.jsx`; `InviteMemberPage.test.jsx`; `useRead.test.jsx` |
| E9 | Page shown by the control | — | The last loaded page | `RolesPage.test.jsx`; `InviteMemberPage.test.jsx` |
| E10 | Clicks that outrun responses | — | Only the last requested page renders | `RolesPage.test.jsx`; `PlatformIdentitiesPage.test.jsx`; `useRead.test.jsx` |
| E11 | A failed refresh after a successful mutation | The read's own code | A read problem only; the mutation is never reported as failed. A `role="status"` confirmation stays where a screen has one (C3-V01 amendment) | `RolesPage.test.jsx` (retirement); the `PlatformPanel.test.jsx` failed-refresh tests |
| E12 | Logging | No `Error` record for a clamp, an empty or past-the-end page, or a binding refusal | — | `PlatformDirectoryContractTests.Out_of_range_pages_are_clamped_answered_and_never_logged_as_errors`; `OpenApiContractTests` binding test |

Related failure paths:

- **A failed resume walk (D11)** shows the action alert "We could not reach the service." and executes nothing (`ExternalProofResume.test.jsx`).
- **A role-catalogue walk (PD-2)** that exceeds 100 pages fails as `unreadable_response`, rendered in the picker's own place with "Try again", which restarts at page 1. `permission_denied` keeps its refused sentence.

What stayed neutral, and why:

- The eight bodyless `202` sites moved to `ToAcceptedHttpResult` (AD8) with byte-identical success and failure: organization registration, personal registration, invitation registration, Platform invitation registration, password recovery, reactivation requests, bootstrap recovery and Platform administrator invitation.
- Sign-in was not migrated.
- No neutral public flow is paginated.
- Collapsed codes are unchanged.
- Enumeration safety is untouched: no status, body, header, timing or screen changed on a neutral route (FT 754/754; journeys 31/31).

SPEC amendments: the identity-access `SPEC.md`, `TRACEABILITY.md` and ADR-004 edits listed in section 8, plus the openspec
`spa-pagination-ui` amendment (C3-V01). The change needed no other SPEC amendment.

Known gaps carried from verify-report #1143, all still true at close:

- The four Platform directories still declare `400 validation_failed`, which no list read can emit; this is error rule 10 (D15, AD9).
- The Platform administrator invitation's neutral `202` has no HTTP-level neutral test.
- No HTTP request exercises a huge valid `pageNumber` or a past-the-end page; both are proven at handler and model level.
- No test renders `unreadable_response` in `es`.
- Coverage was not measured.

## 10. Localization outcomes

- **Keys removed, in `en` and `es`:** `platform:organizations.more`, `platform:identities.more`, `identity:members.showMore`, `identity:invitations.member.showMore` and `identity:roles.showMore`.
- **Kept:** `platform:identities.retry` ("Try again" / "Volver a intentar", PD-7).
- **Added:** none. The locales diff against `2716aa6` is 4 files, +8/−18: exactly the five deletions, plus comma-only rewrites.
- **Unchanged:** `errors.json` (`en`, `es`) and the `.resx` resources. No server-delivered text changed, so no culture plumbing changed.
- **Labels.** `TablePagination` text comes from the MUI locale composed by `themeFor(language)`: `enUS` defaults and `esES`. No screen passes `labelRowsPerPage`, `labelDisplayedRows` or `getItemAriaLabel` (11.15 search exit 1).
- **Invariant data.** Paging member names, OpenAPI names and descriptions, and developer messages in `pagination.js` and `problemDetails.js` stay English (L5).
- **Gates.**
  - `npm run i18n:unused` exit 0.
  - `catalog.contract.test.js` 17/17 (`es` parity).
  - The `problemCatalogue.contract.test.js` words gate iterates `languages.json` `supported` (`en`, `es`), with the registry guard test (A1-03, #1068).
  - `spanish.test.jsx` guards `getItemAriaLabel('next')`.
  - The journeys include the `es` smoke.
- **Translations awaiting native review:** none, because no new Spanish copy was added.

## 11. Engineering and base-product outcome

- **Base-product capability gate.**
  - Offset pagination has one cross-project owner per layer: `Application/Common/Models`, `Infrastructure/Data/Pagination` and `src/api/pagination.js`.
  - The SPA problem transport, catalogue, renderer and field-error helpers are module-neutral.
  - Domain-specific rules stay in their domain: the Identity registration validators remain in `features/identity/fieldErrors.js`.
  - `useRead.js` and `useSubmit.js` stay in `features/identity`, as the proposal scoped.
- **HTTP successes** stay endpoint-specific DTOs or bodyless statuses; no universal envelope exists (12.6 inspection; `ProblemDetailsContractTests`).
- **Strict TDD** held on every work unit, and verify-report's TDD compliance is 6/6.

## 12. Environment side effects during verification

This phase did not re-check these; they come from the launch prompt and verify-report #1143 WARNING 6.

- The journey harness recreated the persistent `dbserver` PostgreSQL container. The container has no data volume, so **the user's local development database is empty**. On a fresh database, EF logs one expected `Error` for the `__EFMigrationsHistory` probe.
- The C: drive briefly reached 0 bytes free during the final verify checks, then recovered: 41 GB free later, and 116 GB free when this phase ran `df -h /c`.
- Docker later stopped answering `docker ps` within 25 s. This phase ran no Docker command.
- Seven orphaned Git Bash `find.exe` processes, scanning `/`, had been stopped earlier with the user's permission (#1141).

This archive ran no test, no Docker command and no root-wide search. It left no temp or snapshot folder behind.

## 13. Pending delivery step: the `openspec/` commit

`openspec/` is untracked (`git status --porcelain -- openspec` gives `?? openspec/`) and is **not committed yet**.

Per decision #1064, the orchestrator commits it after this archive, in one separate `docs(openspec)` conventional commit:

- **Contents:** `openspec/config.yaml`, `openspec/specs/` (the four domain specs and `.gitkeep`), `openspec/changes/archive/.gitkeep`, and the archived change folder, including this report.
- **Procedure:** the commit runs the Native Checking Contract, stages explicit paths only, carries no AI attribution, and is pushed to `origin/main`.

That commit is a post-archive delivery step outside this phase, and it does not block archive.

## 14. Follow-ups

These are out of scope for this change and recorded for separate work.

1. `docs/features/whatsapp-bot/SPEC.md:295,304` and `SCREENS.md:53`: align them with the offset standard **before** the WhatsApp feature is implemented (PD-b, #981). `nextCursor` stays in `FORBIDDEN_SUCCESS_KEYS`, so a client built to the unamended SPEC would get `unreadable_response`.
2. `.agents/skills/error-handling-standards/references/error-handling-rules.md:362`, `:387-389` and `:823` still name the non-existent `features/identity/problemMessages.js` and the old catalogue import.
3. `.agents/skills/error-handling-standards/SKILL.md:17` and `:22` still describe the SPA transport as `features/*/api/`.
4. Snippet drift in `error-handling-rules.md`:
   - the `:594` `fieldError` snippet shows `(problem, name, rule)`, while the helper is `fieldError(problem, name, t)`;
   - the `:476` transport snippet names an identity-specific `CONTEXT` constant.
5. A shared page-state hook (`requested`, `go`, `retry`, `pastTheEnd`, `reloadPage`) for the screens that repeat that state inline.
6. The AppRoutes provider sign-in test (`AppRoutes.test.jsx:67`) has a thin load margin, and the PlatformRetentionPage test once hit 15 s under load.
7. Stale Engram apply-progress prefaces: parts 1–5 say "of 6", and part 1's table still shows 12.1 pending. The archived file is authoritative.
8. `openspec/config.yaml:70-73` (`known_baselines`) and the tasks.md 1.1 record still list entries 1–4.

## 15. Risks

- **Uncommitted archive.** Until the `docs(openspec)` commit lands, the archive, the main specs and this report exist only in the working tree and in Engram. The Engram copies of proposal, spec, design, tasks, apply-progress, verify-report and this report mitigate that.
- **Snapshot claims inside archived files.** The archived `apply-progress.md` and `verify-report.md` contain intermediate claims, for example HEAD `f29ca74` and 12.1 or 11A.3 pending in older sections. Section 7 of this report supersedes them; the archive itself is never edited.
- **WhatsApp drift.** The WhatsApp SPEC still describes cursor pages (follow-up 1).
- **Carried gaps.** The rule-10 gap and the unverified items in section 9 remain.
- **Local database.** The local development database is empty (section 12).

## 16. Engram lineage (observation IDs read by this phase)

All were read in full with `mem_get_observation`. Every artifact preface names the file as authoritative, and the archive
copied the files mechanically, never the Engram text.

| Artifact | IDs (topic) |
| --- | --- |
| Proposal | #978 (`sdd/offset-pagination-standard/proposal`) |
| Spec | #983 (`…/spec`: shared-problem-infrastructure, api-offset-pagination, list-read-error-contract); #1017 (`…/spec-spa-pagination-ui`) |
| Design | #984 (`…/design`, part 1); #1019 (`…/design-part-2`) |
| Tasks | #1033 (`…/tasks`); #1034 (`…/tasks-part-2`) |
| Apply progress | #1065 (`…/apply-progress`); #1066 (`-part-2`); #1079 (`-part-3`); #1087 (`-part-4`); #1100 (`-part-5`); #1133 (`-part-6`); #1140 (`-part-7`) |
| Verify report | #1143 (`…/verify-report`); #1144 (`…/verify-report-part-2`) |
| Decisions | #980 (PD-a clamp); #981 (PD-b WhatsApp out of scope); #982 (proposal approval, PD-1 to PD-7); #1003 (PD-5 expanded); #1063 (one commit); #1064 (`openspec/` commit after archive); #1067 (A1-01 timeout exception); #1068 (A1-03 registry guard); #1130 (C3-V01 option a); #1137 (D1-V01/V02 option a); #1139 (D1-V04 staged waits); #1142 (Phase 13 and Delivery without checkboxes) |
| Gotchas | #1131 (`gotcha/gentle-ai-sdd-attempt-settle-probe`); #1141 (`gotcha/windows-git-bash-find-root-orphans`) |

Total: 29 observations. This report is saved as `sdd/offset-pagination-standard/archive-report` (type architecture,
project `repositoriobase`, `capture_prompt` false).

## 17. Archive contents

| File | Present |
| --- | --- |
| `proposal.md` | yes (22,373 bytes) |
| `specs/api-offset-pagination/spec.md`, `specs/list-read-error-contract/spec.md`, `specs/shared-problem-infrastructure/spec.md`, `specs/spa-pagination-ui/spec.md` | yes (14,172; 9,544; 11,595; 16,637 bytes) |
| `design.md` | yes (56,074 bytes) |
| `tasks.md` | yes (82,712 bytes; 120/120 tasks complete) |
| `exploration.md` | yes (50,184 bytes) |
| `apply-progress.md` | yes (296,033 bytes) |
| `verify-report.md` | yes (61,827 bytes) |
| `archive-report.md` | this file (additive; not part of the move comparison) |

Main specs now reflecting the new behaviour:

- `openspec/specs/api-offset-pagination/spec.md`
- `openspec/specs/list-read-error-contract/spec.md`
- `openspec/specs/shared-problem-infrastructure/spec.md`
- `openspec/specs/spa-pagination-ui/spec.md`

The change has been planned, implemented, verified, delivered and archived. The SDD cycle is complete, apart from the
orchestrator's separate `docs(openspec)` commit.
