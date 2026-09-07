# Identity Access — recovery and retention runbook

What an operator can do about a lost second factor and about stored personal data, and — as importantly — what
they cannot. Written against the behaviour that exists on 2026-09-07; where this and the code disagree, the code
is what runs and this file is wrong.

Scope: **synthetic data only**. Real personal data (G2) and production (G3) are separate gates that nothing here
grants. Nothing in this file has been exercised against real personal data, and the tests behind it use synthetic
fixtures throughout.

## Recovering a Platform second factor

`POST /api/platform/mfa/recover` replaces the factor of somebody whose authenticator is gone. It is the only
route in the system that replaces a working factor without proving that factor, so everything else about the
caller has to be true at once:

| Required | Why |
|---|---|
| an authenticated, confirmed identity | the invitation was consumed at activation; authority now comes from the membership |
| `platform.mfa.enroll` | grants nothing by itself — it is the chance to prove a factor, not the factor |
| antiforgery | the same as every other state-changing route |
| a password proved a moment ago for `platform.mfa.recover` | its own proof action, so a proof bought to change a password does not pay for this |
| one recovery code nobody has spent | the thing that actually makes this recovery rather than rotation |

The answer is `200` with a new shared key, a provisioning URI and a new set of recovery codes, **shown once**.
Nothing here is readable again: the secret is stored encrypted, the codes only as hashes, and there is no route
that reads either back.

**What the operator must expect afterwards.** The replacement has not been proved by anybody, including the
session that just recovered it. The next Platform change answers `401 recent_mfa_required` until they step up
with the new authenticator. That is deliberate: a recovery that left the previous step-up standing would hand
Platform authority to a caller who showed a code from a piece of paper and nothing else.

**Being signed into Platform is not a bar.** Signing in selects the only tenant an operator belongs to, so the
person who lost their authenticator is already inside the Platform tenant by the time they ask.

**Failures and what they mean.**

| Answer | What happened |
|---|---|
| `400 invalid_credential_proof` | a wrong code, a spent one, a code from a set some earlier recovery replaced, or an enrollment nobody ever completed — deliberately one answer for all four |
| `401 recent_proof_required` | no password proof, or one bought for another action |
| `409 platform_mfa_concurrency_conflict` | two devices recovered at once and this one lost; the other holds the factor that exists |
| `429 rate_limit_exceeded` + `Retry-After` | the per-identity budget that `/verify` and `/step-up` share |

**Known gap.** SPEC also gives this route `503 service_unavailable` when the shared budget store is unreachable
(IA-REQ-057). The limiter cannot currently distinguish "unreachable" from "exhausted", and fail-closed behaviour
is Task 27's subject for *every* budget rather than this route alone. Until then, an unreachable store answers
like an exhausted budget, which is the wrong thing to tell somebody. It is listed in Task 27, not fixed here.

## Retention

### The policy is configuration, and there is no default

`IdentityAccess:RetentionPolicy` is the only place a retention period exists. **No period, threshold or
jurisdictional number appears anywhere in the source**, and none is supplied as a fallback.

```
IdentityAccess:RetentionPolicy:PolicyId          RET-2026-01
IdentityAccess:RetentionPolicy:Version           3
IdentityAccess:RetentionPolicy:Owner             platform-operations
IdentityAccess:RetentionPolicy:ApprovedOn        2026-01-15
IdentityAccess:RetentionPolicy:Source            docs/policies/retention-2026.md
IdentityAccess:RetentionPolicy:BackupTreatment   restored copies are re-purged before admission
IdentityAccess:RetentionPolicy:Categories:0:Category          SessionRecords
IdentityAccess:RetentionPolicy:Categories:0:RetentionPeriod   P90D
IdentityAccess:RetentionPolicy:Categories:0:Trigger           LastActivity
IdentityAccess:RetentionPolicy:Categories:0:Action            Erase
IdentityAccess:RetentionPolicy:Categories:0:EvidenceRequired  true
```

`RetentionPeriod` is an ISO-8601 duration. `Category`, `Trigger` and `Action` come from closed sets and are
matched case-sensitively.

**Every way of failing to read a policy produces the same result: no policy, and therefore no destructive
action, in either personal-data mode.** A missing section, a missing identifier, an unparseable `ApprovedOn`, a
period that is not a duration, a category naming something outside the set — all of them. A category the parser
cannot read is dropped rather than completed, because inventing the missing line would be this system writing
policy rather than following it. `GET /api/platform/retention/policy` shows exactly what was understood, and a
null `policyId` there means this deployment will delete nothing.

### What the executor does, and what bounds it

`LifecycleMaintenanceService` runs in the **outbox worker process**, not in the web application — a loop
registered there would start inside every functional test, and this one deletes rows. It has no route, no
permission and no caller that is a person.

| Bound | Value | Why it exists |
|---|---|---|
| interval | 15 minutes | product default |
| rows per pass | 500 | a large batch holds locks too long |
| passes per run | 10 | an unbounded loop never yields |
| wall-clock budget | 60 seconds | a cheap-looking pass over a big table can still outlast the interval |
| single-flight | per category, PostgreSQL advisory lock `0x5E5513` | a second instance leaves a claimed category alone rather than duplicating its work |

Whichever bound runs out first ends the run. What was not reached is reached next time; nothing is claimed that
is not finished.

**A run in a deployment with no Platform tenant does nothing at all.** Audit records are tenant-scoped and this
worker acts for Platform, so an un-bootstrapped deployment has nowhere to record what a purge did — and erasing
without being able to say so is not something this does.

**And a run in a deployment that has not been admitted does nothing either.** The dispatcher refuses before it
claims anything, so a closed or quarantined deployment sends no mail and spends none of a message's eight
attempts — see the restore section below.

### What it currently erases

| Category | Trigger | Implemented |
|---|---|---|
| `SessionRecords` | `RecordCreation`, `LastActivity` | yes — revoked or expired sessions past the period; **a live session is never eligible however old it is** |
| `PersonalIdentityDocument` | `RecordCreation` | yes — ciphertext and every retained fingerprint, together |
| everything else in the closed set | — | **not yet**; the category is skipped and recorded rather than silently treated as "nothing to do" |

A purge writes a `PersonalDataErasureRecord` in the same transaction as the erasure, naming the policy and
version that authorized it and how many rows it reached. The record has a `Restrict` foreign key, so it outlives
every attempt to tidy it away — including deleting the identity it names. The document row that is left is a
non-identifying tombstone stamped with `PurgedAt`, `PurgePolicyId` and `PurgePolicyVersion`, and **a purged
number is reclaimable**: the fingerprints go with the ciphertext, so the number stops occupying the unique index.

### Audit

| Event | Outcome / reason |
|---|---|
| `personal.data.purged` | `purged`, with the category as its reason |
| `personal.data.retention.skipped` | `policy_absent`, `legal_hold`, `synthetic_classification`, `trigger_not_implemented` |
| `personal.data.hold.placed` / `.released` | the operator's reason code |

**An idle pass writes nothing at all.** A worker that recorded every quarter-hour of finding nothing would bury
the one record an operator actually needs to see.

## Legal holds

`POST /api/platform/retention/holds` and `DELETE /api/platform/retention/holds/{holdId}` need
`platform.retention.manage`, an active Platform tenant, a recent MFA step-up and antiforgery. Reading the policy
needs `platform.retention.read` and that the session has proved the factor at least once.

**A hold stops erasure and does nothing else.** It is not an account state. It suspends nobody, refuses no
sign-in, blocks no reactivation, and no authorization decision reads it. Releasing one restores nothing, because
nothing was taken — only eligibility for erasure returns. This is enforced structurally: `RetentionLegalHold`
lives in a domain slice nothing older may depend on, and the architecture tests refuse a dependency on it.

**A hold and a purge of one subject can never both take effect.** Both sides take `SELECT … FOR UPDATE` on the
subject's document rows, and the executor takes it *before* it reads the holds — reading first and locking
afterwards would leave exactly the window where a hold commits between the two. Whichever reaches the rows first
wins, and the loser is told which of the two happened: a hold that lost answers `409
retention_hold_subject_purged`, and a purge that lost affects zero rows and records `reason=legal_hold`. A hold
cannot be made retroactive.

- One standing hold per `(subject, reason)`; a second answers `409 retention_hold_conflict`.
- A different reason is a different hold, and each ends when whoever placed it says so.
- Release is idempotent and silent about existence: repeating a release and naming a hold that never existed
  answer alike, because "does this hold exist" is not a question that route is for.
- `reasonCode` and `reference` are both `^[A-Za-z0-9._:-]{1,64}$`. A retention record is read by people who are
  not the subject, and it is not a place to write prose about them.

## Restore admission

A deployment is armed by naming `IdentityAccess:Recovery:Deployment`. From that moment nothing else in the
configuration can talk it back down: a missing `VerificationKey` is not "unarmed", it is a deployment that cannot
verify and therefore stays closed. A deployment that names nothing is not recovering and serves as it always did.

| State | What it serves | What it delivers |
|---|---|---|
| `Closed` — the default whenever evidence is insufficient | Nothing but `/health` and `/alive`, which answer with a status and no body. Everything else: `503` `recovery_admission_closed` + `Retry-After`. | Nothing. The dispatcher refuses **before the claim**, so no lease is taken and no attempt is spent — a deployment that sat closed for a day opens to find its messages intact. |
| `Quarantined` — verified evidence without the `release` claim | `/api/identity/antiforgery`, `/api/identity/sessions`, `/api/identity/credentials/reauthenticate`. Everything else answers `503`. Deliberately excluded: the context route, which reports tenants and permissions read from restored rows; password recovery, which mints a token from restored state; and the provider callback, which would turn a restored external link into a session. | Nothing. |
| `Open` — verified evidence carrying `release` | Everything. | Everything **except** what predates the recovery epoch. |

**What "predates the epoch" costs.** A session created before the epoch counts as revoked: it came out of the
backup, and it may have been revoked in the hours the backup does not contain. A message written before the epoch
is never sent and its envelope is terminalized: the token sealed into it comes from those same missing hours.

**The evidence.** `{ deployment, recoveryEpoch, backupId, issuedAt, expiresAt, release, signature }`, HMAC-SHA256
over a canonical form with an operator-held key from `IdentityAccess:Recovery:VerificationKey`, read from
`:Evidence` or a file at `:EvidenceFile`. Absent, unreadable, expired, wrongly signed or wrong-deployment evidence
keeps admission closed **on every process start**, because the decision is taken at construction and never cached
anywhere durable — the only durable place a restored deployment has is the restored database.

**What has actually been rehearsed.** `RestoreRehearsalTests` takes a real physical copy with
`CREATE DATABASE … TEMPLATE`, makes writes against the original that the copy therefore predates, and walks all
three states over the copy. It proves the adapter and the pipeline. It is **not** restore certification: the test
signs the evidence with a key it made up, and no external authority issued anything.

## What is not closed by any of this

| Gate | Owner | Missing evidence |
|---|---|---|
| Real personal data (G2) | the deployment's data owner | nothing here has run against real data; the mode stays `Synthetic` |
| Production (G3) | the deployment owner | out of scope for every task up to 28 |
| Legal or compliance certification of retention and erasure | legal | this file describes a mechanism, not an assessment of whether the periods somebody configures are lawful |
| `503` on an unreachable budget store | Task 27 | the limiter cannot yet distinguish unreachable from exhausted |
| Categories other than sessions and documents | a later task | skipped and recorded, not implemented |
| Live restore certification | the restore authority | a physical copy proves the adapter and the pipeline; no external authority issued the evidence |
| "Insufficient external deletion evidence keeps Personal data quarantined" | a later task | nothing in this system models external deletion evidence, so the clause has no code to exercise |
