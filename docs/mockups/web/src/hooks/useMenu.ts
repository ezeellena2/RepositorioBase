import { useCallback, useEffect, useRef, type KeyboardEvent, type RefObject } from "react";

interface Options {
  open: boolean;
  setOpen: (open: boolean) => void;
  /** Al abrir, mover el foco al primer ítem (o al marcado). */
  focusOnOpen?: boolean;
}

const ITEM_SELECTOR = '[role="menuitem"], [role="menuitemradio"], [role="menuitemcheckbox"]';

/**
 * Contrato compartido por hamburguesa, selector de contexto y menú de perfil:
 * aria-expanded en el disparador, cierra con Escape y clic afuera, el foco vuelve
 * al disparador al cerrar, y adentro el foco es itinerante (flechas, Home, End).
 */
export function useMenu({ open, setOpen, focusOnOpen = true }: Options) {
  const triggerRef = useRef<HTMLButtonElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);
  const restoreFocus = useRef(false);

  const items = useCallback(() => Array.from(panelRef.current?.querySelectorAll<HTMLElement>(ITEM_SELECTOR) ?? []), []);

  const close = useCallback(
    (returnFocus = true) => {
      restoreFocus.current = returnFocus;
      setOpen(false);
    },
    [setOpen],
  );

  const toggle = useCallback(() => {
    if (open) close(true);
    else setOpen(true);
  }, [open, close, setOpen]);

  // Foco al abrir, foco de vuelta al cerrar.
  useEffect(() => {
    if (open) {
      if (!focusOnOpen) return;
      const list = items();
      const checked = list.find((el) => el.getAttribute("aria-checked") === "true");
      requestAnimationFrame(() => (checked ?? list[0])?.focus());
    } else if (restoreFocus.current || document.activeElement === document.body) {
      // Cerrado por Escape, o el ítem enfocado se desmontó (por ejemplo tras cambiar de contexto).
      restoreFocus.current = false;
      triggerRef.current?.focus();
    }
  }, [open, focusOnOpen, items]);

  // Escape y clic afuera.
  useEffect(() => {
    if (!open) return;
    function onKey(e: globalThis.KeyboardEvent) {
      if (e.key === "Escape") {
        e.stopPropagation();
        close(true);
      }
    }
    function onPointer(e: MouseEvent) {
      const target = e.target as Node;
      if (panelRef.current?.contains(target) || triggerRef.current?.contains(target)) return;
      close(false);
    }
    document.addEventListener("keydown", onKey);
    document.addEventListener("mousedown", onPointer);
    return () => {
      document.removeEventListener("keydown", onKey);
      document.removeEventListener("mousedown", onPointer);
    };
  }, [open, close]);

  // Foco itinerante dentro del panel.
  const onPanelKeyDown = useCallback(
    (e: KeyboardEvent<HTMLDivElement>) => {
      const list = items();
      if (list.length === 0) return;
      const index = list.indexOf(document.activeElement as HTMLElement);
      let next: number | null = null;
      if (e.key === "ArrowDown") next = (index + 1) % list.length;
      else if (e.key === "ArrowUp") next = (index - 1 + list.length) % list.length;
      else if (e.key === "Home") next = 0;
      else if (e.key === "End") next = list.length - 1;
      else if (e.key === "Tab") {
        close(false);
        return;
      }
      if (next !== null) {
        e.preventDefault();
        list[next].focus();
      }
    },
    [items, close],
  );

  return { triggerRef, panelRef, toggle, close, onPanelKeyDown } as {
    triggerRef: RefObject<HTMLButtonElement | null>;
    panelRef: RefObject<HTMLDivElement | null>;
    toggle: () => void;
    close: (returnFocus?: boolean) => void;
    onPanelKeyDown: (e: KeyboardEvent<HTMLDivElement>) => void;
  };
}
