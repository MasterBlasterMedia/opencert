import { useState, type ReactNode } from "react";
import {
  Check,
  Copy,
  AlertCircle,
  ShieldCheck,
  Clock3,
  ArrowUpRight,
} from "lucide-react";
import { Link } from "react-router-dom";
import type { Website } from "./api";
import s from "./App.module.scss";
export function ErrorBox({ error }: { error: unknown }) {
  return error ? (
    <div role="alert" className={s.error}>
      <AlertCircle size={18} />
      <span>
        {error instanceof Error
          ? error.message
          : "Something went wrong. Please try again."}
      </span>
    </div>
  ) : null;
}
export function Loading() {
  return (
    <div className={s.loading} role="status">
      Loading your information…
    </div>
  );
}
export function Status({ value }: { value: string }) {
  const healthy = value === "Protected";
  return (
    <span className={`${s.badge} ${healthy ? s.success : s.neutral}`}>
      {healthy ? <ShieldCheck size={14} /> : <Clock3 size={14} />}
      {value}
    </span>
  );
}
export function CopyValue({ value }: { value: string }) {
  const [copied, setCopied] = useState(false);
  const [error, setError] = useState("");
  return (
    <div className={s.copy}>
      <code>{value}</code>
      <button
        aria-label={`Copy ${value}`}
        onClick={async () => {
          try {
            await navigator.clipboard.writeText(value);
            setCopied(true);
            setError("");
          } catch {
            setError("Select and copy the value manually.");
          }
        }}
      >
        {copied ? <Check size={16} /> : <Copy size={16} />}
      </button>
      <span role="status">{copied ? "Copied" : error}</span>
    </div>
  );
}
export function PageTitle({
  eyebrow,
  title,
  children,
  action,
}: {
  eyebrow?: string;
  title: string;
  children?: ReactNode;
  action?: ReactNode;
}) {
  return (
    <div className={s.pageTitle}>
      <div>
        {eyebrow && <p className={s.eyebrow}>{eyebrow}</p>}
        <h1>{title}</h1>
        {children && <p className={s.lead}>{children}</p>}
      </div>
      {action}
    </div>
  );
}
export function WebsiteCard({ site }: { site: Website }) {
  return (
    <Link to={`/app/websites/${site.id}`} className={s.websiteCard}>
      <div className={s.siteIcon}>{site.hostname[0].toUpperCase()}</div>
      <div className={s.siteBody}>
        <h3>{site.hostname}</h3>
        <p>
          {site.hostingProvider
            ? `Likely hosted on ${site.hostingProvider}`
            : "Hosting provider not confirmed"}
        </p>
        <small>
          {site.checkedAt
            ? `Last checked ${new Date(site.checkedAt).toLocaleString()}`
            : "Waiting for the first website check"}
        </small>
      </div>
      <div className={s.siteState}>
        <Status value={site.status} />
        <small>Renewal: {site.renewal.toLowerCase()}</small>
      </div>
      <ArrowUpRight size={20} />
    </Link>
  );
}
export function Empty({
  title,
  children,
}: {
  title: string;
  children: ReactNode;
}) {
  return (
    <div className={s.empty}>
      <ShieldCheck size={36} />
      <h2>{title}</h2>
      {children}
    </div>
  );
}
