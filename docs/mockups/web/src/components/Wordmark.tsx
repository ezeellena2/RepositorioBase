import { Link } from "react-router-dom";
import { LogoGlyph } from "./Icons";

export function Wordmark({ size = "md", to = "/" }: { size?: "md" | "lg"; to?: string }) {
  return (
    <Link to={to} className={`wordmark${size === "lg" ? " wordmark--lg" : ""}`} aria-label="Base, inicio">
      <span className="wordmark__mark">
        <LogoGlyph size={size === "lg" ? 22 : 18} />
      </span>
      <span>Base</span>
    </Link>
  );
}
