const base = import.meta.env.VITE_API_URL ?? "";
let csrf: string | undefined;
export class ApiError extends Error {
  constructor(
    message: string,
    public status: number,
  ) {
    super(message);
  }
}
export async function api<T>(
  path: string,
  options: RequestInit = {},
): Promise<T> {
  const method = options.method ?? "GET";
  if (method !== "GET" && !csrf) {
    const r = await fetch(`${base}/api/v1/auth/csrf`, {
      credentials: "include",
    });
    if (!r.ok)
      throw new ApiError(
        r.status === 429
          ? "Please wait a minute before trying again."
          : "We could not connect. Please try again.",
        r.status,
      );
    csrf = (await r.json()).token;
  }
  const response = await fetch(`${base}/api/v1${path}`, {
    ...options,
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
      ...(csrf && method !== "GET" ? { "X-CSRF-Token": csrf } : {}),
      ...options.headers,
    },
  });
  if (!response.ok) {
    const body = await response.json().catch(() => ({}));
    if (response.status === 401) csrf = undefined;
    throw new ApiError(
      body.title ??
        (response.status === 429
          ? "Please wait a minute before trying again."
          : "We could not complete this action. Try again."),
      response.status,
    );
  }
  if (
    path.includes("/finish") ||
    path === "/auth/logout" ||
    path === "/auth/passkeys/login"
  )
    csrf = undefined;
  return response.status === 204 ||
    response.headers.get("content-length") === "0"
    ? (undefined as T)
    : await response.text().then((t) => (t ? JSON.parse(t) : undefined));
}
export const post = <T>(path: string, body: unknown = {}) =>
  api<T>(path, { method: "POST", body: JSON.stringify(body) });
export const certificateUrl = (org: string, id: string) =>
  `${base}/api/v1/organizations/${org}/orders/${id}/certificate`;
export interface Organization {
  id: string;
  name: string;
  role: string;
}
export interface Me {
  email: string;
  displayName: string;
  mfaEnabled: boolean;
  organizations: Organization[];
}
export interface Website {
  id: string;
  hostname: string;
  status: string;
  setupMethod: string;
  httpsTrusted: boolean | null;
  checkedAt: string | null;
  certificateObservedAt: string | null;
  nextCheckAt: string;
  expiresAt: string | null;
  issuer: string | null;
  fingerprint: string | null;
  hostingProvider: string | null;
  dnsProvider: string | null;
  diagnostic: string;
  renewal: string;
}
export interface Order {
  id: string;
  state: string;
  diagnostic: string | null;
  staging: boolean;
  expiresAt: string | null;
  renewalDueAt: string | null;
  renewalSource: string;
  renewalInfoError: string | null;
  challengesJson: string;
  hostnamesJson: string;
}
