# Security and vulnerability reporting

This development build is not independently audited and is not approved for unattended production certificate management. See VERIFICATION.md for unverified boundaries.

Do not put secrets, private keys, login codes, session cookies, DNS credentials or customer data in public issues. Use GitHub's **Report a vulnerability** option in the repository Security tab if available. If private reporting is unavailable, request a private reporting channel without disclosing exploit details or sensitive material. Operators must publish and test a private security contact before opening their instance to real customers.

A useful private report includes the affected version, a minimal reproduction using your own test account, the observed security boundary violation, and expected behavior. No bounty program or response-time guarantee is currently offered.

When a credential leaks, revoke it at its issuer. Rotate affected sessions, API/DNS credentials and ACME account keys as appropriate. Data Protection key compromise can expose encrypted database secrets and cookies; rotate the external wrapping key, invalidate sessions and affected credentials, and investigate backups. Merely re-encrypting the same secrets is insufficient after compromise.
