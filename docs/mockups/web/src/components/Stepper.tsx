/** Dónde está la persona dentro de un recorrido de varios pasos. */
export function Stepper({ steps, current }: { steps: string[]; current: number }) {
  return (
    <ol className="stepper" aria-label={`Paso ${current + 1} de ${steps.length}`}>
      {steps.map((step, index) => (
        <li
          key={step}
          className={`stepper__step${index < current ? " is-done" : index === current ? " is-current" : ""}`}
          aria-current={index === current ? "step" : undefined}
        >
          <span className="stepper__bar" aria-hidden="true" />
          <span className="stepper__label">{step}</span>
        </li>
      ))}
    </ol>
  );
}
