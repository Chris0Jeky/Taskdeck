# Security Policy

Taskdeck is a local-first execution workspace in active pre-1.0 development. We take security reports seriously and appreciate responsible disclosure from the community.

## Supported Versions

Taskdeck has not yet shipped a stable release. The latest commit on the `main` branch is the only version guaranteed to receive security fixes. The latest tagged pre-1.0 release may be reviewed on a best-effort basis, but support for that tag is not guaranteed. Older tags, forks, archived branches, and other prerelease builds are not supported.

| Version | Supported |
|---------|-----------|
| Latest `main`                 | Yes (guaranteed) |
| Latest tagged pre-1.0 release | Best-effort only (not guaranteed) |
| Older tags, forks, archived branches, other prerelease builds | No |

Once a 1.0 release ships, this table will be updated to reflect a formal supported-version window.

## Reporting a Vulnerability

Please report suspected vulnerabilities privately. Do **not** open a public GitHub issue, discussion, or pull request that describes the vulnerability.

Preferred channel:

- **GitHub private vulnerability reporting** — use the [Report a vulnerability](https://github.com/Chris0Jeky/Taskdeck/security/advisories/new) button on the repository Security tab. This is the authoritative channel, is encrypted in transit and at rest, and is the only path we actively monitor today.

Fallback channel (not yet active):

- **Email** — `security@taskdeck.dev` is reserved for future use but **is not yet monitored**. Do not use it for time-sensitive reports. Until this address is announced as live in this document, please use GitHub private vulnerability reporting instead. This section will be updated when the mailbox is active.

We do not currently publish a PGP key. If your report includes sensitive payloads, GitHub's private advisory channel is sufficient (TLS in transit, encrypted at rest); use pseudonymized data if you are uncomfortable sharing raw captures.

When reporting, please include:

- A description of the issue and its potential impact
- Steps to reproduce (proof-of-concept code, affected endpoint, request payload, etc.)
- The commit SHA or build you tested against
- Any suggested mitigation, if known

## Response Timeline

These targets are best-effort for a pre-1.0 project maintained by a small team.

| Stage              | Target                                 |
|--------------------|----------------------------------------|
| Acknowledgment     | Within 48 hours of receipt             |
| Initial assessment | Within 7 calendar days of acknowledgment |
| Fix or mitigation  | Prioritized by severity; no fixed SLA  |
| Public disclosure  | Coordinated with the reporter once a fix or mitigation is available |

We will keep the reporter informed of progress and credit you in the advisory unless you prefer to remain anonymous.

## Scope

In scope:

- Source code under this repository (backend, frontend, CLI, scripts)
- Official container images built from `deploy/`
- Default configuration and documented deployment guidance

Out of scope:

- Social engineering of maintainers, contributors, or users
- Physical attacks against developer hardware
- Denial-of-service findings that require sustained traffic or resource exhaustion without a novel amplification vector
- Vulnerabilities in third-party services Taskdeck integrates with (report those to the upstream vendor)
- Issues that require an already-compromised host, root access, or a malicious OS-level user
- Missing security-hardening best practices without a demonstrated exploit (for example, lack of a specific header on a non-sensitive endpoint)
- Automated scanner output without a working proof-of-concept

## What We Do Not Offer

- We do **not** currently run a paid bug bounty program.
- We cannot guarantee 24/7 response times. Taskdeck is maintained by a small team.
- We do not provide CVE assignment directly; we will coordinate with GitHub Security Advisories and, where appropriate, MITRE.

## Safe Harbor

We will not pursue legal action against researchers who:

- Make a good-faith effort to avoid privacy violations, data destruction, and service disruption
- Only interact with systems and accounts they own or have explicit permission to test
- Give us reasonable time to respond before any public disclosure
- Do not exploit findings beyond what is necessary to demonstrate the vulnerability

## Related Documents

- [`docs/security/SECURITY_OWASP_BASELINE.md`](docs/security/SECURITY_OWASP_BASELINE.md) — baseline hardening posture
- [`docs/security/SECURITY_DEPENDENCY_VULNERABILITY_POLICY.md`](docs/security/SECURITY_DEPENDENCY_VULNERABILITY_POLICY.md) — dependency vulnerability management policy
- [`docs/security/SECRETS_MANAGEMENT_BASELINE.md`](docs/security/SECRETS_MANAGEMENT_BASELINE.md) — secrets handling
