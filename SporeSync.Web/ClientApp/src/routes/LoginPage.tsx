import { useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "@tanstack/react-router";
import { Loader2, Server } from "lucide-react";
import { type FormEvent, useState } from "react";
import { api } from "../api/client";
import { queryKeys } from "../api/queryKeys";
import { Button } from "../components/Button";

export function LoginPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  const onSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setError(null);
    setPending(true);

    try {
      const session = await api.login(username, password);
      queryClient.setQueryData(queryKeys.session, session);
      await navigate({ to: "/dashboard" });
    } catch {
      setError("Invalid username or password.");
      setPending(false);
    }
  };

  return (
    <div className="flex min-h-screen items-center justify-center bg-background px-4 text-foreground">
      <div className="w-full max-w-sm rounded-lg border border-border bg-panel p-6 shadow-lg">
        <div className="mb-6 flex items-center gap-2">
          <Server className="text-accent" size={24} />
          <h1 className="text-lg font-semibold tracking-wide">SporeSync</h1>
        </div>
        <p className="mb-4 text-sm text-muted-foreground">
          Sign in with the administrator account to continue.
        </p>
        <form className="space-y-4" onSubmit={onSubmit}>
          <label className="block text-sm">
            <span className="mb-1 block font-medium">Username</span>
            <input
              name="username"
              autoComplete="username"
              required
              className="h-9 w-full rounded-md border border-border bg-background px-3 text-sm outline-none focus:shadow-focus"
              value={username}
              onChange={(event) => setUsername(event.target.value)}
            />
          </label>
          <label className="block text-sm">
            <span className="mb-1 block font-medium">Password</span>
            <input
              name="password"
              type="password"
              autoComplete="current-password"
              required
              className="h-9 w-full rounded-md border border-border bg-background px-3 text-sm outline-none focus:shadow-focus"
              value={password}
              onChange={(event) => setPassword(event.target.value)}
            />
          </label>
          {error && (
            <p role="alert" className="text-sm text-red-600 dark:text-red-400">
              {error}
            </p>
          )}
          <Button
            type="submit"
            disabled={pending}
            className="w-full bg-accent text-accent-foreground hover:bg-accent/90"
          >
            {pending && <Loader2 className="animate-spin" size={16} />}
            Sign in
          </Button>
        </form>
      </div>
    </div>
  );
}
