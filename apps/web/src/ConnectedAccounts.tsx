import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, post } from "./api";
import { ErrorBox } from "./components";
import s from "./App.module.scss";
export function ConnectedAccounts({ login = false }: { login?: boolean }) {
  const client = useQueryClient();
  const providers = useQuery({
    queryKey: ["login-providers"],
    queryFn: () =>
      api<{ name: string; label: string }[]>("/auth/external/providers"),
  });
  const connections = useQuery({
    queryKey: ["connections"],
    queryFn: () =>
      api<{ provider: string; name: string }[]>("/auth/external/connections"),
    enabled: !login,
  });
  const start = useMutation({
    mutationFn: (provider: string) =>
      post<{ url: string }>("/auth/external/begin", {
        provider,
        intent: login ? "login" : "link",
      }),
    onSuccess: (data) => window.location.assign(data.url),
  });
  const remove = useMutation({
    mutationFn: (provider: string) =>
      api("/auth/external/connections/" + encodeURIComponent(provider), {
        method: "DELETE",
      }),
    onSuccess: () => client.invalidateQueries({ queryKey: ["connections"] }),
  });
  const buttons = providers.data
    ?.filter((p) => !connections.data?.some((c) => c.provider === p.name))
    .map((p) => (
      <button
        key={p.name}
        className={s.secondary}
        onClick={() => start.mutate(p.name)}
        disabled={start.isPending}
      >
        {login ? "Continue with" : "Connect"} {p.label}
      </button>
    ));
  const error = (
    <ErrorBox
      error={
        providers.error || connections.error || start.error || remove.error
      }
    />
  );
  if (login)
    return (
      <>
        {buttons}
        {error}
      </>
    );
  return (
    <section className={s.panel}>
      <h2>Connected accounts</h2>
      <p>
        Connect a provider after a recent sign-in. Accounts are linked only when
        you explicitly connect them here. Your authenticator remains required
        after social sign-in.
      </p>
      {error}
      {connections.data?.map((c) => (
        <div key={c.provider} className={s.statusRow}>
          <span>{c.name ?? c.provider}</span>
          <button
            className={s.secondary}
            onClick={() => remove.mutate(c.provider)}
            disabled={remove.isPending}
          >
            Disconnect {c.provider}
          </button>
        </div>
      ))}
      {buttons}
      {providers.data?.length === 0 && (
        <p>
          The instance operator has not configured social sign-in yet. Email and
          passkeys remain available.
        </p>
      )}
    </section>
  );
}
