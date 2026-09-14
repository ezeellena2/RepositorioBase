#!/usr/bin/env bash
# Stop hook: checks that ApplicationError categories in the diff mean what the
# failure actually is (IA-REQ-057).
# exit 0 = stay quiet and let the turn end.  exit 2 = block, stderr goes back to the agent.
set -uo pipefail

input=$(cat)

# Guard 1 - recursion. Without this the block re-triggers the hook forever.
if [[ "$(jq -r '.stop_hook_active // false' <<<"$input" 2>/dev/null)" == "true" ]]; then
  exit 0
fi

cd "${CLAUDE_PROJECT_DIR:-$(git rev-parse --show-toplevel 2>/dev/null)}" 2>/dev/null || exit 0

# Guard 2 - is there anything to look at?
base=$(git merge-base HEAD origin/main 2>/dev/null || echo HEAD)
# `git diff <commit>` already compares the WORKING TREE to that commit, so it
# covers uncommitted changes too. Diffing a second time would double-report.
diff=$(git diff "$base" -- 'src/Application/*.cs' 2>/dev/null)
[[ -z "$diff" ]] && exit 0

# Guard 3 - does the diff touch error construction at all? If not, spend nothing.
if ! grep -qE '^\+.*(ApplicationErrorCategory\.|ApplicationError\(|Result(<[^>]*>)?\.Failure)' <<<"$diff"; then
  exit 0
fi

# Guard 4 - never judge the same diff twice.
fingerprint=$(sha256sum <<<"$diff" | cut -c1-16)
stamp="$(git rev-parse --git-dir)/error-semantics.last"
[[ -f "$stamp" && "$(cat "$stamp")" == "$fingerprint" ]] && exit 0

prompt=$(cat <<'PROMPT'
You verify one contract on a diff. Nothing else.

THE RULE (IA-REQ-057), quoted from src/Application/Common/Models/ApplicationErrorCategory.cs:
  Unavailable = "The request could not be decided because something it depends on is
  not answering." It is distinct from RateLimited on purpose: a caller who has spent
  nothing must not be told they tried too often, and an outage must stay visible.

For every ApplicationError added or changed in the diff below, decide:

  1. Does the category match the real cause?
       RateLimited  - ONLY when the caller consumed an attempt budget.
       Unavailable  - a dependency (lock, store, service) did not answer.
       Conflict     - the resource's own state forbids the operation.
       NotFound / Authentication / Authorization / Validation - their plain meaning.
  2. Does the factory method's NAME agree with the category it returns?
  3. Is retryAfterSeconds set on a category other than RateLimited or Unavailable?
     (ApplicationError.cs:33 throws on this at runtime - catch it earlier.)

BEFORE REPORTING ANYTHING, READ THE DOC COMMENT ON THE MEMBER YOU ARE ABOUT TO FLAG,
and the one on any factory it calls. A deliberate choice explained in a comment is
CORRECT and must NOT be reported. A real example you must not flag:
IdentityAccessErrors.SessionLockUnavailable() returns RateLimited, and its comment
explains why - on an authentication route a 503 reachable only with a valid credential
would confirm the password was right, so 429 is chosen deliberately. That reasoning is
scoped to credential-bearing routes; the SAME helper called where no credential is in
play (an already-authenticated write colliding on a lock) is NOT covered by it.

Report only what this diff changed. Do not comment on style, naming, tests, or anything
else. Do not invent findings. If everything is correct, reply with exactly: OK

Otherwise one line per finding:
  file:line  CURRENT -> EXPECTED  <cause, and why the documented justification does not cover it>
PROMPT
)

report=$(timeout 180 claude -p "${prompt}

--- DIFF ---
${diff}" \
  --allowed-tools "Read,Grep" \
  --permission-mode acceptEdits \
  --output-format text 2>&1)
rc=$?

# A broken verifier must not wedge every turn. Fail open, loudly.
if [[ $rc -ne 0 ]]; then
  echo "error-semantics: verifier failed (rc=$rc), skipping." >&2
  exit 0
fi

echo "$fingerprint" > "$stamp"
[[ "$report" == OK* ]] && exit 0

{
  echo "Error-semantics check failed (IA-REQ-057):"
  echo "$report"
  echo
  echo "Fix the category, or if the choice is deliberate, say why in a doc comment"
  echo "on the factory method - the check reads comments and accepts a justified choice."
} >&2
exit 2
