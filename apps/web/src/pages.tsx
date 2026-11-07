import { useEffect, useState } from "react";
import { Link, useNavigate, useParams, useLocation } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import QRCode from "qrcode";
import {
  ArrowRight,
  ShieldCheck,
  Globe2,
  Check,
  RefreshCw,
  LockKeyhole,
  Mail,
  KeyRound,
} from "lucide-react";
import { api, post, certificateUrl, type Order, type Website } from "./api";
import { useWebsites, useWorkspace, useAccount } from "./App";
import {
  CopyValue,
  Empty,
  ErrorBox,
  Loading,
  PageTitle,
  Status,
} from "./components";
import { guides } from "./content";
import s from "./App.module.scss";
import { ConnectedAccounts } from "./ConnectedAccounts";
import { AccountControls } from "./AccountControls";
import { registerPasskey, signInPasskey } from "./passkeys";
const emailSchema = z.object({
  email: z.email("Enter a valid email address."),
});
export function Login() {
  const location = useLocation();
  const destination =
    typeof location.state?.invitation === "string"
      ? "/app/team" + location.state.invitation
      : "/app";
  const initial = new URLSearchParams(window.location.hash.slice(1));
  const [flow, setFlow] = useState<string | null>(
    initial.get("flow") ?? initial.get("mfa"),
  );
  const [code, setCode] = useState(initial.get("code") ?? "");
  const [mfa, setMfa] = useState(!!initial.get("mfa"));
  const navigate = useNavigate();
  const client = useQueryClient();
  const form = useForm<{ email: string }>({
    resolver: zodResolver(emailSchema),
  });
  useEffect(() => {
    if (window.location.hash)
      window.history.replaceState(null, "", window.location.pathname);
  }, []);
  const start = useMutation({
    mutationFn: (data: { email: string }) =>
      post<{ flowId: string }>("/auth/email/start", data),
    onSuccess: (data) => setFlow(data.flowId),
  });
  const passkey = useMutation({
    mutationFn: signInPasskey,
    onSuccess: () => {
      client.invalidateQueries({ queryKey: ["me"] });
      navigate(destination);
    },
  });
  const finish = useMutation({
    mutationFn: () =>
      post<{ requiresMfa?: boolean }>(
        mfa ? "/auth/mfa/finish" : "/auth/email/finish",
        { flowId: flow, code },
      ),
    onSuccess: async (data) => {
      if (data?.requiresMfa) {
        setMfa(true);
        setCode("");
      } else {
        await client.invalidateQueries({ queryKey: ["me"] });
        navigate(destination);
      }
    },
  });
  return (
    <div className={s.loginPage}>
      <div className={s.loginAside}>
        <span className={s.eyebrow}>WELCOME TO A SIMPLER HTTPS</span>
        <h1>
          A little less worry.
          <br />A little more web.
        </h1>
        <p>
          Keep track of your websites, understand your next step, and keep your
          private keys where they belong.
        </p>
        <ShieldCheck size={130} strokeWidth={0.8} />
        <span>Free software. Open to everyone.</span>
      </div>
      <div className={s.loginForm}>
        <div className={s.formIcon}>{mfa ? <KeyRound /> : <Mail />}</div>
        <h2>
          {mfa
            ? "One more security check."
            : flow
              ? "Check your inbox."
              : "Welcome to OpenCert."}
        </h2>
        <p>
          {mfa
            ? "Enter your authenticator code or one of your saved recovery codes."
            : flow
              ? "Enter the code we sent you. Keep this page open in the browser where you requested it."
              : "Sign in or create your account with your email. No password to remember."}
        </p>
        {!flow ? (
          <form onSubmit={form.handleSubmit((data) => start.mutate(data))}>
            <label htmlFor="email">Email address</label>
            <input
              id="email"
              type="email"
              autoComplete="email"
              placeholder="you@example.com"
              {...form.register("email")}
              aria-invalid={!!form.formState.errors.email}
            />
            <ErrorBox
              error={
                form.formState.errors.email?.message
                  ? new Error(form.formState.errors.email.message)
                  : start.error
              }
            />
            <button className={s.button} disabled={start.isPending}>
              {start.isPending ? "Sending your code…" : "Continue with email"}
              <ArrowRight size={17} />
            </button>
            <p className={s.subtle}>
              By using this development instance, review its{" "}
              <Link to="/privacy">privacy notice</Link> and{" "}
              <Link to="/terms">terms</Link>.
            </p>
          </form>
        ) : (
          <form
            onSubmit={(e) => {
              e.preventDefault();
              finish.mutate();
            }}
          >
            <label htmlFor="code">
              {mfa ? "Authenticator or recovery code" : "Sign-in code"}
            </label>
            <input
              id="code"
              autoComplete="one-time-code"
              value={code}
              onChange={(e) => setCode(e.target.value)}
              maxLength={64}
              required
            />
            <ErrorBox error={finish.error} />
            <button className={s.button} disabled={finish.isPending}>
              {finish.isPending
                ? "Checking your code…"
                : mfa
                  ? "Verify and sign in"
                  : "Confirm sign in"}
              <ArrowRight size={17} />
            </button>
            <button
              type="button"
              className={s.textButton}
              onClick={() => {
                setFlow(null);
                setMfa(false);
                setCode("");
                finish.reset();
              }}
            >
              Use a different email or request a new code
            </button>
          </form>
        )}
        <ConnectedAccounts login />
        {new URLSearchParams(location.search).get("external") ===
          "unlinked" && (
          <p role="status">
            First sign in with your verified email, then connect this provider
            in Account settings. Matching email addresses never link accounts
            automatically.
          </p>
        )}
        {new URLSearchParams(location.search).get("external") === "failed" && (
          <p role="alert">
            Provider sign-in did not complete. Try again or use email.
          </p>
        )}
        <button
          className={s.secondary}
          onClick={() => passkey.mutate()}
          disabled={passkey.isPending}
        >
          Sign in with a passkey
        </button>
        <ErrorBox error={passkey.error} />
        <div className={s.note}>
          <LockKeyhole size={17} />
          Login links only work after you confirm. Opening an email link alone
          does not sign you in.
        </div>
      </div>
    </div>
  );
}
const hostSchema = z.object({
  hostname: z
    .string()
    .trim()
    .min(3, "Enter your website address.")
    .max(253, "That address is too long.")
    .refine(
      (v) => !/[\s@?#]/.test(v),
      "Enter the website address without a path or sign-in details.",
    ),
});
export function Protect() {
  const { org } = useWorkspace();
  const nav = useNavigate();
  const client = useQueryClient();
  const form = useForm<{ hostname: string }>({
    resolver: zodResolver(hostSchema),
  });
  const create = useMutation({
    mutationFn: (data: { hostname: string }) =>
      post<Website>(`/organizations/${org.id}/websites`, data),
    onSuccess: async (site) => {
      await client.invalidateQueries({ queryKey: ["websites"] });
      nav(`/app/websites/${site.id}`);
    },
  });
  return (
    <>
      <PageTitle
        eyebrow="YOUR WEBSITE SECURITY ASSISTANT"
        title="Let’s start with your website."
      >
        One address. We’ll help you figure out the rest.
      </PageTitle>
      <div className={s.wizardLayout}>
        <section className={s.wizardCard}>
          <div className={s.wizardSteps}>
            <span className={s.currentStep}>
              1 <small>Website</small>
            </span>
            <span>
              2 <small>Setup guide</small>
            </span>
            <span>
              3 <small>Check HTTPS</small>
            </span>
          </div>
          <div className={s.formIcon}>
            <Globe2 />
          </div>
          <h2>What’s your website address?</h2>
          <p>Use the address your visitors type into their browser.</p>
          <form onSubmit={form.handleSubmit((data) => create.mutate(data))}>
            <label htmlFor="hostname">Website address</label>
            <div className={s.domainInput}>
              <Globe2 size={18} />
              <input
                id="hostname"
                placeholder="mywebsite.dev"
                autoComplete="url"
                {...form.register("hostname")}
                aria-invalid={!!form.formState.errors.hostname}
              />
            </div>
            <small>
              You can include https://. We’ll tidy the address for you.
            </small>
            <ErrorBox
              error={
                form.formState.errors.hostname
                  ? new Error(form.formState.errors.hostname.message)
                  : create.error
              }
            />
            <button className={s.button} disabled={create.isPending}>
              {create.isPending ? "Saving your website…" : "Find my next step"}
              <ArrowRight size={17} />
            </button>
          </form>
        </section>
        <aside className={s.wizardHelp}>
          <ShieldCheck size={27} />
          <h3>We’ll look before we leap.</h3>
          <p>
            Your hosting provider may already include free HTTPS. We’ll check
            your current setup first.
          </p>
          <hr />
          <h3>Nothing changes without you.</h3>
          <p>
            Adding your website allows us to check its public DNS and HTTPS. It
            does not change your DNS records or server.
          </p>
          <Link to="/help/godaddy">
            Bought your domain at GoDaddy? <ArrowRight size={16} />
          </Link>
        </aside>
      </div>
    </>
  );
}
export function WebsiteDetail() {
  const { id } = useParams();
  const { org } = useWorkspace();
  const client = useQueryClient();
  const base = `/organizations/${org.id}/websites/${id}`;
  const q = useQuery({
    queryKey: ["website", org.id, id],
    queryFn: () => api<Website>(base),
    refetchInterval: 5000,
  });
  const check = useMutation({
    mutationFn: () => post(`${base}/check`),
    onSuccess: () => client.invalidateQueries({ queryKey: ["website"] }),
  });
  const setup = useMutation({
    mutationFn: (method: string) =>
      api(base + "/setup", { method: "PUT", body: JSON.stringify({ method }) }),
    onSuccess: () => client.invalidateQueries({ queryKey: ["website"] }),
  });
  if (q.isLoading) return <Loading />;
  if (!q.data)
    return (
      <>
        <ErrorBox error={q.error} />
        <button onClick={() => q.refetch()}>Try again</button>
      </>
    );
  const w = q.data;
  const provider = w.hostingProvider?.toLowerCase();
  return (
    <>
      <Link to="/app/websites" className={s.breadcrumb}>
        ← My websites
      </Link>
      <PageTitle
        eyebrow="WEBSITE SECURITY STATUS"
        title={w.hostname}
        action={<Status value={w.status} />}
      >
        {w.diagnostic}
      </PageTitle>
      <ErrorBox error={check.error || setup.error} />
      <div className={s.detailGrid}>
        <section className={s.panel}>
          <h2>Your connection</h2>
          <div className={s.statusRow}>
            <span>Live HTTPS</span>
            <strong>
              {w.httpsTrusted === true
                ? "Trusted certificate verified"
                : w.httpsTrusted === false
                  ? "Needs attention"
                  : "Not verified"}
            </strong>
          </div>
          <div className={s.statusRow}>
            <span>Certificate expiration</span>
            <strong>
              {w.expiresAt
                ? new Date(w.expiresAt).toLocaleDateString()
                : "Not available"}
            </strong>
          </div>
          <div className={s.statusRow}>
            <span>Automatic renewal</span>
            <strong>Not confirmed</strong>
          </div>
          <div className={s.statusRow}>
            <span>Last checked</span>
            <strong>
              {w.checkedAt
                ? new Date(w.checkedAt).toLocaleString()
                : "Check queued"}
            </strong>
          </div>
          <button
            className={s.secondary}
            onClick={() => check.mutate()}
            disabled={check.isPending}
          >
            <RefreshCw size={16} />
            {check.isPending ? "Queuing check…" : "Check HTTPS again"}
          </button>
          {check.isSuccess && (
            <p role="status">
              Your check is queued. This page updates when the worker finishes.
            </p>
          )}
        </section>
        <section className={`${s.panel} ${s.recommendation}`}>
          <span className={s.eyebrow}>YOUR NEXT STEP</span>
          <h2>
            {w.httpsTrusted
              ? "Check who handles renewal."
              : "Start with your hosting provider."}
          </h2>
          <p>
            {w.httpsTrusted
              ? "Your website presented a trusted certificate. Keep the existing setup and confirm that your host renews it automatically."
              : "Many hosting companies include free HTTPS. Check their built-in option before requesting a separate certificate."}
          </p>
          {provider && guides[provider] ? (
            <Link to={`/help/${provider}`} className={s.button}>
              Open {w.hostingProvider} guide <ArrowRight size={16} />
            </Link>
          ) : (
            <Link to="/help" className={s.button}>
              Find my hosting guide <ArrowRight size={16} />
            </Link>
          )}
          <button
            className={s.textButton}
            onClick={() => setup.mutate("ProviderManaged")}
          >
            Save “My host manages HTTPS”
          </button>
          <small>
            This saves your chosen setup; it does not verify automatic renewal.
          </small>
          {w.setupMethod === "ProviderManaged" && (
            <p role="status">
              <Check size={15} />
              Hosting setup preference saved.
            </p>
          )}
        </section>
      </div>
      <div className={s.panel}>
        <h2>Where your website lives</h2>
        <div className={s.twoColumns}>
          <div>
            <span className={s.eyebrow}>LIKELY WEB HOST</span>
            <h3>{w.hostingProvider ?? "Not identified"}</h3>
            <p>Provides the server or website builder.</p>
          </div>
          <div>
            <span className={s.eyebrow}>LIKELY DNS PROVIDER</span>
            <h3>{w.dnsProvider ?? "Not identified"}</h3>
            <p>Manages where your website name points.</p>
          </div>
        </div>
        <p className={s.subtle}>
          These are suggestions from public DNS, not confirmed account
          information. Your domain registrar may be a different company.
        </p>
        <Link to="/help/godaddy">
          I’m not sure where my website is hosted →
        </Link>
      </div>
      <details className={s.panel}>
        <summary>Advanced: certificate details and manual requests</summary>
        <p>Issuer: {w.issuer ?? "Not available"}</p>
        <p>SHA-256 fingerprint: {w.fingerprint ?? "Not available"}</p>
        <CertificateRequests website={w} />
      </details>
    </>
  );
}
function CertificateRequests({ website }: { website: Website }) {
  const { org } = useWorkspace();
  const client = useQueryClient();
  const base = `/organizations/${org.id}`;
  const [csr, setCsr] = useState("");
  const [names, setNames] = useState(website.hostname);
  const [terms, setTerms] = useState(false);
  const [revoke, setRevoke] = useState<string | null>(null);
  const q = useQuery({
    queryKey: ["orders", org.id, website.id],
    queryFn: () => api<Order[]>(`${base}/websites/${website.id}/orders`),
    refetchInterval: 5000,
  });
  const create = useMutation({
    mutationFn: () =>
      post(`${base}/websites/${website.id}/orders`, {
        csrPem: csr,
        hostnames: names.split(",").map((n) => n.trim()),
        acceptTerms: terms,
      }),
    onSuccess: () => {
      setCsr("");
      client.invalidateQueries({ queryKey: ["orders"] });
    },
  });
  const action = useMutation({
    mutationFn: ({ id, verb }: { id: string; verb: string }) =>
      post(`${base}/orders/${id}/${verb}`),
    onSuccess: () => {
      setRevoke(null);
      client.invalidateQueries({ queryKey: ["orders"] });
    },
  });
  return (
    <>
      <h3>Request a certificate with your local CSR</h3>
      <p>
        Keep your private key on your server. Enable an authenticator in{" "}
        <Link to="/app/account">Account Settings</Link> and confirm recent
        authentication before requesting a certificate.
      </p>
      <div className={s.note}>
        The default certificate authority is Let’s Encrypt staging. Test
        certificates are not browser-trusted. Manual requests require you to
        install and renew the certificate.
      </div>
      <form
        onSubmit={(e) => {
          e.preventDefault();
          create.mutate();
        }}
      >
        <label htmlFor="names">
          Certificate website names, separated by commas
        </label>
        <input
          id="names"
          value={names}
          onChange={(e) => setNames(e.target.value)}
        />
        <label htmlFor="csr">Certificate signing request (CSR)</label>
        <textarea
          id="csr"
          rows={7}
          value={csr}
          onChange={(e) => setCsr(e.target.value)}
          placeholder="-----BEGIN CERTIFICATE REQUEST-----"
          required
          maxLength={24000}
        />
        <label className={s.checkbox}>
          <input
            type="checkbox"
            checked={terms}
            onChange={(e) => setTerms(e.target.checked)}
            required
          />
          I have read and accept the{" "}
          <a
            href="https://letsencrypt.org/repository/"
            target="_blank"
            rel="noreferrer"
          >
            Let’s Encrypt subscriber agreement
          </a>{" "}
          and am authorized to manage these names.
        </label>
        <ErrorBox error={create.error} />
        <button className={s.button} disabled={create.isPending}>
          {create.isPending ? "Submitting request…" : "Request certificate"}
        </button>
      </form>
      <ErrorBox error={q.error || action.error} />
      {q.data?.map((order) => (
        <article key={order.id} className={s.order}>
          <div className={s.listHeading}>
            <h3>{order.state}</h3>
            <span className={s.badge}>
              {order.staging ? "Test certificate" : "Production certificate"}
            </span>
          </div>
          <p>{order.diagnostic}</p>
          {(
            JSON.parse(order.challengesJson) as {
              name: string;
              value: string;
            }[]
          ).map((c, i) => (
            <div key={i}>
              <label>TXT record name</label>
              <CopyValue value={c.name} />
              <label>TXT record value</label>
              <CopyValue value={c.value} />
            </div>
          ))}
          {["AwaitingVerification", "Verifying"].includes(order.state) && (
            <button
              className={s.button}
              disabled={action.isPending}
              onClick={() => action.mutate({ id: order.id, verb: "verify" })}
            >
              Verify ownership
            </button>
          )}
          {order.state === "Issued" && (
            <>
              <a className={s.button} href={certificateUrl(org.id, order.id)}>
                Download certificate and chain
              </a>
              <button
                className={s.textButton}
                onClick={() => setRevoke(order.id)}
              >
                Revoke certificate
              </button>
            </>
          )}
          {revoke === order.id && (
            <div
              role="group"
              aria-label="Confirm certificate revocation"
              className={s.error}
            >
              <p>
                Revocation can disrupt websites using this certificate and
                cannot be undone. Replace it wherever installed.
              </p>
              <button
                className={s.danger}
                onClick={() => action.mutate({ id: order.id, verb: "revoke" })}
                disabled={action.isPending}
              >
                Confirm revocation
              </button>
              <button onClick={() => setRevoke(null)}>Cancel</button>
            </div>
          )}
        </article>
      ))}
    </>
  );
}
export function Account() {
  const me = useAccount();
  const client = useQueryClient();
  const [setup, setSetup] = useState<{ secret: string; uri: string } | null>(
    null,
  );
  const [qr, setQr] = useState("");
  const [code, setCode] = useState("");
  const [recovery, setRecovery] = useState<string[]>([]);
  const [stepCode, setStepCode] = useState("");
  const enroll = useMutation({
    mutationFn: () => post<{ secret: string; uri: string }>("/auth/totp/setup"),
    onSuccess: setSetup,
  });
  useEffect(() => {
    if (setup)
      QRCode.toDataURL(setup.uri, { width: 200, margin: 2 }).then(setQr);
  }, [setup]);
  const enable = useMutation({
    mutationFn: () =>
      post<{ recoveryCodes: string[] }>("/auth/totp/enable", { code }),
    onSuccess: (data) => {
      setRecovery(data.recoveryCodes);
      setSetup(null);
      client.invalidateQueries({ queryKey: ["me"] });
    },
  });
  const step = useMutation({
    mutationFn: () => post("/auth/step-up", { code: stepCode }),
    onSuccess: () => setStepCode(""),
  });
  const sessions = useQuery({
    queryKey: ["sessions"],
    queryFn: () =>
      api<{ id: string; createdAt: string; expiresAt: string }[]>(
        "/auth/sessions",
      ),
  });
  const revoke = useMutation({
    mutationFn: (id: string) =>
      api("/auth/sessions/" + id, { method: "DELETE" }),
    onSuccess: () => {
      client.invalidateQueries({ queryKey: ["sessions"] });
      client.invalidateQueries({ queryKey: ["me"] });
    },
  });
  return (
    <>
      <PageTitle eyebrow="YOUR ACCOUNT" title="A little extra protection.">
        Manage how you sign in and where you’re signed in.
      </PageTitle>
      <AccountControls me={me} />
      <section className={s.panel}>
        <h2>Authenticator app</h2>
        <p>
          {me.mfaEnabled
            ? "Your account requires an authenticator or recovery code after email sign-in."
            : "Add a second sign-in step using an authenticator app. You’ll also receive recovery codes."}
        </p>
        <ErrorBox error={enroll.error || enable.error} />
        {!me.mfaEnabled && !setup && (
          <button
            className={s.button}
            onClick={() => enroll.mutate()}
            disabled={enroll.isPending}
          >
            Set up authenticator <ArrowRight size={16} />
          </button>
        )}
        {setup && (
          <div className={s.enrollment}>
            {qr && (
              <img
                src={qr}
                alt="Scan this QR code with your authenticator app"
                width={200}
                height={200}
              />
            )}
            <p>Or enter this setup key in your app:</p>
            <CopyValue value={setup.secret} />
            <form
              onSubmit={(e) => {
                e.preventDefault();
                enable.mutate();
              }}
            >
              <label htmlFor="totp">Six-digit authenticator code</label>
              <input
                id="totp"
                inputMode="numeric"
                autoComplete="one-time-code"
                value={code}
                onChange={(e) => setCode(e.target.value)}
                required
              />
              <button className={s.button} disabled={enable.isPending}>
                Verify and enable
              </button>
            </form>
          </div>
        )}
        {recovery.length > 0 && (
          <div className={s.note}>
            <h3>Save your recovery codes now.</h3>
            <p>
              Each works once. Store them somewhere private outside this
              browser.
            </p>
            {recovery.map((code) => (
              <CopyValue key={code} value={code} />
            ))}
            <button onClick={() => setRecovery([])}>
              I’ve saved these codes
            </button>
          </div>
        )}
      </section>
      {me.mfaEnabled && (
        <section className={s.panel}>
          <h2>Confirm a sensitive action</h2>
          <p>
            Verification lasts for 10 minutes, for certificate requests and
            other sensitive settings.
          </p>
          <form
            onSubmit={(e) => {
              e.preventDefault();
              step.mutate();
            }}
          >
            <label htmlFor="step">Current authenticator code</label>
            <input
              id="step"
              value={stepCode}
              onChange={(e) => setStepCode(e.target.value)}
              inputMode="numeric"
              autoComplete="one-time-code"
              required
            />
            <ErrorBox error={step.error} />
            <button className={s.button} disabled={step.isPending}>
              Confirm it’s me
            </button>
            {step.isSuccess && (
              <p role="status">
                Verified. You can return to your certificate request.
              </p>
            )}
          </form>
        </section>
      )}
      <PasskeySettings />
      <ConnectedAccounts />
      <section className={s.panel}>
        <h2>Your sessions</h2>
        <p>Revoking a session signs that browser out on its next request.</p>
        <ErrorBox error={sessions.error || revoke.error} />
        {sessions.data?.map((session) => (
          <div key={session.id} className={s.statusRow}>
            <span>Started {new Date(session.createdAt).toLocaleString()}</span>
            <button
              className={s.secondary}
              onClick={() => revoke.mutate(session.id)}
              disabled={revoke.isPending}
            >
              Revoke session
            </button>
          </div>
        ))}
      </section>
    </>
  );
}
export function Notifications() {
  const { org } = useWorkspace();
  const client = useQueryClient();
  const q = useQuery({
    queryKey: ["notifications", org.id],
    queryFn: () =>
      api<{ id: string; message: string; createdAt: string; read: boolean }[]>(
        `/organizations/${org.id}/notifications`,
      ),
  });
  const read = useMutation({
    mutationFn: (id: string) =>
      api(`/organizations/${org.id}/notifications/${id}/read`, {
        method: "PUT",
      }),
    onSuccess: () => client.invalidateQueries({ queryKey: ["notifications"] }),
  });
  return (
    <>
      <PageTitle title="Your notifications">
        Updates that help you know what needs attention.
      </PageTitle>
      <ErrorBox error={q.error || read.error} />
      {q.isLoading ? (
        <Loading />
      ) : !q.data?.length ? (
        <Empty title="You’re all caught up.">
          <p>
            Certificate and website updates will appear here when there’s
            something to report.
          </p>
        </Empty>
      ) : (
        q.data.map((n) => (
          <article key={n.id} className={s.panel}>
            <h2>{n.message}</h2>
            <p>{new Date(n.createdAt).toLocaleString()}</p>
            {!n.read && (
              <button onClick={() => read.mutate(n.id)} className={s.secondary}>
                Mark as read
              </button>
            )}
          </article>
        ))
      )}
    </>
  );
}
export function Advanced() {
  const { org } = useWorkspace();
  const websites = useWebsites();
  const activity = useQuery({
    queryKey: ["activity", org.id],
    queryFn: () =>
      api<{ action: string; at: string; resourceId: string }[]>(
        `/organizations/${org.id}/activity`,
      ),
  });
  return (
    <>
      <PageTitle
        eyebrow="ADVANCED TOOLS"
        title="More detail. The same safeguards."
      >
        Manage certificate requests and inspect workspace activity.
      </PageTitle>
      <section className={s.panel}>
        <h2>Certificates</h2>
        <p>
          Open a website’s advanced section to submit a CSR, verify DNS records,
          retrieve a certificate chain or request revocation.
        </p>
        <ErrorBox error={websites.error} />
        {websites.data?.map((w) => (
          <div className={s.statusRow} key={w.id}>
            <Link to={`/app/websites/${w.id}`}>{w.hostname}</Link>
            <Status value={w.status} />
          </div>
        ))}
        {!websites.data?.length && (
          <Link to="/app/protect">Add a website to begin →</Link>
        )}
      </section>
      <section className={s.panel}>
        <h2>Workspace activity</h2>
        <ErrorBox error={activity.error} />
        {activity.data?.length ? (
          activity.data.map((a, i) => (
            <div className={s.statusRow} key={i}>
              <span>{a.action.replaceAll(".", " ")}</span>
              <time>{new Date(a.at).toLocaleString()}</time>
            </div>
          ))
        ) : (
          <p>No recorded workspace activity yet.</p>
        )}
      </section>
    </>
  );
}

function PasskeySettings() {
  const client = useQueryClient();
  const [name, setName] = useState("My passkey");
  const q = useQuery({
    queryKey: ["passkeys"],
    queryFn: () =>
      api<{ id: string; name: string; createdAt: string }[]>("/auth/passkeys/"),
  });
  const add = useMutation({
    mutationFn: () => registerPasskey(name),
    onSuccess: () => client.invalidateQueries({ queryKey: ["passkeys"] }),
  });
  const remove = useMutation({
    mutationFn: (id: string) =>
      api("/auth/passkeys/" + id, { method: "DELETE" }),
    onSuccess: () => client.invalidateQueries({ queryKey: ["passkeys"] }),
  });
  const verify = useMutation({ mutationFn: signInPasskey });
  return (
    <section className={s.panel}>
      <h2>Passkeys</h2>
      <p>
        Use your device unlock, fingerprint or security key. User verification
        is required, and verified passkey sign-in satisfies OpenCert’s
        strong-authentication policy.
      </p>
      <ErrorBox error={q.error || add.error || remove.error || verify.error} />
      {q.data?.map((p) => (
        <div key={p.id} className={s.statusRow}>
          <span>{p.name}</span>
          <button className={s.secondary} onClick={() => remove.mutate(p.id)}>
            Remove passkey
          </button>
        </div>
      ))}
      <label htmlFor="passkey-name">Name this passkey</label>
      <input
        id="passkey-name"
        value={name}
        onChange={(e) => setName(e.target.value)}
        maxLength={80}
      />
      <button
        className={s.button}
        onClick={() => add.mutate()}
        disabled={add.isPending}
      >
        Add a passkey
      </button>
      <button
        className={s.textButton}
        onClick={() => verify.mutate()}
        disabled={verify.isPending}
      >
        Confirm a sensitive action with my passkey
      </button>
      {verify.isSuccess && <p role="status">Verified for 10 minutes.</p>}
    </section>
  );
}
