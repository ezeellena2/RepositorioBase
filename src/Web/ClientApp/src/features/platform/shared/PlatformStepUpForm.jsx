/**
 * The one form every Platform screen asks the second factor with. It is fully controlled and owns no state: the
 * code lives with whoever renders it, so it survives a screen going from gated to loaded instead of being dropped
 * when this component unmounts.
 *
 * `inputId` is a parameter because two of these can be on one page and an id may not repeat; the label points at
 * whichever one it belongs to.
 */
export function PlatformStepUpForm({ inputId, code, onCodeChange, onSubmit, isBusy }) {
  return (
    <form
      aria-label="Step up"
      onSubmit={(event) => { event.preventDefault(); onSubmit(); }}
    >
      <label htmlFor={inputId}>Authenticator code</label>
      <input id={inputId} type="text" inputMode="numeric" value={code} onChange={(event) => onCodeChange(event.target.value)} required />
      <button type="submit" disabled={isBusy}>Step up</button>
    </form>
  );
}
