# Taskdeck Master Strategy: From Local Tool to Product

**Date:** 2026-03-29
**Scope:** Unified strategy covering market adoption, packaging, cloud, mobile, and everything in between
**Status:** Strategic planning document — the north star for the next 12 months

---

## 0. The Honest Assessment

You built something impressive. 4.5 months, 2,867 commits, 2,585+ tests, production-grade CI, clean architecture, thoughtful security posture. The engineering would make a funded startup jealous.

**But here's the truth:** None of that matters yet. Zero external users means zero validated product thesis. The capture-and-review loop might be exactly what developers need, or it might be solving a problem nobody has. You don't know. I don't know. Only users will tell us.

Everything in this document is designed to get from "impressive engineering project" to "product that people use and depend on." Every decision below prioritizes learning speed — how fast can we find out what's right and what's wrong?

---

## 1. The Four Pillars (and How They Connect)

| Pillar | Document | Core Question |
|--------|----------|---------------|
| Market Adoption | `01_MARKET_ADOPTION_STRATEGY.md` | How do people find out about Taskdeck? |
| Packaging | `02_PACKAGING_DISTRIBUTION_STRATEGY.md` | How do people start using it in <3 minutes? |
| Cloud/Collaboration | `03_CLOUD_COLLABORATION_STRATEGY.md` | How do people use it without installing anything? |
| Mobile | `04_MOBILE_STRATEGY.md` | How do people capture and review on the go? |

**These are not independent.** They form a pipeline:

```
Discovery          →  First Use           →  Retention           →  Expansion
(Market Adoption)     (Packaging/Cloud)      (Mobile + Cloud)      (Collaboration)

HN/Reddit/Blog → GitHub README → Download or Cloud URL → Use daily → Invite team
```

The weakest link determines throughput. If the product is easy to find but hard to install, you lose at First Use. If it's easy to install but desktop-only, you lose at Retention (people forget about it when away from their desk).

---

## 2. Release Architecture

This is the version plan that sequences all four pillars into deliverable releases.

### v0.1.0 — "First Light" (Week 1-2)

**Goal:** Get 5-10 people using Taskdeck on their own machines.

**Delivers:**
- P0 blocker fixes (#508 queue data isolation, #509 board auto-switching)
- Self-contained single-file executable (Windows + Linux + macOS)
- Auto-creates SQLite DB and JWT secret on first run
- Browser auto-opens on launch
- GitHub Release with downloads and checksums
- Polished README with GIF, install steps, and value prop
- 90-second demo video
- GitHub Discussion "Beta Interest" thread

**Does not include:** Cloud, mobile, installers, auto-update

**Success metric:** 5 users complete capture → review → board independently.

### v0.2.0 — "Open Doors" (Week 3-5)

**Goal:** Remove the install barrier. Let anyone try Taskdeck from a URL.

**Delivers:**
- Hosted cloud instance on Railway/Render (app.taskdeck.io or similar)
- GitHub OAuth login (no manual registration for cloud)
- Production config (TLS, CORS, monitoring, backups)
- Landing page on custom domain with demo video and "Try it now" button
- Show HN post, r/selfhosted post, first Dev.to blog post

**Does not include:** Collaboration, mobile optimization, sync

**Success metric:** 50 users. 20+ weekly active. Average time-to-first-value <5 minutes.

### v0.3.0 — "In Your Pocket" (Week 6-9)

**Goal:** Taskdeck works on phones. Capture from anywhere.

**Delivers:**
- PWA manifest + service worker (installable from mobile browser)
- Mobile-responsive CSS for Home, Inbox, Capture, Review
- Bottom tab navigation for mobile
- Touch-optimized capture modal
- Mobile board view (card list, not kanban columns)
- Push notifications for proposals ready to review

**Does not include:** App store listing, offline sync, native wrapper

**Success metric:** 30% of users access from mobile at least once per week.

### v0.4.0 — "Bring Friends" (Week 10-14)

**Goal:** Multiple people can work on the same boards.

**Delivers:**
- Board sharing with permission levels (Owner, Editor, Viewer)
- Workspace invitations (email/link)
- Email notifications for mentions and proposals
- Activity feed per board
- SignalR real-time for shared boards (already works, needs cloud hardening)

**Does not include:** Teams/organizations, SSO, advanced permissions

**Success metric:** 10+ shared boards with 2+ users each.

### v0.5.0 — "Power Up" (Week 15-20)

**Goal:** Platform maturity and growth engine.

**Delivers:**
- Platform installers (Inno Setup, DMG, AppImage)
- Package manager listings (winget, Homebrew, Snap)
- Google Play listing (TWA or Capacitor)
- PostgreSQL backend option for cloud (alongside SQLite for local)
- Free tier limits for hosted cloud
- Pro tier introduction (live LLM providers, unlimited boards)
- Offline capture queue (sync when online)

**Does not include:** Apple App Store, E2EE, team billing

**Success metric:** 500+ total users, 20+ paying users, 3+ package manager installs per week.

### v1.0.0 — "Generally Available" (Month 6-8)

**Goal:** Taskdeck is a product, not a project.

**Delivers:**
- Apple App Store listing (via Capacitor)
- Workspace/team model with organization billing
- Local + cloud sync (API-based)
- Tauri 2.0 native desktop shell (optional, alongside browser experience)
- Agent substrate (inspectable runs, bounded templates)
- Content marketing flywheel (blog, changelog, community)

**Success metric:** 2,000+ total users, 100+ paying users, <5% monthly churn.

---

## 3. The Dependency Map

```
Fix P0 Blockers (#508, #509)
  │
  ├─→ v0.1.0 Self-Contained Executable
  │     │
  │     ├─→ v0.2.0 Hosted Cloud Instance
  │     │     │
  │     │     ├─→ v0.3.0 PWA + Mobile Responsive
  │     │     │     │
  │     │     │     ├─→ v0.4.0 Collaboration (Shared Boards)
  │     │     │     │     │
  │     │     │     │     ├─→ v0.5.0 Platform Maturity
  │     │     │     │     │     │
  │     │     │     │     │     └─→ v1.0.0 GA
  │     │     │     │
  │     │     │     └─→ Google Play (TWA)
  │     │     │
  │     │     └─→ PostgreSQL migration
  │     │
  │     └─→ Platform installers
  │
  └─→ Demo video + README polish
        │
        └─→ Show HN + Reddit + Dev.to
```

---

## 4. What You Haven't Thought About (But Need To)

### 4.1 Legal and Compliance

Before any cloud offering:
- **Privacy Policy** — Required by GDPR, Apple, Google. Use Termly or Iubenda to generate one.
- **Terms of Service** — Required for any hosted offering. Protects you from liability.
- **Cookie consent** — Required if using analytics in the EU.
- **Open source license clarity** — Taskdeck is on GitHub but I don't see a LICENSE file. You need to decide:
  - MIT/Apache 2.0 (fully permissive, anyone can use/modify/sell)
  - AGPL (copyleft, forces derivative works to be open source)
  - BSL/SSPL (source-available, prevents competitors from hosting your code as a service)
  - **Recommendation:** MIT for the core, with a commercial license for the hosted cloud service. This is the open-core model that Linear, Supabase, and others use.

### 4.2 Domain and Branding

- **Domain:** Secure `taskdeck.io`, `taskdeck.dev`, or `taskdeck.app` if available. Check now — good domains get taken fast.
- **Logo:** A simple, recognizable logo for README, PWA icon, app store listing. Doesn't need to be fancy, but needs to exist.
- **Social accounts:** Reserve @taskdeck on Twitter/X, GitHub (already have), Discord, Reddit (r/taskdeck).

### 4.3 Support and Community Infrastructure

Before you have 50+ users:
- **GitHub Discussions** — primary support channel (already planned in beta intake)
- **Discord server** — real-time community (developers love Discord)
- **Email** — support@taskdeck.io or similar for formal communication
- **Changelog** — public, accessible, human-readable. GitHub Releases + a `/changelog` page.
- **Status page** — for the hosted cloud instance. Use Instatus (free tier) or UptimeRobot.

### 4.4 Analytics and Telemetry

You need to know what's happening without violating privacy:
- **Product telemetry (opt-in):** Time-to-first-capture, captures/day, proposals reviewed/day, board mutations/day. Aggregate, anonymous, opt-in with clear disclosure. Ship counts, not content.
- **Error tracking:** Sentry (free for open source) for frontend and backend crash reporting
- **Infrastructure monitoring:** For the hosted cloud: uptime, latency, error rates. Railway/Render dashboards plus UptimeRobot.
- **Privacy-respecting web analytics:** Plausible or Umami (self-hosted) for landing page and app usage. No cookies, no personal data.
- Issue #341 (product telemetry taxonomy) is already seeded — this is a strong foundation.

### 4.5 Backup and Disaster Recovery

For the hosted cloud instance:
- **Automated daily backups** of the SQLite/PostgreSQL database
- **Point-in-time recovery** capability (PostgreSQL native, SQLite via file backup)
- **Backup testing** — monthly restore drills (you already have a rehearsal cadence for this)
- **Data export** — users should always be able to export their data (already exists)

### 4.6 Rate Limiting and Abuse at Scale

The current rate limiting (auth, capture, hot paths) is designed for local use. For hosted cloud:
- **Per-user API rate limits** (already have framework, needs tuning)
- **LLM usage limits** (managed key abuse controls, #235-#240)
- **Storage limits** (per-user board/card/attachment limits)
- **Signup abuse protection** (CAPTCHA, email verification, IP rate limiting)
- **Content moderation** — what if someone uses the cloud instance for abusive content?

### 4.7 Bus Factor and Sustainability

You are one person. This is fine for building, but risky for running a product people depend on.

**Mitigations:**
- Keep the architecture clean (you're doing this)
- Document operations runbooks (you're doing this)
- Consider: If you're unavailable for 2 weeks, can the hosted instance keep running? Automated backups, auto-restart on crash, and monitoring alerts are the minimum.
- Plan for contributor onboarding if/when you want help. The AGENTS.md and CLAUDE.md are excellent foundations.

### 4.8 Internationalization (i18n)

Not urgent, but plan for it:
- Don't hardcode English strings in components (use Vue I18n or a similar library)
- Right-to-left (RTL) support if targeting MENA markets
- Date/time formatting based on locale
- This is a "consider now, implement when there's demand" item

### 4.9 Accessibility

Issue #92 (WCAG audit) exists but isn't prioritized. For a public product:
- Screen reader compatibility
- Keyboard navigation (you're strong here — keyboard-first is a core value)
- Color contrast (check the design token system)
- Focus indicators (already have focus-visible rings)
- ARIA labels on interactive elements (Reka UI provides good defaults)

### 4.10 Competitive Moat

The biggest strategic question: what prevents someone from cloning Taskdeck?

**Current moats:**
- Proposal-first automation (unique in the category)
- Local-first + review-first trust model
- Clean architecture and test coverage (hard to replicate quickly)
- Head start in the category

**Future moats:**
- Community and ecosystem (starter packs, templates, integrations)
- Data network effects (more captures → better LLM triage → better proposals)
- Brand association with "safe AI task management"
- Integration ecosystem (VS Code, CLI, browser extension, mobile)

---

## 5. Resource Allocation

You are one person. Every hour matters. Here's where to spend time:

### Month 1 (Weeks 1-4)

| Activity | Time Allocation | Rationale |
|----------|----------------|-----------|
| Fix P0 blockers | 20% | Can't ship broken product |
| Packaging (self-contained exe) | 25% | Unblocks user acquisition |
| Demo video + README | 15% | Marketing asset for launch |
| Cloud deployment | 20% | Removes install friction |
| Outreach + Show HN | 10% | First users |
| Feedback processing | 10% | Learning from users |

### Month 2 (Weeks 5-8)

| Activity | Time Allocation | Rationale |
|----------|----------------|-----------|
| Mobile responsive + PWA | 30% | Capture from anywhere |
| Respond to user feedback | 25% | This is the most important work now |
| Content marketing (blog) | 15% | Sustained discovery |
| Cloud hardening | 15% | Reliability for growing users |
| Bug fixes from beta | 15% | Trust and retention |

### Month 3-4 (Weeks 9-16)

| Activity | Time Allocation | Rationale |
|----------|----------------|-----------|
| Feature work driven by feedback | 35% | Build what users want |
| Collaboration features | 25% | Growth vector |
| Platform maturity | 20% | Installers, package managers |
| Marketing + community | 10% | Flywheel |
| Operations + reliability | 10% | Keep the lights on |

---

## 6. Decision Framework

When you're unsure what to work on, use this priority stack:

```
1. Is it broken? Fix it.
   (P0 bugs, data loss risks, security issues)

2. Are users asking for it?
   (Feedback-driven features, friction reports)

3. Does it reduce friction to first value?
   (Onboarding, packaging, setup simplification)

4. Does it increase retention?
   (Mobile access, notifications, daily workflow)

5. Does it increase reach?
   (Cloud, collaboration, marketing, integrations)

6. Does it increase quality?
   (Tests, docs, performance, accessibility)

7. Everything else.
   (Cool features nobody asked for yet)
```

---

## 7. Risk Register

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Nobody uses it (product-market fit failure) | Medium | Critical | Ship fast, collect feedback, iterate |
| LLM provider costs exceed revenue | Medium | High | Rate limiting, free tier caps, hybrid key model |
| Solo developer burnout | Medium | High | Prioritize ruthlessly, say no to feature creep |
| Competitor launches similar product | Low | Medium | Speed to market, community, unique positioning |
| Security incident on hosted instance | Low | High | OWASP baseline (done), monitoring, incident playbook (done) |
| Apple rejects app store submission | Medium | Low | Don't rush to App Store, PWA is sufficient initially |
| Open source fork / hostile competitor | Low | Low | Open-core model, community goodwill, execution speed |
| SQLite breaks under cloud load | Medium | Medium | Monitor, migrate to PostgreSQL when signals appear |
| Legal/privacy complaint | Low | Medium | Privacy policy, ToS, GDPR compliance before cloud launch |

---

## 8. Metrics Dashboard

When you have users, you need a single-page view of health:

```
┌─────────────────────────────────────────────────┐
│ TASKDECK HEALTH DASHBOARD                       │
├──────────────────┬──────────────────────────────┤
│ Total Users      │ ___                          │
│ WAU (7-day)      │ ___                          │
│ DAU              │ ___                          │
│ New Users (7d)   │ ___                          │
├──────────────────┼──────────────────────────────┤
│ Captures (7d)    │ ___                          │
│ Proposals (7d)   │ ___                          │
│ Approvals (7d)   │ ___                          │
│ Activation Rate  │ ___% (first capture <5min)   │
├──────────────────┼──────────────────────────────┤
│ Cloud Uptime     │ ___%                         │
│ API p95 Latency  │ ___ms                        │
│ Error Rate       │ ___%                         │
│ LLM Cost (7d)    │ $___                         │
├──────────────────┼──────────────────────────────┤
│ GitHub Stars     │ ___                          │
│ Open Issues      │ ___                          │
│ Paying Users     │ ___                          │
│ MRR              │ $___                         │
└──────────────────┴──────────────────────────────┘
```

---

## 9. The One Thing That Matters Most Right Now

**Ship to users.** Everything else is downstream.

The demo video. The single executable. The first Show HN post. The first 5 people who try it and tell you what's wrong.

You've built the engine. Now turn the key.

---

## Companion Documents

| Document | Purpose |
|----------|---------|
| `01_MARKET_ADOPTION_STRATEGY.md` | How people find Taskdeck |
| `02_PACKAGING_DISTRIBUTION_STRATEGY.md` | How people install and run Taskdeck |
| `03_CLOUD_COLLABORATION_STRATEGY.md` | How people use Taskdeck online and together |
| `04_MOBILE_STRATEGY.md` | How people use Taskdeck on phones |

---

## Appendix A: Things That Can Wait

These are valid ideas that should NOT be prioritized in the next 3 months:

- Agent substrate (AgentProfile/Run/Event) — no user has asked for this yet
- Knowledge/FTS surface — build when users need cross-board search
- Plugin/extension architecture — premature abstraction
- CQRS/MediatR refactor — architectural astronautics unless app layer becomes unmanageable
- Calendar/timeline views — nice to have, not core
- Advanced analytics — need users before you need analytics
- Enterprise features (SSO, SCIM, SAML) — no enterprise customers yet
- Voice capture — text capture isn't validated yet
- Third-party connectors (Slack, GitHub, Teams) — integrations without users = wasted work

## Appendix B: Quick Wins (High Impact, Low Effort)

| Win | Effort | Impact |
|-----|--------|--------|
| Fix #508 (queue data isolation) | 1 day | Unblocks all external users |
| Fix #509 (board auto-switching) | 1 day | UX blocker |
| Polish README with GIF and badges | 2-3 hours | First impression |
| Record 90-second demo video | 2-3 hours | Most important marketing asset |
| Add web app manifest for PWA | 2-3 hours | Mobile installable |
| Deploy to Railway (Docker) | 2-4 hours | Cloud access for anyone |
| GitHub Discussion "Beta Interest" | 30 minutes | Intake channel |
| Reserve social media handles | 30 minutes | Brand protection |
| Add LICENSE file to repo | 10 minutes | Legal clarity |

## Appendix C: 12-Month Roadmap Summary

```
Month 1:     v0.1.0 (First Light)      → Self-contained exe, first users
             v0.2.0 (Open Doors)        → Hosted cloud, HN/Reddit launch

Month 2-3:   v0.3.0 (In Your Pocket)   → PWA, mobile responsive

Month 4-5:   v0.4.0 (Bring Friends)    → Collaboration, shared boards

Month 5-6:   v0.5.0 (Power Up)         → Platform maturity, monetization

Month 6-8:   v1.0.0 (GA)               → Full product, all platforms

Month 8-12:  v1.x                       → Feature expansion driven by users
                                           Agent substrate, knowledge, integrations
```
