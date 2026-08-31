import { useState, type MouseEvent, type ReactNode } from "react";
import { Link } from "react-router-dom";
import { touchTargetStyle } from "../design-system/touchTarget";
import { useIsNarrowViewport } from "./useIsNarrowViewport";

export interface NavItemDefinition {
  readonly to: string;
  readonly label: string;
}

export interface AppShellProps {
  readonly navItems: readonly NavItemDefinition[];
  readonly children: ReactNode;
  readonly banner?: ReactNode;
  /** Rendered in the header alongside primary navigation, e.g. the Notification Center. */
  readonly headerActions?: ReactNode;
}

const MAIN_CONTENT_ID = "main-content";
const NAV_ID = "app-shell-primary-nav";

export function AppShell({ navItems, children, banner, headerActions }: AppShellProps) {
  const isNarrow = useIsNarrowViewport();
  const [mobileNavOpen, setMobileNavOpen] = useState(false);

  function handleSkipLinkClick(event: MouseEvent<HTMLAnchorElement>) {
    event.preventDefault();
    document.getElementById(MAIN_CONTENT_ID)?.focus();
  }

  // The nav stays mounted at every viewport width so it is never removed from the accessibility
  // tree; on narrow viewports a CSS class (not `hidden`/`display:none`) visually collapses it
  // behind the toggle, and its links drop out of tab order via tabIndex while collapsed so
  // keyboard users cannot tab into content that is not visually present.
  const collapsedOnNarrow = isNarrow && !mobileNavOpen;
  const navClassName = ["app-shell__nav", collapsedOnNarrow && "app-shell__nav--collapsed"].filter(Boolean).join(" ");

  return (
    <div className="app-shell">
      <a href={`#${MAIN_CONTENT_ID}`} className="skip-link" onClick={handleSkipLinkClick}>
        Skip to main content
      </a>
      {banner}
      <header className="app-shell__header">
        <span className="app-shell__title">AC Cloud Mule</span>
        {isNarrow ? (
          <button
            type="button"
            aria-expanded={mobileNavOpen}
            aria-controls={NAV_ID}
            className="app-shell__nav-toggle"
            style={touchTargetStyle}
            onClick={() => setMobileNavOpen((wasOpen) => !wasOpen)}
          >
            Menu
          </button>
        ) : null}
        <nav id={NAV_ID} aria-label="Primary" className={navClassName}>
          <ul className="app-shell__nav-list">
            {navItems.map((item) => (
              <li key={item.to}>
                <Link
                  to={item.to}
                  className="app-shell__nav-link"
                  style={touchTargetStyle}
                  tabIndex={collapsedOnNarrow ? -1 : undefined}
                >
                  {item.label}
                </Link>
              </li>
            ))}
          </ul>
        </nav>
        {headerActions}
      </header>
      <main id={MAIN_CONTENT_ID} tabIndex={-1} className="app-shell__main">
        {children}
      </main>
    </div>
  );
}
