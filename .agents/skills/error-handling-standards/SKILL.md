---
name: error-handling-standards
description: "Trigger: any change that creates, maps, shows, logs or tests a failure — a command, validator or value-object rule, an error code, an endpoint's problem contract, middleware that answers, the SPA transport, a form or read that renders a refusal, a background loop. Apply the repository's error-handling standard end to end."
license: Apache-2.0
metadata:
  author: repository-maintainers
  version: "1.0"
---

## Activation Contract

Load before adding or changing anything that can fail in front of a caller, or anything that reports a failure:

- a MediatR request, its validator or handler gate, or a Domain value object's input rule;
- an `ApplicationError` factory, an `ApiProblemContract`, an endpoint's `WithApiProblemDetails` / `WithBodyBindingFailureCode`, or middleware that writes a response;
- `ProblemDetailsExceptionHandler`, `ApiProblemDetailsMapper`, the MediatR behaviours, a hosted service or worker loop;
- the SPA transport (`features/*/api/`), `useSubmit`, `ProblemMessage`, `IdentityProvider`, routing, and any screen that renders a refusal;
- the tests for any of the above.

Load it together with `engineering-standards`, and with `frontend-design-standards` for anything that renders. Exclude layout or copy changes that touch no failure path, and documentation without technical impact.

Error-handling work legitimately edits `features/*/api/`, adds tests, and changes `disabled` expressions and message copy where a rule below requires it. The `frontend-design-standards` prohibitions on those still bind any visual change made alongside.

## The standing decision

The error architecture exists and is not reopened. Extend it; never replace it.

- **Expected failures** are `Result` / `Result<T>` carrying exactly one `ApplicationError`:
  - a stable `code`;
  - one `ApplicationErrorCategory`;
  - an optional public-safe `Detail`;
  - `ValidationErrors` only for `Validation`;
  - `RetryAfterSeconds` only for `RateLimited` / `Unavailable`.
- **Unexpected failures** stay exceptions.
- **One HTTP writer**, `IProblemDetailsService` (`ApiProblemDetailsMapper`). Every non-success is RFC 9457 `application/problem+json` with `type`, `title`, matching `status`, `instance`, `code`, `traceId`, an optional `detail`, and `errors` only for validation (IA-REQ-038).
- **Category to status is one exhaustive switch:**

  | Category | Status |
  | --- | --- |
  | Validation | 400 |
  | Authentication | 401 |
  | Authorization | 403 |
  | NotFound | 404 |
  | Conflict | 409 |
  | RateLimited | 429 + `Retry-After` |
  | Unavailable | 503 + `Retry-After` |

  An unexpected exception is a fixed `500 internal_server_error` with no detail.
- **No leakage of internals.** The internal `Result` and any `{ success, data, error }` envelope never cross the wire.
- **The client reads failures only through `problemDetails.js`.** It requires problem+json with a code, drops `detail` on 5xx and parses `Retry-After`. The words a person sees come from the client catalogue, keyed by `code`, never from server `detail`.
- **One join key.** `Activity.Current`'s trace id links the problem body, the logs and the denial audit.

This skill adds the rules that make that architecture complete in every layer.

## Enumeration safety outranks helpfulness

Some flows answer the same neutral result — bodyless `202` or `204` — whatever the account or token state, and tell the real address owner by email. They are:

- anonymous organization and personal registration;
- invitation and Platform-invitation registration;
- password recovery and reactivation requests;
- sign-in;
- Platform administrator invitation and bootstrap recovery.

This is required by IA-REQ-003, IA-REQ-016, IA-REQ-019 and IA-REQ-048 and by SPEC section 6. Never turn one of them into a distinguishing error, status, body, header, response time or screen.

**Collapsed codes are deliberate.** `invalid_invitation`, `invalid_reactivation`, `invalid_credential_token`, `invalid_external_login`, `invalid_platform_operation`, `invalid_credential_proof`, `personal_registration_conflict`, `session_not_found`, and `not_found` for another tenant's resource each merge several causes on purpose. The reason is documented in `IdentityAccessErrors`. Never split one without a SPEC amendment.

**Allowed, and required, on those same routes:** field errors for input that is wrong on its own. That covers a missing field, bad email syntax, a CUIT check digit, a DNI length, a password the policy refuses, and a missing or malformed token. Such a check is enumeration-safe only when all three hold:

1. It reads nothing but the request and the caller's own validated session. No address, token or account lookup.
2. It runs before any lookup.
3. It answers byte-identically, apart from `traceId`, for a taken and a free address and for a live and a dead token. A test proves this.

Sign-in, password recovery and reactivation requests stay neutral even for malformed input, because SPEC.md:249 and :252 pin them. The client's own shape checks cover them.

A neutral outcome must still be explained with a next step that is true for everyone. For example: "Already have an account? We emailed you instead — sign in to add the organization to it."

## Hard rules

### Domain and Application

1. An expected failure is `Result.Failure(<Context>Errors.X())`. Never throw for an expected outcome. Never return a `Result` for a programmer or infrastructure fault.
2. **Input-only rules are field errors.** This covers required, length, syntax, closed sets, check digits and password policy. They go in a FluentValidation `AbstractValidator<TCommand>` beside the command, or in a handler gate that runs before any lookup. The answer is `400 validation_failed` with `errors` keyed by the request's JSON member names. Operation codes (`invalid_registration`, `invalid_invitation`, `invalid_role_operation`, …) are reserved for refusals that depend on state or are collapsed on purpose.
3. **Every rule carries its own message** (`WithMessage`). It describes the rule, never the value, and is written for the person filling the form. A FluentValidation default message never reaches a user. Password-policy text comes from ASP.NET Identity's describer, which states the rule.
4. **A format invariant lives once, in its value object** (`NormalizedCuit`, `NormalizedDocument`), and validators call that same rule.
   - A CUIT is 11 digits with a valid modulo-11 check digit.
   - The rule is enforced where a value enters the system, never where a stored value is read back. EF converters keep a non-validating materialization path, so older rows stay readable.
5. **A port that validates returns why.** `ValidatePasswordAsync` returns the policy's rule descriptions, as `IdentityCredentialService.Describe` already does for reset and change.
6. **Validators on `IPublicRequest` commands** depend on nothing that reads stored state, apart from the caller's own `IValidatedOptionalSession`.
7. **A refusal names what was wrong.**
   - A wrong second-factor code, a wrong recovery code and a wrong password are each named as such. None of them is `invalid_session`, `invalid_invitation` or another cause's code.
   - `401` means the identity is absent or invalid (IA-REQ-030). A mistyped code inside a valid session is a `400`.

### HTTP boundary

8. **Every non-success goes through `IProblemDetailsService`.** Nothing writes a problem body by hand. Middleware that answers before routing still produces `code`, `traceId` and `instance`.
9. **`errors` keys on the wire are camelCase JSON member paths.** They are normalized once, in `ApiProblemDetailsMapper.Create`, whatever produced them.
10. **Every route declares every code it can emit, and nothing it cannot.**
    - Its body-binding code is declared.
    - `RequireAuthorization` routes declare `401 authentication_required` and `invalid_session`, and `403 permission_denied`.
    - A route that spends an attempt budget declares both `429 rate_limit_exceeded` and `503 service_unavailable` (IA-REQ-057).
11. **Unknown `/api/*` paths** answer `404 not_found` problem+json, never the SPA's `index.html`.
12. **`UseExceptionHandler` runs before any middleware that can throw.**

### The catalogue — one source of truth

13. **A code is born once**: one factory in `<Context>Errors` (today `IdentityAccessErrors`) and one contract in `ApiProblemMetadata`. A factory that collapses several causes says so in its XML doc.
14. **`src/Web/ClientApp/src/api/problemCodes.json` is checked in** and maps every code the API can answer to its status.
    - A backend contract test fails when the served OpenAPI `x-problem-codes` union, plus the pre-routing boundary codes, differs from it.
    - A Vitest test fails when a code in it has no message, or when a message key is neither in it nor a client code.
    - The MSW `problem()` helper refuses any `(status, code)` pair that is not in it.
15. **Dead factories and dead contracts are deleted.**

### Frontend

16. **Failures are classified in one place, the transport.**
    - A problem document becomes an `ApiProblem`.
    - Anything else becomes a `ClientFailure` with `status: 0` and one of `network_unavailable`, `request_timeout`, `unreadable_response` or `client_failure`. These four are the only codes the client may invent.
    - `unexpected`, `context_unreadable`, and `internal_server_error` with status 0 are retired.
17. **Every mutation runs through `useSubmit`**, or its `toProblem` helper. No awaited call in an event handler is left uncaught.
18. **Server field errors are shown on the field.**
    - Use MUI `TextField` `error` + `helperText` from `problem.errors[name]`, where `name` is the form's state key and equals the JSON member.
    - `ProblemMessage` still shows the code's sentence and lists only the keys no field claimed.
    - After a refused submit, focus the first field in error. With no field error, focus the `Alert`.
    - Every required field keeps `slotProps={{ inputLabel: { required: false } }}`.
19. **The client mirrors input-only rules** for immediate feedback: CUIT check digit, DNI length, and the password rule stated in `helperText`. It blocks the submit, and never replaces the server's check.
20. **A refusal appears where the action was taken**: at the top of a short card, or beside the row, editor or dialog that sent it.
    - Refusals are `role="alert"`; confirmations are `role="status"`. Never both for one event.
21. **Per status:**
    - `401` `invalid_session` / `authentication_required` from any call except the context read ends the client session centrally. The context is cleared, `ProtectedRoute` sends the person to `/login?returnUrl=…`, and the sign-in page says why. `credential_superseded`, `recent_proof_required` and `recent_mfa_required` are not session loss.
    - `403`: explain it in place; never redirect.
    - `404`: the thing is not available. An unknown route renders a not-found page.
    - `409`: offer a refresh, and keep what the person typed.
    - `429` / `503`: disable the action, count `Retry-After` down, then re-enable it.
    - `5xx` and client failures: a generic sentence, plus `Reference: <traceId>` when there is one. Offer a retry where repeating is safe.
    - A mutation whose outcome is unknown (timeout, or network loss after sending) is never reported as failed. Tell the person to refresh and check before trying again.
22. **Reads have four states**: loading, refused, errored, loaded. A skeleton shows only while loading. Whether to offer a retry is decided from status and code, never from whether a problem document arrived.
23. **Error boundaries.** One sits inside the theme; a second wraps the routes, keyed on the path. `createRoot` reports caught and uncaught errors.
24. **A mailed-link screen without its token** says the link is incomplete and disables its action. It never submits an empty token.
25. **Follow-up calls never turn a success into a failure.** A housekeeping call after a successful mutation (token refresh, context reload) is never reported as the mutation failing.

### Background processing and logging

26. **Record each unexpected exception exactly once, where it is swallowed**: the unexpected branch of `ProblemDetailsExceptionHandler` for requests, the loop's `catch` for background passes. Layers that rethrow do not log it at `Error`.
27. **What that record may carry:** the trace id, the endpoint display name or loop name, the exception type chain, stack frames (type and method names only), and a `DbException.SqlState`.
    - It never carries `Exception.Message`, `ToString()`, parameters, SQL text, payloads or personal data (IA-REQ-029).
    - Never pass the exception object to `ILogger`.
    - Never enable `EnableSensitiveDataLogging`, `EnableDetailedErrors` or Npgsql `Include Error Detail` outside Development.
28. **Expected failures are not errors.**
    - `Result` failures are not logged.
    - The boundary-mapped exceptions (`ValidationException`, `UnauthorizedAccessException`, `ForbiddenAccessException`, `NotFoundException`, `BadHttpRequestException`, `JsonException`) are never logged at `Error`.
    - Security denials are audited.
29. **A background loop:**
    - survives a failed pass;
    - backs off exponentially across consecutive failures;
    - logs the safe record, and never message identifiers or payloads.

    An outbox message carries the trace id of the request that queued it.

### Tests

30. **Every error path ships with the test that proves it**, at the narrowest level that can:
    - **Validator**: each rule's key and message, and that no message contains the value.
    - **HTTP**: status, media type, `code`, `traceId`, the `status` member, the exact `errors` keys, no echoed secret, and zero durable effects.
    - **Neutral route**: parity between taken and free addresses, and between live and dead tokens.
    - **Contract**: declared codes and emitted codes match on every endpoint.
    - **Logging**: exactly one safe `Error` for a forced fault, and none for an expected refusal.
    - **SPA**: the field is marked (`aria-invalid`, accessible description), focus moves, the catalogue matches, a network failure is handled, a mid-flow 401 redirects, the 429 countdown works, and the error boundary renders.

    A test that asserts only "an alert is visible" proves no message.

## Decision gates

| Situation | Action |
| --- | --- |
| A check you want on a neutral route | Apply the three-part test. If it needs a lookup, the route stays neutral. |
| A SPEC row pins another code for input-only failures (SPEC.md:243, :244, :263 today) | Amend the SPEC in the same change that ships the field errors. Never ship a code the SPEC contradicts. |
| A new failure cause | Reuse a code only if the person does the same thing about it and its message stays true. Otherwise add a factory, contract, catalogue entry, message and test together. |
| Tempted to render server `detail` | Don't. Write or fix the client message for that code. |
| A dependency outage outside the attempt-budget store | Stays `500 internal_server_error` (IA-REQ-038). Record it once with its `SqlState`. A 503 needs a SPEC amendment. |
| A framework 413/415 or wrong-method request | Today it is 400 with the binding code, or the `/api` 404. A new category needs a SPEC amendment. |
| Tempted to log the exception "just to see it" | Log the safe record. More needs a redaction-policy decision, not a code change. |
| Rewording an existing message | Only when the sentence is false for a case it covers. Update the tests that pin it in the same change. |
| A fixture uses an undeclared code or status | Fix the fixture. Never declare the fixture's code. |
| A value object's rule gets stricter | Keep a non-validating read path for stored rows, and migrate test fixtures in the same change. |

## Execution steps

1. Trace the failure from producer to screen: handler or validator, then `IdentityAccessErrors`, `ApiProblemMetadata`, the endpoint declaration, `problemCodes.json`, the client messages, and finally the screen. Note whether the route is neutral.
2. Choose the category, code and field keys from the rules and gates. Write the discriminating test first.
3. Change the backend, the catalogue, the client message and the screen in one change. A code without words, or words without a code, is not done.
4. Run the affected `dotnet test` projects, then `cd src/Web/ClientApp && npx vitest run && npx eslint src/ && npx vite build`.

## Output contract

For each error path touched, report:

- its producer, code and status, where it is shown, and the test that proves it;
- what was left neutral, and why;
- the SPEC amendments it needs;
- the commands run and their results;
- anything unverified.

No new plan documents.

## References

- [Error-handling rules](references/error-handling-rules.md) — copyable patterns per layer: value object, validator, wire shape, catalogue, endpoint contract, logging, transport, inline field errors, session loss, error boundary, and the test for each.
