import { useEffect, useState } from 'react';

/**
 * Reads a one-time token out of the URL fragment and erases it.
 *
 * A fragment is never sent to a server and never appears in a proxy or access log, which is why every link this
 * application mails carries its token there rather than in the query string. It is read once into memory and the
 * address bar is rewritten with replaceState, so the token does not survive in history, in a bookmark, or in
 * whatever the next page decides to log (IA-REQ-025/029).
 *
 * The three onboarding journeys — organization confirmation, member invitations and Platform invitations — all
 * answer a mailed link, so they all need exactly this and must not drift apart on it.
 */
export function useFragmentToken(name = 'token') {
  const [token] = useState(() => new URLSearchParams(window.location.hash.replace(/^#/, '')).get(name));

  useEffect(() => {
    if (window.location.hash) {
      window.history.replaceState({}, '', `${window.location.pathname}${window.location.search}`);
    }
  }, []);

  return token;
}
