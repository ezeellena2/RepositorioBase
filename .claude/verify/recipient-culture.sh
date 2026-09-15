#!/usr/bin/env bash
# Stop hook: server-delivered text must be localized in the RECIPIENT's language,
# passed explicitly - never the ambient culture of a worker.
# Sources: openspec/specs/localization/spec.md and
# .agents/skills/localization-standards/references/localization-rules.md.
# exit 0 = stay quiet.  exit 2 = block, stderr goes back to the agent.
set -uo pipefail

input=$(cat)

# Guard 1 - recursion.
if [[ "$(jq -r '.stop_hook_active // false' <<<"$input" 2>/dev/null)" == "true" ]]; then
  exit 0
fi

cd "${CLAUDE_PROJECT_DIR:-$(git rev-parse --show-toplevel 2>/dev/null)}" 2>/dev/null || exit 0

base=$(git merge-base HEAD origin/main 2>/dev/null || echo HEAD)
# `git diff <commit>` already compares the WORKING TREE to that commit, so it
# covers uncommitted changes too. Diffing a second time would double-report.
diff=$(git diff "$base" -- 'src/*.cs' 2>/dev/null)
[[ -z "$diff" ]] && exit 0

# Carry the file each hunk belongs to, so a finding names a real path.
added=$(awk '/^\+\+\+ b\// { file = substr($2, 3); next }
             /^\+/ && !/^\+\+\+/ { print file ": " substr($0, 2) }' <<<"$diff")

# ---------------------------------------------------------------------------
# LAYER 1 - deterministic, free, no model. Ambient culture is never correct in
# a worker: the thread's culture is the server's, not the recipient's.
# ---------------------------------------------------------------------------
ambient=$(grep -E 'CultureInfo\.(CurrentUICulture|CurrentCulture)|DateTime\.Now' <<<"$added" | sort -u || true)
if [[ -n "$ambient" ]]; then
  {
    echo "Ambient culture in server-delivered text (canonical localization specification):"
    echo "$ambient"
    echo
    echo "A worker's ambient culture is the server's, not the recipient's. Resolve the"
    echo "recipient's culture (account PreferredLanguage, else the captured intent) and"
    echo "pass it explicitly, as IdentityEmailLocalizer does at Emails.cs:35-38."
  } >&2
  exit 2
fi

# ---------------------------------------------------------------------------
# LAYER 2 - judgment. Only for diffs that actually localize something.
# ---------------------------------------------------------------------------
if ! grep -qE 'GetString|IdentityEmailLocalizer|CultureInfo|PreferredLanguage|Outbox|Email' <<<"$added"; then
  exit 0
fi

fingerprint=$(sha256sum <<<"$diff" | cut -c1-16)
stamp="$(git rev-parse --git-dir)/recipient-culture.last"
[[ -f "$stamp" && "$(cat "$stamp")" == "$fingerprint" ]] && exit 0

prompt=$(cat <<'PROMPT'
You verify one contract on a diff. Nothing else.

THE RULE, from openspec/specs/localization/spec.md and the repository localization standard:
  Point 4: text delivered by the server - any document or notification - is localized
  in the backend, in the RECIPIENT's language, passed explicitly. Never the ambient
  culture of a worker.
  Point 6: machines read invariant English. Logs, audit records, outbox payloads,
  error codes, identifiers, URLs, route paths, developer-facing exception messages
  and OpenAPI stay invariant. Localization is presentation only.

The reference implementation is IdentityEmailLocalizer (src/Infrastructure/Localization/Emails.cs):
it takes the language as a parameter and calls Resources.GetString(key, culture).
The recipient's culture resolves from the account's PreferredLanguage, else the
language captured with the intent that created the request.

For the diff below, check:

  1. WHOSE culture is passed? It must be the RECIPIENT's. An actor, operator, or
     current-user language passed to text delivered to someone else is a defect
     even though it compiles and fixtures pass - both are usually 'en' in tests.
     Name the variable you believe is wrong and the one it should be.
  2. Is a localized lookup missing its culture argument entirely (GetString(key)
     rather than GetString(key, culture))? That silently falls back to the
     server's culture.
  3. Is anything invariant being localized? A log line, an audit record, an
     outbox payload, an error code, or a developer-facing exception message
     must stay English. This is the opposite failure and is equally a defect.

Read the surrounding method before reporting: a variable named for the actor may
legitimately BE the recipient on a self-service route, where the person acting and
the person receiving are the same. Say so instead of reporting it.

Report only what this diff changed. Do not comment on style, naming, or tests.
Do not invent findings. If everything is correct, reply with exactly: OK

Otherwise one line per finding:
  file:line  <what is passed> -> <what should be>  <why>
PROMPT
)

report=$(timeout 180 claude -p "${prompt}

--- DIFF ---
${diff}" \
  --allowed-tools "Read,Grep" \
  --permission-mode acceptEdits \
  --output-format text 2>&1)
rc=$?

if [[ $rc -ne 0 ]]; then
  echo "recipient-culture: verifier failed (rc=$rc), skipping." >&2
  exit 0
fi

echo "$fingerprint" > "$stamp"
[[ "$report" == OK* ]] && exit 0

{
  echo "Recipient-culture check failed (canonical localization specification):"
  echo "$report"
} >&2
exit 2
