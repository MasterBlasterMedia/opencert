# Contributing

OpenCert is under active development. Review the architecture, threat model and verification matrix before changing authentication, tenant queries, ACME, DNS or agent code. Open an issue describing the user problem before a large design change.

Keep beginner-facing instructions concrete. Never label a website protected from an issued certificate alone, infer domain ownership from public DNS, store customer certificate private keys centrally, or silently enable production ACME.

Run the relevant checks in the README. Security changes need behavior-level tests, including failure cases. Use disposable databases and test identities. Do not send test mail to real people or run DNS mutations against an unrelated zone. Include migration and rollback implications in a pull request.

Contributions are made under Apache-2.0. Identify copied code and its license. Include dependency lockfile changes and check advisories when adding packages. Reviewers should reject embedded secrets, captured session tokens, real recovery codes, private keys and unredacted logs.

Be respectful and specific in discussions. Harassment, threats and publishing private information are not acceptable. Maintainers may remove harmful content and restrict participation. No uptime, support response or security-audit guarantee is implied by this development repository.
