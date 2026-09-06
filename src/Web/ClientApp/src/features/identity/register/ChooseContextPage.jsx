import { Link } from 'react-router-dom';

/**
 * The choice a visitor makes before anything else: whether they are setting up their own account or a company's.
 * The two are different products for the same person, and nothing later can undo choosing wrongly on their behalf,
 * so the product asks rather than guesses (SPEC section 2.1).
 */
export function ChooseContextPage() {
  return (
    <section aria-labelledby="choose-context-heading">
      <h1 id="choose-context-heading">What are you registering?</h1>
      <ul>
        <li>
          <Link to="/personal/register">A personal account</Link>
          <p>For yourself. You will be asked for your name and your DNI.</p>
        </li>
        <li>
          <Link to="/organizations/register">An organization</Link>
          <p>For a company. You will be asked for its legal name and CUIT.</p>
        </li>
      </ul>
      <p>Already have an account? <Link to="/login">Sign in</Link>.</p>
    </section>
  );
}
