import { useState, type FormEvent } from "react";
import { Button } from "../design-system/primitives/Button";
import { ErrorState } from "../design-system/primitives/ErrorState";
import { useSession } from "../session/SessionContext";

export function LoginPage() {
  const { login } = useSession();
  const [accountName, setAccountName] = useState("");
  const [password, setPassword] = useState("");
  const [failed, setFailed] = useState(false);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setFailed(false);
    const result = await login(accountName, password);
    if (!result.ok) {
      setFailed(true);
    }
  }

  return (
    <section>
      <h1>Log in</h1>
      <form onSubmit={handleSubmit}>
        <label htmlFor="account-name">ACE account name</label>
        <input
          id="account-name"
          name="accountName"
          autoComplete="username"
          value={accountName}
          onChange={(event) => setAccountName(event.target.value)}
        />

        <label htmlFor="password">Password</label>
        <input
          id="password"
          name="password"
          type="password"
          autoComplete="current-password"
          value={password}
          onChange={(event) => setPassword(event.target.value)}
        />

        <Button type="submit">Log in</Button>
      </form>
      {failed ? <ErrorState title="Could not log in" description="Check your account name and password and try again." /> : null}
    </section>
  );
}
