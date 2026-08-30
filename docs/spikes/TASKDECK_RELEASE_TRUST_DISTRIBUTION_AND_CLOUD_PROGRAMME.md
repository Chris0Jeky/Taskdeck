# Taskdeck Release Trust, Distribution, Cloud, and Student Benefits Programme

**Prepared for:** the Taskdeck repository agent/coordinator  
**Repository:** `Chris0Jeky/Taskdeck`  
**Research date:** 26 August 2026  
**Purpose:** reconcile the current repository with a Windows-first release-trust programme, bounded macOS and Linux distribution work, a safe private-cloud path, and a deliberate use of GitHub Student Developer Pack benefits.

> This is an execution brief, not repository authority. Live repository state, `AGENTS.md`, `docs/STATUS.md`, `.codex/memories/00_ACTIVE.md`, accepted ADRs, the ProjectV2 queue, and explicit maintainer instructions outrank this document.

---

## 0. Agent mandate

Use this document to **reconcile, document, and seed work**, not to create a second strategy universe.

### Required operating sequence

1. Read, in the repository-prescribed order:
   - `docs/STATUS.md`
   - `AGENTS.md`
   - `.codex/memories/00_ACTIVE.md`
   - `.codex/README.md`
   - `docs/REVIVAL_PLAN.md`
   - `docs/IMPLEMENTATION_MASTERPLAN.md`
   - `docs/GOLDEN_PRINCIPLES.md`
   - `docs/ISSUE_EXECUTION_GUIDE.md`
   - `docs/GITHUB_PROJECT_AUTOMATION.md`
   - `docs/ops/GITHUB_LABEL_TAXONOMY.md`
   - `OUTSTANDING_TASKS.md`
   - relevant release, deployment, security, and packaging docs.
2. Refresh the live repository, releases, workflows, open issues, milestones, ProjectV2 state, open PRs, CI, review threads, branches, and worktrees. Do not rely on the 26 August snapshot below when live state differs.
3. Reconcile the existing issues listed in this brief before creating anything new.
4. Prefer updating and splitting existing trackers over seeding duplicates.
5. Respect the normal weekly limit of **five new issues**, the **four-item Now cap**, the **eight-item Next cap**, and priority-field synchronisation. The current 26 August snapshot already has a full Now queue. Unless live state or explicit owner direction changes, newly seeded work stays `Pending` or `Blocked`.
6. Create no purchase, legal identity, registrar, Apple, Microsoft, cloud, or Student Pack account on behalf of the maintainer. Those are `human-action` items. Never request or expose signing keys, API keys, recovery codes, personal student identifiers, payment details, certificate files, or private account evidence in public issues.
7. Keep issue prose concise and human. Put the full research and option analysis in one maintained programme document. Put implementation truth in existing authoritative docs after the relevant work ships.
8. Do not make code signing, a hosted URL, or an app-store listing sound like a security guarantee. Identity, artifact integrity, reputation, malware scanning, operational security, and application behaviour are separate trust surfaces.
9. Do not silently promote a private two-person deployment into a public SaaS plan. Proposed ADR-0061 and issue `#1772` own that boundary.
10. Finish with:
    - a reconciliation report;
    - updated existing issues;
    - no more than the authorised first-wave new issues;
    - dependency and milestone mapping;
    - ProjectV2 priority/status parity;
    - one documentation PR, if repository authority requires it;
    - a human-action list in `OUTSTANDING_TASKS.md`;
    - exact commands, checks, and links in the handoff.

### Expected repository-level outputs

Prefer these minimal documentation changes rather than many new documents:

- **One new programme document**, suggested path: `docs/ops/RELEASE_TRUST_AND_DISTRIBUTION.md`.
- **One sanitised external-services register**, suggested path: `docs/ops/EXTERNAL_SERVICES_REGISTER.md`, containing no personal account details.
- Updates to existing release, Windows quick-start, cloud deployment, release checklist, status, implementation plan, and decision-index documents only where repository truth or sequencing actually changes.
- Human-only tasks in `OUTSTANDING_TASKS.md`.

The maintainer's detailed Student Pack redemption ledger should remain private. The public repository may record vendor purpose, dependency class, expiry risk, and exit plan, but not student-account identity, billing data, redemption codes, or personal renewal information.

---

## 1. Executive recommendation

### 1.1 Direction

Taskdeck should adopt a **Windows-first trust and distribution programme**, because Windows is already the only released desktop platform and the current public release is an unsigned portable ZIP. macOS and Linux should be deliberately staged behind that path rather than presented as nominally supported platforms before their runtime, packaging, upgrade, and clean-machine behaviour are proven.

The recommended order is:

1. **Stabilise publisher and product identity.** Decide who publishes Taskdeck, which legal identity owns certificates/accounts, and whether the product name/domain is sufficiently clear for durable Store IDs, package IDs, domains, and certificate infrastructure.
2. **Choose and provision a Windows signing route.** Prefer Microsoft Artifact Signing when the chosen organisation is eligible. Apply to SignPath Foundation in parallel as a zero-cost open-source bridge/fallback. Treat a traditional OV certificate as the paid fallback. Do not use a self-signed certificate for public distribution.
3. **Integrate fail-closed Authenticode signing and timestamping into the existing release workflow.** Sign before packaging, hashing, and attestation. Verify every expected signable. Preserve current immutable-source, checksum, untouched-archive acceptance, and provenance controls.
4. **Add a user-grade signed installer and coherent metadata.** Keep the portable ZIP, but make it the advanced option rather than the normal installation experience.
5. **Add standard SBOMs and GitHub artifact attestations.** Keep Taskdeck's current custom provenance file as a readable supplement, not the only provenance mechanism.
6. **Run clean-machine Windows trust tests.** Verify signature validity, installation, upgrade, uninstall, Mark-of-the-Web behaviour, Defender/SmartScreen observations, and false-positive escalation. Never instruct users to disable security controls.
7. **Complete the trusted private cloud proof.** Preserve the one-instance SQLite boundary, private access, invite-only registration, application-consistent backup, connector-key backup, restore drill, budget guardrails, and SignalR/reconnect proof. This is not a SaaS launch.
8. **Then expand distribution.** Microsoft Store and winget, Linux archives/attestations, macOS feasibility, and eventually notarised macOS distribution.

### 1.2 Default option choices

| Decision | Recommended default | Reason |
| --- | --- | --- |
| Public publisher | A stable legal organisation, probably DeliveraSoft Ltd if it legally owns the product and passes provider validation | Durable identity across Taskdeck and future software; avoids tying production trust to a personal student account |
| Windows signing | Microsoft Artifact Signing Basic if eligible; SignPath Foundation application in parallel | Strong public-trust identity and CI integration; SignPath gives a credible free open-source route if company eligibility is delayed |
| Windows installer | Signed MSIX or WiX-based installer after a short packaging decision | Normal install/uninstall lifecycle and Store/enterprise readiness; retain portable ZIP |
| Windows Store | Company account after publisher/name decision | Microsoft currently waives new developer registration fees; Store signs Store-delivered packages but does not solve direct GitHub ZIP trust |
| macOS | Defer paid membership until a real x64/arm64 candidate exists; prepare company/D-U-N-S/domain prerequisites now | Avoid paying before the product has a proven macOS lifecycle; preserve a professional organisation identity |
| Linux | Tested x64/arm64 archives plus checksums, GitHub attestations, and signed container first | Zero direct signing cost and low packaging complexity; package-manager work follows demand |
| Private cloud | Preserve Render as the implementation default because configuration and issues already exist; compare Railway only if the decision is genuinely open | Avoid platform churn; Render matches current repository assets and `#1777` |
| Student Pack cloud | Use Azure for labs, clean VMs, and experiments; do not use Heroku for current SQLite production | Azure Student credit cannot fund Artifact Signing; Heroku's ephemeral filesystem is incompatible with Taskdeck's SQLite persistence |
| Observability | Sentry first; do not wire Sentry, Datadog, New Relic, and Honeybadger simultaneously | Taskdeck already exposes Sentry configuration and one focused signal stack is easier to operate honestly |
| Secrets | 1Password for human-held secrets; optionally Doppler for deployment configuration after an exit/ownership review | Separates human custody from runtime injection and avoids committing secrets |
| Domain | Claim only after a bounded brand/domain/publisher decision | Free first-year domains create renewal and identity commitments; the exact `TaskDeck` name already has unrelated software usage |

### 1.3 One important newly surfaced dependency: brand and naming clearance

The exact `TaskDeck` name is already used by unrelated software, including an active `taskdeck.app` service, a current Visual Studio Code extension, and a historical macOS application. This is **not a legal conclusion that Taskdeck cannot use the name**. It is evidence that the repository should not lock in a canonical domain, app-store identity, reverse-DNS package ID, signing display strategy, or marketing claim without a bounded name/domain review.

The name review should answer:

- whether the current name remains acceptable for open-source and commercial use;
- whether a distinguishing descriptor is needed;
- which canonical domain is available and defensible;
- whether the publisher should be more prominent than the product name;
- whether Store/package IDs should be based on the company domain rather than a speculative product domain;
- which existing product and extension names create practical confusion;
- what a solicitor or trademark professional should review before a commercial launch.

A safe interim pattern is a company-owned canonical domain and a Taskdeck subdomain or product page, rather than selecting a free TLD simply because the Student Pack offers it.

---

## 2. Reconciled Taskdeck state as of 26 August 2026

### 2.1 Release and roadmap state

- Taskdeck v0.1.2 shipped on 25 August 2026.
- The repository's active context currently targets:
  - v0.2 final: **1 September 2026**;
  - v0.3 release candidate: **4 September 2026**;
  - v0.3 final: **8 or 9 September 2026**.
- The exact live queue must be refreshed. The 26 August snapshot reports four `Now` issues and five `Next` issues, so there is no normal first-wave capacity for new work in `Now`.
- New product surface remains governed by the revival plan and accepted ADR authority. Trust and packaging work should be admitted as hardening/distribution work, not as an excuse to expand product scope.

### 2.2 What the Windows release already does well

The existing `.github/workflows/release-desktop.yml` already provides a strong foundation:

- resolves one release tag and exact commit;
- validates untrusted dispatch input;
- pins downstream checkouts to the resolved commit;
- fails closed on source mismatch;
- builds the Vue frontend and self-contained .NET 8 Windows x64 executable;
- stamps a product version;
- stages licences and reviewed quick-start material;
- tests the untouched ZIP;
- generates SHA-256 checksums;
- creates a readable provenance asset with tag, commit, workflow, and run;
- verifies the release tag still points to the built commit before publication;
- publishes through GitHub Actions.

The public v0.1.2 release therefore already has meaningful **integrity and provenance**. The material gap is **publisher identity**: no Authenticode signing stage currently exists.

The current primary release artifact is a roughly 54 MB `win-x64` ZIP containing `Taskdeck.Api.exe`. That is technically valid, but the API-oriented filename and extract-and-run flow are not an ideal end-user desktop experience.

### 2.3 Current cloud boundary

The repository already has:

- one combined frontend/API production container;
- Render and Railway deployment guidance;
- a Render Blueprint with one instance and a persistent `/app/data` disk;
- authentication and registration modes;
- board-access roles;
- per-board SignalR;
- SQLite persistence and backup guidance;
- proposed ADR-0061 separating:
  1. trusted shared instance;
  2. dependable small-team alpha;
  3. managed public SaaS.

The current supported posture remains local-first, self-hosted, one application instance, and SQLite. Static hosting proves only the backend-less demo, not collaboration. A private shared URL does not prove tenancy, billing, account recovery, abuse control, production support, or SaaS readiness.

### 2.4 Existing issues that already own part of this programme

| Issue | Current role | Required treatment |
| --- | --- | --- |
| `#1167` Code-sign + notarize desktop release binaries | Existing umbrella for Windows and macOS signing; currently stale, broad, and `Priority V` | Re-scope as the programme tracker. Update v0.1.0 language, split Windows/macOS/Linux children, add current identity options and human-action boundary |
| `#550` Domain, logo, social handles | Existing brand asset issue | Expand narrowly to include canonical domain, publisher identity alignment, collision evidence, renewal/ownership, and Store/package-ID dependency. Do not turn it into a full trademark opinion |
| `#1772` Private shared two-person instance | Owns ADR-0061 and trusted-instance proof | Keep as the cloud parent. Add access boundary, cost owner, backup/restore objectives, session/MFA treatment, budget alerts, provider decision, and Student Pack non-dependency |
| `#1777` Migrate private instance to Render | Existing Render migration issue | Update live pricing assumptions, deploy-after-CI, off-platform backup, restore proof, access boundary, data/key migration, and spend guardrails |
| `#1504` Real GitHub production environment protection | Owns external deployment approval boundary | Link from signing and cloud CI work. Do not claim an environment name is protected until required reviewers/rules are verified live |
| `#1310` Open-beta launch kit | Owns backend-less hosted demo and launch claims | Link trust/download page, signed-installer status, and truthful supported-platform matrix. Do not block the static demo on cloud collaboration |
| `#1644` Browser token storage before hosted multi-user | Hosted-session security boundary | Resolve before broad hosted use, or record a narrow trusted-two-person risk decision. Do not silently accept localStorage as public-service posture |
| `#1653` Encrypt MFA TOTP secrets at rest | Production MFA blocker | Keep MFA disabled/unadvertised on hosted production until fixed or explicitly fail-closed |
| `#1992` Deployment routing residuals | v0.3 deploy/PWA route truth | Include relevant route and proxy proof in hosted-instance acceptance, particularly if split topology is used |
| `#2010` Reminders/email epic | Future email service consumer | Testmail and mail-catcher benefits are relevant later, but this is not a reason to accelerate outbound email ahead of its ADR |
| `#2012` Commercial/licensing decision | Public managed-service boundary | Must be resolved before public hosted-commercial commitments |
| `#1947` Dogfooding/release tracker | Current release sequencing | Link only where trust work becomes a release gate; do not create a replacement release tracker |

### 2.5 Reconciliation rule

The first pass should update existing issue bodies/comments and create a dependency map. It should not create a new issue for every bullet in this document. The immediate issue-seeding wave below deliberately contains no more than five new issues.

---

## 3. Trust model: what each control does and does not prove

| Trust surface | Proves | Does not prove |
| --- | --- | --- |
| Authenticode / Developer ID | A trusted identity signed these exact bytes | The code is bug-free, harmless, or reputable |
| Trusted timestamp | The signature was made while its certificate was valid | The file has never been vulnerable |
| SmartScreen/Gatekeeper reputation | The ecosystem recognises the app/publisher or notarisation state | A permanent exemption from future warnings or detections |
| SHA-256 checksum | Downloaded bytes match the published digest | Who published the digest |
| GitHub artifact attestation | Artifact provenance is bound to a repository/workflow identity | The workflow itself is perfectly secure or the software is safe |
| SBOM | Declared dependency/material inventory | Absence of undeclared components or vulnerabilities |
| Store/package-manager distribution | Platform-controlled delivery, identity, and update channel | Complete application security |
| Antivirus scan | No currently detected malicious pattern | Future safety or absence of false negatives |
| Cloud HTTPS | Transport encryption to the configured service | Correct application authorization, backup, tenancy, or incident readiness |
| Private access boundary | Limits who can reach the service | Correct board-level permissions or safe data handling |
| Backup | Recoverable copy exists | That restoration actually works |
| Restore drill | Recovery path has been exercised | A full disaster-recovery or public-SaaS operating model |

Taskdeck's release language and documentation should preserve these distinctions.

---

## 4. Publisher and account ownership model

Before any external account is created, classify each asset by its durable owner.

| Asset | Recommended owner | Notes |
| --- | --- | --- |
| Windows public signing identity | Legal publisher organisation | Avoid a student/personal identity if Taskdeck is a company-owned product |
| Microsoft Store developer account | Same legal publisher organisation | Microsoft account type cannot simply be converted later; decide first |
| Apple Developer account | Organisation if company-published | Requires legal verification and D-U-N-S; defer payment until candidate exists |
| Canonical domain | Company or an account with an explicit transfer plan | Record registrar, renewal price, lock/transfer rules, and recovery contacts |
| Cloud production service | Company-controlled billing/workspace where possible | Student benefits are useful for experiments, but production must have an exit path |
| GitHub Student Pack credits | Maintainer's personal student account | Treat as temporary, non-transferable leverage rather than permanent architecture |
| Human secrets | 1Password or equivalent company vault | Never commit or paste into issues |
| Runtime secrets | Provider secret store or Doppler after exit review | Access by workload identity where possible |
| Release OIDC identity | Dedicated Entra/GitHub workload identity | Least privilege; no personal interactive credential in CI |
| Public observability account | Product/company workspace | Data retention and privacy boundary must be documented |

### Human decision required

Record one explicit publisher decision:

- **Option A:** DeliveraSoft Ltd publishes Taskdeck.
- **Option B:** Cristian Tcaci publishes Taskdeck personally.
- **Option C:** SignPath Foundation is the Windows publisher for qualifying open-source releases while a future organisation identity matures.
- **Option D:** another legal owner identified by the maintainer.

Do not infer Option A merely because the company exists. Establish product ownership, legal name, registered address, validation readiness, domain control, account recovery, and budget first.

---

## 5. Windows programme, highest priority

### 5.1 Current problem

Windows sees the current downloaded executable as code without a publicly trusted publisher signature. GitHub identity, source history, checksums, and custom provenance do not populate the Windows publisher field.

The programme must address three separate outcomes:

1. replace `Unknown publisher` with a verified publisher identity;
2. allow publisher reputation to accumulate consistently across versions;
3. reduce avoidable antivirus/heuristic triggers and provide a proper false-positive response path.

No plan should promise that signing instantly removes every SmartScreen warning. New files and new publishers can still lack reputation.

### 5.2 Signing route decision tree

#### Route A: Microsoft Artifact Signing Basic, preferred when eligible

Current published terms indicate:

- Basic tier: **US$9.99/month**;
- includes **5,000 signatures/month**;
- public-trust signing and timestamping integration;
- intended for modern CI integration;
- identity validation may take roughly **1 to 20 business days or longer**;
- free, trial, and sponsored Azure subscriptions are not supported for the service;
- a paid Azure subscription such as pay-as-you-go is required.

A material eligibility risk must be checked before making this the only plan. Microsoft's current MSIX guidance states that public-trust organisation validation is available in the UK and certain other regions but may require a verifiable organisation history, including tax history of at least three years. Individual public-trust eligibility is more geographically restricted. If DeliveraSoft Ltd is too new for validation, the agent must record this as an external blocker rather than designing CI around an unavailable account.

**Use this route when:** the chosen legal publisher is eligible, can pass validation, accepts the monthly cost, and wants a direct Microsoft-managed signing identity.

#### Route B: SignPath Foundation, recommended parallel application / bridge

SignPath Foundation offers free code signing for qualifying open-source projects. Important trade-offs:

- the Foundation is the certificate publisher/holder;
- the project must be accepted and comply with Foundation policy;
- release approval and governance are more constrained than a publisher-owned certificate;
- it is a credible route for direct-download trust while a young organisation builds eligibility;
- it may be preferable to spending hundreds per year on a traditional certificate during beta.

**Use this route when:** Taskdeck qualifies, the publisher-display and governance model are acceptable, or Artifact Signing eligibility is delayed.

#### Route C: traditional organisation-validation certificate

A traditional publicly trusted OV code-signing certificate remains a fallback. Current Microsoft guidance gives a rough annual market range of several hundred US dollars. Operational costs include identity validation, key protection, renewal, CI integration, and possible hardware/cloud key custody.

**Use this route when:** direct-download organisation identity is required and neither Artifact Signing nor SignPath is viable.

#### Route D: Microsoft Store signing only

A Store package can be signed through Store distribution, and Microsoft currently waives registration fees for newly onboarded individual and company developers. This is useful but incomplete:

- it covers Store-delivered packages;
- it does not give the GitHub ZIP a direct-download publisher signature;
- it creates Store policy, packaging, identity, and submission work;
- the account type should be chosen correctly before onboarding.

**Use this route as:** an additional distribution channel, not the only answer while GitHub/direct downloads remain supported.

### 5.3 Recommended Windows account and CI architecture

```text
GitHub release tag
        |
        v
Resolve exact immutable commit
        |
        v
Build frontend + .NET publish
        |
        v
Stage exact distribution tree
        |
        v
Sign every expected PE / installer
  - Microsoft Artifact Signing via OIDC, or approved alternative
  - trusted timestamp
        |
        v
Verify signatures fail-closed
  - expected signable manifest
  - subject / issuer / timestamp / digest algorithm
  - no unsigned unexpected executable
        |
        v
Package ZIP / MSIX / installer
        |
        v
Generate SHA-256 + SBOM
        |
        v
Run untouched-package acceptance
        |
        v
Generate GitHub artifact attestations
        |
        v
Publish draft release
        |
        v
Final tag, asset, signature, checksum, and provenance verification
        |
        v
Publish release
```

### 5.4 CI requirements

- Sign after final binary mutation and before packaging, hashing, and attestation.
- Sign all expected Windows PE files that users execute or load, not merely the outer installer.
- Timestamp every signature using the signing service's trusted timestamp path.
- Maintain an explicit expected-signables manifest. The workflow must fail when:
  - an expected binary is unsigned;
  - a signature is invalid;
  - a timestamp is missing or invalid;
  - the publisher identity differs from the approved identity;
  - an unexpected executable appears in the release tree.
- Verify with native Windows tooling, including `signtool verify /pa /all /v` and a PowerShell `Get-AuthenticodeSignature` assertion.
- Use GitHub OIDC/workload federation and a least-privilege signer role where the provider supports it. Avoid long-lived Azure client secrets or exportable PFX files in ordinary repository secrets.
- Put production signing behind a real protected GitHub Environment, linking `#1504`. A named environment without verified reviewers/rules is not a gate.
- Restrict production signing to protected release tags and reviewed workflow files.
- Keep an unsigned rehearsal mode that exercises all release logic except production signing/publication.
- Include signing provider/profile, approved publisher subject, certificate thumbprint or equivalent identity, timestamp result, workflow run, tag, and commit in the release evidence without exposing credentials.
- Preserve current immutable tag-to-commit and untouched-ZIP tests.
- Add standard artifact attestations without deleting the readable custom provenance file.

### 5.5 Installer and product metadata

The current `Taskdeck.Api.exe` name exposes implementation architecture rather than product identity. The packaging programme should determine whether to:

- rename the user-facing executable to `Taskdeck.exe`;
- provide a small signed launcher while retaining internal API assembly names;
- ship MSIX;
- ship a WiX/MSI or signed bootstrapper;
- support both installer and portable ZIP.

Required user-facing properties:

- publisher, product, description, copyright, original filename, file version, and product version agree;
- Start menu entry and application icon;
- normal uninstall entry;
- documented user-data location and preservation rules;
- upgrade and downgrade contract;
- no silent deletion of `%LOCALAPPDATA%\Taskdeck` data;
- clear port/browser lifecycle if Taskdeck remains a local web application;
- predictable startup, single-instance behaviour, logs, and shutdown;
- install path does not require unsafe write permissions;
- no routine instruction to bypass Defender, SmartScreen, or execution policy;
- portable ZIP remains available for advanced users.

### 5.6 SmartScreen and Defender validation

A signature and a clean antivirus result are related but independent.

Create a clean-machine acceptance matrix covering at least:

- Windows 11 current supported build, clean local account;
- Edge and Chrome download paths with Mark-of-the-Web preserved;
- direct GitHub release download and canonical download page;
- signature details visible before execution;
- portable ZIP extraction and installer path;
- fresh install, first launch, restart, upgrade, uninstall, and data preservation;
- Defender scan and observed SmartScreen state;
- VirusTotal or multi-engine submission only under an explicit privacy/release policy, because submissions may redistribute binaries;
- Microsoft file-submission workflow for false positives;
- exact evidence: version, hash, signature identity, OS build, source URL, observed prompt, screenshots/logs, and disposition.

The documentation must say that reputation can take time. Never claim that code signing guarantees warning-free execution.

### 5.7 Microsoft Store and winget

Sequence after a stable publisher identity and signed installer contract:

1. create the correct company or individual Store account;
2. reserve the product identity only after the naming review;
3. run package feasibility and policy checks;
4. publish a private/hidden flight where available;
5. validate install, update, uninstall, data retention, privacy links, and support information;
6. publish publicly only after claims match shipped behaviour;
7. create winget manifests for the signed direct installer, with update automation and hash verification.

Do not let Store work block the first properly signed direct release.

---

## 6. macOS programme

### 6.1 Goal and boundary

macOS support is not a v0.2 or v0.3 release blocker. The correct goal is a tested, maintainable macOS distribution, not a nominal `dotnet publish` artifact.

Apple's normal outside-Mac-App-Store trust path is:

1. Apple Developer Program membership;
2. Developer ID Application certificate;
3. correctly signed application and nested code;
4. hardened runtime and entitlements;
5. notarisation through `notarytool`;
6. stapled ticket where the container format supports it;
7. Gatekeeper verification on a clean machine.

The Apple Developer Program currently costs **US$99/year**, subject to local pricing/tax. Organisation enrolment requires legal verification and a D-U-N-S number. These are human-owned prerequisites.

### 6.2 Recommended sequence

#### Phase M0: prerequisites, no subscription purchase

- decide organisation versus individual Apple publisher;
- verify company legal name and D-U-N-S readiness;
- secure a company-controlled domain and role email;
- decide macOS bundle identifier using the stable company domain;
- define supported CPU architectures and minimum macOS version;
- identify access to Intel and Apple Silicon test hardware or CI.

#### Phase M1: unsigned engineering feasibility

- build `osx-x64` and `osx-arm64` candidates;
- prove startup, local browser launch, port selection, data path, SQLite lifecycle, shutdown, logging, upgrade, and uninstall/manual removal semantics;
- decide universal binary versus two architecture-specific packages;
- create a real `.app` bundle and icon/metadata;
- identify native nested files and entitlements;
- run clean macOS tests without presenting the artifact as generally supported.

#### Phase M2: enrolment and signing

Only after M1 is credible:

- enrol the chosen publisher in the Apple Developer Program;
- create protected certificate/notarisation credentials;
- prefer App Store Connect API key or approved CI credential model over a personal Apple ID password;
- sign every nested executable/library in the correct order;
- enable hardened runtime;
- notarise and staple the DMG or PKG;
- verify with:
  - `codesign --verify --deep --strict --verbose=2`;
  - `spctl --assess --type execute --verbose=4`;
  - `xcrun stapler validate`;
  - clean-machine download and launch.

### 6.3 macOS distribution issue boundaries

Keep separate:

- runtime/package feasibility;
- human enrolment and credential provisioning;
- signing/notarisation CI;
- user lifecycle/upgrade testing;
- Mac App Store work, which is not required for Developer ID distribution.

Do not seed all of these into `Now`. The first macOS issue should remain `Pending` until Windows signing and current v0.3 work no longer compete for the same release pipeline.

---

## 7. Linux programme

### 7.1 Near-term target

Linux does not have one universal equivalent of Windows publisher display or Apple notarisation. Trust comes from signed/attested artifacts, trusted repositories, reproducible provenance, and package-manager identity.

The first supported Linux release should provide:

- tested `linux-x64` and, if practical, `linux-arm64` artifacts;
- a clear runtime and browser-launch contract;
- a documented data/config/log location following Linux conventions;
- SHA-256 checksums;
- GitHub artifact attestations;
- SBOMs;
- signed/attested GHCR container images;
- clean Ubuntu/Debian-family acceptance tests;
- accurate unsupported-distro boundaries.

### 7.2 Signing and attestations

Recommended order:

1. use GitHub artifact attestations for release archives and container provenance;
2. retain Taskdeck's readable provenance asset;
3. use Sigstore/cosign keyless signing for GHCR or broader OCI interoperability when justified;
4. record GitHub OIDC identity and transparency-log evidence;
5. verify attestations in CI and document user verification commands.

Direct cost is normally zero beyond CI usage.

### 7.3 Packaging channels

Do not create `.deb`, RPM, AppImage, Flatpak, Snap, Homebrew/Linuxbrew, and distro repositories simultaneously.

Recommended progression:

1. tested tar archives and container;
2. one demand-led package, likely `.deb` for the first Ubuntu/Debian audience;
3. Flatpak only after desktop lifecycle, sandbox/portal behaviour, local-server model, filesystem access, and update semantics are understood;
4. Flathub submission after a stable domain and reverse-DNS app ID exist;
5. additional formats only when users or maintainers justify them.

Flathub verification is tied to domain/app-ID ownership, reinforcing the need to resolve domain and publisher identity before package IDs become durable.

---

## 8. Cloud and hosting programme

### 8.1 Preserve the product boundary

The programme must continue to distinguish:

| Stage | What it proves | What it does not prove |
| --- | --- | --- |
| Backend-less static demo | UI/interaction preview with fake/local data | API, auth, persistence, collaboration, backups |
| Trusted shared instance | A few known users can safely use one persistent instance | Public tenancy, billing, abuse resistance, SLA, broad support |
| Dependable small-team alpha | Regular use with stronger recovery, concurrency, diagnostics, and operations | Public SaaS readiness |
| Managed public SaaS | Separate accepted product/operating model | Not authorised by a private deployment |

### 8.2 Recommended current hosting choice

Taskdeck already has Render configuration, a cloud deployment guide, and `#1777`. The default recommendation is therefore:

- **keep Render as the primary implementation path** unless a live provider comparison shows a material blocker;
- use one paid web service and one persistent disk;
- keep `numInstances=1` while SQLite is authoritative;
- deploy the combined frontend/API image;
- run over HTTPS with verified SignalR/WebSocket behaviour;
- keep registration `InviteOnly` during onboarding and `Closed` only after all intended accounts exist;
- add an independent private-access layer or provider access control. Registration mode is not a network perimeter;
- deploy only from an exact tested image/version after CI and a real protected environment gate;
- do not equate provider disk snapshots with an application-consistent database recovery plan.

Current verified Render list pricing starts at roughly **US$7/month** for a Starter web service. Persistent disk, egress, tax, and any workspace fees must be verified at purchase time. The current repository Blueprint requests a 1 GB disk and auto-deploys from `main`; the programme should reconsider direct auto-deploy in favour of deploy-after-CI and protected approval.

### 8.3 Railway alternative

Railway is a credible alternative when the hosting decision is still genuinely open:

- current Hobby plan has a **US$5 monthly minimum/included usage**;
- additional resource use is metered;
- volume backups have documented daily/weekly/monthly retention tiers;
- one-volume/one-instance SQLite deployment is feasible;
- final cost depends on RAM, CPU, volume, egress, and runtime duration.

Use Railway only after a bounded comparison of:

- persistent-volume guarantees and restore mechanics;
- WebSocket/SignalR behaviour;
- deploy gating;
- service sleep/cold-start policy;
- operational access;
- data export/migration;
- actual measured monthly cost.

Do not churn from the existing Render path merely because another platform has a lower headline minimum.

### 8.4 Why Heroku is not the current Taskdeck host

The Student Pack currently offers **US$13/month of Heroku credit for 24 months**, a nominal value of **US$312**. However, Heroku's dyno filesystem is ephemeral. A local SQLite database on that filesystem can be lost during dyno cycling or redeployment. Current Taskdeck is explicitly a persistent single-file SQLite application.

Therefore:

- do not deploy current Taskdeck to Heroku with local SQLite;
- do not redesign Taskdeck around Heroku solely to consume a benefit;
- reserve the offer for a stateless demo/API, a separate portfolio service, or a future PostgreSQL-backed experiment;
- activate it only when a real 24-month use case exists, because the benefit clock is valuable.

### 8.5 Azure Student credit

The Student Pack currently includes **US$100 Azure credit** and selected services without requiring a credit card. Recommended uses:

- clean Windows VMs for install/signature/SmartScreen testing;
- short-lived Linux/macOS-adjacent build experiments where supported;
- Azure/DevOps learning and portfolio evidence;
- a disposable staging environment;
- optional encrypted off-site backup experiment;
- identity/OIDC learning.

Do not plan to pay for Microsoft Artifact Signing with Azure for Students. Artifact Signing excludes free/trial/sponsored subscriptions and requires a supported paid subscription.

Do not make the production Taskdeck service permanently dependent on student credit without a migration and billing-owner plan.

### 8.6 Cloud safety acceptance

The trusted-instance programme should not close until all of the following are evidenced:

#### Identity and access

- provider and legal/account owner recorded;
- private access boundary chosen and tested;
- registration kept `InviteOnly` during onboarding;
- intended users created with separate accounts;
- board access and destructive actions tested across roles;
- anonymous and uninvited access denied;
- admin/operator path documented;
- browser token risk from `#1644` either fixed or explicitly accepted only for the bounded trusted deployment;
- MFA remains disabled/unadvertised until `#1653` is resolved.

#### Deployment

- exact image digest, tag, commit, and configuration posture recorded;
- deploy follows required CI and protected environment approval;
- one application instance enforced;
- persistent volume mounted at `/app/data`;
- health/readiness checks pass;
- SignalR/WebSocket and reconnect tested;
- database-authoritative reload verified after disconnect/reconnect;
- route/PWA/proxy truth from `#1992` addressed for the chosen topology.

#### Secrets and LLM cost

- strong JWT and connector-encryption keys generated outside the repository;
- keys stored in approved human/runtime stores;
- no key appears in logs or evidence;
- connector-encryption key backed up separately with recovery instructions;
- live LLM use off by default until owner, provider, egress, quota, and payer are recorded;
- BYO versus operator-funded key decision recorded;
- cost ceiling and kill switch tested.

#### Backup and recovery

- application-consistent SQLite backup, not a raw live-file copy;
- encrypted off-platform backup target;
- connector-encryption key preserved separately;
- daily schedule or explicitly approved RPO;
- retention and deletion policy;
- backup integrity checks;
- one clean restore into a fresh service/volume;
- restored users, boards, connector decryption, and application startup verified;
- measured RPO and RTO recorded;
- rollback path for deployment/migration;
- provider snapshots treated as a supplement, not the only recovery mechanism.

Suggested private-beta targets for maintainer ratification:

- **RPO:** no more than 24 hours;
- **RTO:** no more than 4 hours for a maintainer-run restore;
- **availability claim:** none beyond best-effort beta unless operating evidence supports it.

#### Cost and operations

- monthly budget ceiling;
- provider spending alerts where available;
- disk and egress monitoring;
- log retention and privacy boundary;
- Sentry or one selected error/availability stack, disabled until configured truthfully;
- incident contact and shutdown procedure;
- export/decommission procedure;
- Student Pack expiry does not make data inaccessible.

### 8.7 Managed SaaS remains later

Do not seed implementation for public managed SaaS until the repository has an accepted decision covering at least:

- tenancy and isolation;
- PostgreSQL or another approved data architecture;
- billing/entitlements;
- account recovery and verified transactional email;
- abuse controls and rate limits;
- privacy, data processing, retention, deletion, and legal operations;
- observability and incident response;
- backups, PITR, disaster recovery, and support;
- commercial/licensing decision from `#2012`;
- evidence that private/shared use has retention worth scaling.

---
## 9. GitHub Student Developer Pack strategy

### 9.1 Operating principle

Do **not** activate every benefit immediately.

Student Pack benefits differ in four ways that matter:

1. some start a fixed clock on redemption;
2. some remain active only while GitHub verifies student status;
3. some convert to paid renewal or require a payment method;
4. most are personal benefits and may not transfer cleanly to a company or collaborator.

For every claimed offer, record:

- offer and current terms;
- intended project and concrete use;
- personal versus company account owner;
- redemption date;
- expiry or student-status dependency;
- ordinary renewal price and currency;
- whether a card/auto-renewal is enabled;
- transferability and domain/account lock period;
- data export and exit path;
- production dependency level;
- secrets location;
- review reminders at 60, 30, and 7 days before expiry.

Timed benefits should normally be activated just before the first real use. Domains are the exception only when a naming decision is complete and loss of the desired name is a genuine risk.

### 9.2 Highest-value claims for the maintainer now

| Offer | Recommendation | Taskdeck use | Broader value |
| --- | --- | --- | --- |
| GitHub Pro | Ensure active | Repository/private-project capacity and GitHub features | Core development estate |
| GitHub Copilot Student | Claim/use if not already active | Development and review support, subject to repo agent policy | High daily productivity value |
| GitHub Codespaces Pro quota | Claim/use deliberately | Reproducible clean Linux development and review environments | Useful for portfolio and collaborator onboarding |
| JetBrains student licence | Claim | Rider/WebStorm/DataGrip or equivalent Taskdeck workflow | Direct fit with the maintainer's tooling |
| 1Password Developer Tools | Claim | Human custody for recovery codes, registrar, cloud, Apple, Microsoft, signing-account secrets | High-value security baseline across all projects |
| Canonical domain benefit | Decide first, then claim through the best registrar/TLD offer | Trust/download page, publisher validation, package IDs, email | Brand and company infrastructure |
| Sentry student benefit | Claim when Taskdeck cloud proof starts | One focused error/diagnostic path, already anticipated by Taskdeck config | Reusable across DeliveraSoft projects |
| POEditor | Claim when active localisation work resumes | Taskdeck en/it/es catalogue workflow and contributor translation | Useful evidence of disciplined i18n |
| Testmail | Claim when `#2010` or another email feature enters implementation | Deterministic test inboxes, aliases, and email E2E | Useful for auth/email projects |
| Requestly | Claim when doing hosted/SignalR/failure testing | Request/response modification, failure simulation, debugging | Broad API and frontend debugging value |
| LambdaTest or BrowserStack | Choose one first | Clean cross-browser/manual or mobile-automation evidence | Public QA/portfolio proof |
| Termius | Claim if managing real remote hosts | SSH/session organisation for cloud instance | Useful for consultancy/DevOps work |
| CodeScene or Codecov | Run a bounded trial, not an automatic new hard gate | Maintainability/hotspot or coverage insight | Portfolio-grade engineering evidence |
| Azure for Students | Claim when a concrete lab is ready | Clean Windows VMs, staging, cloud/security learning | High cloud-learning and CV value |

### 9.3 Domain offers: use carefully

The screenshots and current Pack catalogue include:

- Name.com: one free domain for one year from selected TLDs, including options such as `.software`, `.app`, and `.dev` depending on current availability;
- Namecheap: one free `.me` domain for one year plus a one-year SSL offer;
- `.TECH`: one free `.tech` domain for one year.

#### Recommendation

- Do not select `taskdeck.app`; it is already active and unrelated.
- Check all desired names live at redemption time. Do not infer availability from an offer page.
- Prefer a canonical company-owned domain or a durable product domain with acceptable renewal pricing.
- A free first year is a small part of the decision. Record years 2-5 renewal cost, transfer rules, WHOIS/privacy, DNS controls, DNSSEC, account recovery, and registrar support.
- Managed hosts and GitHub Pages already provide TLS. The Namecheap SSL benefit is not a reason to choose a registrar or manually manage certificates for those services.
- A `.me` domain is better suited to a personal portfolio than a company software publisher unless deliberately chosen as part of the brand.
- A `.tech` domain can be useful as a campaign or redirect, but should not become canonical merely because year one is free.
- Register using an account that can be transferred to company control, and document the process before the student benefit expires.

#### Suggested domain allocation

- personal portfolio: a `.me` benefit, if a strong personal name is available and renewal is acceptable;
- Taskdeck/company: Name.com selected TLD only after the naming review;
- `.tech`: optional redirect/campaign, not required;
- trust/download page: canonical company/product domain, not a GitHub raw asset URL alone.

### 9.4 Cloud and backend offers

| Offer | Current value/term | Taskdeck recommendation | Better use elsewhere |
| --- | --- | --- | --- |
| Azure for Students | US$100 credit plus selected free services; current offer says no card required | Labs, clean VMs, staging, backup experiment; **not Artifact Signing payment** | Azure architecture, Entra/OIDC, Windows administration, cloud portfolio |
| Heroku | US$13/month for 24 months | Do not host current SQLite Taskdeck | Stateless APIs, bots, demos, or future PostgreSQL experiment |
| Appwrite Education | Two projects with Pro-equivalent limits while eligible; screenshot values it around US$40/month | Do not replace Taskdeck's .NET backend/auth/storage | Fast prototypes, client demos, mobile/web experiments |
| Clerk | Pro while student | Do not replace Taskdeck auth solely to consume it | New frontend-first prototypes needing managed identity |
| MongoDB | Pack benefit varies by current catalogue | Not a Taskdeck migration target without an accepted architecture decision | Separate data/API experiments and certification learning |
| LocalStack | Student access to local AWS emulation | Not needed for current Render path | Strong AWS portfolio/CI work; offline S3/SQS/Lambda integration tests |
| Camber | Current compute/data-science benefit | No direct Taskdeck need | ML/data experiments or thesis follow-up work |
| Deepnote | Student data-notebook benefit | No production Taskdeck dependency | Reproducible analysis notebooks and public research evidence |
| CARTO | Geospatial tooling benefit | No Taskdeck fit | GIS portfolio work, relevant to prior GE Smallworld experience |
| Stripe | First US$1,000 in processed revenue with waived fees under current Pack offer | Claim only after payment/commercial path exists and eligibility terms are confirmed | DeliveraSoft/client prototypes or later Taskdeck billing |
| Pageclip | Static-form backend | Not needed for Taskdeck app | Simple portfolio/contact forms |
| Zyte | Web-scraping benefit | No core Taskdeck dependency | Research/data-collection prototypes with legal/robots review |

#### Appwrite and Clerk opinion

Both are valuable, but neither should be inserted into Taskdeck. Taskdeck already owns authentication, registration, roles, SQLite persistence, API policy, and a local-first thesis. Replacing those systems would create migration, lock-in, privacy, testing, and product-truth costs without solving the immediate release-trust problem.

Use Appwrite or Clerk for a separate rapid prototype where managed backend/auth is the point of the experiment.

### 9.5 Observability, security, and secrets offers

| Offer | Recommendation | Notes |
| --- | --- | --- |
| 1Password | Claim now | Primary human-secret and recovery-code vault. Use separate vaults/items for personal, company, and product identities |
| Doppler | Evaluate, then claim when deployment configuration is real | Strong runtime-secret option, but record export, pricing after benefit, ownership, and provider outage fallback |
| Sentry | First choice for Taskdeck cloud error diagnostics | Enable only with truthful privacy/telemetry disclosure, data scrubbing, environment separation, and bounded retention |
| Datadog Pro, 10 servers for 2 years | High financial value, but probably excessive for one Taskdeck container | Best used as a deliberate observability learning lab or consultancy capability; do not duplicate Sentry/New Relic in production |
| New Relic | Alternative broad observability stack | Pick instead of, not alongside, Datadog for a bounded evaluation |
| Honeybadger | Alternative error/uptime tool | Useful for a small service, but overlaps Sentry |
| Astra Security | Evaluate only if current terms and scope fit | External scanning never replaces application threat modelling or authenticated tests |
| Simple Analytics | Good candidate for the public landing/docs site | Privacy-oriented high-level traffic metrics; keep product telemetry separate and opt-in/truthful |
| Dashlane | Skip if 1Password is chosen | Avoid maintaining two secret-vault systems |

#### Observability recommendation

For Taskdeck:

1. use local structured logs and health endpoints as the base;
2. select Sentry for bounded error diagnostics when hosting begins;
3. add uptime/availability checks only when the service has a real operator response path;
4. use Datadog or New Relic as a separate learning/evaluation exercise, not a second mandatory product stack;
5. document what leaves the instance, retention, redaction, user identifiers, and the kill switch.

### 9.6 Testing and code-quality offers

| Offer | Recommendation | Intended use |
| --- | --- | --- |
| LambdaTest Live, one year | Good just-in-time claim for manual cross-browser acceptance | Windows/macOS/Linux browser matrix, install/download page, PWA, responsive capture/review |
| BrowserStack mobile automation | Choose if mobile/responsive automation is the current priority | Real-device/browser automation; do not duplicate LambdaTest without a gap |
| Requestly | High practical value | Simulate API errors, headers, latency, route failures, and hosted proxy behaviour |
| Testmail | High value when email work starts | Account verification, unsubscribe, bounce-ish/test scenarios, deterministic aliases |
| Codecov | Use only if it improves current coverage reporting | Do not replace test quality with a coverage percentage or introduce a brittle hard gate without evidence |
| CodeScene | Good bounded architecture/hotspot analysis | Compare findings against Taskdeck's own metrics and known high-churn areas |
| DeepScan | Optional frontend static-analysis trial | Adopt rules only after false-positive and CI-cost review |
| Blackfire | Stack mismatch for current .NET/Vue application | Skip for Taskdeck; use only on PHP work |
| Travis CI | Skip for Taskdeck | GitHub Actions already provides a deeply integrated release/CI estate |
| Imgbot | Optional | Useful only if repository image weight is a measured issue; avoid noisy automated churn |

### 9.7 Development tools

| Offer | Recommendation | Notes |
| --- | --- | --- |
| JetBrains | Claim | Direct fit: Rider/WebStorm/DataGrip/PyCharm/IntelliJ depending on work |
| GitHub Codespaces | Use deliberately | Clean reproducibility, collaborator onboarding, and temporary Linux environment; monitor quota |
| GitKraken Student | Pick only if it improves workflow | Screenshot says six months then a large student discount; do not add alongside several paid Git GUIs by default |
| GitLens | Useful inside VS Code | Choose GitLens or GitKraken/Tower according to actual use, not all at once |
| Tower | Alternative premium Git client | Low need if current Git CLI/IDE workflow is effective |
| GitHub Desktop | Already free | Good for simple tasks, not a Pack decision |
| Termius | Claim if remote operations begin | Organise SSH hosts and team sharing carefully; no production secret in plain notes |
| Working Copy | Useful only for iOS/iPad Git work | Optional |
| Visual Studio Dev Essentials / VS tooling | Claim where it adds test or cloud benefits | Relevant for .NET/Windows build ecosystem |
| Xojo | Stack mismatch | Skip unless exploring cross-platform desktop development separately |
| ToDiagram | Optional | Useful for converting structured architecture/data to diagrams when it saves real time |

### 9.8 Design, localisation, and public presence

| Offer | Recommendation | Use |
| --- | --- | --- |
| POEditor | Claim when translation workflow is active | Taskdeck locale catalogue, reviewer roles, translation memory |
| IconScout, 60 premium icons/month for one year | Claim only with an asset/licence ledger | Brand/landing assets; verify source redistribution in open-source binaries |
| Icons8 | Similar optional value | Pick one primary asset source to reduce licence tracking |
| Polypane | Strong frontend testing/design tool | Responsive, accessibility, and layout checks; useful during public UI polish |
| Bootstrap Studio | Stack mismatch with Vue design system unless used for marketing pages | Optional for rapid static landing experiments |
| Visme | Optional | Presentations/marketing, not core product workflow |
| Notion | Claim if useful, but do not make it repository authority | Private product/business workspace; public engineering truth remains in Git |
| Microsoft 365 | Claim if current student entitlement adds value | Role email/docs/spreadsheets; company mail identity may need a company-owned paid tenant later |
| GitHub Pages | Use | Static docs, landing, trust/download page, or backend-less demo; not Taskdeck collaboration hosting |

#### Open-source asset rule

Any icon/font/template benefit used in Taskdeck must record:

- source and asset ID;
- exact licence at download time;
- whether modification is allowed;
- whether source redistribution is allowed;
- required attribution;
- whether the asset may ship in a GPL-3.0-only repository/binary;
- local reviewed copy of the licence where required.

A premium download entitlement does not automatically grant unrestricted redistribution in an open-source product.

### 9.9 Learning offers with high career value

The Pack includes or periodically includes substantial learning subscriptions. These are not Taskdeck dependencies, but they can yield high personal value:

- Frontend Masters: advanced JavaScript, web performance, accessibility, architecture;
- DataCamp / Deepnote: analytics, SQL, data engineering, ML;
- Educative, Interview Cake, AlgoExpert, Codédex, Scrimba: interview and structured skill development;
- Boot.dev: backend and computer-science practice;
- GoRails / SymfonyCasts: stack-specific, lower direct value unless taking on Ruby/PHP work;
- GitHub Campus Experts: community/leadership if still eligible and useful;
- Arduino/Adafruit hardware offers: embedded/IoT portfolio experiments.

Recommended focus for this user profile:

1. advanced systems/design and distributed-systems material;
2. cloud architecture and identity;
3. observability and security operations;
4. frontend accessibility/performance;
5. interview algorithms only as a bounded parallel track.

Do not activate overlapping course subscriptions simultaneously. Choose one learning objective per quarter and consume the benefit deeply.

### 9.10 Offers to defer or skip for Taskdeck

#### Defer until a concrete trigger

- Heroku: stateless/PostgreSQL project exists.
- Appwrite/Clerk: separate rapid prototype exists.
- Stripe: paid product/service path exists.
- Testmail: email implementation begins.
- Datadog/New Relic: observability learning plan exists.
- LocalStack: AWS integration/lab exists.
- LambdaTest/BrowserStack: defined acceptance matrix and owner exist.
- IconScout/Icons8: approved asset need and licence ledger exist.
- DevCycle/ConfigCat: real feature-flag requirement exists; choose one.
- POEditor: active localisation workflow and contributor plan exist.

#### Skip for current Taskdeck

- Travis CI, because GitHub Actions is already the authoritative CI/release platform.
- Blackfire, because Taskdeck is not a PHP application.
- multiple overlapping Git clients, observability platforms, vaults, or browser-test vendors.
- Namecheap SSL for managed TLS paths.
- backend/auth rewrites whose primary rationale is consuming Appwrite or Clerk credits.
- any provider whose benefit ends without a tested export/decommission path for production data.

### 9.11 Student benefit ledger template

Keep the detailed version private.

```yaml
benefit_id: vendor-offer-name
vendor: ""
offer_snapshot_date: 2026-08-26
offer_summary: ""
source_url: ""
intended_project: ""
concrete_use: ""
priority: claim-now | just-in-time | defer | skip
account_owner: personal | company | project-workspace
legal_owner: ""
claimed_on: null
expires_on: null
student_status_dependent: true
ordinary_renewal_price: unknown
auto_renew: unknown
payment_method_present: unknown
transferability: unknown
production_dependency: none | low | medium | high
data_export_path: ""
exit_deadline: null
secrets_location: ""
public_repo_safe_notes: ""
private_notes_location: ""
reminders:
  - 60-days-before
  - 30-days-before
  - 7-days-before
status: unclaimed
```

---

## 10. Cost model

All prices are research snapshots as of 26 August 2026. The human operator must verify checkout price, VAT/tax, currency conversion, plan limits, overages, renewal, and eligibility before purchase.

### 10.1 Direct trust/distribution costs

| Item | Working cost | Timing | Notes |
| --- | ---: | --- | --- |
| Microsoft Artifact Signing Basic | US$9.99/month | Start once eligibility/account path is confirmed | Includes 5,000 signatures/month under current published terms; paid Azure subscription required |
| SignPath Foundation | US$0 | Apply immediately if acceptable | Qualification, Foundation publisher identity, and approval/governance trade-offs |
| Traditional OV signing certificate | Roughly US$300-500/year market range | Fallback only | Verify exact CA, hardware/cloud key, renewal, and CI terms |
| New Microsoft Store developer account | US$0 under current onboarding programme | After publisher/name decision | Company versus individual account type matters |
| Apple Developer Program | US$99/year | Only after macOS candidate and identity prerequisites | Local pricing/tax may differ |
| GitHub artifact attestations / Sigstore keyless | Usually US$0 direct | Add during release hardening | CI usage and platform plan constraints still apply |
| Linux package publication | Usually US$0 direct | After supported artifact | Maintenance time is the main cost |
| Canonical domain | First year may be US$0 via Pack | After identity/name decision | Renewal is the material long-term cost |

### 10.2 Cloud costs

| Scenario | Working monthly base | Exclusions |
| --- | ---: | --- |
| Render Starter web service | US$7 | Persistent disk, egress, tax, workspace/add-ons |
| Railway Hobby | US$5 minimum/included usage | Metered compute, memory, storage, egress, tax |
| Heroku Student benefit | Up to US$13/month credit for 24 months | Not suitable for current local SQLite persistence |
| Azure Student | US$100 credit pool | Cannot pay for Artifact Signing; production costs after credit |

### 10.3 Recommended beta budget scenarios

#### Scenario A: free-signing bridge + private Render

- SignPath Foundation: US$0 if accepted;
- Render web service: US$7/month;
- domain year one: potentially US$0 via Pack;
- GitHub attestations: US$0 direct;
- Apple: deferred.

**Base:** at least **US$84/year**, before disk, egress, tax, backup storage, and renewal.

#### Scenario B: Artifact Signing + private Render

- Artifact Signing: US$9.99/month;
- Render web service: US$7/month.

**Verified base before disk/egress/tax:** **US$16.99/month**, or **US$203.88/year**.

A prior/public working estimate of roughly US$0.25/GB-month for a small Render disk would make a 1 GB configuration approximately US$206.88/year, but the operator must verify the current disk price in the dashboard before this is treated as a budget.

#### Scenario C: Scenario B plus macOS distribution

- Windows/cloud base: US$203.88/year before disk/egress/tax;
- Apple Developer Program: US$99/year.

**Base:** **US$302.88/year** before disk, egress, domain renewal, backup storage, tax, and currency conversion. If the 1 GB disk working estimate applies, the rough total becomes US$305.88/year.

#### Scenario D: traditional Windows certificate + cloud + Apple

This can exceed US$500-700/year before hosting extras. Use only when publisher-owned direct signing is required and lower-cost routes are unavailable.

### 10.4 Cost controls

- Record a monthly ceiling before deployment.
- Configure provider budget/spend alerts where available.
- Disable live LLM providers until payer and quota are explicit.
- Keep one instance while SQLite is used.
- Review resource use weekly during the first month.
- Avoid simultaneous paid observability stacks.
- Set renewal reminders for domain, Apple membership, certificate/signing plan, cloud service, and every timed Student Pack offer.
- Preserve export/decommission instructions before the first production dependency is created.

---

## 11. Proposed delivery timeline

This timeline respects the repository's current v0.2/v0.3 targets but does not assume external identity validation can be compressed to fit them.

### 26-29 August 2026: reconciliation and human decisions

Repository/agent work:

- refresh live state;
- update `#1167`, `#550`, `#1772`, and `#1777`;
- create the programme doc and sanitised external-service register;
- seed no more than five first-wave issues;
- link `#1504`, `#1644`, `#1653`, `#1992`, `#2012`, and `#1310`;
- define release trust gates and cloud Stage 1 gates;
- add human tasks without credentials.

Maintainer work:

- choose tentative publisher owner;
- confirm whether DeliveraSoft Ltd legally owns Taskdeck;
- establish company age/validation readiness;
- choose the Windows signing route application order;
- decide whether to apply to SignPath Foundation;
- decide canonical-domain review scope;
- decide cloud budget and account owner;
- claim no timed Pack offer without a concrete use.

### 30 August-1 September 2026: v0.2 remains narrow

- do not destabilise v0.2 to force external signing setup;
- land only low-risk release evidence work if it fits existing release scope, such as attestations or documentation that does not alter shipped claims;
- begin Artifact Signing/SignPath validation outside the release critical path;
- prototype signing in no-publish rehearsals only when credentials/provider are ready.

### 2-4 September 2026: v0.3 release-candidate opportunity

Ideal but not mandatory:

- signed Windows RC if identity validation is complete;
- otherwise unsigned RC explicitly labelled as such, with signing targeted to v0.3.x;
- private hosting rehearsal with synthetic data;
- backup/restore rehearsal;
- SignalR/reconnect and access-boundary proof;
- no public SaaS claim.

### 5-9 September 2026: v0.3 final decision

Ship signing in v0.3 only when:

- publisher identity is ratified;
- production signing succeeds through the protected path;
- every expected signable verifies;
- untouched archive/installer acceptance is green;
- docs and download claims agree;
- external validation is terminal, not pending.

Otherwise:

- preserve release quality;
- ship v0.3 with explicit unsigned/direct-download truth if that remains the accepted posture;
- target signing to the first v0.3.x maintenance release;
- do not invent evidence or weaken release gates to meet the date.

### 10-30 September 2026: distribution hardening

- signed installer decision and implementation;
- clean-machine SmartScreen/Defender matrix;
- false-positive runbook;
- Store company account and private package feasibility;
- winget preparation;
- Linux x64/arm64 archives, SBOMs, and attestations;
- Sentry/private-host observability boundary;
- first real private-instance restore drill;
- Student Pack claims that now have concrete owners.

### October-November 2026: second-platform maturity

- macOS x64/arm64 feasibility;
- Apple organisation enrolment only after candidate readiness;
- Developer ID signing/notarisation/stapling;
- one Linux package channel if demand exists;
- small-team alpha hardening only after trusted-instance evidence;
- re-evaluate cloud provider and costs from measured use.

### Later, evidence-gated

- public managed SaaS;
- PostgreSQL/multi-instance operation;
- billing and entitlements;
- transactional email/account recovery;
- broader stores and package formats;
- support/SLA claims.

---
## 12. Existing-issue reconciliation instructions

### 12.1 Update `#1167` as the programme tracker

**Do not close and replace it.** Rework it into a concise umbrella with child links.

Recommended tracker title:

> `[TRACKER] Release trust and supported distribution: Windows signing first, macOS notarisation, Linux attestations`

Recommended tracker changes:

- replace the stale v0.1.0 wording with current release state;
- state that v0.1.2 ships a tested Windows x64 ZIP with checksum and custom provenance, but no Authenticode signature;
- state Windows is the first priority;
- split platform implementation into child issues;
- distinguish signing identity, reputation, antivirus classification, installer UX, attestations, and app-store/package-manager distribution;
- record Artifact Signing, SignPath Foundation, traditional OV, and Store-only options;
- record brand/publisher identity as a dependency;
- link `#550`, `#1504`, the release workflow, and this programme document;
- retain `Priority V` if it remains a meta-tracker, while child issues carry delivery urgency;
- do not store certificates in a generic repository secret by default. Prefer provider-managed key custody and OIDC;
- remove the blanket statement that the agent needs a maintainer-held certificate in a repo secret;
- list human actions separately;
- link macOS and Linux deferred issues when seeded;
- define closure as supported-platform work being either delivered or explicitly parked with a truthful distribution matrix.

Suggested tracker checklist:

```markdown
## Windows, first priority
- [ ] Publisher/name/signing route decided
- [ ] Signing account/profile and protected CI identity provisioned
- [ ] Every Windows release signable is signed, timestamped, and verified fail-closed
- [ ] User-grade installer and portable ZIP contract delivered
- [ ] Clean-machine SmartScreen/Defender evidence and false-positive runbook delivered
- [ ] Store/winget decision delivered or explicitly parked

## Cross-platform supply chain
- [ ] Distribution SBOM and GitHub artifact attestations delivered
- [ ] Verification instructions tested from downloaded assets

## macOS
- [ ] Runtime/package feasibility completed
- [ ] Developer ID/notarisation route delivered or explicitly parked

## Linux
- [ ] Supported architectures and archive/container attestations delivered
- [ ] Package channel delivered or explicitly demand-gated

## Documentation truth
- [ ] Supported-platform matrix matches shipped artifacts
- [ ] Unsigned-workaround copy removed only from paths that are actually signed
- [ ] Human account/renewal tasks live in OUTSTANDING_TASKS.md
```

### 12.2 Update `#550` without turning it into a legal essay

Recommended title:

> `BRAND-01: Canonical domain, product identity, publisher alignment, logo, and handles`

Add acceptance criteria for:

- live availability and renewal comparison for candidate domains;
- evidence that `taskdeck.app` and other `TaskDeck` software usages exist;
- a bounded naming/confusion review;
- legal/trademark review trigger before commercial launch;
- canonical publisher legal name;
- company-domain email for signing/Store/Apple accounts;
- reverse-DNS package ID strategy;
- registrar owner, recovery, transfer, DNSSEC, and renewal owner;
- no Student Pack domain claim before the decision;
- no personal/student data in the issue.

Keep logo/social work separate in checkboxes so identity infrastructure can proceed without forcing a full marketing launch.

### 12.3 Update `#1772` as the single trusted-instance parent

Add or clarify:

- Render remains the current implementation default unless the maintainer reopens provider choice;
- self-host+tunnel versus direct-Render sequence remains an owner decision;
- network/private access boundary is required in addition to `InviteOnly`;
- exact operator, account owner, budget owner, and LLM payer;
- `#1644` treatment before hosted use;
- MFA remains disabled/unadvertised until `#1653`;
- deploy-after-CI and real protected environment linkage to `#1504`;
- RPO/RTO decision;
- application-consistent SQLite backup and separate connector-key backup;
- encrypted off-platform backup and clean restore drill;
- cost ceiling and alerts;
- Sentry/observability privacy boundary;
- Student Pack credits may reduce experiments but are not a permanent architecture dependency;
- no managed-SaaS claim.

Recommended Stage 1 closure evidence:

- image digest/tag/commit;
- redacted configuration posture;
- provider plan and monthly budget;
- access-control proof;
- two-user role matrix;
- SignalR/reconnect/database reload transcript;
- backup hash and encrypted storage location class, not credentials;
- restore transcript and measured RPO/RTO;
- rollback/decommission path;
- known risk acceptance.

### 12.4 Update `#1777` for a production-like Render migration

Add:

- verify current Render pricing and persistent-disk terms at execution time;
- do not rely on free service local storage;
- change or gate `autoDeploy: true` so production deployment follows exact-head CI and approval;
- link `#1504`;
- back up via SQLite's online backup mechanism/API rather than a raw live-file copy;
- back up the connector-encryption key separately;
- define data transfer into the Render volume using a documented maintenance window;
- verify file ownership and non-root runtime access;
- add private access boundary before inviting a collaborator;
- configure spend alerts/ceiling;
- verify SignalR/WebSocket, same-origin, CORS, health, restart, redeploy, and rollback;
- complete a restore drill after migration, not merely a pre-migration backup;
- record Student Pack benefits as optional external resources, not prerequisites.

### 12.5 Link rather than duplicate other issues

- `#1504`: production environment/reviewer protection for both signing and cloud deployment.
- `#1310`: static demo, trust/download page, truthful supported-platform matrix.
- `#1644`: hosted session design.
- `#1653`: MFA at-rest blocker.
- `#1992`: proxy/PWA route contract.
- `#2010`: later Testmail/email use.
- `#2012`: public managed-service/commercial boundary.
- `#1879`: shared-instance LLM key ownership and BYOK UX.
- current release tracker: only link signing as a release blocker after explicit milestone admission.

---

## 13. First-wave issue seed manifest

### 13.1 Capacity rule

This is the **maximum recommended first wave: five new issues**. Updating existing issues does not require creating more. Since the 26 August `Now` queue is full, default all new items to `Pending` or `Blocked`. Do not promote them until live WIP capacity and dependencies allow it.

```yaml
programme: release-trust-distribution-cloud
as_of: 2026-08-26
parent_tracker: 1167
existing_updates:
  - 1167
  - 550
  - 1772
  - 1777
linked_existing:
  - 1504
  - 1310
  - 1644
  - 1653
  - 1992
  - 2010
  - 2012
  - 1879
new_issue_wave_1:
  - key: TRUST-WIN-01
    title: Decide the stable publisher, product/domain identity, and Windows signing route
    priority: Priority II
    status: Pending
    milestone: none-until-owner-ratifies-release-gate
  - key: TRUST-WIN-02
    title: Provision the Windows signing identity and protected release-signing boundary
    priority: Priority II
    status: Blocked
    depends_on: TRUST-WIN-01
  - key: TRUST-WIN-03
    title: Authenticode-sign, timestamp, and fail-closed verify every Windows release signable
    priority: Priority II
    status: Blocked
    depends_on: TRUST-WIN-02
  - key: DIST-WIN-01
    title: Ship a user-grade signed Windows installer and coherent product metadata
    priority: Priority II
    status: Blocked
    depends_on: TRUST-WIN-03
  - key: SUPPLY-01
    title: Add distribution SBOMs, GitHub artifact attestations, and tested verification instructions
    priority: Priority II
    status: Pending
```

### 13.2 Issue TRUST-WIN-01

**Title**

> `[Decision][Packaging] Decide the stable publisher, product/domain identity, and Windows signing route`

**Recommended labels**

- `decision`
- `human-action`
- `security`
- `packaging`
- `strategy`
- `Priority II`

**Recommended milestone/status**

- no milestone until the maintainer decides whether Windows signing is a v0.3 gate;
- `Pending`, or `Blocked` if the Project model uses Blocked for human decisions.

**Body**

```markdown
## Problem

Taskdeck's Windows ZIP has checksums, untouched-archive acceptance, and release provenance, but its executable is not Authenticode-signed. Windows therefore cannot display a trusted public publisher identity. The product name/domain and durable legal publisher also affect Store identity, package IDs, macOS enrolment, and public trust pages.

The exact TaskDeck name has unrelated software usage, including an active taskdeck.app service, a current VS Code extension, and an older macOS app. This is collision evidence, not a legal conclusion.

## Decision scope

Record one durable decision covering:

1. who legally publishes and owns Taskdeck;
2. canonical company/product domain and role email strategy;
3. whether the current product name remains acceptable or needs differentiation;
4. Windows signing route:
   - Microsoft Artifact Signing Basic if the organisation is eligible;
   - SignPath Foundation as a free OSS route/bridge;
   - traditional OV certificate as fallback;
   - Store signing as an additional channel, not a direct-download replacement;
5. account owner, annual budget, renewal owner, and exit path;
6. whether signing is a v0.3 blocker or a v0.3.x target.

## Facts to verify live

- Microsoft's current organisation/region/history eligibility, including whether the chosen UK entity has sufficient verifiable history.
- SignPath Foundation qualification and publisher-display/governance terms.
- Current certificate and Store pricing.
- Live candidate-domain availability and years 2-5 renewal cost.
- Current package/Store name conflicts.

## Acceptance

- [ ] Legal publisher and product owner are explicitly named, or the unresolved owner is recorded as a blocker.
- [ ] Current-name/domain collision evidence is recorded with a bounded professional-review trigger.
- [ ] Canonical domain/email/package-ID direction is recorded without claiming an unavailable domain.
- [ ] One primary and one fallback Windows signing route are selected with cost and eligibility evidence.
- [ ] Direct-download and Store signing boundaries are stated correctly.
- [ ] Release target is explicit: v0.3, v0.3.x, or later.
- [ ] Human account/purchase actions are mirrored to OUTSTANDING_TASKS.md without credentials or personal identifiers.
- [ ] #1167, #550, #1504, and the release workflow are linked.

## Out of scope

- Buying or creating accounts.
- Legal/trademark conclusions by an agent.
- CI signing implementation.
- Store submission.
```

### 13.3 Issue TRUST-WIN-02

**Title**

> `[Security][CI][Human action] Provision the Windows signing identity and protected release-signing boundary`

**Recommended labels**

- `security`
- `ci`
- `packaging`
- `human-action`
- `hardening`
- `Priority II`

**Dependency**

- Depends on TRUST-WIN-01.
- Links `#1504`.

**Body**

```markdown
## Goal

Provision the selected public-trust Windows signing route and a least-privilege, auditable CI boundary without exposing a reusable private key or personal interactive credential to ordinary release jobs.

## Supported route shapes

- Microsoft Artifact Signing: paid supported Azure subscription, identity validation, certificate profile, Entra workload identity, GitHub OIDC, least-privilege signer role.
- SignPath Foundation: accepted project, approved policy/release process, CI integration.
- Traditional OV fallback: approved secure key custody and non-exportable/hardware/cloud signing path where possible.

The selected route comes from the parent decision. Do not implement several production routes simultaneously.

## Acceptance

- [ ] Provider/account/profile exists under the approved owner; human-owned evidence is recorded without secrets.
- [ ] Publisher subject/display identity matches the decision.
- [ ] Production signing uses workload identity/OIDC or another approved non-interactive least-privilege mechanism.
- [ ] No signing key, PFX password, student identity, recovery code, or payment detail is committed, logged, uploaded as an artifact, or pasted into an issue.
- [ ] A real protected GitHub Environment and reviewer/tag rules are verified live, not inferred from its name; coordinate #1504.
- [ ] Only approved protected release refs can reach production signing.
- [ ] Rehearsal/no-publish jobs cannot produce artifacts that look production-signed.
- [ ] Credential/profile rotation, revocation, expiry, renewal, and incident owner are documented.
- [ ] A harmless test binary is signed and independently verified on Windows before release workflow integration.
- [ ] OUTSTANDING_TASKS.md records remaining human actions.

## Out of scope

- Signing Taskdeck release artifacts in the production workflow.
- Installer design.
- Store submission.
```

### 13.4 Issue TRUST-WIN-03

**Title**

> `[Packaging][Security][CI] Authenticode-sign, timestamp, and fail-closed verify every Windows release signable`

**Recommended labels**

- `packaging`
- `security`
- `ci`
- `testing`
- `hardening`
- `Priority II`

Promote to `Priority I` and an active milestone only if the maintainer explicitly makes signed Windows releases a release gate.

**Dependencies**

- Depends on TRUST-WIN-02.
- Parent `#1167`.

**Body**

```markdown
## Problem

The release workflow produces a tested Windows x64 ZIP, SHA-256 digest, and custom provenance, but no Authenticode signature. Signing only one obvious file without proving the complete staged tree would leave silent gaps.

## Scope

Integrate the ratified signing provider into `.github/workflows/release-desktop.yml` after final binary staging and before packaging, checksums, SBOMs, attestations, untouched-archive tests, and publication.

## Acceptance

- [ ] The workflow owns an explicit expected-signables manifest for the staged Windows distribution.
- [ ] Every expected PE/installer is signed with the approved publisher and trusted timestamp.
- [ ] An expected unsigned binary, invalid signature, missing timestamp, wrong publisher, or unexpected executable fails the job before release creation.
- [ ] Verification runs on Windows with `signtool verify /pa /all /v` and an independent PowerShell Authenticode assertion.
- [ ] Production signing is reachable only through the protected identity/environment from TRUST-WIN-02.
- [ ] Rehearsal mode remains non-publishing and clearly distinguishable.
- [ ] Existing immutable source resolution, checkout pinning, licence checks, SHA-256 generation, untouched-ZIP acceptance, tag recheck, and resumable release publication remain intact.
- [ ] Release evidence records tag, commit, run, signing provider/profile class, publisher subject, signature/timestamp verification, and artifact digest without credentials.
- [ ] The downloaded ZIP is re-verified after extraction, not only before packaging.
- [ ] A synthetic negative test proves an unsigned or wrong-signed canary is rejected.
- [ ] Windows quick-start and release checklist stop saying "unknown publisher" only after exact published evidence exists.

## Non-claims

The issue does not claim immediate SmartScreen reputation or antivirus exemption.
```

### 13.5 Issue DIST-WIN-01

**Title**

> `[Packaging][UX] Ship a user-grade signed Windows installer and coherent product metadata`

**Recommended labels**

- `packaging`
- `ux`
- `hardening`
- `testing`
- `Priority II`

**Dependencies**

- Depends on TRUST-WIN-03.
- Coordinates with product naming decision.

**Body**

```markdown
## Problem

The primary Windows experience is currently download ZIP, extract it, find `Taskdeck.Api.exe`, and run a local web application. This is acceptable as a portable developer artifact but not the clearest default installation path.

## Scope

Choose and deliver one signed installer path, likely MSIX or WiX/MSI/bootstrapper, while retaining the portable ZIP. Do not create several installer technologies in parallel.

## Acceptance

- [ ] A short decision records the chosen installer, alternatives, Store/winget compatibility, update model, rollback, and data-preservation contract.
- [ ] User-facing executable/product name is `Taskdeck` rather than exposing `Api` as the product identity, either by renaming or an approved launcher boundary.
- [ ] Publisher, product, description, icon, copyright, original filename, file version, and product version agree.
- [ ] Installer and all nested executable artifacts are signed and timestamped through the release trust path.
- [ ] Start menu and uninstall entries are correct.
- [ ] Install, first run, restart, upgrade, failed upgrade/rollback, uninstall, and reinstall are tested on a clean supported Windows VM.
- [ ] `%LOCALAPPDATA%\Taskdeck` user data is preserved unless the user explicitly requests removal.
- [ ] Port/browser lifecycle, logs, shutdown, and single-instance behaviour are documented truthfully.
- [ ] Portable ZIP remains available and receives the same signature/integrity verification.
- [ ] Store and winget readiness are documented without claiming publication.

## Out of scope

- Microsoft Store public submission.
- Automatic updater beyond the chosen installer's bounded contract unless separately admitted.
```

### 13.6 Issue SUPPLY-01

**Title**

> `[Security][CI] Add distribution SBOMs, GitHub artifact attestations, and tested verification instructions`

**Recommended labels**

- `security`
- `ci`
- `packaging`
- `docs`
- `testing`
- `Priority II`

**Dependency**

- Can begin independently, but final attestation order must agree with TRUST-WIN-03 and DIST-WIN-01.

**Body**

```markdown
## Goal

Complement Taskdeck's readable checksum/provenance assets with standard, independently verifiable distribution evidence for Windows archives/installers, future Linux/macOS artifacts, and GHCR images.

## Scope

- Generate a distribution-level SBOM in one standard format selected after tool evaluation (SPDX or CycloneDX).
- Generate GitHub artifact attestations for final release assets and container images.
- Preserve the existing human-readable provenance file and SHA-256 assets.
- Publish tested user/maintainer verification commands.

## Acceptance

- [ ] SBOM describes the final distribution materials rather than only one project file.
- [ ] SBOM generation is deterministic enough for review and contains no secrets, local paths, tokens, or private host identity.
- [ ] Every published release archive/installer has a GitHub artifact attestation bound to the correct repository, workflow, tag, commit, and digest.
- [ ] GHCR image digest is attested; cosign keyless signing is evaluated and added only if it supplies interoperability beyond the GitHub attestation.
- [ ] Workflow permissions use the minimum required OIDC/attestation grants.
- [ ] Attestations are generated after final signing/packaging and cannot describe mutable pre-signing bytes.
- [ ] A clean environment verifies checksum, Authenticode where applicable, and GitHub attestation from the downloaded asset.
- [ ] Release notes/download documentation links concise verification instructions.
- [ ] Existing custom provenance remains accurate and non-contradictory.
- [ ] Negative tests reject a modified artifact and an attestation from the wrong repository/workflow identity.
```

---

## 14. Deferred seed manifest

Seed these only as capacity, dependencies, and current direction allow. The programme tracker may hold them as future children before dedicated issues exist.

### 14.1 Windows follow-ups

#### TRUST-WIN-04: clean-machine reputation and false-positive operations

**Suggested title**

> `[Testing][Security] Certify clean-machine Windows download/install behaviour and false-positive response`

**Priority:** II after signed artifact exists.  
**Depends on:** TRUST-WIN-03 and preferably DIST-WIN-01.

Acceptance should cover:

- Mark-of-the-Web download paths;
- signature UI;
- SmartScreen observation without promising a pass;
- Defender scan;
- clean install/upgrade/uninstall;
- bounded VirusTotal policy;
- Microsoft submission path;
- release hash/evidence template;
- never recommending security exclusions as normal setup.

#### DIST-WIN-02: Microsoft Store and winget

**Suggested title**

> `[Packaging] Establish Microsoft Store and winget distribution after direct-release signing`

**Priority:** III unless launch strategy promotes it.  
**Depends on:** identity decision, installer, trust QA.

Split Store and winget later if one becomes materially larger.

### 14.2 macOS issues

#### DIST-MAC-01: feasibility

> `[Packaging][Spike] Prove Taskdeck macOS x64/arm64 runtime and .app lifecycle`

**Priority:** III.  
**Scope:** no paid Apple account required; app bundle, lifecycle, data path, browser/port behaviour, architecture matrix.

#### TRUST-MAC-01: Apple publisher provisioning

> `[Human action][Security] Provision the approved Apple Developer organisation and notarisation identity`

**Priority:** III, Blocked on feasibility and identity.  
**Scope:** D-U-N-S, legal account, role access, certificate/API-key custody, renewal/revocation.

#### TRUST-MAC-02: signing and notarisation

> `[Packaging][Security][CI] Sign, notarise, staple, and verify Taskdeck macOS releases`

**Priority:** III.  
**Depends on:** both macOS issues above.

### 14.3 Linux issues

#### DIST-LINUX-01: supported artifacts

> `[Packaging][CI] Ship tested Linux x64/arm64 archives and attested container artifacts`

**Priority:** III.  
**Scope:** platform/data-path contract, checksums, SBOMs, attestations, clean Ubuntu proof.

#### DIST-LINUX-02: first package channel

> `[Packaging] Select and deliver Taskdeck's first Linux package-manager channel`

**Priority:** IV until demand.  
**Depends on:** stable Linux artifact and domain/app ID.

### 14.4 Cloud follow-ups

Prefer folding Stage 1 work into `#1772` and `#1777`. Seed separate issues only when their scopes become independently reviewable.

Potential splits:

- `[Cloud][Security] Add the private access boundary and hosted-session decision for the trusted instance`;
- `[Cloud][Hardening] Automate application-consistent encrypted SQLite backups and prove clean restore`;
- `[Cloud][CI] Deploy exact tested images through a protected environment and rollback gate`;
- `[Cloud][Observability] Add bounded error/uptime diagnostics with redaction and telemetry truth`;
- `[Cloud][Cost] Add budget ceilings, provider usage evidence, and decommission/export runbook`.

Do not seed a public-SaaS infrastructure epic merely because a private Render instance exists.

### 14.5 Student Pack/external service documentation

This does not need a standalone public issue if the initial programme documentation PR can own it. If repository policy requires an issue, use:

> `[Docs][Ops] Add a sanitised external-service register and private Student Pack redemption workflow`

Acceptance:

- no personal student identifiers, billing details, secrets, or redemption evidence in Git;
- public register records vendor purpose, owner class, production dependency, expiry risk, data/exit path, and relevant issue;
- private ledger location and 60/30/7-day reminder process documented;
- overlapping vendors explicitly require a choose-one decision;
- expired benefits cannot silently break production.

---

## 15. Documentation plan

### 15.1 New programme document

Suggested `docs/ops/RELEASE_TRUST_AND_DISTRIBUTION.md` should be concise and durable. It should contain:

- supported platform/distribution matrix;
- identity/signing option decision and status;
- release pipeline order;
- trust-surface distinctions;
- human-account boundaries;
- platform roadmap and issue links;
- current costs and renewal owner, without sensitive account data;
- verification commands and evidence links after implementation;
- last-updated date and owner.

Do not copy the entire Student Pack catalogue into an authoritative product doc. Put only Taskdeck-relevant selected services in the sanitised register.

### 15.2 Existing docs to update only when justified

Potential targets:

- `.github/workflows/release-desktop.yml` and its tests;
- `docs/releases/WINDOWS_QUICK_START.md`;
- release checklist/runbook;
- deployment guide;
- `docs/platform/CLOUD_DEPLOYMENT_GUIDE.md`;
- `deploy/render.yaml`;
- `docs/STATUS.md`;
- `docs/IMPLEMENTATION_MASTERPLAN.md`;
- `docs/REVIVAL_PLAN.md` only if scope/sequence is formally changed;
- ADR index and ADR-0061 only after maintainer ratification;
- README/download page only after shipped evidence;
- security/telemetry/privacy docs when external services are enabled.

### 15.3 Documentation truth rules

- Use present tense only for shipped, verified behaviour.
- Say `signed` only when the exact published asset verifies.
- Say `notarised` only when Apple's accepted notarisation and stapling/online validation are evidenced.
- Say `supported` only when clean-machine lifecycle tests exist.
- Say `hosted demo` for the backend-less demo and `trusted shared instance` for the private persistent service.
- Never call the private instance `Taskdeck Cloud` or `SaaS` without an accepted decision.
- State current costs as snapshots with verification dates.
- Remove unsigned workaround instructions only from release paths that are actually signed.

---

## 16. Human-action checklist

Mirror the applicable unchecked items into `OUTSTANDING_TASKS.md`. Do not mark them complete based on an agent's inference.

### Publisher/name/domain

- [ ] Confirm who legally owns and publishes Taskdeck.
- [ ] Confirm whether DeliveraSoft Ltd is the intended publisher.
- [ ] Confirm company age and validation evidence relevant to Artifact Signing.
- [ ] Decide whether the current Taskdeck name proceeds unchanged.
- [ ] Obtain legal/trademark advice before commercial launch if the name review triggers it.
- [ ] Choose canonical domain and role email.
- [ ] Verify live domain availability and years 2-5 renewal cost.
- [ ] Claim/register domain through the chosen owner account.
- [ ] Record registrar recovery and transfer plan privately.

### Windows

- [ ] Choose Artifact Signing, SignPath, or OV primary route and fallback.
- [ ] Create the required paid Azure subscription if Artifact Signing is selected.
- [ ] Submit identity validation.
- [ ] Apply to SignPath Foundation if selected as bridge/fallback.
- [ ] Create/verify the protected GitHub Environment and reviewers.
- [ ] Decide whether signing blocks v0.3 or targets v0.3.x.
- [ ] Create the correctly typed Microsoft Store account after identity decision.

### Cloud

- [ ] Confirm self-host+tunnel first versus direct Render.
- [ ] Confirm Render versus Railway if provider choice is reopened.
- [ ] Set monthly budget ceiling and payer.
- [ ] Establish provider account/workspace owner.
- [ ] Establish private access method.
- [ ] Choose backup storage and retention.
- [ ] Decide LLM key owner/payer.
- [ ] Decide bounded `#1644` risk treatment for the trusted instance.
- [ ] Keep MFA disabled until `#1653` is resolved.
- [ ] Participate in two-user and restore walkthroughs.

### macOS, later

- [ ] Confirm organisation versus individual Apple publisher.
- [ ] Verify D-U-N-S and legal details.
- [ ] Approve US$99/year membership only after feasibility.
- [ ] Create protected roles/certificates/API keys.

### Student Pack

- [ ] Create private benefit ledger.
- [ ] Activate only benefits with concrete use and exit plan.
- [ ] Set 60/30/7-day expiry reminders.
- [ ] Check auto-renew/card state after every redemption.
- [ ] Keep personal student identity out of the public Taskdeck repository.
- [ ] Move production assets to durable company ownership before eligibility ends where required.

---

## 17. Risk register

| Risk | Likelihood | Impact | Mitigation / owner |
| --- | --- | --- | --- |
| DeliveraSoft is too new for Artifact Signing public-trust validation | Medium/high until verified | High for chosen timeline | Apply to SignPath in parallel; traditional OV/Store alternatives; do not couple v0.3 quality to an external pending process |
| Product name/domain conflicts cause confusion or later rework | Medium | High once Store/package IDs/domains are public | Bounded name review before durable identifiers; company-domain IDs; professional review trigger |
| Signing added after checksum/attestation or followed by binary mutation | Medium | High | Enforce pipeline order and final-byte tests |
| Only outer installer is signed while nested executable is unsigned | Medium | High | Expected-signables manifest and fail-closed tree scan |
| SmartScreen warning persists after signing and is reported as failure | High for new publisher/files | Medium | Separate identity from reputation; clean-machine evidence and truthful release notes |
| Signing credential leaks through CI | Low/medium | Critical | Provider-managed key, OIDC, least privilege, protected environment, no PFX in generic secrets |
| Student account becomes a production single point of failure | Medium | High | Company ownership/transfer plan, exit ledger, no permanent architecture based on credits |
| Free domain renews at unexpectedly high price or cannot transfer promptly | Medium | Medium/high | Years 2-5 cost and transfer review before claim |
| Timed Pack offers are activated and wasted | High | Medium | Just-in-time redemption and private ledger |
| Overlapping tools create telemetry, cost, and maintenance sprawl | High | Medium | Choose-one policy for observability, browser testing, secrets, feature flags, and Git clients |
| Heroku SQLite deployment loses data | High if attempted | Critical | Explicitly prohibit current local SQLite Heroku hosting |
| Render/Railway disk snapshot is treated as sufficient backup | Medium | High | Application-consistent off-platform backup plus restore drill |
| Connector encryption key is not restored with database | Medium | Critical for stored connectors | Separate key backup and restore acceptance |
| `InviteOnly` is treated as a private network boundary | Medium | High | Identity-aware/private provider access plus app auth |
| Browser token storage is accepted beyond bounded trusted users | Medium | High | Resolve `#1644` before public hosting; explicit private-beta decision |
| MFA is enabled while TOTP secrets remain plaintext | Medium | High | Keep disabled/fail-closed until `#1653` |
| Direct auto-deploy from main bypasses reviewed release gate | Medium | High | Deploy exact tested image through protected environment; link `#1504` |
| macOS membership purchased before viable product exists | Medium | Low/medium | Feasibility phase first |
| Too many package formats become permanent maintenance burden | High | Medium | One demand-led channel at a time |
| SBOM/attestation describes pre-signing bytes | Medium | High | Generate against final immutable package and verify digest ordering |
| Public repo records personal student/billing data | Low/medium | High privacy/operational cost | Sanitised register only; private ledger elsewhere |
| External service expiry silently disables production | Medium | High | Dependency classification, expiry reminders, export/decommission test |
| Private shared instance is marketed as SaaS | Medium | High product/legal/ops impact | ADR-0061 boundary, truthful language, `#2012` gate |

---

## 18. Definition of programme success

The Windows-first programme is successful when:

- the published Windows artifacts display an approved trusted publisher;
- every signable is signed, timestamped, and verified fail-closed;
- checksums, SBOMs, attestations, and custom provenance bind to final bytes;
- users have a normal signed installer and an advanced portable ZIP;
- clean-machine install/upgrade/uninstall and data preservation are proven;
- SmartScreen/Defender behaviour is documented without false guarantees;
- human identity and renewal operations are owned and recoverable;
- the private cloud proof has access, backup, restore, budget, and reconnect evidence;
- supported-platform and hosting claims match reality;
- macOS/Linux work is staged honestly rather than nominally supported;
- Student Pack benefits reduce cost or improve evidence without dictating architecture or creating hidden expiry failures.

The programme is **not** successful merely because:

- a certificate/account was purchased;
- one executable was signed manually;
- a public URL exists;
- a cloud provider says it takes snapshots;
- a Store account was created;
- every Student Pack offer was redeemed;
- a large number of issues were seeded.

Progress is merged, verified release/operational capability and correctly maintained external human actions.

---

## 19. Agent execution prompt

Copy this section with the full document into the Taskdeck agent session.

```text
GOAL
Reconcile Taskdeck's current repository, release pipeline, roadmap, issues, and docs against the attached Release Trust, Distribution, Cloud, and Student Benefits Programme. Produce a bounded, dependency-correct execution backlog and authoritative documentation. Windows code signing and trustworthy direct distribution are the first priority. Preserve the private shared-instance boundary. Do not create a public SaaS plan.

AUTHORITY AND ORIENTATION
Follow live repository authority. Read docs/STATUS.md, AGENTS.md, .codex/memories/00_ACTIVE.md, .codex/README.md, docs/REVIVAL_PLAN.md, docs/IMPLEMENTATION_MASTERPLAN.md, docs/GOLDEN_PRINCIPLES.md, docs/ISSUE_EXECUTION_GUIDE.md, docs/GITHUB_PROJECT_AUTOMATION.md, docs/ops/GITHUB_LABEL_TAXONOMY.md, OUTSTANDING_TASKS.md, and relevant release/deployment/security docs. Use the taskdeck-repo-onramp and taskdeck-issue-batch-orchestrator skills where applicable. Refresh GitHub, ProjectV2, releases, workflows, CI, open PRs, review threads, milestones, branches, and worktrees before changing anything.

RECONCILIATION FIRST
Inspect at least #1167, #550, #1772, #1777, #1504, #1310, #1644, #1653, #1992, #2010, #2012, #1879, and the current release tracker. Inspect .github/workflows/release-desktop.yml, deploy/render.yaml, the cloud guide, release docs, and current release assets. Search for later or duplicate work. Live state outranks the brief.

ISSUE POLICY
Update existing issues before creating new ones. Do not create a replacement tracker for #1167 or #1772. Seed at most five new issues in the first wave unless an explicit maintainer instruction waives the cap. Respect four Now and eight Next issue WIP limits. Do not promote new issues into a full queue. Every issue gets exactly one Priority label and matching Project Priority. Use explicit dependencies, milestones only when authorised, and concise human prose.

FIRST-WAVE TARGET
Re-scope #1167 into the release-trust tracker. Update #550, #1772, and #1777. Seed, subject to live duplicate search:
1. publisher/product/domain/signing-route decision;
2. Windows signing identity and protected CI provisioning;
3. Authenticode signing/timestamp/fail-closed verification;
4. signed user-grade Windows installer and metadata;
5. SBOMs, GitHub artifact attestations, and verification instructions.
Keep SmartScreen QA, Store/winget, macOS, Linux package channels, and extra cloud splits deferred unless live capacity/authority says otherwise.

HUMAN BOUNDARY
Do not purchase, register, redeem, enrol, validate, or expose credentials. Put legal identity, domain, Azure/Artifact Signing, SignPath, Microsoft Store, Apple, Render/Railway, Student Pack, billing, and subjective acceptance actions in OUTSTANDING_TASKS.md. Never place student identity, payment details, recovery codes, signing keys, PFX files/passwords, API keys, or private account evidence in public issues/docs.

DOCUMENTATION
Create one concise release trust/distribution programme document and one sanitised external-services register only if no existing authoritative document can absorb the content cleanly. Update existing docs only when behaviour or sequencing changes. Keep the detailed Student Pack ledger private. Public docs may record vendor purpose, owner class, expiry risk, and exit path, not account details.

CLOUD BOUNDARY
Preserve ADR-0061/#1772: one trusted private instance, one SQLite volume, a few known users, private access, InviteOnly onboarding, exact image evidence, SignalR/reconnect proof, application-consistent encrypted backup, connector-key backup, restore drill, cost owner, LLM payer/egress disclosure, and no SaaS claim. Do not use Heroku for current local SQLite. Treat Azure Student credit as lab/staging leverage, not Artifact Signing payment or permanent architecture.

VERIFICATION
Run the exact governance, issue/project sync, docs, workflow lint, and relevant repository checks. Report exact commands and results. Do not claim external account actions completed. Do not merge implementation merely because issues were seeded unless live authority and task scope explicitly permit it.

DELIVER
1. reconciliation summary with duplicates/stale facts;
2. existing issues updated;
3. bounded new issue set with dependencies, labels, priorities, milestones/statuses;
4. ProjectV2 parity and WIP audit;
5. docs PR if warranted;
6. OUTSTANDING_TASKS human actions;
7. cost/timeline/risk summary;
8. exact verification evidence and remaining blockers.
```

---

## 20. Source register

The agent must re-check current terms before implementation or purchase. These sources establish the 26 August 2026 research baseline.

### Taskdeck repository

- Repository: https://github.com/Chris0Jeky/Taskdeck
- Existing release trust issue: https://github.com/Chris0Jeky/Taskdeck/issues/1167
- Trusted shared instance: https://github.com/Chris0Jeky/Taskdeck/issues/1772
- Render migration: https://github.com/Chris0Jeky/Taskdeck/issues/1777
- Domain/brand issue: https://github.com/Chris0Jeky/Taskdeck/issues/550
- Production environment protection: https://github.com/Chris0Jeky/Taskdeck/issues/1504
- Hosted session issue: https://github.com/Chris0Jeky/Taskdeck/issues/1644
- MFA at-rest issue: https://github.com/Chris0Jeky/Taskdeck/issues/1653
- Deployment routing residuals: https://github.com/Chris0Jeky/Taskdeck/issues/1992
- Email/reminders epic: https://github.com/Chris0Jeky/Taskdeck/issues/2010
- Windows release workflow: https://github.com/Chris0Jeky/Taskdeck/blob/main/.github/workflows/release-desktop.yml
- Render Blueprint: https://github.com/Chris0Jeky/Taskdeck/blob/main/deploy/render.yaml
- Cloud deployment guide: https://github.com/Chris0Jeky/Taskdeck/blob/main/docs/platform/CLOUD_DEPLOYMENT_GUIDE.md
- Proposed ADR-0061: https://github.com/Chris0Jeky/Taskdeck/blob/main/docs/decisions/ADR-0061-trusted-shared-instance-and-managed-saas-boundary.md

### Windows

- Microsoft Artifact Signing overview/quickstart: https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart
- Artifact Signing FAQ/subscription support: https://learn.microsoft.com/en-us/azure/artifact-signing/faq
- Artifact Signing pricing/SKU: https://learn.microsoft.com/en-us/azure/artifact-signing/how-to-change-sku
- Signing integrations: https://learn.microsoft.com/en-us/azure/artifact-signing/how-to-signing-integrations
- Certificate/timestamp model: https://learn.microsoft.com/en-us/azure/artifact-signing/concept-certificate-management
- SmartScreen reputation: https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation
- MSIX signing options and eligibility: https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview
- Store company onboarding: https://learn.microsoft.com/en-us/windows/apps/publish/whats-new-company-developer
- SignPath Foundation: https://signpath.org/
- SignPath Foundation terms: https://signpath.org/terms.html
- SignPath Foundation application: https://signpath.org/apply.html

### macOS

- Apple Developer Program enrolment: https://developer.apple.com/programs/enroll/
- Membership comparison: https://developer.apple.com/support/compare-memberships/
- Developer ID: https://developer.apple.com/developer-id/
- macOS distribution: https://developer.apple.com/macos/distribution/
- Notarising macOS software: https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution

### Linux and supply chain

- GitHub artifact attestations: https://docs.github.com/en/actions/security-for-github-actions/using-artifact-attestations/using-artifact-attestations-to-establish-provenance-for-builds
- Sigstore/cosign signing overview: https://docs.sigstore.dev/cosign/signing/overview/
- Sigstore quickstart: https://docs.sigstore.dev/quickstart/quickstart-cosign/
- Flathub verification: https://docs.flathub.org/docs/for-app-authors/verification/
- Flathub requirements: https://docs.flathub.org/docs/for-app-authors/requirements/
- Flathub submission: https://docs.flathub.org/docs/for-app-authors/submission/

### Cloud

- Render pricing: https://render.com/pricing
- Render persistent disks: https://render.com/docs/disks
- Render free services: https://render.com/docs/free
- Render deploys: https://render.com/docs/deploys
- Railway pricing: https://railway.com/pricing
- Railway usage pricing: https://docs.railway.com/pricing
- Railway volume backups: https://docs.railway.com/volumes/backups
- Heroku SQLite limitations: https://devcenter.heroku.com/articles/sqlite3
- Azure for Students: https://azure.microsoft.com/en-us/free/students

### GitHub Student Developer Pack

- Current Pack catalogue: https://education.github.com/pack
- Student access and troubleshooting: https://docs.github.com/en/education/about-github-education/github-education-for-students/solving-problems-with-your-github-education-access

### Naming collision evidence, not legal conclusions

- Active unrelated service: https://www.taskdeck.app/
- VS Code extension using TaskDeck name: https://marketplace.visualstudio.com/items?itemName=emanuelebartolesi.taskdeck
- Historical macOS app listing: https://taskdeck.macupdate.com/

---

## 21. Final maintainer decision summary

The most effective practical path is:

1. keep v0.2/v0.3 product work stable;
2. immediately start publisher/name eligibility decisions and parallel Artifact Signing/SignPath preparation;
3. update existing issues rather than flooding the repository;
4. land signing through the existing hardened release pipeline when external validation is ready, even if that means v0.3.x rather than weakening v0.3;
5. make a signed installer the normal Windows path;
6. preserve checksums/provenance and add standard SBOM/attestation evidence;
7. use the existing Render path for a private two-person proof with real backup/restore and access controls;
8. use Student Pack benefits to reduce cost and improve testing/operations, but keep production identity and data portable beyond student eligibility;
9. treat macOS and Linux as real platform programmes with proof gates, not checkbox builds;
10. do not commit to public SaaS until retention, security, operations, legal, and commercial decisions justify it.
