import { useEffect, useRef, type CSSProperties, type MouseEvent, type ReactNode } from "react";

export interface DialogProps {
  readonly open: boolean;
  readonly onClose: () => void;
  readonly titleId: string;
  readonly title: string;
  readonly children: ReactNode;
  /** Optional per-instance styling for the dialog box itself (e.g. a fidelity surface's own panel treatment). Never required -- omitting it preserves this primitive's plain default look. */
  readonly style?: CSSProperties;
  /** Optional per-instance styling for the `<h2>` title, independent of `style` above. */
  readonly titleStyle?: CSSProperties;
}

const FOCUSABLE_SELECTOR =
  'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])';

function focusableElementsWithin(container: HTMLElement | null): HTMLElement[] {
  if (!container) {
    return [];
  }
  return Array.from(container.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR));
}

export function Dialog({ open, onClose, titleId, title, children, style, titleStyle }: DialogProps) {
  const dialogRef = useRef<HTMLDivElement>(null);
  const previouslyFocusedElementRef = useRef<HTMLElement | null>(null);

  useEffect(() => {
    if (open) {
      previouslyFocusedElementRef.current = document.activeElement as HTMLElement | null;
      focusableElementsWithin(dialogRef.current)[0]?.focus();
    } else {
      previouslyFocusedElementRef.current?.focus();
    }
  }, [open]);

  useEffect(() => {
    if (!open) {
      return;
    }

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") {
        onClose();
        return;
      }

      if (event.key !== "Tab") {
        return;
      }

      const focusable = focusableElementsWithin(dialogRef.current);
      if (focusable.length === 0) {
        return;
      }

      const first = focusable[0]!;
      const last = focusable[focusable.length - 1]!;

      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    }

    document.addEventListener("keydown", handleKeyDown);
    return () => document.removeEventListener("keydown", handleKeyDown);
  }, [open, onClose]);

  if (!open) {
    return null;
  }

  function handleOverlayClick(event: MouseEvent<HTMLDivElement>) {
    if (event.target === event.currentTarget) {
      onClose();
    }
  }

  return (
    <div data-testid="dialog-overlay" className="dialog-overlay" onClick={handleOverlayClick}>
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        className="dialog"
        style={style}
        onClick={(event) => event.stopPropagation()}
      >
        <h2 id={titleId} className="dialog__title" style={titleStyle}>
          {title}
        </h2>
        {children}
      </div>
    </div>
  );
}
