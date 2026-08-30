/**
 * AUTH-001: "Login uses the private ACE account name. Public identity uses a Display Character."
 * This component's props deliberately admit no account-name field at all -- there is no path by
 * which a caller could thread one through, by construction rather than by convention.
 */
export interface PublicDisplayIdentityProps {
  readonly displayCharacterName: string;
}

export function PublicDisplayIdentity({ displayCharacterName }: PublicDisplayIdentityProps) {
  return <span className="public-display-identity">{displayCharacterName}</span>;
}
