# Error-handling rules

Concrete patterns for the [skill](../SKILL.md), grounded in this repository's files. Copy the shape; keep each screen's existing ids, labels and copy.

## 1. The wire shape

This is what `ApiProblemDetailsMapper` writes (`src/Web/Infrastructure/ApiProblemDetailsMapper.cs`). `errors` appears only for `Validation`, and its keys are camelCase JSON members.

```json
{
  "type": "about:blank",
  "title": "Bad Request",
  "status": 400,
  "instance": "/api/identity/organizations/register",
  "code": "validation_failed",
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
  "errors": {
    "cuit": ["That CUIT's check digit does not match. Check the number."],
    "password": ["Passwords must be at least 12 characters."]
  }
}
```

The other shapes:

- `429` / `503` add the `Retry-After` header. The body carries no retry member.
- `500` is always `code: "internal_server_error"`, with a `traceId` and nothing about the fault.

## 2. Choosing category and code

| The failure is… | Category / status | Code |
| --- | --- | --- |
| Input wrong on its own (shape, syntax, check digit, policy) | Validation / 400 | `validation_failed` + `errors` |
| A state or deliberately collapsed refusal | Validation / 400 or Conflict / 409 | the operation code (`invalid_invitation`, `registration_conflict`, …) |
| No identity, or an invalid one | Authentication / 401 | `authentication_required`, `invalid_session` |
| A proof or second factor is required | Authentication / 401 | `recent_proof_required`, `recent_mfa_required` |
| A valid identity without permission | Authorization / 403 | `permission_denied` or a specific code |
| Another tenant's resource, or a missing one | NotFound / 404 | `not_found` |
| A lost update | Conflict / 409 | `*_concurrency_conflict` |
| An exhausted budget, or the budget store is down | RateLimited / 429, Unavailable / 503 | `rate_limit_exceeded`, `service_unavailable` (+ `retryAfterSeconds`) |
| A bug or an infrastructure fault | exception → 500 | `internal_server_error` |

Neutral routes (never distinguish account or token state): `POST /api/identity/organizations/register` (anonymous), `/personal/register`, `/sessions`, `/credentials/password/recovery`, `/account/reactivation-requests`, `/api/invitations/register`, `/api/platform/invitations/register`, `/api/platform/admins/invitations`, `/api/platform/bootstrap/recover`. Sign-in shows the pattern: it collapses every failure a stranger can provoke to `204` (`src/Web/Endpoints/Identity/SessionEndpoints.cs:90-99`).

## 3. Domain: one invariant, two paths

`NormalizedCuit.From` is also the EF converter on read (`OrganizationProfileConfiguration.cs:16`, `PendingRegistrationIntentConfiguration.cs:38`). Enforce the check digit on input and keep the read path non-validating, so that rows written before the rule stay readable.

```csharp
// src/Domain/IdentityAccess/Organizations/NormalizedCuit.cs
public enum CuitRule { Satisfied, Empty, Characters, Length, CheckDigit }

public readonly record struct NormalizedCuit
{
    private static readonly int[] Weights = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2];

    private NormalizedCuit(string value) => Value = value;

    public string Value { get; }

    /// <summary>A CUIT entering the system: every rule, the check digit included.</summary>
    public static NormalizedCuit From(string value) =>
        Evaluate(value, out var digits) is var rule && rule == CuitRule.Satisfied
            ? new NormalizedCuit(digits)
            : throw new ArgumentException($"The CUIT breaks the {rule} rule.", nameof(value));

    /// <summary>A CUIT read back from storage. Stored before the check-digit rule existed, so only its shape is asserted.</summary>
    public static NormalizedCuit FromStored(string value) =>
        value is { Length: 11 } && value.All(char.IsAsciiDigit)
            ? new NormalizedCuit(value)
            : throw new InvalidOperationException("A stored CUIT is not eleven digits.");

    /// <summary>Which rule a submitted value breaks. It reads nothing but the value, so a validator on a neutral route may call it.</summary>
    public static CuitRule Evaluate(string? value, out string digits)
    {
        digits = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return CuitRule.Empty;
        var collected = new List<char>(11);
        foreach (var character in value)
        {
            if (char.IsAsciiDigit(character)) collected.Add(character);
            else if (character != '-' && !char.IsWhiteSpace(character)) return CuitRule.Characters;
        }
        if (collected.Count != 11) return CuitRule.Length;
        var sum = 0;
        for (var i = 0; i < 10; i++) sum += (collected[i] - '0') * Weights[i];
        var expected = (11 - sum % 11) % 11;          // 11 → 0; 10 → no valid digit exists
        if (expected == 10 || expected != collected[10] - '0') return CuitRule.CheckDigit;
        digits = new string([.. collected]);
        return CuitRule.Satisfied;
    }

    public override string ToString() => Value;
}
```

In the same change:

- the two EF converters switch to `NormalizedCuit.FromStored`;
- fixtures move off `30-12345678-9`, which fails the check digit; `30-12345678-1` is valid;
- the test CUIT generator computes the check digit instead of always emitting `-9`.

Domain test:

```csharp
[TestCase("30-12345678-1", CuitRule.Satisfied)]
[TestCase("20-12345678-6", CuitRule.Satisfied)]
[TestCase("30-12345678-9", CuitRule.CheckDigit)]
[TestCase("20-00000001-0", CuitRule.CheckDigit)]   // remainder 1: no digit can be valid
[TestCase("30-1234567-1", CuitRule.Length)]
[TestCase("30-ABCDEFGH-1", CuitRule.Characters)]
public void Evaluates_the_submitted_cuit(string value, CuitRule expected) =>
    NormalizedCuit.Evaluate(value, out _).ShouldBe(expected);
```

## 4. Application: a shape validator for a neutral route

It sits beside the command, like `RegisterPlatformInviteeCommandValidator` (`src/Application/IdentityAccess/Platform/Invitations/RegisterPlatformInvitee.cs:20-26`). `ValidationBehaviour` runs it before the handler, which means before any lookup.

```csharp
// src/Application/IdentityAccess/Organizations/RegisterOrganization/RegisterOrganizationCommandValidator.cs
/// <summary>
/// Shape only, and nothing that reads stored state. The password policy reads no state either (a decoy user),
/// so every answer here is identical for a taken and a free address (IA-REQ-003/048).
/// </summary>
public sealed class RegisterOrganizationCommandValidator : AbstractValidator<RegisterOrganizationCommand>
{
    private static readonly IReadOnlyDictionary<CuitRule, string> CuitMessages = new Dictionary<CuitRule, string>
    {
        [CuitRule.Empty] = "Enter the CUIT.",
        [CuitRule.Characters] = "A CUIT has only digits and hyphens.",
        [CuitRule.Length] = "A CUIT has 11 digits.",
        [CuitRule.CheckDigit] = "That CUIT's check digit does not match. Check the number."
    };

    public RegisterOrganizationCommandValidator(IIdentityAccountService identities, IValidatedOptionalSession session)
    {
        RuleFor(c => c.LegalName)
            .Must(value => !string.IsNullOrWhiteSpace(value)).WithMessage("Enter the organization's legal name.")
            .MaximumLength(256).WithMessage("Use at most 256 characters.");
        RuleFor(c => c.Cuit)
            .MaximumLength(32).WithMessage("A CUIT has 11 digits.")
            .Custom((value, context) =>
            {
                var rule = NormalizedCuit.Evaluate(value, out _);
                if (rule != CuitRule.Satisfied) context.AddFailure(CuitMessages[rule]);
            });
        RuleFor(c => c.Email)
            .Must(value => !string.IsNullOrWhiteSpace(value) && value.Contains('@')).WithMessage("Enter an email address.")
            .MaximumLength(256).WithMessage("Use at most 256 characters.");
        // A signed-in caller registers for their own address and their password is not used (handler :56).
        RuleFor(c => c.Password)
            .MaximumLength(256).WithMessage("Use at most 256 characters.")
            .CustomAsync(async (password, context, ct) =>
            {
                foreach (var rule in (await identities.ValidatePasswordAsync(password ?? string.Empty, ct)).Failures)
                    context.AddFailure(rule);
            })
            .When(_ => session.IdentityId is null);
    }
}
```

The port returns reasons. Collect every validator's failures and keep reading no state:

```csharp
// IIdentityAccountService.cs
public sealed record IdentityAccountValidationResult(bool IsValid, IReadOnlyList<string>? Failures = null)
{
    public IReadOnlyList<string> Failures { get; } = Failures ?? [];
}

// IdentityAccountService.ValidatePasswordAsync
var failures = new List<string>();
foreach (var validator in passwordValidators)
    failures.AddRange((await validator.ValidateAsync(userManager, DecoyUser, password)).Errors.Select(e => e.Description));
return new IdentityAccountValidationResult(failures.Count == 0, failures);
```

The handler's `TryNormalize` stays as a backstop, and valid wire input no longer reaches it. `invalid_registration` remains for the signed-in email mismatch (SPEC.md:243).

Validator unit test (FluentValidation.TestHelper, Moq):

```csharp
[Test]
public async Task A_wrong_check_digit_is_a_cuit_field_error_that_never_echoes_the_value()
{
    var identities = new Mock<IIdentityAccountService>();
    identities.Setup(i => i.ValidatePasswordAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new IdentityAccountValidationResult(true));
    var session = Mock.Of<IValidatedOptionalSession>();
    var validator = new RegisterOrganizationCommandValidator(identities.Object, session);

    var result = await validator.TestValidateAsync(new RegisterOrganizationCommand("owner@example.test", "Testing1234!", "Northwind", "30-12345678-9"));

    result.ShouldHaveValidationErrorFor(c => c.Cuit).WithErrorMessage("That CUIT's check digit does not match. Check the number.");
    result.Errors.ShouldAllBe(e => !e.ErrorMessage.Contains("12345678"));
}
```

## 5. HTTP boundary

Wire keys are normalized once, in `ApiProblemDetailsMapper`. The same change caches the serializer options:

```csharp
private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

Errors = error.Category == ApplicationErrorCategory.Validation && error.ValidationErrors.Count > 0
    ? WireKeys(error.ValidationErrors)
    : null

private static IReadOnlyDictionary<string, string[]> WireKeys(IReadOnlyDictionary<string, string[]> errors) =>
    errors.GroupBy(pair => string.Join('.', pair.Key.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName)), StringComparer.Ordinal)
          .ToDictionary(group => group.Key, group => group.SelectMany(pair => pair.Value).ToArray(), StringComparer.Ordinal);
```

The endpoint declares what it can emit (`src/Web/Endpoints/Identity.cs:31-34`):

```csharp
group.MapPost("/organizations/register", Register)
    .Produces(StatusCodes.Status202Accepted)
    .WithApiProblemDetails(ApiProblemMetadata.AntiforgeryValidationFailed, ApiProblemMetadata.ValidationFailed,
        ApiProblemMetadata.InvalidRegistration, ApiProblemMetadata.InvalidSession, ApiProblemMetadata.RegistrationConflict,
        ApiProblemMetadata.InternalServerError)
    .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidRegistration.Code);
```

In `Program.cs`, unknown `/api` paths get a problem, and the exception handler comes first:

```csharp
app.UseForwardedHeaders();
app.UseExceptionHandler(options => { });   // before anything that can throw
// … admission guard, headers, file server, auth …
app.MapFallback("/api/{**path}", (HttpContext context, IProblemDetailsService problems) =>
    problems.WriteAsync(context, new ApplicationError("not_found", ApplicationErrorCategory.NotFound), context.RequestAborted));
#if (!UseApiOnly)
app.MapFallbackToFile("index.html");
#endif
```

Pre-routing refusals also go through the writer. In `RecoveryAdmissionMiddleware`, the singleton writer reads no database:

```csharp
public async Task InvokeAsync(HttpContext context, IProblemDetailsService problems)
{
    // … admitted → next …
    await problems.WriteAsync(context, new ApplicationError("recovery_admission_closed",
        ApplicationErrorCategory.Unavailable, "This deployment is not admitting requests.", retryAfterSeconds: 60));
}
```

The HTTP test uses the existing style (`RegistrationHttpValidationTests.cs`), plus a parity check on a neutral route:

```csharp
[Test]
public async Task Malformed_registration_names_its_fields_and_answers_alike_for_a_taken_and_a_free_address()
{
    const string taken = "owner-taken@example.test";
    await IdentityHttpHarness.SeedConfirmedUserAsync(taken, "Testing1234!");
    var bodies = new List<string>();
    foreach (var email in new[] { taken, "owner-free@example.test" })
    {
        var host = $"https://registration-fields-{Guid.NewGuid():N}.localhost";
        var token = await BootstrapAsync(host);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{host}/api/identity/organizations/register")
        { Content = JsonContent.Create(new { email, password = "short", legalName = "Northwind", cuit = "30-12345678-9" }) };
        request.Headers.Add("Origin", host);
        request.Headers.Add("X-CSRF-TOKEN", token);

        var response = await FunctionalTestSetup.HttpClient.SendAsync(request);
        var payload = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation_failed", hasErrors: true);
        payload.GetProperty("errors").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ShouldBe(["cuit", "password"]);
        var text = payload.GetRawText();
        text.ShouldNotContain("short");
        bodies.Add(System.Text.RegularExpressions.Regex.Replace(text, "\"(traceId|instance)\":\"[^\"]*\"", string.Empty));
    }

    bodies[0].ShouldBe(bodies[1]);
    (await TestApp.CountAsync<RegistrationSubmission>()).ShouldBe(0);
    (await TestApp.CountAsync<OutboxMessage>()).ShouldBe(0);
}
```

`AssertProblemAsync` (in `ProblemDetailsContractTests.cs:297-310`) is extended to also assert `status == (int)response.StatusCode`, `type == "about:blank"` and a non-empty `title`.

## 6. The catalogue — one source for server and client

`src/Web/ClientApp/src/features/identity/problemCodes.json` is checked in, maps each code to its status, and is sorted:

```json
{
  "antiforgery_validation_failed": 400,
  "authentication_required": 401,
  "internal_server_error": 500,
  "invalid_credential_token": 400,
  "not_found": 404,
  "rate_limit_exceeded": 429,
  "recovery_admission_closed": 503,
  "validation_failed": 400
}
```

The excerpt above is partial; the real file lists all 50 or more codes.

The backend side is a functional test beside `OpenApiContractTests`. `v1.json` is gitignored and built, so the test reads the served document:

```csharp
[Test]
public async Task The_shared_catalogue_is_exactly_what_the_api_can_answer()
{
    var paths = JsonDocument.Parse(await (await FunctionalTestSetup.HttpClient.GetAsync("/openapi/v1.json")).Content.ReadAsStringAsync())
        .RootElement.GetProperty("paths");
    var declared = new SortedDictionary<string, int>(StringComparer.Ordinal)
    {
        ["recovery_admission_closed"] = 503   // answered before routing, so no route declares it
    };
    foreach (var path in paths.EnumerateObject())
    foreach (var operation in path.Value.EnumerateObject())
    {
        if (operation.Value.ValueKind != JsonValueKind.Object || !operation.Value.TryGetProperty("responses", out var responses)) continue;
        foreach (var response in responses.EnumerateObject())
            if (response.Value.TryGetProperty("x-problem-codes", out var codes))
                foreach (var code in codes.EnumerateArray())
                    declared[code.GetString()!] = int.Parse(response.Name, CultureInfo.InvariantCulture);
    }

    var catalogue = JsonSerializer.Deserialize<SortedDictionary<string, int>>(
        File.ReadAllText(RepositoryFile("src/Web/ClientApp/src/features/identity/problemCodes.json")))!;
    declared.ShouldBe(catalogue);
}

private static string RepositoryFile(string relative)
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CleanArchitecture.slnx"))) directory = directory.Parent;
    return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repository root not found."), relative);
}
```

The per-endpoint rules come from endpoint metadata:

```csharp
[Test]
public void Every_route_declares_the_codes_its_pipeline_can_answer()
{
    using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
    var endpoints = scope.ServiceProvider.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
        .Where(e => e.RoutePattern.RawText?.StartsWith("/api/", StringComparison.Ordinal) == true);
    foreach (var endpoint in endpoints)
    {
        var codes = endpoint.Metadata.GetOrderedMetadata<ApiProblemContractMetadata>().SelectMany(m => m.Contracts).Select(c => c.Code).ToHashSet();
        var name = endpoint.DisplayName;
        if (endpoint.Metadata.GetMetadata<ApiBodyBindingFailureMetadata>() is { } binding) codes.ShouldContain(binding.Code, name);
        if (endpoint.Metadata.GetMetadata<IAuthorizeData>() is not null && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null)
            codes.ShouldBeSupersetOf(["authentication_required", "invalid_session", "permission_denied"], name);
        if (endpoint.Metadata.GetMetadata<LoginAttemptBudgetMetadata>() is not null)
            codes.ShouldBeSupersetOf(["rate_limit_exceeded", "service_unavailable"], name);
    }
}
```

The client side keeps the words in a plain module, `features/identity/problemMessages.js`:

```js
export const CLIENT_CODES = ['network_unavailable', 'request_timeout', 'unreadable_response', 'client_failure'];
export const FALLBACK = 'That request could not be completed.';
export const MESSAGES = {
  // … the existing 43 entries …
  invalid_credential_token: 'That reset link has expired or was already used. Ask for a new one.',
  personal_registration_conflict: 'Your personal account could not be created. If this address is confirmed, sign in and try again.',
  session_not_found: 'That session has already ended. Refresh the list.',
  personal_profile_concurrency_conflict: 'Your profile changed while you were editing. Refresh and try again.',
  profile_field_not_editable: 'That detail cannot be changed here.',
  email_confirmation_required: 'Confirm your email address first.',
  invalid_request: 'That request could not be read. Reload the page and try again.',
  recovery_admission_closed: 'The service is not accepting requests right now. Try again shortly.',
  network_unavailable: 'We could not reach the service. Check your connection. If you were saving something, refresh to see whether it was saved before trying again.',
  request_timeout: 'That took too long to answer. Refresh to see whether it went through before trying again.',
  unreadable_response: 'We could not read the answer. Reload the page and try again.',
  client_failure: 'Something went wrong on this page. Reload and try again.',
};
```

The Vitest contract, plus a fixture guard in `src/test/identityServer.js`:

```js
// features/identity/problemCatalogue.contract.test.js
import catalogue from './problemCodes.json';
import { CLIENT_CODES, MESSAGES } from './problemMessages';

describe('problem catalogue', () => {
  it.each(Object.keys(catalogue))('has words for %s', (code) => expect(MESSAGES[code], code).toBeTruthy());
  it('has no words for a code nobody sends', () => {
    const known = new Set([...Object.keys(catalogue), ...CLIENT_CODES]);
    expect(Object.keys(MESSAGES).filter((code) => !known.has(code))).toEqual([]);
  });
});

// test/identityServer.js
import catalogue from '../features/identity/problemCodes.json';
export const problem = (status, code, extra = {}, headers = {}) => {
  if (catalogue[code] !== status) throw new Error(`The API never answers ${status} ${code}; see problemCodes.json.`);
  return HttpResponse.json({ code, traceId: 'trace-1', ...extra }, { status, headers: { 'Content-Type': 'application/problem+json', ...headers } });
};
```

## 7. Logging: once, safely

```csharp
// src/Application/Common/Logging/SafeFailure.cs — types, frames, SqlState; never Message
public static class SafeFailure
{
    public static string Types(Exception exception) =>
        string.Join(" <- ", Chain(exception).Select(e => e.GetType().FullName));

    public static string Frames(Exception exception, int take = 12) =>
        string.Join(" < ", (new System.Diagnostics.StackTrace(exception, false).GetFrames() ?? [])
            .Select(f => f.GetMethod()).Where(m => m is not null).Take(take)
            .Select(m => $"{m!.DeclaringType?.FullName}.{m.Name}"));

    public static string? SqlState(Exception exception) =>
        Chain(exception).OfType<System.Data.Common.DbException>().Select(e => e.SqlState).FirstOrDefault(s => s is not null);

    private static IEnumerable<Exception> Chain(Exception? exception)
    {
        for (; exception is not null; exception = exception.InnerException) yield return exception;
    }
}
```

`ProblemDetailsExceptionHandler` writes the single record from its unexpected branch:

```csharp
if (error is null)
{
    var feature = httpContext.Features.Get<IExceptionHandlerFeature>();
    logger.LogError("Unexpected failure (unexpected_failure) at {Endpoint}; TraceId {TraceId}; Types {Types}; SqlState {SqlState}; Frames {Frames}",
        feature?.Endpoint?.DisplayName ?? "unmatched",
        System.Diagnostics.Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier,
        SafeFailure.Types(exception), SafeFailure.SqlState(exception), SafeFailure.Frames(exception));
    await problemDetails.WriteUnexpectedAsync(httpContext, cancellationToken);
    return true;
}
```

`UnhandledExceptionBehaviour` stops logging at `Error`. It logs nothing for boundary-mapped exceptions, and at most `Debug` with the request name for anything else. The background loop (`src/OutboxWorker/Worker.cs`):

```csharp
catch (Exception exception)
{
    consecutiveFailures++;
    logger.LogError("An outbox dispatch pass failed (outbox_pass_failed); Types {Types}; SqlState {SqlState}; Frames {Frames}",
        SafeFailure.Types(exception), SafeFailure.SqlState(exception), SafeFailure.Frames(exception));
}
var wait = consecutiveFailures == 0
    ? (delivered == 0 ? IdleInterval : TimeSpan.Zero)
    : TimeSpan.FromSeconds(Math.Min(300, 5 * Math.Pow(2, consecutiveFailures - 1)));
```

The logging test uses the capture already installed by `WebApiFactory.cs:140-186`:

```csharp
TestApp.ResetCapturedLogs();
TestApp.ForceUnexpectedFailure();
var payload = await AssertProblemAsync(faulted, HttpStatusCode.InternalServerError, "internal_server_error");
var records = TestApp.CapturedLogs.Where(e => e.StartsWith("[Error] CleanArchitecture.", StringComparison.Ordinal)).ToArray();
records.Length.ShouldBe(1);
records[0].ShouldContain(payload.GetProperty("traceId").GetString()!);
records[0].ShouldContain("Types");
records[0].ShouldNotContain(token);
// …and a validation_failed / 403 request produces no "[Error] CleanArchitecture." record at all.
```

## 8. Transport: classify once, report session loss

This goes in `features/identity/api/apiTransport.js`:

```js
const CONTEXT = '/api/identity/context';
const SESSION_LOST = new Set(['invalid_session', 'authentication_required']);
const TIMEOUT_MS = 30_000; // product default

export class ClientFailure extends Error {
  constructor(code) { super(code); this.name = 'ClientFailure'; this.problem = { code, status: 0 }; }
}
export const toProblem = (failure) => failure?.problem ?? { code: 'client_failure', status: 0 };
export const isRetryable = (problem) => problem.status === 0 || problem.status === 429 || problem.status >= 500;

// inside createApiTransport()
const sessionLost = new Set();
const request = async (path, init) => {
  try { return await fetch(path, { ...init, signal: AbortSignal.timeout(TIMEOUT_MS) }); }
  catch (failure) { throw new ClientFailure(failure?.name === 'TimeoutError' ? 'request_timeout' : 'network_unavailable'); }
};
const refuse = async (path, response) => {
  if (!isProblem(response)) throw new ClientFailure('unreadable_response');
  const problem = await readProblem(response).catch(() => { throw new ClientFailure('unreadable_response'); });
  if (response.status === 401 && SESSION_LOST.has(problem.code) && path !== CONTEXT) sessionLost.forEach((listener) => listener(problem));
  return new ApiProblem(problem);
};
const bootstrapAntiforgery = async () => {
  requestToken = null;                                   // a failed refresh leaves nothing stale behind
  const response = await request(ANTIFORGERY);
  if (!response.ok) throw await refuse(ANTIFORGERY, response);
  requestToken = (await readSuccess(response, ['requestToken'])).requestToken;
  return requestToken;
};
// send(): response = await request(path, {...}); ok → readSuccess (drift → ClientFailure('unreadable_response'));
// otherwise const refusal = await refuse(path, response); refresh on antiforgery_validation_failed with .catch(() => {}); throw refusal;
return { bootstrapAntiforgery, hasRequestToken: () => requestToken !== null, send,
  onSessionLost: (listener) => { sessionLost.add(listener); return () => sessionLost.delete(listener); } };
```

`IdentityProvider` ends the client session centrally. `ProtectedRoute` then redirects with `returnUrl`, and `LoginPage` already renders `contextProblem`:

```js
useEffect(() => identityClient.transport?.onSessionLost?.((problem) => {
  if (!mounted.current) return;
  setContext(null);
  setContextProblem(problem);
}), [identityClient]);
```

The same file also fixes these:

- `signIn` catches the post-sign-in bootstrap failure and still returns `loadContext()`.
- `signOut` clears the context only after a `204` or a `401`. Any other failure keeps the context and rethrows to a caller that shows it.

## 9. Submitting and presenting

`useSubmit` classifies once and counts `Retry-After` down:

```js
export function useSubmit(action) {
  const [problem, setProblem] = useState(null);
  const [isBusy, setIsBusy] = useState(false);
  const [result, setResult] = useState(null);
  const [waitSeconds, setWaitSeconds] = useState(0);

  useEffect(() => {
    if (waitSeconds <= 0) return undefined;
    const tick = setTimeout(() => setWaitSeconds((seconds) => seconds - 1), 1000);
    return () => clearTimeout(tick);
  }, [waitSeconds]);

  const submit = useCallback(async (...args) => {
    setProblem(null);
    setIsBusy(true);
    try {
      const value = await action(...args);
      setResult(value ?? true);
      return value;
    } catch (failure) {
      const refused = toProblem(failure);
      setProblem(refused);
      setWaitSeconds(refused.retryAfterSeconds ?? 0);
      return undefined;
    } finally {
      setIsBusy(false);
    }
  }, [action]);

  return { submit, problem, isBusy, result, waitSeconds };
}
```

`ProblemMessage` shows the code's sentence, lists only unclaimed errors, and adds a reference and focus:

```jsx
export function ProblemMessage({ problem, claimed = [], waitSeconds, autoFocus = false }) {
  const ref = useRef(null);
  useEffect(() => { if (problem && autoFocus) ref.current?.focus(); }, [problem, autoFocus]);
  if (!problem) return null;

  const unclaimed = Object.entries(problem.errors ?? {}).filter(([field]) => !claimed.includes(field));
  const wait = waitSeconds ?? problem.retryAfterSeconds;
  return (
    <Alert ref={ref} tabIndex={-1} severity="error">
      <Typography variant="body2">{MESSAGES[problem.code] ?? FALLBACK}</Typography>
      {wait > 0 && <Typography variant="body2">Try again in {wait} seconds.</Typography>}
      {unclaimed.length > 0 && (
        <Box component="ul" sx={{ m: 0, pl: 2.5 }}>
          {unclaimed.map(([field, messages]) => <li key={field}>{messages.join(' ')}</li>)}
        </Box>
      )}
      {problem.status >= 500 && problem.traceId && (
        <Typography variant="caption" component="p">Reference: {problem.traceId}</Typography>
      )}
    </Alert>
  );
}
```

The field helper lives in `features/identity/fieldErrors.js`:

```js
/** The field's refusal when there is one, otherwise the rule it always states. Keys are the JSON member names. */
export const fieldError = (problem, name, rule) => {
  const messages = problem?.errors?.[name];
  return messages?.length ? { error: true, helperText: messages.join(' ') } : { error: false, helperText: rule };
};
export const firstInvalid = (problem, names) => names.find((name) => problem?.errors?.[name]?.length);
```

A client mirror of the Domain rule, in `features/identity/register/cuit.js`, uses the same sentences as the validator:

```js
const WEIGHTS = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2];
/** Mirrors NormalizedCuit.Evaluate. It never replaces it: the server checks again. */
export function cuitMessage(value) {
  const raw = value ?? '';
  if (raw.trim() === '') return 'Enter the CUIT.';
  if (/[^\d\s-]/.test(raw)) return 'A CUIT has only digits and hyphens.';
  const digits = raw.replace(/\D/g, '');
  if (digits.length !== 11) return 'A CUIT has 11 digits.';
  const sum = WEIGHTS.reduce((total, weight, index) => total + weight * Number(digits[index]), 0);
  const expected = (11 - (sum % 11)) % 11;
  return expected === 10 || expected !== Number(digits[10]) ? "That CUIT's check digit does not match. Check the number." : null;
}
```

An inline field error on `RegisterOrganizationPage.jsx` keeps every id, label and the `required` slot:

```jsx
const FIELDS = ['legalName', 'cuit', 'email', 'password'];
const IDS = { legalName: 'register-legal-name', cuit: 'register-cuit', email: 'register-email', password: 'register-password' };
const [clientErrors, setClientErrors] = useState(null);
const { submit, problem, isBusy, result, waitSeconds } = useSubmit((request) => identity.client.registerOrganization(request));
const shown = clientErrors ?? problem;                        // a client refusal renders exactly like a server one
const invalid = firstInvalid(shown, FIELDS);

useEffect(() => { if (invalid) document.getElementById(IDS[invalid])?.focus(); }, [shown, invalid]);

const onSubmit = (event) => {
  event.preventDefault();
  const cuit = cuitMessage(form.cuit);
  setClientErrors(cuit ? { errors: { cuit: [cuit] } } : null);
  if (!cuit) submit(form);
};

<ProblemMessage problem={problem} claimed={FIELDS} waitSeconds={waitSeconds} autoFocus={!invalid} />
<TextField
  id="register-cuit"
  label="CUIT"
  required
  fullWidth
  slotProps={{ inputLabel: { required: false } }}
  {...fieldError(shown, 'cuit', 'Include the check digit.')}
  value={form.cuit}
  onChange={update('cuit')}
/>
<TextField
  id="register-password"
  label="Password"
  type="password"
  autoComplete="new-password"
  required
  fullWidth
  slotProps={{ inputLabel: { required: false } }}
  {...fieldError(shown, 'password', 'At least 12 characters, with upper and lower case, a digit and a symbol.')}
  value={form.password}
  onChange={update('password')}
/>
<Button type="submit" variant="contained" size="large" fullWidth disabled={isBusy || waitSeconds > 0}>Register</Button>
```

Stock MUI already makes this accessible: `error` sets `aria-invalid` on the input, and an `id` ties `helperText` to it through `aria-describedby`. Clear `clientErrors` in `update()` once the person edits the field.

The neutral outcome gets a next step that is true for everyone. The existing `role="status"` sentence stays untouched:

```jsx
<Alert severity="success" role="status">If that address can register, we have sent it a confirmation link. Check the inbox.</Alert>
<Typography variant="body2" color="text.secondary">
  Already have an account? We emailed you instead.{' '}
  <Link component={RouterLink} to={`/login?returnUrl=${encodeURIComponent('/organizations/register')}`}>Sign in</Link> to add the organization to it.
</Typography>
```

A read uses status, not the presence of a problem, as its discriminator (in `usePlatformRead.js` and the identity screens' reads):

```js
const problem = toProblem(failure);
setProblem(problem);
setStatus(isRetryable(problem) ? 'errored' : 'refused');   // errored shows "Try again"; skeleton only while 'loading'
```

For a missing link token, copy the existing pattern in `PasswordPages.jsx:111-115,142`: a sentence under the title and `disabled={isBusy || !token}`. Never write `submit(token ?? '')` behind an enabled button.

## 10. Error boundary and not-found

```jsx
// src/components/AppErrorBoundary.jsx
export class AppErrorBoundary extends Component {
  state = { failed: false };
  static getDerivedStateFromError() { return { failed: true }; }
  render() {
    if (!this.state.failed) return this.props.children;
    return (
      <Stack spacing={2} sx={{ maxWidth: 560, mx: 'auto', my: 4 }}>
        <Alert severity="error"><Typography variant="body2">Something went wrong on this page. Reload to continue.</Typography></Alert>
        <Button variant="contained" onClick={() => window.location.reload()} sx={{ alignSelf: 'flex-start' }}>Reload</Button>
      </Stack>
    );
  }
}
```

In `App.jsx`, the outer boundary sits inside the theme and the inner one resets on navigation:

```jsx
const location = useLocation();
<MaterialThemeProvider theme={appTheme}>
  <CssBaseline enableColorScheme />
  <AppErrorBoundary>
    <IdentityProvider>
      <Layout>
        <AppErrorBoundary key={location.pathname}>
          <Routes>{/* AppRoutes as today */}</Routes>
        </AppErrorBoundary>
      </Layout>
    </IdentityProvider>
  </AppErrorBoundary>
</MaterialThemeProvider>
```

In `main.jsx`, report and nothing more:

```js
const report = (error, info) => console.error('ui_failure', error?.name, info?.componentStack);
const root = createRoot(document.getElementById('root'), { onCaughtError: report, onUncaughtError: report });
window.addEventListener('unhandledrejection', (event) => console.error('unhandled_rejection', event.reason?.name));
```

`AppRoutes.jsx` gets one more entry, last: `{ path: '*', element: <NotFoundPage /> }`. It is a page header ("That page does not exist") with a link to `/identity` or `/login`.

## 11. SPA tests for each path

These copy the existing patterns: `PasswordPages.test.jsx`, `App.test.jsx`, and `test/identityServer.js`.

```jsx
it('marks a CUIT whose check digit is wrong, focuses it and sends nothing', async () => {
  const sent = [];
  server.use(antiforgery(), contextIs(null), http.post('/api/identity/organizations/register', async ({ request }) => {
    sent.push(await request.json()); return new HttpResponse(null, { status: 202 });
  }));
  renderPage(<RegisterOrganizationPage />);
  await userEvent.type(screen.getByLabelText('Legal name'), 'Northwind');
  await userEvent.type(screen.getByLabelText('CUIT'), '30-12345678-9');
  await userEvent.type(screen.getByLabelText('Email'), 'owner@example.test');
  await userEvent.type(screen.getByLabelText('Password'), 'Testing1234!');
  await userEvent.click(screen.getByRole('button', { name: 'Register' }));

  const cuit = screen.getByLabelText('CUIT');
  expect(cuit).toHaveAttribute('aria-invalid', 'true');
  expect(cuit).toHaveAccessibleDescription(/check digit does not match/i);
  expect(cuit).toHaveFocus();
  expect(sent).toHaveLength(0);
});

it("puts the server's field errors on their fields", async () => {
  server.use(antiforgery(), contextIs(null), http.post('/api/identity/organizations/register', () =>
    problem(400, 'validation_failed', { errors: { password: ['Passwords must be at least 12 characters.'] } })));
  renderPage(<RegisterOrganizationPage />);
  // …type valid values, CUIT 30-12345678-1, password "short"…
  await userEvent.click(screen.getByRole('button', { name: 'Register' }));
  const password = screen.getByLabelText('Password');
  await waitFor(() => expect(password).toHaveAttribute('aria-invalid', 'true'));
  expect(password).toHaveAccessibleDescription(/at least 12 characters/i);
  expect(screen.getByRole('alert')).toHaveTextContent(/not accepted/i);
});

it('sends the person to sign in when a mutation finds the session gone', async () => {
  server.use(antiforgery(), contextIs(signedInContext()),
    http.post('/api/identity/credentials/reauthenticate', () => problem(401, 'invalid_session')));
  renderApp('/identity/password');
  await userEvent.type(await screen.findByLabelText('Current password'), 'Testing1234!');
  await userEvent.type(screen.getByLabelText('New password'), 'Replaced5678!');
  await userEvent.click(screen.getByRole('button', { name: 'Change it' }));

  expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument();
  expect(screen.getByTestId('return-url')).toHaveTextContent('/identity/password');
  expect(screen.getByTestId('context-problem')).toHaveTextContent('invalid_session');
});

it('counts a rate limit down and gives the button back', async () => {
  vi.useFakeTimers({ shouldAdvanceTime: true });
  const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
  server.use(antiforgery(), contextIs(null), http.post('/api/identity/sessions', () =>
    problem(429, 'rate_limit_exceeded', {}, { 'Retry-After': '2' })));
  renderPage(<LoginPage />);
  await user.type(screen.getByLabelText('Email'), 'a@example.test');
  await user.type(screen.getByLabelText('Password'), 'Testing1234!');
  await user.click(screen.getByRole('button', { name: 'Sign in' }));
  expect(await screen.findByRole('button', { name: 'Sign in' })).toBeDisabled();
  await act(() => vi.advanceTimersByTimeAsync(2000));
  expect(screen.getByRole('button', { name: 'Sign in' })).toBeEnabled();
  vi.useRealTimers();
});

it('says the connection failed and gives the button back', async () => {
  server.use(antiforgery(), contextIs(null), http.post('/api/identity/credentials/password/recovery', () => HttpResponse.error()));
  renderPage(<ForgotPasswordPage />);
  await userEvent.type(screen.getByLabelText('Email'), 'nobody@example.test');
  await userEvent.click(screen.getByRole('button', { name: 'Send the link' }));
  expect(await screen.findByRole('alert')).toHaveTextContent(/could not reach the service/i);
  expect(screen.getByRole('button', { name: 'Send the link' })).toBeEnabled();
});

it('replaces a page that throws with a way back', () => {
  vi.spyOn(console, 'error').mockImplementation(() => {});
  const Thrower = () => { throw new Error('render'); };
  render(<AppErrorBoundary><Thrower /></AppErrorBoundary>);
  expect(screen.getByRole('alert')).toHaveTextContent(/something went wrong/i);
  expect(screen.getByRole('button', { name: 'Reload' })).toBeInTheDocument();
});
```

## 12. Checklist before calling an error path done

1. Is the category right, and does the code say what the person can do about it?
2. If the route is neutral, does the check pass the three-part test? Is there a parity test?
3. Are field keys camelCase wire names? Does each message describe the rule without the value?
4. Is the code declared on the route, present in `problemCodes.json`, and given words in `problemMessages.js`?
5. Is the refusal shown on the field or next to the action, focused and announced? Is what the person typed kept?
6. Does a forced fault write exactly one safe `Error` record, and an expected refusal none?
7. Does a test assert the exact code, keys and wording, rather than just an alert?
