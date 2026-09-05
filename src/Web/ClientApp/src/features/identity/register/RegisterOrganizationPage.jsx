import { useState } from 'react';
import { useIdentity } from '../context/IdentityProvider';
import { ProblemMessage } from '../ProblemMessage';
import { useSubmit } from '../useSubmit';

/**
 * Registration answers with a neutral bodyless 202 whether or not the address is already taken, so the page says
 * the same thing in either case. Telling the visitor which one happened would answer a question the API
 * deliberately refuses to answer (SPEC section 6).
 */
export function RegisterOrganizationPage() {
  const identity = useIdentity();
  const [form, setForm] = useState({ email: '', password: '', legalName: '', cuit: '' });
  const { submit, problem, isBusy, result } = useSubmit((request) => identity.client.registerOrganization(request));
  const update = (field) => (event) => setForm((current) => ({ ...current, [field]: event.target.value }));

  if (result) {
    return (
      <section aria-labelledby="register-heading">
        <h1 id="register-heading">Register an organization</h1>
        <p role="status">If that address can register, we have sent it a confirmation link. Check the inbox.</p>
      </section>
    );
  }

  return (
    <section aria-labelledby="register-heading">
      <h1 id="register-heading">Register an organization</h1>
      <ProblemMessage problem={problem} />
      <form onSubmit={(event) => { event.preventDefault(); submit(form); }}>
        <label htmlFor="register-legal-name">Legal name</label>
        <input id="register-legal-name" value={form.legalName} onChange={update('legalName')} required />
        <label htmlFor="register-cuit">CUIT</label>
        <input id="register-cuit" value={form.cuit} onChange={update('cuit')} required />
        <label htmlFor="register-email">Email</label>
        <input id="register-email" type="email" autoComplete="username" value={form.email} onChange={update('email')} required />
        <label htmlFor="register-password">Password</label>
        <input id="register-password" type="password" autoComplete="new-password" value={form.password} onChange={update('password')} required />
        <button type="submit" disabled={isBusy}>Register</button>
      </form>
    </section>
  );
}
