# Taskdeck Landing Copy

Last Updated: 2026-03-27

## Purpose

Thesis-aligned landing page content for early beta positioning. Every claim maps to shipped functionality. This is source material for a future landing page — not a deployed page itself.

---

## Headline

**Stop managing your task board. Start using it.**

## Subheadline

Taskdeck captures messy inputs, generates structured proposals, and only changes your board when you approve. Local-first. Review-first. Yours.

## Value Proposition (3 Bullets)

1. **Capture anything, structure it later.**
   Paste a client email, a voice-note transcript, a checklist dump. Taskdeck triages it into actionable board changes — cards, columns, labels — without you doing the formatting.

2. **Nothing changes without your approval.**
   Every automation produces a proposal you review before it touches your board. You see exactly what will change, where it came from, and why. No silent mutations, no surprise cards.

3. **Your data stays on your machine.**
   Taskdeck runs locally with SQLite. No cloud account required, no data leaves your device unless you choose to export or share. Full control, zero lock-in.

## How It Works (4-Step Visual)

| Step | Label | Description |
| --- | --- | --- |
| 1 | **Capture** | Paste or type anything into Inbox. |
| 2 | **Triage** | Taskdeck generates a structured proposal from your input. |
| 3 | **Review** | See exactly what will change. Approve, edit, or reject. |
| 4 | **Apply** | Approved changes land on your board — clean, traceable, intentional. |

## Social Proof Placeholder

> "I used to spend 20 minutes a day reorganizing my board. Now I paste my notes and review what Taskdeck suggests."
>
> — *(beta tester quote placeholder — replace with real feedback after beta intake)*

## Install

```bash
# Clone and run locally
git clone https://github.com/Chris0Jeky/Taskdeck.git
cd Taskdeck

# Backend
dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj

# Frontend
cd frontend/taskdeck-web
npm install && npm run dev
```

Open `http://localhost:5173` to start.

See `docs/START_HERE.md` for the full getting-started guide.

## Beta Interest CTA

**Want early access?**

We are looking for developers and small-team leads who manage their own boards and want to reduce maintenance overhead.

*(Email capture form placeholder — implementation TBD based on channel choice: GitHub Discussions, simple form, or mailing list.)*

**What to expect:**
- Local install, no cloud dependency
- Direct feedback channel with the maintainer
- Influence on what gets built next

## What Taskdeck Is NOT

- Not a cloud-hosted SaaS (yet — local-first is the current posture)
- Not a team collaboration platform (single-user with local SignalR for dev)
- Not an autonomous AI agent (review-first means you stay in control)

## Tone Guidelines

- Lead with the problem (maintenance overhead), not the technology
- "Review-first" is the trust anchor — use it early and often
- Avoid superlatives ("best", "revolutionary", "AI-powered")
- Every claim must map to a shipped feature — check `docs/STATUS.md` before adding copy
- Keep it conversational and direct, not corporate

## Related

- Demo script: `DEMO_SCRIPT.md`
- Product thesis: `../InReview/HUMAN/01_PRODUCT_THESIS.md`
- GTM strategy: `../InReview/HUMAN/04_GTM_AND_MARKETING.md`
