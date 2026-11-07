export const guides: Record<
  string,
  {
    title: string;
    intro: string;
    steps: string[];
    link?: string;
    linkLabel?: string;
  }
> = {
  godaddy: {
    title: "A GoDaddy domain, hosted anywhere",
    intro:
      "Buying your domain from GoDaddy does not mean GoDaddy hosts your website. Start with the company where you edit or publish your site.",
    steps: [
      "Find your website host in your welcome email, hosting bill or website builder.",
      "If the host includes HTTPS, follow its custom-domain instructions. You usually do not need to buy a certificate from your registrar.",
      "If OpenCert asks for a TXT record, open the DNS manager for the authoritative nameservers. That may be GoDaddy or another provider.",
      "Copy the exact record name and value. Some DNS managers add your domain automatically: enter only _acme-challenge when appropriate.",
      "Keep other TXT values. Save, allow time for DNS propagation, then return to OpenCert and verify.",
    ],
    link: "https://www.godaddy.com/help/add-a-txt-record-19232",
    linkLabel: "GoDaddy TXT record guide",
  },
  cloudflare: {
    title: "Cloudflare DNS and HTTPS",
    intro:
      "Cloudflare edge certificates and your origin server certificate protect different connections.",
    steps: [
      "Check which domain uses Cloudflare nameservers.",
      "Use Cloudflare’s SSL/TLS overview to inspect edge and origin settings. Prefer Full (strict) with a valid origin certificate.",
      "For manual ACME verification, add a TXT record using the exact name and value in OpenCert.",
      "Do not delete existing TXT values when another certificate uses the same record.",
      "After issuance, verify the origin separately if a Cloudflare proxy serves the public website.",
    ],
    link: "https://developers.cloudflare.com/ssl/",
    linkLabel: "Cloudflare SSL/TLS documentation",
  },
  render: {
    title: "Free HTTPS on Render",
    intro:
      "Render manages certificates for supported custom domains. Check its built-in HTTPS before requesting a separate certificate.",
    steps: [
      "Open your Render service and its custom-domain settings.",
      "Add the hostname you want visitors to use.",
      "Follow Render’s current DNS instructions at your DNS provider.",
      "Wait for Render to verify the domain and provision HTTPS.",
      "Return to OpenCert and check the live website. Render remains responsible for renewal.",
    ],
    link: "https://render.com/docs/custom-domains",
    linkLabel: "Render custom domain guide",
  },
  vercel: {
    title: "Free HTTPS on Vercel",
    intro:
      "Vercel can provision and renew certificates for properly configured project domains.",
    steps: [
      "Open the project’s Domains settings in Vercel.",
      "Add your website name and apply the DNS records Vercel provides.",
      "Resolve any domain verification or DNS warnings in Vercel.",
      "Wait until Vercel reports the domain ready, then check HTTPS in OpenCert.",
    ],
    link: "https://vercel.com/docs/domains/working-with-ssl",
    linkLabel: "Vercel certificate documentation",
  },
  netlify: {
    title: "Free HTTPS on Netlify",
    intro:
      "Start with Netlify’s managed certificates when your site is hosted there.",
    steps: [
      "Open your site’s Domain management settings.",
      "Add the custom domain and follow the DNS instructions.",
      "Inspect the HTTPS section and resolve any DNS verification errors.",
      "When the certificate is active, check your live website in OpenCert.",
    ],
    link: "https://docs.netlify.com/manage/domains/secure-domains-with-https/https-ssl/",
    linkLabel: "Netlify HTTPS documentation",
  },
  cpanel: {
    title: "WordPress and cPanel",
    intro:
      "Many cPanel hosts include AutoSSL. Your hosting company decides whether it is enabled.",
    steps: [
      "Sign into the cPanel account supplied by your web host.",
      "Look for SSL/TLS Status or AutoSSL.",
      "Check the website names and ask the host to enable its included certificate service if needed.",
      "If managed HTTPS is unavailable, generate the private key and CSR in cPanel. Upload only the CSR to OpenCert.",
      "Install the issued certificate and chain in cPanel, then verify HTTPS. Manual installation requires manual renewal follow-up.",
    ],
    link: "https://docs.cpanel.net/cpanel/security/ssl-tls-status/",
    linkLabel: "cPanel SSL/TLS Status",
  },
  nginx: {
    title: "Install on Nginx",
    intro:
      "A server administrator should handle private keys, permissions and reloads.",
    steps: [
      "Generate a CSR locally with OpenCert Agent. Keep private-key.pem on your server.",
      "Submit request.pem in Advanced Tools and complete domain verification.",
      "Save the returned full chain alongside the matching private key, with restricted file access.",
      "Set ssl_certificate and ssl_certificate_key in the approved Nginx site configuration.",
      "Run nginx -t locally before an administrator-approved reload. Preserve a working rollback copy.",
      "Check public HTTPS in OpenCert. This manual workflow does not enable automatic renewal.",
    ],
    link: "https://nginx.org/en/docs/http/configuring_https_servers.html",
    linkLabel: "Nginx HTTPS documentation",
  },
  apache: {
    title: "Install on Apache",
    intro:
      "Use your host’s managed certificate service first. Manual server changes require an administrator.",
    steps: [
      "Generate a CSR and private key on the server.",
      "Request the certificate using the CSR and complete DNS verification.",
      "Configure the HTTPS virtual host with SSLCertificateFile and SSLCertificateKeyFile.",
      "Test the Apache configuration before reloading it. Keep the previous certificate available for rollback.",
      "Verify live HTTPS and plan renewal and installation.",
    ],
    link: "https://httpd.apache.org/docs/2.4/ssl/ssl_howto.html",
    linkLabel: "Apache TLS guide",
  },
  iis: {
    title: "Install on IIS",
    intro:
      "Use IIS or a Windows certificate-management tool to keep the private key in the Windows certificate store.",
    steps: [
      "Create a certificate request on the Windows server.",
      "Submit that CSR in OpenCert and complete DNS verification.",
      "Complete the certificate request in IIS using the returned certificate.",
      "Bind the certificate to the correct HTTPS website and hostname.",
      "Check the public website. Plan renewal before its actual expiration date.",
    ],
    link: "https://learn.microsoft.com/en-us/iis/manage/configuring-security/how-to-set-up-ssl-on-iis",
    linkLabel: "Microsoft IIS TLS guide",
  },
  docker: {
    title: "HTTPS with Docker",
    intro: "A reverse proxy usually handles HTTPS for containerized websites.",
    steps: [
      "Identify the public reverse proxy, such as Caddy, Nginx or Traefik.",
      "For Caddy and other proxies with built-in ACME, use their managed HTTPS workflow.",
      "For manual Nginx certificates, mount a persistent certificate directory read-only into the serving container.",
      "Keep the private key outside images, Git and build logs.",
      "Validate the configuration, reload the proxy and verify live HTTPS.",
    ],
    link: "https://caddyserver.com/docs/automatic-https",
    linkLabel: "Caddy automatic HTTPS",
  },
};
export const articles: Record<
  string,
  { title: string; intro: string; sections: [string, string][] }
> = {
  features: {
    title: "Everything starts with a clear next step.",
    intro:
      "OpenCert brings website checks, guided setup and certificate requests into one understandable workspace.",
    sections: [
      [
        "Website checks",
        "See whether your public website presents a trusted certificate, when it expires and when it was last checked. An unavailable check never becomes a green success.",
      ],
      [
        "Guided setup",
        "Start with your host’s existing free HTTPS. For supported manual setups, use a CSR so your private key stays on your server.",
      ],
      [
        "Account security",
        "Email sign-in, authenticator enrollment, recovery codes and revocable sessions protect access to your workspace.",
      ],
      [
        "Honest automation status",
        "Issuance, installation and renewal are tracked separately. Manual certificate requests still require installation and renewal action.",
      ],
    ],
  },
  "how-it-works": {
    title: "Your website. A little more peace of mind.",
    intro:
      "We explain the next step, without expecting you to know the technical terms.",
    sections: [
      [
        "01 · Tell us your website",
        "Enter the address visitors use. We check public DNS and HTTPS to understand the current setup.",
      ],
      [
        "02 · Follow your setup guide",
        "Your host may already include free HTTPS. If you need a separate certificate, verify domain control and keep your private key local.",
      ],
      [
        "03 · Check that it works",
        "After installation, check the live site. Understand who renews the certificate and what you still need to do.",
      ],
    ],
  },
  faq: {
    title: "A few good questions.",
    intro: "Website security, explained in everyday language.",
    sections: [
      [
        "Is it really free?",
        "OpenCert software is free and open source. Public certificate authorities such as Let’s Encrypt issue certificates without a certificate fee. Domain registration, hosting and optional infrastructure can still cost money.",
      ],
      [
        "Is SSL different from TLS?",
        "TLS is the current protocol used for HTTPS. “SSL certificate” is a common name for a certificate used with TLS.",
      ],
      [
        "Do I need to buy a certificate from my registrar?",
        "Often, no. Your web host may include managed HTTPS even if your domain was bought elsewhere.",
      ],
      [
        "What is a private key?",
        "It is the secret your server uses to prove its identity. Keep it on your server. OpenCert’s manual workflow accepts a CSR, which contains public information, instead.",
      ],
      [
        "What happens when a certificate expires?",
        "Browsers can show warnings and visitors may be unable to access your website safely. Renew and install the replacement before the expiry date.",
      ],
      [
        "Does a certificate make my whole website secure?",
        "HTTPS protects the connection and helps identify the server. It does not fix malicious content, weak passwords or vulnerable website software.",
      ],
    ],
  },
  security: {
    title: "Security needs evidence.",
    intro:
      "OpenCert separates what has been observed from what still needs verification.",
    sections: [
      [
        "Private keys stay with you",
        "The hosted application accepts certificate requests and returns public certificates. Keep certificate private keys on customer-controlled infrastructure.",
      ],
      [
        "Explicit checks",
        "Adding a website does not prove ownership. ACME domain-control validation is required for issuance. An issued certificate does not prove installation.",
      ],
      [
        "Release status",
        "This is an early development release. Production integrations and independent security review are not yet complete. Do not treat automated tests as a security audit.",
      ],
    ],
  },
  "open-source": {
    title: "Open by design.",
    intro: "Free software for a web that works for everyone.",
    sections: [
      [
        "Apache 2.0",
        "The project is distributed under Apache License 2.0, subject to the repository license and dependency notices. There are no paid feature tiers, advertisements or mandatory analytics.",
      ],
      [
        "Self-hosting",
        "Run the React frontend, .NET API and worker with PostgreSQL. The repository includes configuration, deployment notes and a verification report.",
      ],
    ],
  },
  contributing: {
    title: "Help make HTTPS easier.",
    intro:
      "Good contributions include clearer instructions as well as reliable code.",
    sections: [
      [
        "Before you contribute",
        "Read CONTRIBUTING.md, the architecture decisions and threat model in the repository. Run relevant tests and describe the actual behavior you verified.",
      ],
      [
        "Security-sensitive changes",
        "Changes to authentication, tenant access, cryptography, DNS or agents need focused security review. Never include credentials or customer keys in issues.",
      ],
    ],
  },
  privacy: {
    title: "Privacy and your data",
    intro:
      "Deployment policy draft — the service operator must review and publish its final privacy notice before accepting public users.",
    sections: [
      [
        "Necessary information",
        "Accounts store an email address, authentication records, organization memberships, website names, certificate metadata and security activity. Customer certificate private keys are not accepted.",
      ],
      [
        "External services",
        "Configured deployments may use Neon for database hosting, Resend for email, Render for hosting and a certificate authority for issuance. Public certificate hostnames are normally published in Certificate Transparency logs.",
      ],
      [
        "Data controls",
        "The operator must publish its identity, contact address, retention schedule, deletion procedure and applicable rights. These are not configured for this development instance.",
      ],
    ],
  },
  terms: {
    title: "Terms of service",
    intro:
      "Deployment policy draft — final operator terms and legal review are required before public launch.",
    sections: [
      [
        "Authorized use",
        "Manage only websites and servers you are authorized to administer. You must accept the certificate authority’s subscriber agreement for issuance.",
      ],
      [
        "Responsibilities",
        "Keep private keys secret, check live deployment and maintain a renewal plan. OpenCert software does not guarantee uninterrupted certificate issuance or hosting availability.",
      ],
      [
        "Software license",
        "The repository’s Apache 2.0 license governs the software. Hosted-service terms must identify the operator and be reviewed separately.",
      ],
    ],
  },
  "security-disclosure": {
    title: "Report a security issue safely.",
    intro:
      "Never publish credentials, private keys or an exploit against another person’s website.",
    sections: [
      [
        "Private reporting",
        "Use the repository’s private security advisory channel when enabled. The operator must configure SECURITY.md and a monitored private reporting address before production.",
      ],
      [
        "Useful details",
        "Include the affected version, reproducible steps in a test environment, impact and suggested mitigation. Do not collect another user’s data.",
      ],
    ],
  },
  contact: {
    title: "Need a hand?",
    intro: "Start with the guide for your hosting provider.",
    sections: [
      [
        "Setup help",
        "The Help Center explains common setups and how to check whether your host already includes free HTTPS.",
      ],
      [
        "Project feedback",
        "Use the project repository for reproducible software issues. Never post passwords, API tokens, private keys or recovery codes.",
      ],
      [
        "Hosted support",
        "A service support address has not been configured for this development instance.",
      ],
    ],
  },
};
