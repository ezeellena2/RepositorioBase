# Screen Inventory

**Status:** Proposed. Companion to [SPEC.md](SPEC.md); the specification is normative and this inventory serves it.

Fourteen screens across four actors. Each names the requirements it serves, the states it must handle, and the increment it belongs to. A screen absent from this list is a screen nobody agreed to build.

## What this inventory does not cover

These are the screens the **channel** brings. They are not the product's screens.

Every module brings its own — a list of the documents it issued, a report on what was billed, a configuration page for its own rules — and those belong to that module, specified with it, not here. The assistant is one surface onto a product that must be fully usable without it: a person who never opens WhatsApp should be able to do everything from the web, and a person who lives in WhatsApp should be able to check, correct, and audit the same things there.

That has a practical consequence for sequencing. The channel is worth building when there is already something to reach through it. A module's own screens come first, and the assistant is added to a feature that already works.

## States every screen handles

Before the per-screen notes, six states apply everywhere and are not repeated below. A screen that only defines its happy path is not specified.

| State | Rule |
|---|---|
| Loading | never a blank frame; the shape of what is coming is visible |
| Empty | explains what would fill it and the one action that starts it |
| Error | says what failed and what to do next; carries the Problem Details `code`, never a raw exception |
| Forbidden | the permission is missing; the screen is absent from navigation rather than present and refusing |
| Stale | data was fetched before a state change elsewhere; the screen refetches rather than showing a value it cannot vouch for |
| Degraded | the underlying channel or module is suspended or unverified; actions that would fail are disabled with the reason, not left to fail |

Two rules follow from the specification and bind every screen: a secret is never rendered, only a non-reversible suffix and a replace action (WA-REQ-002); and a value a person is asked to approve is rendered from persisted data, never composed in the client (WA-REQ-034).

---

## Platform panel

The operator's surface. Every screen requires an active Platform tenant and an explicit `platform.*` permission; mutations require recent MFA step-up (WA-REQ-004).

### P1 · Channel configuration
**Serves** WA-REQ-001, 002, 003 · **Increment** first

The eight provider credentials, the declared audiences, and the lifecycle status. Secrets render as a suffix with a replace action and are write-only. A connectivity check runs on demand and reports token validity and number registration with the time of the check.

*Particular states:* never configured; configured but unverified; verification failed with the provider's reason; suspended.

### P2 · Setup checklist
**Serves** WA-REQ-055 · **Increment** first

Each setup item with its outcome and last check, derived from the provider rather than from memory: number registration and display name, **application subscription to the business account**, business verification and account review, quality rating, messaging tier.

*Particular states:* an item unknown because the provider was unreachable — shown as unknown with the last successful check, never as satisfied. The subscription item is the one that most often reads healthy while delivering nothing, so it is given its own emphasis and its own remedy text.

### P3 · Linked numbers
**Serves** WA-REQ-007, 012, 044 · **Increment** first

The global directory across every organization: organization, identity, number, kind, status, last activity. Bounded and cursor-paged. Actions: revoke, block. Creating a link here produces a `Pending` record awaiting confirmation from the handset (WA-REQ-010) — the screen must make that consequence obvious before submitting, or an operator will assume the person is enrolled.

*Particular states:* pending confirmation with time remaining; revoked; membership suspended while the link remains active.

### P4 · Capability catalog
**Serves** WA-REQ-024, 025, 030 · **Increment** first

Every capability with its module, kind, declared audiences, required permission, reversibility, and default confirmation. A query may be authored here; **an action may not** (WA-REQ-025), and the form makes that structural rather than advisory. Per capability, a form tab shows the flow definition and its publication state per channel with checksum drift (WA-REQ-040).

*Particular states:* capability inactive; module unconfigured, so the capability is absent from every catalog; flow definition drifted from what is published.

### P5 · Monitor
**Serves** WA-REQ-020, 032, 046, 056 · **Increment** first

Inbound volume, rejections by unresolved sender, routing outcomes, not-understood rate, token spend by capability and organization, failures, and open alerts. Message bodies are subject to retention and are not rendered in aggregate views.

*Particular states:* an open degradation alert is surfaced here and on P1, because an operator who lives in the monitor should not have to visit configuration to learn the channel is failing.

---

## Organization administration

For an administrator of a client organization, inside their own tenant.

### O1 · Integrations
**Serves** WA-REQ-029, 031 · **Increment** first

Modules available to this organization with their installation and credential health, including expiry warned ahead of effect. A module without valid credentials shows why its capabilities are unavailable rather than leaving the person to discover it in a chat.

### O2 · Module credentials
**Serves** WA-REQ-031 · **Increment** first

**Generated from the module's declared credential schema** — a new module adds no new screen. Handles both secret-bearing and secret-free arrangements, sandbox and production, and the non-secret operating configuration a module needs, such as an issuing point of sale.

*Particular states:* `Pending` because a required element is missing, with that element named; `Pending` because an external authorization has not taken effect yet, with what is being waited on; `Invalid` with the provider's reason; expiring within the warning window.

### O3 · Capability settings
**Serves** WA-REQ-030 · **Increment** first

Per capability: enabled, confirmation mode, amount cap, daily cap, permitted channels. Tightening is always allowed; loosening an irreversible capability below its catalog default is refused, and the control communicates that before submission rather than after.

### O4 · Members and their WhatsApp
**Serves** WA-REQ-007, 010, 012 · **Increment** first

Which members have linked numbers, in what state. Revocation is immediate. Per decision 5 of SPEC section 10, this screen **never** exposes a member's personal profile.

---

## Member

For an ordinary member of an organization, or an individual in their own tenant.

### M1 · My WhatsApp
**Serves** WA-REQ-007, 008 · **Increment** first

The person's own links, one per context, with label and status. Add and revoke. When a second context becomes available, this screen is where the possibility is explained — a person who works for an organization and also acts for themselves needs to understand that these are two links, not one.

### M2 · Link a number
**Serves** WA-REQ-008, 009, 013 · **Increment** first

Enter the number, receive a single-use code, send it from that handset. Shows the code, a prefilled deep link to the conversation, and time remaining. Normalizes the number on entry and shows the canonical form, so a person entering a national variant sees what will actually be matched.

*Particular states:* code expired with reissue; code sent from a different number; already linked in this context.

### M3 · Confirm an action
**Serves** WA-REQ-033, 034, 035, 054 · **Increment** first

The destination of the deep link the assistant sends for anything requiring web confirmation. Renders the persisted summary — never a client-side recomposition — with the acting tenant named whenever the capability exists in more than one of that person's contexts. Confirm and cancel, with antiforgery.

*Particular states:* expired; already confirmed, showing the outcome; cancelled; belonging to another tenant, which is indistinguishable from not found.

---

## Self-service

Roadmap, gated on WA-014 and the identity-access `Personal` tenant slice.

### S1 · Complete registration from a claim
**Serves** WA-REQ-049 · **Increment** roadmap

Reached from the assistant when an unknown sender begins enrolment. Creates the identity, confirms the email, creates the `Personal` tenant, and only then activates the held claim. Shows what the assistant already collected so the person is not asked twice, and states plainly that nothing is active until this completes.

### S2 · External authorization progress
**Serves** WA-REQ-031, 055 · **Increment** roadmap

For a module whose authorization is granted outside the platform and is not effective immediately — a tax-authority delegation, for one. Shows the single step the person must take, a direct link to it, what is being waited on, and the current state. The assistant messages the person when it clears, so this screen is a place to check rather than a place to wait.

---

## Coverage

| Actor | Screens | First increment |
|---|---|---|
| Platform panel | 5 | 5 |
| Organization administration | 4 | 4 |
| Member | 3 | 3 |
| Self-service | 2 | 0 |
| **Total** | **14** | **12** |

WA-009 verifies the twelve first-increment screens end to end. The two self-service screens are verified with WA-014.

