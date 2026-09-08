# Identity Access — delivery record

What was built, what it is evidence for, and what it is not. Written on 2026-09-07 against the tree this file
lands in. Where this and the code disagree, the code is what runs and this file is wrong.

**The claim this record supports, in full and with nothing implied beyond it:**

> Local B2B/B2C identity access is functionally complete with synthetic data.

Everything below is inside that sentence. Nothing below is a production readiness claim, a compliance
assessment, or a statement about real people's data.

## 1. What is here

A multitenant identity foundation on .NET 10 and PostgreSQL with a React client, delivered as Tasks 17–28 of
[the plan](../../superpowers/plans/2026-08-31-identity-access-foundation.md) and recorded task by task in
[TASKS.md](TASKS.md).

| Area | What a person can do |
|---|---|
| Registration | Register an organization, or a personal account, with the same neutral answer whether or not the address is taken; confirm from a delivered link |
| Sign-in | Password sign-in, session cookies bound to the host, a list of the devices they hold, and ending one or all the others |
| Credentials | Change a password from inside, recover one from a mailed link, and prove presence again for anything sensitive |
| Providers | Link and unlink Google over a real OpenID Connect round trip, with no linking by matching address |
| Organizations | Custom roles, member administration, invitations with one usable offer at a time, and ownership transfer |
| Personal | A documentary identity, masked wherever it is shown, correctable only through a two-party dispute |
| Platform | A bootstrapped owner, MFA-bound administration, organization suspension, and a second factor that can be recovered |
| Operations | Shared abuse budgets, deployment guards, retention with legal holds, and restore admission |

## 2. What the journeys prove, and where

Twenty-four identity journeys in the browser — eleven for identity access, seven for the continuation Tasks
19–25 added, six for Platform — over the real routes, real cookies, real antiforgery and delivered mail, with
the rest of the suites below them. No journey confirms an address, grants a membership or composes a token URL
in SQL: the links are read out of the mail the dispatcher actually delivered, because the tokens are sealed with
keys only the application holds.

Coverage is recorded separately by level, because they answer different questions. The browser answers whether a
person can do it. The request-level suites answer what happens under concurrency, replay and refusal, which a
browser cannot pose — the registration privacy sequence and the hold-versus-purge race are both there for that
reason, not because a journey was skipped.

The full matrix and what covers each row is in [TASKS.md](TASKS.md#task-28--done-2026-09-07).

## 3. The verification run

Recorded command by command in [TASKS.md](TASKS.md#task-28--done-2026-09-07). Every applicable command passed.
The two build warnings are `ASPIRE010`, about a CLI bundle this repository does not install; they are unrelated
to anything here and are the reason the build is reported as warning-free *for applicable warnings* rather than
warning-free outright.

## 4. The decisions that shaped it

Seven acceptances (C1–C7) and their amendments are recorded in [SPEC §14](SPEC.md) and
[ADR-004](../../decisions/ADR-004-Adopt-Multitenant-Identity-Access.md). The ones that show up most in the code:

- **Neutral answers.** Registration, recovery and bootstrap recovery answer the same way whatever the state is,
  so no route is an oracle for whether an address has an account.
- **Proof is spent, not held.** A sensitive action consumes a single-use server-side proof bound to one session
  and one action. A proof bought to change a password does not pay for anything else.
- **Fail closed at the point of use.** No retention policy means nothing is erased. No document key means no
  document is recorded. No admission evidence means nothing is served. Two configurations are worse than that,
  and a deployment holding either refuses to start at all.
- **Budgets are rows.** Every abuse control is one budget for the deployment, not one per process.

## 5. What was found on the way, and named rather than absorbed

Each of these was found by a test that failed, and each is written up where it happened:

- **Two ports that were written, documented, registered and read by nothing.** `AdmitsDelivery` and
  `PredatesRecovery` looked finished from inside the task that built them. Neither had a test that failed when
  it was deleted, which is the check that would have caught them.
- **Four abuse budgets held in process memory**, two of whose docstrings said so plainly. A suite that only
  ever asks one process cannot tell that design from a real limit.
- **Three tests that proved nothing**, each found by removing the protection and watching them pass anyway.
- **A screen that pointed a signed-in person at a stranger's signup**, so the thing they wanted could not be
  reached at all.
- **An acceptance suite at 7 of 21**, for two premises that had quietly stopped being true.
- **Data Protection does not refuse a key ring it cannot decrypt.** It writes a new key and carries on, so a
  wrongly replaced certificate looks healthy while every envelope already in flight is dead.

## 6. What is not closed, and who owns it

None of these is a defect, and none is closed by anything above.

| Gate | Owner | What is missing |
|---|---|---|
| Real personal data (G2) | the deployment's data owner | Nothing here has run against real data; the mode stays `Synthetic`, and a deployment that says `Real` without a retention policy refuses to start |
| Production (G3) | the deployment owner | Real keys, a real mail domain, a real provider, an operator recovery authority |
| Google activation | whoever owns the Google project | A real client, redirect URIs and a consent screen. The protocol is proved against a controlled provider; Google's own behaviour is not |
| Live restore certification | the restore authority | The rehearsal takes a real physical copy and signs its evidence with a key a test made up |
| Legal or compliance certification | legal | This describes a mechanism, not an assessment of whether the configured periods are lawful |
| Full reference-standard compliance | whoever accepts the standard | Unadopted mandatory controls are recorded as deviations, not as gaps that were closed |
| Multi-machine abuse limits | a deployment | Two hosts in one test process share a database and nothing else — the property, but not a network |
| Google in the browser | a later task | Needs a controlled provider on an endpoint the web process trusts, or `RequireHttpsMetadata` turned off in production code. Neither is worth what it adds |
| External deletion evidence | a later task | Nothing in this system models it, so the accepted clause has no code to exercise |
| Retention categories beyond sessions and documents | a later task | Skipped and recorded, not implemented |

## 7. Where to go next

- [RUNNING-LOCALLY.md](RUNNING-LOCALLY.md) — run it and walk the journeys yourself.
- [OPERATIONS.md](OPERATIONS.md) — what an operator configures, watches, and still cannot do.
- [RECOVERY-AND-RETENTION.md](RECOVERY-AND-RETENTION.md) — lost second factors, stored personal data, restore.
- [EMAIL-SETUP.md](EMAIL-SETUP.md) — mail transport.
- [TRACEABILITY.md](TRACEABILITY.md) — requirement to test, with the gaps named as gaps.

## 8. What a next increment should do first

In this order, because each makes the next one worth doing:

1. **Activate one external account** — the mail domain, then Google — and re-walk the journeys against it.
   Every remaining unknown about those two is behaviour nobody in this repository can observe.
2. **Exercise the budgets across two machines**, not two processes. The property is proved; the network is not.
3. **Model external deletion evidence**, which is the one accepted clause with no code at all.
4. **Take the real-PII decision explicitly**, with the retention policy and key ownership in front of whoever
   owns the data. The deployment guard already refuses the careless version of it.
