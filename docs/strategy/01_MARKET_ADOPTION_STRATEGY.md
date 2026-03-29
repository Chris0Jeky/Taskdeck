# Market Adoption Strategy

**Date:** 2026-03-29
**Scope:** Go-to-market strategy, user acquisition, growth channels, and positioning for Taskdeck
**Status:** Strategic planning document — not yet executed

---

## 1. Current Reality Check

Taskdeck is 4.5 months old with 2,867 commits, 2,585+ automated tests, 19 CI workflows, and **zero external users**. The engineering is production-grade. The product thesis (near-zero-friction capture with review-first automation) is clear and defensible. But no one outside the development team has validated it.

The comprehensive status analysis (2026-03-29) identified this as the single biggest strategic risk: *"Every week spent on infrastructure hardening without user feedback is a bet that the current thesis is correct."*

**This document exists to close that gap.**

---

## 2. Positioning and Messaging

### 2.1 Category Definition

Taskdeck does not fit neatly into existing categories:
- **Not a Kanban board** (Trello, GitHub Projects) — those are the maintenance problem, not the solution
- **Not a meeting-to-task extractor** (Otter, Fireflies) — those are noisy and not your system of record
- **Not an AI agent** (Cursor, Devin) — those do things for you; Taskdeck proposes and you decide

**Proposed category:** "AI-assisted execution workspace" or "review-first task automation"

### 2.2 One-Line Positioning

> "Taskdeck captures messy inputs and turns them into structured board changes — but nothing happens without your approval."

### 2.3 The Wedge

The unique combination that competitors don't offer together:
1. **Local-first** — your data, your machine, no cloud dependency
2. **Proposal-first** — AI suggests, you decide; full transparency and auditability
3. **Near-zero-friction capture** — paste anything, get structure
4. **Keyboard-first** — built for developers who live in the terminal

### 2.4 Pain-Point Messaging (What Resonates)

Lead with the problem, not the technology:
- "You stopped using your task board because maintaining it felt like work"
- "You lost tasks because capturing them was harder than just remembering"
- "You don't trust AI tools because they make changes you didn't ask for"
- "You don't want to pay Notion/Linear $10/month for a task board"

---

## 3. Target User Segments (Phased)

### Phase 1: Beachhead (Months 1-3)

**Primary:** Solo developers and CS students
- Already use some task system (Trello, Notion, TODO.md, sticky notes)
- Have experienced "board attrition" — started organized, then stopped
- Value privacy and local control
- Comfortable running a local app or Docker
- Active on GitHub, Reddit, Hacker News

**Why this segment first:**
- Lowest friction to adoption (technical users who can self-serve)
- Most likely to give honest, detailed feedback
- Natural word-of-mouth in developer communities
- Don't need collaboration features yet

### Phase 2: Expansion (Months 3-6)

**Secondary:** Indie builders, freelancers, and small team leads
- Managing multiple projects
- Need collaboration on shared boards
- Want the simplicity of local tools with the reach of cloud tools
- Willing to pay for a hosted version

### Phase 3: Growth (Months 6-12)

**Tertiary:** Small dev teams (2-10 people)
- Need shared workspaces, permissions, real-time collaboration
- Interested in automation for team workflows
- Willing to pay per-seat pricing

---

## 4. User Acquisition Channels

### 4.1 Pre-Launch (Now — Before First External User)

**Goal:** Validate the thesis with 5-10 trusted users before any public launch.

| Action | Timeline | Effort |
|--------|----------|--------|
| Personal outreach to 10-15 developer friends/classmates | Week 1 | Low |
| Record a 90-second demo video (capture → triage → review → apply) | Week 1 | Medium |
| Polish the GitHub README with GIF, clear install steps, and value prop | Week 1 | Medium |
| Create a GitHub Discussion "Beta Interest" thread | Week 1 | Low |
| Set up a simple feedback form (Google Forms or Tally) | Week 1 | Low |
| Dogfood daily for 2 weeks and document the experience | Weeks 1-2 | Ongoing |

**Success criteria:** 3-5 people complete the capture → review → board loop independently.

### 4.2 Soft Launch (Weeks 2-4)

**Goal:** 20-50 users, validate time-to-first-value.

| Channel | Strategy | Expected Reach |
|---------|----------|----------------|
| **Show HN** | "Show HN: Taskdeck — local-first task board that maintains itself via reviewable AI proposals." Keep the post factual, focus on the problem, include demo GIF. Post Tuesday-Thursday 10am-12pm ET. | 50-200 visitors |
| **r/selfhosted** | "I built a local-first task board with AI-powered capture and proposal-first automation." Self-hosted crowd loves local-first. | 20-100 visitors |
| **r/SideProject** | Share the building story, not just the product. What you learned, decisions you made. | 10-50 visitors |
| **Dev.to / Hashnode** | Blog post: "Why I built a proposal-first task automation system." Technical, honest, shows architecture. | 20-80 visitors |

### 4.3 Public Launch (Weeks 4-8)

**Goal:** 100-500 users, establish presence.

| Channel | Strategy | Expected Reach |
|---------|----------|----------------|
| **Product Hunt** | Launch with demo video, screenshots, clear positioning. Get 5-10 beta users to upvote early. Tuesday launch, 12:01am PT. | 200-1000 visitors |
| **GitHub Trending** | Optimize README (GIF, badges, clear value prop). Add relevant topics (`productivity`, `task-management`, `local-first`, `ai-automation`). Encourage stars from beta users. | Variable |
| **Twitter/X** | Build-in-public thread showing the development journey. Tag relevant dev tool accounts. Short demo clips. | 50-200 followers |
| **YouTube** | 3-5 minute walkthrough video. "How I eliminated task board maintenance with AI proposals." | 100-500 views |
| **Reddit (broader)** | r/programming, r/productivity, r/DevOps with tailored messaging per community. | 50-200 per post |
| **Discord communities** | Developer Discords (Theo's, Fireship, local dev meetups). Share genuinely, don't spam. | 10-50 per community |

### 4.4 Sustained Growth (Months 2-6)

| Channel | Strategy |
|---------|----------|
| **Content marketing** | Weekly blog posts on Dev.to: architecture decisions, local-first patterns, proposal-first design, comparisons |
| **SEO** | Target long-tail keywords: "local task board", "AI task management private", "Trello alternative local", "review-first automation" |
| **Integration partnerships** | VS Code extension (quick capture from editor), CLI tool (capture from terminal), browser extension |
| **Community building** | Discord server for users, regular "office hours", monthly changelog posts |
| **Referral mechanics** | Shareable starter packs, exportable board templates, "Powered by Taskdeck" badge |

---

## 5. Content Strategy

### 5.1 Content Themes That Attract the Right Users

| Theme | Format | Channel |
|-------|--------|---------|
| "How I reduced task management overhead by 80%" | Blog post | Dev.to, HN |
| "Proposal-first automation: why AI shouldn't touch your data without asking" | Blog post | Dev.to, Reddit |
| "Local-first vs cloud-first: what developers actually want" | Blog post | Dev.to, HN |
| "Building a Clean Architecture .NET + Vue 3 app from scratch" | Tutorial series | Dev.to, YouTube |
| "Why I have more test code than production code" | Blog post | Dev.to, Reddit |
| "From 0 to 2,500 tests in 4 months as a solo developer" | Blog post | Dev.to, Twitter |
| Demo walkthrough: capture → triage → review → apply | Video (90s) | YouTube, Twitter, Product Hunt |
| Architecture deep-dive: how Taskdeck's proposal system works | Video (10min) | YouTube |
| "I replaced Trello with a local AI board — here's what happened" | Blog post | Reddit, Dev.to |

### 5.2 Demo Video Requirements (Critical Asset)

The single most important marketing asset is a **90-second screen recording** showing:
1. (0-15s) Problem statement: "Your task board becomes a chore to maintain"
2. (15-40s) Capture: paste messy text, one hotkey, done
3. (40-60s) Review: see the structured proposal, what changes, where it came from
4. (60-75s) Apply: one click, board updated, full provenance
5. (75-90s) CTA: "Try it locally — your data stays on your machine"

**Requirements:**
- Use the demo seed with realistic data (not lorem ipsum)
- Show the premium UI skin (design tokens, not raw Tailwind)
- Record at 1080p or higher
- Include captions (many watch muted)
- Host on YouTube + embed in README + landing page

---

## 6. Pricing and Monetization Strategy

### 6.1 Phase 1: Free and Open Source (Months 1-6)

Keep Taskdeck fully open source during the validation phase.

**Rationale:**
- Removes all adoption friction
- Builds trust with developer community
- Generates feedback and contributions
- GitHub stars and forks are social proof

### 6.2 Phase 2: Open Core (Months 6-12)

Introduce a separation:

| Tier | Features | Price |
|------|----------|-------|
| **Community (free)** | Full local-first experience, unlimited boards/cards, mock LLM provider, export/import, CLI | Free forever |
| **Pro (self-hosted)** | Live LLM providers (OpenAI/Gemini), advanced starter packs, priority support | $8-12/month or $80-100/year |
| **Cloud (hosted)** | Hosted version, collaboration, shared boards, sync, no install required | $10-15/user/month |

### 6.3 Phase 3: Team and Enterprise (Year 2+)

| Tier | Features | Price |
|------|----------|-------|
| **Team** | Shared workspaces, permissions, team boards, SSO | $15-20/user/month |
| **Enterprise** | Self-hosted cloud, SAML/SCIM, audit logs, SLA, dedicated support | Custom pricing |

### 6.4 Revenue Projections (Conservative)

| Milestone | Users | Paying | MRR |
|-----------|-------|--------|-----|
| Month 6 | 500 | 0 | $0 (validation phase) |
| Month 12 | 2,000 | 50-100 | $500-1,200 |
| Month 18 | 5,000 | 200-400 | $2,000-4,800 |
| Month 24 | 10,000 | 500-1,000 | $5,000-12,000 |

---

## 7. Metrics to Track

### 7.1 Acquisition Metrics

| Metric | Target | Tool |
|--------|--------|------|
| GitHub page visitors | Track weekly | GitHub Insights |
| GitHub stars | 100 in first month | GitHub |
| README → clone conversion | >5% | GitHub Insights |
| Landing page visitors | Track weekly | Plausible/Umami (privacy-respecting) |
| Beta signups | 50 in first month | Form submissions |

### 7.2 Activation Metrics

| Metric | Target | Tool |
|--------|--------|------|
| Time to first capture | <2 minutes | Product telemetry (opt-in) |
| Time to first proposal review | <5 minutes | Product telemetry |
| Time to first board change | <10 minutes | Product telemetry |
| First-run completion rate | >60% | Product telemetry |

### 7.3 Retention Metrics

| Metric | Target | Tool |
|--------|--------|------|
| Day 1 return rate | >40% | Product telemetry |
| Week 1 active rate | >25% | Product telemetry |
| Day 30 retention | >15% | Product telemetry |
| Captures per active user per week | >3 | Product telemetry |

### 7.4 Referral Metrics

| Metric | Target | Tool |
|--------|--------|------|
| NPS from beta users | >40 | Survey |
| Organic GitHub stars per week | >10 after launch | GitHub |
| Word-of-mouth referrals | Track | Intake workflow |

---

## 8. Common Mistakes to Avoid

### 8.1 What Solo Dev Tool Builders Get Wrong

1. **Building too long before shipping** — Taskdeck is already here. Ship NOW.
2. **Over-engineering marketing** — A genuine Show HN post beats a fancy landing page
3. **Targeting too broad** — "everyone who uses task boards" is no one. Target solo devs who lost their Trello habit.
4. **Ignoring retention for growth** — 10 users who use it daily > 1,000 who tried it once
5. **Not asking "why" when users leave** — Every churned beta user is a lesson
6. **Competing on features** — Trello/Notion will always have more features. Compete on trust and friction.
7. **Premature monetization** — Don't charge until people would be upset if you took it away

### 8.2 What To Do Instead

1. Ship the beta this week. Accept rough edges.
2. Personal outreach to 10 developers. Watch them use it. Take notes.
3. Write 1 blog post per week for 8 weeks. Genuine, technical, problem-focused.
4. Respond to every piece of feedback within 24 hours.
5. Measure activation (did they complete the core loop?) above all else.

---

## 9. Competitive Landscape

### 9.1 Direct Competitors

| Product | Strengths | Weaknesses | Taskdeck's Advantage |
|---------|-----------|------------|---------------------|
| **Trello** | Ubiquitous, simple, team-ready | Maintenance overhead, no AI capture, SaaS lock-in | Local-first, proposal-first automation |
| **Notion** | Flexible, databases, AI features | Complex, slow, privacy concerns, $10/mo | Focused on execution, local-first, faster |
| **Linear** | Fast, beautiful, developer-focused | Cloud-only, team-focused, $8/user/mo | Local-first, solo-friendly, free |
| **Todoist** | Simple, cross-platform | No board view, no AI automation, limited | Board-first, AI-powered capture |
| **Obsidian** | Local-first, extensible, privacy | Not a task board, plugin dependency | Purpose-built for execution, not notes |

### 9.2 Indirect Competitors

| Product | Overlap | Differentiation |
|---------|---------|-----------------|
| GitHub Issues/Projects | Board view, developer audience | Taskdeck is personal workflow, not project management |
| Apple Reminders / Google Tasks | Quick capture | Taskdeck adds structure and board execution |
| AI assistants (ChatGPT, Claude) | Task extraction from text | Taskdeck is the system of record, not a chat |

---

## 10. Launch Timeline

### Week 1-2: Pre-Launch

- [ ] Fix P0 blockers (#508, #509)
- [ ] Record 90-second demo video
- [ ] Polish GitHub README (GIF, badges, install steps, value prop)
- [ ] Create GitHub Discussion "Beta Interest" thread
- [ ] Personal outreach to 10-15 developer contacts
- [ ] Set up feedback collection (Google Forms / GitHub Discussions)
- [ ] Create simple landing page (GitHub Pages or single HTML page)

### Week 3-4: Soft Launch

- [ ] Post on Show HN (Tuesday-Thursday, 10am-12pm ET)
- [ ] Post on r/selfhosted and r/SideProject
- [ ] Publish first Dev.to blog post
- [ ] Start daily dogfooding cadence
- [ ] Collect and respond to all feedback within 24h

### Week 5-8: Public Launch

- [ ] Product Hunt launch (once 10+ users are active and providing social proof)
- [ ] Publish 2-3 more blog posts
- [ ] YouTube walkthrough video
- [ ] Twitter/X build-in-public thread
- [ ] Begin weekly changelog posts

### Month 3-6: Growth

- [ ] Content marketing cadence (1 post/week)
- [ ] Community Discord server
- [ ] VS Code extension for quick capture
- [ ] CLI tool for terminal capture
- [ ] Respond to user feedback with visible product changes

---

## 11. What Success Looks Like

| Timeframe | Metric | Target |
|-----------|--------|--------|
| Week 2 | Beta users who completed core loop | 5 |
| Month 1 | Total registered users | 50-100 |
| Month 1 | Weekly active users | 10-20 |
| Month 3 | Total users | 500 |
| Month 3 | Weekly active users | 50-100 |
| Month 3 | GitHub stars | 200+ |
| Month 6 | Total users | 2,000 |
| Month 6 | Weekly active users | 200-400 |
| Month 6 | Community Discord members | 100+ |
| Month 12 | Total users | 5,000-10,000 |
| Month 12 | Paying users (if monetized) | 100-200 |

---

## 12. Budget Considerations

### 12.1 Zero-Cost Channels (Start Here)

- GitHub (README, Discussions, releases, Pages)
- Reddit (genuine community participation)
- Hacker News (Show HN)
- Dev.to (free blog hosting)
- Twitter/X (organic)
- Personal network outreach
- Screen recording (OBS, free)

### 12.2 Low-Cost Investments ($0-100/month)

- Domain name for landing page: ~$12/year
- Privacy-respecting analytics (Plausible/Umami self-hosted): free
- Email for beta communication (Gmail/Proton): free
- Video editing (DaVinci Resolve): free

### 12.3 When to Invest More

Only after validation signals:
- 50+ weekly active users (time for a real landing page and domain)
- 200+ users (time for community infrastructure — Discord, docs site)
- 500+ users (time for content marketing push, maybe paid promotion)
- 1000+ users (time to consider hiring help, paid acquisition)

---

## Related Documents

- `docs/product/LANDING_COPY.md` — Existing landing copy source material
- `docs/product/BETA_INTAKE_WORKFLOW.md` — Beta intake process
- `docs/product/DEMO_SCRIPT.md` — Demo walkthrough script
- `docs/InReview/HUMAN/04_GTM_AND_MARKETING.md` — Original GTM planning
- `docs/InReview/HUMAN/02_MARKET_AND_VALUE.md` — Market positioning notes
- `docs/strategy/00_MASTER_STRATEGY.md` — Master strategy document
