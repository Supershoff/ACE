/**
 * Synthetic, disposable test-account credentials for the local acceptance stack (issue #34). These
 * are never real ACE account credentials -- the runbook (`Tools/LocalAcceptance/README.md`) has the
 * operator create throwaway accounts on their disposable test world specifically for this purpose.
 * Missing prerequisites must fail loudly and specifically, never skip silently.
 */
export interface AcceptanceTestAccount {
  readonly accountName: string;
  readonly password: string;
}

function requireEnv(name: string): string {
  const value = process.env[name];
  if (!value) {
    throw new Error(
      `Missing required environment variable "${name}". Copy Tools/LocalAcceptance/acceptance.settings.example.json to ` +
        `acceptance.settings.json, fill in synthetic test-account credentials for your disposable ACE test world, and ` +
        `re-run Start-LocalAcceptance.ps1 (it exports these before invoking \`npm run test:e2e\`).`,
    );
  }
  return value;
}

export function mainAccount(): AcceptanceTestAccount {
  return {
    accountName: requireEnv("ACE_ACCEPTANCE_MAIN_ACCOUNT_NAME"),
    password: requireEnv("ACE_ACCEPTANCE_MAIN_ACCOUNT_PASSWORD"),
  };
}

export function linkedAccount(): AcceptanceTestAccount {
  return {
    accountName: requireEnv("ACE_ACCEPTANCE_LINKED_ACCOUNT_NAME"),
    password: requireEnv("ACE_ACCEPTANCE_LINKED_ACCOUNT_PASSWORD"),
  };
}

/**
 * A second, entirely independent Main Account (issue #39): Transfer Offers and Sharing Grants both
 * require a genuinely separate ownership group, unlike `linkedAccount()`'s account, which is deliberately
 * linked under the same Main Account and could never legally be an offer recipient or grantee.
 */
export function secondaryAccount(): AcceptanceTestAccount {
  return {
    accountName: requireEnv("ACE_ACCEPTANCE_SECONDARY_ACCOUNT_NAME"),
    password: requireEnv("ACE_ACCEPTANCE_SECONDARY_ACCOUNT_PASSWORD"),
  };
}

/** The current character name to type when addressing `secondaryAccount()` as a Transfer Offer recipient or Sharing Grant grantee. */
export function secondaryAccountCharacterName(): string {
  return requireEnv("ACE_ACCEPTANCE_SECONDARY_ACCOUNT_CHARACTER_NAME");
}
