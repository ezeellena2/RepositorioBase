# Identity Access — operations

What an operator configures, what they can watch, and what they still cannot do. Written against the behaviour
that exists on 2026-09-07; where this and the code disagree, the code is what runs and this file is wrong.

The four sections are deliberately separate, because they are four different permissions:

| Section | Who does it | What it needs |
|---|---|---|
| [Synthetic smoke](#1-synthetic-smoke) | anyone with a checkout | nothing outside the repository |
| [Operator configuration](#2-operator-configuration) | whoever holds the deployment's settings | keys, a policy, a mail transport |
| [External account activation](#3-external-account-activation) | whoever owns the Google project and the mail domain | accounts this repository has never held |
| [Release evidence](#4-release-evidence) | whoever signs off | everything above, plus what is still open |

Nothing in section 1 grants anything in sections 2–4, and finishing section 3 does not finish section 4.

## 1. Synthetic smoke

The scope this system is closed at: **local B2B/B2C with synthetic data**. See
[RUNNING-LOCALLY.md](RUNNING-LOCALLY.md) for the run itself. What matters operationally is what a smoke run
proves and what it does not:

- It proves the journeys work over the real routes, the real cookies, the real antiforgery and real PostgreSQL.
- It does **not** prove anything about real personal data, a real mail domain, a real identity provider, or a
  restore performed by an authority outside this repository. Those are section 3 and section 4.

A deployment with `IdentityAccess:PersonalData:Mode` unset is `Synthetic`, and that is the fail-safe direction:
anything that is not exactly `Real` is synthetic, including a misspelling.

## 2. Operator configuration

### The settings that stop a deployment from starting

Two configurations are contradictions rather than gaps, and a process holding one refuses to start
(`IdentityDeploymentGuard`). Both refusals name the missing setting and never a configured value.

| Refused | Why it is a refusal rather than a runtime failure |
|---|---|
| `IdentityAccess:Recovery:Deployment` set, `IdentityAccess:Recovery:VerificationKey` missing | The deployment is armed for restore and cannot verify evidence, so it would answer `503` to every route. The process knows that before it opens a socket. |
| `IdentityAccess:PersonalData:Mode=Real` with no readable `IdentityAccess:RetentionPolicy` | Running with no policy is legitimate and erases nothing. Running with no policy over real people's data is a decision somebody has to have taken. |
| `IdentityAccess:PersonalData:Mode=Real` with no `IdentityAccess:People:DocumentProtection` key for its current version | This also fails closed at the point of use, but the failure lands on the person registering rather than on the operator who forgot the key. |

Everything else fails closed where it is used, which is the right place for it. This guard holds only what is
worse than a refusal at the point of use.

### Data Protection: the one that is silent when it is wrong

`IdentityAccess:DataProtection:{ApplicationName,KeyRingPath,CertificatePath,CertificatePassword}` must be
**identical in the web application and in the outbox worker**. The web application seals a confirmation token into
an outbox message and the worker opens it; a key ring the two processes do not share turns every invitation into
an envelope nobody can open, and the symptom arrives hours later as mail that never went out.

**Replacing the wrapping certificate carelessly is silent.** Data Protection does not refuse a key ring it cannot
decrypt: it skips the keys it cannot read and writes one of its own. A deployment given the wrong certificate
therefore looks healthy — new mail is sealed and delivered — while every envelope already in flight is dead.
Nothing warns. The evidence is a rise in settlements with reason `envelope_unreadable`, which is why that counter
exists. `WebWorkerKeyCompatibilityTests` holds both halves of this.

Rotation is safe: a new key in the ring does not orphan what an old one sealed, and both are readable by a
process that starts afterwards.

### Attempt budgets

Every budget is a row in `IdentityAttemptBudgets`, shared by every instance and surviving every restart. The
numbers are **product choices** — the SPEC fixes none:

| Scope | Limit | Window | Key |
|---|---|---|---|
| `identity.login.client` | 20 | 5 min | the client address, opaque digest |
| `identity.login.account` | 10 | 15 min | the normalized account, opaque digest |
| `platform.mfa.attempt` | 5 | 15 min | the identity behind the validated session |
| `platform.bootstrap.recovery` | 5 | 15 min | the pending invitation and the transport source |
| `personal.document.claim` | 3 | 24 h | the identity |

The login budgets bound six routes, not one: `POST /api/identity/sessions`, `/credentials/password/recovery`,
`/account/reactivation-requests`, `/account/reactivate`, `/external/{provider}/login/start` and
`/external/complete`. They share one client budget and one account budget on purpose — they are the same front
door.

**An unreachable store answers `503 service_unavailable` with `Retry-After: 30`, never `429`.** Telling somebody
who spent nothing that they spent everything is a lie, and it hides the outage from whoever is watching. Every
route the budgets bound declares both answers in its OpenAPI contract.

**High-cardinality abuse is bounded by a sweep, not by a refusal.** A caller who varies the key writes a row per
key and the spending path cannot tell that from real traffic; refusing to write would hand them the limit they
wanted removed. Closed windows decide nothing and are swept in the worker's maintenance loop, five thousand rows
a run, oldest first. **A deployment running the web application without the outbox worker sweeps nothing** — the
table then grows until somebody prunes it by hand.

### Retention and restore

Both have their own runbook: [RECOVERY-AND-RETENTION.md](RECOVERY-AND-RETENTION.md). Two operational notes belong
here rather than there:

- The maintenance loop and the sweep run **in the outbox worker**, not in the web application.
- A deployment that has not been admitted (`Closed` or `Quarantined`) delivers nothing and claims nothing, so a
  growing backlog is the expected symptom of a closed deployment, not a delivery fault.

### Mail

[EMAIL-SETUP.md](EMAIL-SETUP.md) is the configuration. Operationally: `IdentityAccess:Email:Enabled` not being
true makes the worker announce, once, at start, that it will deliver nothing. Silence is the worst possible
answer there — a worker that never dispatched looks exactly like a broken application rather than like a switch
nobody turned on.

## 3. External account activation

None of this has been done, and none of it can be done from this repository. Each needs an account somebody owns.

| Activation | Owner | What is still missing |
|---|---|---|
| Google as an identity provider | whoever owns the Google Cloud project | a real client id and secret, real redirect URIs, and a consent screen. Every provider test runs against a controlled OIDC provider in-process. |
| A mail domain | whoever owns the domain | SPF, DKIM and a Resend (or equivalent) key. Local runs write to a folder. |
| Real personal data (G2) | the deployment's data owner | nothing here has run against real data, and the mode stays `Synthetic` |
| Restore authority | whoever performs restores | the rehearsal signs its evidence with a key a test made up. No external authority has issued anything. |

## 4. Release evidence

### What to watch

Four instruments, all with closed label sets, under the meter `CleanArchitecture.IdentityAccess`:

| Instrument | Labels | The question it answers |
|---|---|---|
| `identity_access.attempt_budget.decisions` | `scope`, `outcome` | Are refusals rising? Is the store unreachable (`outcome=Unavailable`)? |
| `identity_access.outbox.settlements` | `status`, `reason` | Are envelopes unreadable or expired? Is a provider failing? |
| `identity_access.retention.erased` | `category` | Did maintenance actually erase anything? |
| `identity_access.recovery.admission` | `state` | Is this process open, quarantined or closed? |

**No label names a caller.** Not the key, not the address, not the identity, and not the stored digest — the
digest is stable per caller, so an exporter holding it counts one person across days, which is the directory
these instruments must not become. `IdentityAccessMetricsTests` fails if a key label is added.

The alerts worth having are `outcome=Unavailable` appearing at all, `reason=envelope_unreadable` appearing at
all, and a `state` other than `NotRecovering` persisting after a deployment is meant to be released.

**Diagnostics carry none of it either.** A provider callback code, a session handle, a document number and an
address are each enough to become somebody. `SafeTelemetryTests` asks a real request for each, with a witness
proving the capture covered the request — so an absence check cannot pass against a capture of nothing.

### What a release does not follow from

- Passing suites are evidence about synthetic data on one machine. They are not a production readiness claim, a
  compliance assessment, or a legal opinion about the retention periods somebody configures.
- The restore rehearsal proves the adapter and the pipeline over a real physical copy. It is not restore
  certification.
- "Insufficient external deletion evidence keeps Personal data quarantined" has no code to exercise: nothing in
  this system models external deletion evidence.
- Retention categories other than sessions and documents are skipped and recorded, not implemented.
