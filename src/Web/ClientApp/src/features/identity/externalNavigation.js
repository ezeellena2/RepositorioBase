/**
 * Handing the browser to the provider.
 *
 * It is a full navigation rather than a router transition on purpose: what follows is the provider's own site,
 * and the round trip back is a top-level request the server's middleware answers before this application runs
 * again. It lives behind an object so a test can watch where a page would have sent someone — jsdom will not
 * let `window.location.assign` be replaced.
 */
export const externalNavigation = {
  leaveFor: (uri) => { window.location.assign(uri); },
};
