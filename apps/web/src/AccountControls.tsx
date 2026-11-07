import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api, post, type Me } from "./api";
import { CopyValue, ErrorBox } from "./components";
import s from "./App.module.scss";
export function AccountControls({ me }: { me: Me }) {
  const client = useQueryClient();
  const navigate = useNavigate();
  const [name, setName] = useState(me.displayName);
  const [email, setEmail] = useState("");
  const [flow, setFlow] = useState<string | null>(null);
  const [code, setCode] = useState("");
  const [codes, setCodes] = useState<string[]>([]);
  const [confirm, setConfirm] = useState("");
  const [deletion, setDeletion] = useState("");
  const profile = useMutation({
    mutationFn: () =>
      api("/account/profile", {
        method: "PUT",
        body: JSON.stringify({ name }),
      }),
    onSuccess: () => client.invalidateQueries({ queryKey: ["me"] }),
  });
  const start = useMutation({
    mutationFn: () =>
      post<{ flowId: string }>("/account/email/start", { email }),
    onSuccess: (d) => setFlow(d.flowId),
  });
  const finish = useMutation({
    mutationFn: () => post("/account/email/finish", { flowId: flow, code }),
    onSuccess: () => {
      setFlow(null);
      setEmail("");
      setCode("");
      client.invalidateQueries({ queryKey: ["me"] });
    },
  });
  const recovery = useMutation({
    mutationFn: () =>
      post<{ recoveryCodes: string[] }>("/account/recovery-codes"),
    onSuccess: (d) => setCodes(d.recoveryCodes),
  });
  const remove = useMutation({
    mutationFn: () =>
      api("/account/authenticator", {
        method: "DELETE",
        body: JSON.stringify({ confirmation: confirm }),
      }),
    onSuccess: () => {
      setCodes([]);
      setConfirm("");
      client.invalidateQueries({ queryKey: ["me"] });
    },
  });
  const close = useMutation({
    mutationFn: () =>
      api("/account/", {
        method: "DELETE",
        body: JSON.stringify({ confirmation: deletion }),
      }),
    onSuccess: () => {
      client.clear();
      navigate("/login");
    },
  });
  return (
    <>
      <section className={s.panel}>
        <h2>Your profile</h2>
        <p>{me.email} · verified</p>
        <form
          onSubmit={(e) => {
            e.preventDefault();
            profile.mutate();
          }}
        >
          <label htmlFor="display-name">Display name</label>
          <input
            id="display-name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            required
            maxLength={80}
          />
          <ErrorBox error={profile.error} />
          <button className={s.secondary} disabled={profile.isPending}>
            Save profile
          </button>
          {profile.isSuccess && <p role="status">Profile saved.</p>}
        </form>
      </section>
      <details className={s.panel}>
        <summary>Change your sign-in email</summary>
        <p>
          Confirm your identity using an authenticator or passkey first. You
          must verify the new address in this browser. Other sessions will be
          signed out.
        </p>
        <form
          onSubmit={(e) => {
            e.preventDefault();
            if (flow) finish.mutate();
            else start.mutate();
          }}
        >
          {flow ? (
            <>
              <label htmlFor="email-change-code">
                Code sent to your new address
              </label>
              <input
                id="email-change-code"
                value={code}
                onChange={(e) => setCode(e.target.value)}
                required
                autoComplete="one-time-code"
              />
            </>
          ) : (
            <>
              <label htmlFor="new-email">New email address</label>
              <input
                id="new-email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                type="email"
                required
              />
            </>
          )}
          <ErrorBox error={start.error || finish.error} />
          <button
            className={s.button}
            disabled={start.isPending || finish.isPending}
          >
            {flow ? "Confirm new email" : "Send verification code"}
          </button>
          {finish.isSuccess && (
            <p role="status">
              Email changed. Your other sessions have been signed out.
            </p>
          )}
        </form>
      </details>
      {me.mfaEnabled && (
        <details className={s.panel}>
          <summary>Recovery codes and authenticator removal</summary>
          <p>
            Confirm your identity using your authenticator or a passkey before
            making changes.
          </p>
          <button
            className={s.secondary}
            onClick={() => recovery.mutate()}
            disabled={recovery.isPending}
          >
            Replace all recovery codes
          </button>
          <ErrorBox error={recovery.error || remove.error} />
          {codes.length > 0 && (
            <div className={s.note}>
              <h3>Save these new codes</h3>
              <p>
                All earlier recovery codes are invalid. Each new code works
                once.
              </p>
              {codes.map((c) => (
                <CopyValue key={c} value={c} />
              ))}
              <button onClick={() => setCodes([])}>
                I’ve saved these codes
              </button>
            </div>
          )}
          <form
            onSubmit={(e) => {
              e.preventDefault();
              remove.mutate();
            }}
          >
            <p>
              Removing your authenticator disables its recovery codes and signs
              out other sessions. You can enroll a replacement afterward.
            </p>
            <label htmlFor="remove-mfa">
              Type REMOVE to disable your authenticator
            </label>
            <input
              id="remove-mfa"
              value={confirm}
              onChange={(e) => setConfirm(e.target.value)}
            />
            <button
              className={s.danger}
              disabled={remove.isPending || confirm !== "REMOVE"}
            >
              Remove authenticator
            </button>
          </form>
        </details>
      )}
      <details className={s.panel}>
        <summary>Delete your account</summary>
        <p>
          Transfer or delete every workspace you own first. Confirm your
          identity using an authenticator or passkey. This removes your profile,
          sign-in methods and sessions. Shared workspace audit records are
          retained without your account identifier.
        </p>
        <form
          onSubmit={(e) => {
            e.preventDefault();
            close.mutate();
          }}
        >
          <label htmlFor="delete-account">Type DELETE to confirm</label>
          <input
            id="delete-account"
            value={deletion}
            onChange={(e) => setDeletion(e.target.value)}
          />
          <ErrorBox error={close.error} />
          <button
            className={s.danger}
            disabled={close.isPending || deletion !== "DELETE"}
          >
            Permanently delete my account
          </button>
        </form>
      </details>
    </>
  );
}
