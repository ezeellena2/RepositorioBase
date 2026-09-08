import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { useIdentity } from '../context/IdentityProvider';
import { ProblemMessage } from '../ProblemMessage';
import { useSubmit } from '../useSubmit';

/**
 * A newcomer setting up their own account. Like the organization signup it answers with a neutral bodyless 202
 * whether or not the address is already taken, so this page says the same thing in either case (SPEC section 6).
 */
export function PersonalRegisterPage() {
  const identity = useIdentity();
  const [form, setForm] = useState({ email: '', password: '', fullName: '', displayName: '', documentNumber: '' });
  const { submit, problem, isBusy, result } = useSubmit((request) => identity.client.registerPersonal(request));
  const update = (field) => (event) => setForm((current) => ({ ...current, [field]: event.target.value }));

  if (result) {
    return (
      <section aria-labelledby="personal-register-heading">
        <h1 id="personal-register-heading">Set up your personal account</h1>
        <p role="status">If that address can register, we have sent it a confirmation link. Check the inbox.</p>
      </section>
    );
  }

  return (
    <section aria-labelledby="personal-register-heading">
      <h1 id="personal-register-heading">Set up your personal account</h1>
      <ProblemMessage problem={problem} />
      <form onSubmit={(event) => { event.preventDefault(); submit(form); }}>
        <label htmlFor="personal-full-name">Full name</label>
        <input id="personal-full-name" value={form.fullName} onChange={update('fullName')} required />
        <label htmlFor="personal-display-name">Display name</label>
        <input id="personal-display-name" value={form.displayName} onChange={update('displayName')} required />
        <label htmlFor="personal-document">DNI</label>
        <input id="personal-document" inputMode="numeric" autoComplete="off" value={form.documentNumber} onChange={update('documentNumber')} required />
        <label htmlFor="personal-email">Email</label>
        <input id="personal-email" type="email" autoComplete="username" value={form.email} onChange={update('email')} required />
        <label htmlFor="personal-password">Password</label>
        <input id="personal-password" type="password" autoComplete="new-password" value={form.password} onChange={update('password')} required />
        <button type="submit" disabled={isBusy}>Register</button>
      </form>
      <p>Registering a company instead? <Link to="/organizations/register">Register an organization</Link>.</p>
    </section>
  );
}

/**
 * Saying the recorded document is wrong. The screen offers this unconditionally when a document is recorded,
 * because whether a correction is *possible* must not depend on what the person can see about anybody else —
 * only on whether they already have a dispute open (IA-REQ-058).
 *
 * The claimed number is typed here and sent once. Nothing keeps it: no state survives the submit, and the answer
 * carries only an opaque identifier back.
 */
function DocumentDispute({ client, available, country, type }) {
  const [claimedNumber, setClaimedNumber] = useState('');
  const [reasonCode, setReasonCode] = useState('TypedWrongAtSignup');
  const { submit, problem, isBusy, result } = useSubmit((request) => client.openDocumentDispute(request));

  if (!available) {
    return <p>A correction is already being reviewed for this document.</p>;
  }

  if (result) {
    return <p>The correction was sent for review. Nothing about your account changes while it is open.</p>;
  }

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        submit({ claimedCountry: country, claimedType: type, claimedNumber, reasonCode });
        setClaimedNumber('');
      }}
    >
      <h3>Correct this document</h3>
      <p>An operator reviews the correction. Your account keeps working while it is open.</p>
      <ProblemMessage problem={problem} />
      <label htmlFor="dispute-number">What the number should be</label>
      <input
        id="dispute-number"
        value={claimedNumber}
        onChange={(event) => setClaimedNumber(event.target.value)}
        required
      />
      <label htmlFor="dispute-reason">Why</label>
      <select id="dispute-reason" value={reasonCode} onChange={(event) => setReasonCode(event.target.value)}>
        <option value="TypedWrongAtSignup">I typed it wrong when I signed up</option>
        <option value="DocumentReissued">My document was reissued</option>
        <option value="RecordedByMistake">It is not my document</option>
      </select>
      <button type="submit" disabled={isBusy}>Send for review</button>
    </form>
  );
}

/**
 * Adding a personal context to an identity that already exists.
 *
 * It is deliberately not the signup at `/personal/register`. That page is for a stranger: it asks for an address
 * and a password this person already has, and the route behind it answers the same neutral acknowledgement
 * whether or not the address is taken — so somebody who is already signed in would be told nothing and given
 * nothing. What they need is the claim itself, which is the identity they are holding plus a document.
 */
function AddPersonalContext({ client, onAdded }) {
  const [form, setForm] = useState({ fullName: '', displayName: '', documentNumber: '' });
  const { submit, problem, isBusy, result } = useSubmit((request) => client.createPersonalContext(request));
  const update = (field) => (event) => setForm((current) => ({ ...current, [field]: event.target.value }));

  // Reloading rather than rendering what was sent: what a claim becomes is the server's answer, and the masked
  // number this page then shows is the only form of it that ever comes back.
  useEffect(() => {
    if (result !== null && result !== undefined) onAdded();
  }, [result, onAdded]);

  return (
    <form onSubmit={(event) => { event.preventDefault(); submit(form); }}>
      <ProblemMessage problem={problem} />
      <label htmlFor="add-personal-full-name">Full name</label>
      <input id="add-personal-full-name" value={form.fullName} onChange={update('fullName')} required />
      <label htmlFor="add-personal-display-name">Display name</label>
      <input id="add-personal-display-name" value={form.displayName} onChange={update('displayName')} required />
      <label htmlFor="add-personal-document">DNI</label>
      <input
        id="add-personal-document"
        inputMode="numeric"
        autoComplete="off"
        value={form.documentNumber}
        onChange={update('documentNumber')}
        required
      />
      <button type="submit" disabled={isBusy}>Add my personal account</button>
    </form>
  );
}

/**
 * The owner's own profile. The document is shown masked and is never editable here: correcting one is a separate
 * verified process that takes two parties, and all this screen can do is start it. `correctionAvailable` is what
 * decides whether the form is offered, because at most one dispute is open at a time (IA-REQ-058).
 */
export function PersonalProfilePage() {
  const identity = useIdentity();
  const [loaded, setLoaded] = useState(null);
  const [loadProblem, setLoadProblem] = useState(null);
  const [edits, setEdits] = useState(null);
  const { submit, problem, isBusy, result } = useSubmit((request) => identity.client.updatePersonalProfile(request));

  // The saved response is the newest truth about the row, so it wins over what was loaded rather than being
  // copied into state after the fact. Deriving it keeps one source and avoids a render that syncs itself.
  const profile = result ?? loaded;
  const form = edits ?? (profile ? { fullName: profile.fullName, displayName: profile.displayName } : { fullName: '', displayName: '' });

  const load = useCallback(async () => {
    try {
      setLoaded(await identity.client.getPersonalProfile());
      setLoadProblem(null);
    } catch (error) {
      setLoaded(null);
      setLoadProblem(error.problem ?? { code: 'unexpected' });
    }
  }, [identity]);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      if (!cancelled) await load();
    })();
    return () => { cancelled = true; };
  }, [load]);

  const update = (field) => (event) => {
    const { value } = event.target;
    setEdits((current) => ({ ...(current ?? { fullName: profile?.fullName ?? '', displayName: profile?.displayName ?? '' }), [field]: value }));
  };

  if (loadProblem?.code === 'personal_profile_not_found') {
    return (
      <section aria-labelledby="profile-heading">
        <h1 id="profile-heading">Your profile</h1>
        <p role="status">You have no personal context yet.</p>
        <AddPersonalContext client={identity.client} onAdded={load} />
      </section>
    );
  }

  if (!profile) {
    return (
      <section aria-labelledby="profile-heading">
        <h1 id="profile-heading">Your profile</h1>
        <ProblemMessage problem={loadProblem} />
      </section>
    );
  }

  return (
    <section aria-labelledby="profile-heading">
      <h1 id="profile-heading">Your profile</h1>
      <ProblemMessage problem={problem} />
      <dl>
        <dt>Email</dt>
        <dd>{profile.email}</dd>
        {profile.document && (
          <>
            <dt>{profile.document.country} {profile.document.type}</dt>
            <dd>{profile.document.maskedNumber}</dd>
          </>
        )}
      </dl>
      {profile.document && profile.document.status === 'recorded' && (
        <DocumentDispute
          client={identity.client}
          available={profile.document.correctionAvailable}
          country={profile.document.country}
          type={profile.document.type}
        />
      )}
      <form onSubmit={(event) => { event.preventDefault(); setEdits(null); submit({ ...form, version: profile.version }); }}>
        <label htmlFor="profile-full-name">Full name</label>
        <input id="profile-full-name" value={form.fullName} onChange={update('fullName')} required />
        <label htmlFor="profile-display-name">Display name</label>
        <input id="profile-display-name" value={form.displayName} onChange={update('displayName')} required />
        <button type="submit" disabled={isBusy}>Save</button>
      </form>
    </section>
  );
}
