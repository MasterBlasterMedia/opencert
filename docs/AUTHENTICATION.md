# Authentication configuration

Email is the initial verified identity. Social accounts are explicitly linked from Account settings after a recent sign-in; provider email claims never merge accounts. After linking, provider sign-in uses the stable provider subject plus scheme, and still requires enrolled TOTP or a recovery code. Required WebAuthn user verification qualifies as strong authentication. Sensitive changes require strong authentication within ten minutes.

## Email

Set `Email__ResendApiKey` and `Email__From` to a verified sender on your own domain. Store the API key in the host's secret configuration. Never paste it into an issue or source file. Production does not fall back to the development mailbox. Test delivery, spam handling and Resend domain verification before offering registration publicly. Invitations and account-change notices use an encrypted transactional outbox; current sign-in emails are sent synchronously with provider idempotency keys.

## Social providers

Register a separate application for this OpenCert deployment with each provider you want to enable. Configure only providers you can verify. Set `Web__Origin` to the exact public HTTPS origin with no trailing slash. Register these exact redirect URIs using that origin:

| Provider | Callback | Configuration |
| --- | --- | --- |
| Google | `/signin-google` | `Authentication__Google__ClientId`, `Authentication__Google__ClientSecret` |
| Microsoft | `/signin-microsoft` | `Authentication__Microsoft__ClientId`, `Authentication__Microsoft__ClientSecret` |
| GitHub | `/signin-github` | `Authentication__GitHub__ClientId`, `Authentication__GitHub__ClientSecret` |
| Discord | `/signin-discord` | `Authentication__Discord__ClientId`, `Authentication__Discord__ClientSecret` |
| Apple | `/signin-apple` | `Authentication__Apple__ClientId` plus the signed `ClientSecret`, or `PrivateKeyFile`, `KeyId`, and `TeamId` under that prefix |

Google and Microsoft use official ASP.NET middleware. GitHub and Discord use aspnet-contrib providers. Apple uses the standard OpenID Connect handler with nonce, issuer, audience, signature and lifetime validation. Authorization-code flow is used throughout. PKCE S256 is enabled for Google, Microsoft, GitHub and Discord. Apple's published discovery metadata does not advertise PKCE; it uses a confidential-client signed secret instead. Provider access/refresh tokens are not persisted or exposed to the browser.

Google requests `openid profile`; Microsoft requests `User.Read`; Discord requests `identify`; GitHub requests public identity without repository permissions; Apple requests `openid`. Email claims from providers are not used as account ownership proof. Review provider consent pages and test with your registered application because provider requirements may change.

Apple can generate a short-lived signed client secret for each code exchange from an externally mounted `.p8` file. That file is an operator secret, not a customer certificate key. A pre-generated Apple client secret must be rotated before its expiry. Apple developer enrollment/eligibility is an external prerequisite; OpenCert does not purchase it or bypass provider requirements.

Provider callbacks use middleware state/correlation cookies. The additional OpenCert flow is short-lived, single-use, and bound to its initiating browser; link flows are also bound to the signed-in user. Callback failures use fixed local redirects. Do not add arbitrary return URLs. Proxy headers and public origin must be correct before testing callbacks.

## MFA and sessions

TOTP secrets are encrypted with shared Data Protection. Recovery codes are hashed and consumed atomically under a user lock. Rotating codes replaces the set; removal invalidates them and other sessions. Email changes verify a new-address code in the initiating strong-authenticated session and sign out other sessions.

Cookies are HttpOnly, Secure in production and SameSite=Lax. Unsafe browser API requests require antiforgery headers. Server-side session IDs expire after twelve hours and are checked on each request alongside the Identity security stamp. No authentication token is stored in localStorage.

Production key-ring protection, backup and rotation are described in DEPLOYMENT.md. Test account recovery on your own instance before onboarding real customers. Platform-administrator bootstrap is not implemented yet.

Primary references checked 2026-11-07: [ASP.NET external authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/social/), [aspnet-contrib providers](https://github.com/aspnet-contrib/AspNet.Security.OAuth.Providers), [GitHub authorization and PKCE](https://docs.github.com/en/apps/oauth-apps/building-oauth-apps/authorizing-oauth-apps), [Discord discovery](https://discord.com/.well-known/oauth-authorization-server), [Apple OIDC discovery](https://appleid.apple.com/.well-known/openid-configuration).
