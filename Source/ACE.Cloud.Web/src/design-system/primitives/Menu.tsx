import { useEffect, useRef, useState, type KeyboardEvent } from "react";
import { touchTargetStyle } from "../touchTarget";

export interface MenuItemDefinition {
  readonly id: string;
  readonly label: string;
}

export interface MenuProps {
  readonly label: string;
  readonly items: readonly MenuItemDefinition[];
  readonly onSelect: (id: string) => void;
}

export function Menu({ label, items, onSelect }: MenuProps) {
  const [open, setOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const itemRefs = useRef<Array<HTMLButtonElement | null>>([]);

  useEffect(() => {
    if (open) {
      itemRefs.current[0]?.focus();
    }
  }, [open]);

  useEffect(() => {
    if (!open) {
      return;
    }

    function handleOutsideClick(event: MouseEvent) {
      if (containerRef.current && !containerRef.current.contains(event.target as Node)) {
        setOpen(false);
      }
    }

    document.addEventListener("mousedown", handleOutsideClick);
    return () => document.removeEventListener("mousedown", handleOutsideClick);
  }, [open]);

  function closeMenu(returnFocusToTrigger: boolean) {
    setOpen(false);
    if (returnFocusToTrigger) {
      triggerRef.current?.focus();
    }
  }

  function focusItemAt(index: number) {
    const count = items.length;
    const wrapped = ((index % count) + count) % count;
    itemRefs.current[wrapped]?.focus();
  }

  function handleItemKeyDown(event: KeyboardEvent<HTMLButtonElement>, index: number) {
    switch (event.key) {
      case "ArrowDown":
        event.preventDefault();
        focusItemAt(index + 1);
        break;
      case "ArrowUp":
        event.preventDefault();
        focusItemAt(index - 1);
        break;
      case "Home":
        event.preventDefault();
        focusItemAt(0);
        break;
      case "End":
        event.preventDefault();
        focusItemAt(items.length - 1);
        break;
      case "Enter":
      case " ": {
        event.preventDefault();
        const item = items[index];
        if (item) {
          onSelect(item.id);
        }
        closeMenu(true);
        break;
      }
      case "Escape":
        event.preventDefault();
        closeMenu(true);
        break;
      case "Tab":
        closeMenu(false);
        break;
      default:
        break;
    }
  }

  return (
    <div className="menu" ref={containerRef}>
      <button
        ref={triggerRef}
        type="button"
        aria-haspopup="menu"
        aria-expanded={open}
        className="menu__trigger"
        style={touchTargetStyle}
        onClick={() => setOpen((wasOpen) => !wasOpen)}
      >
        {label}
      </button>
      {open ? (
        <ul role="menu" aria-label={label} className="menu__list">
          {items.map((item, index) => (
            <li role="none" key={item.id}>
              <button
                role="menuitem"
                type="button"
                className="menu__item"
                style={touchTargetStyle}
                ref={(element) => {
                  itemRefs.current[index] = element;
                }}
                onKeyDown={(event) => handleItemKeyDown(event, index)}
                onClick={() => {
                  onSelect(item.id);
                  closeMenu(true);
                }}
              >
                {item.label}
              </button>
            </li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}
