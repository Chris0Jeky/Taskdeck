# Beta Intake Workflow and Cadence

Last Updated: 2026-03-27

## Purpose

Define the process for acquiring, onboarding, and retaining early beta users. This is an operator document — it describes what the maintainer does, not what users see.

## Goals

- Acquire 5-20 weekly active users (per GTM target in [`04_GTM_AND_MARKETING.md`](../InReview/HUMAN/04_GTM_AND_MARKETING.md))
- Collect structured feedback on the capture-to-board loop
- Validate that time-to-first-value is under 5 minutes
- Identify friction points before broader exposure

## Intake Channels

| Channel | Status | Owner Action |
| --- | --- | --- |
| GitHub Discussions (beta-interest thread) | Recommended first channel | Pin a "Beta Interest" discussion, respond within 48h |
| Direct outreach (classmates, dev communities) | Active | Personal message with install link + demo video |
| Landing page email capture | Future | Implement when landing page ships |
| Hacker News / Reddit | Deferred | Only after demo recording is solid and 5+ beta users are active |

## Intake Process

### Step 1: Interest Signal

A potential user expresses interest via any channel above.

**Owner response (within 48h):**
- Acknowledge interest
- Share install instructions (`docs/START_HERE.md`)
- Share the demo video link (once recorded)
- Ask: "What do you currently use for task/project management?"

### Step 2: Onboarding

**Owner provides:**
- Clone + run instructions
- Link to `docs/product/FIRST_RUN_WORKFLOWS.md`
- Offer a 15-minute walkthrough call if they want one

**Success criteria:**
- User has a running local instance
- User has completed at least one capture-to-board loop
- Elapsed time from install to first applied proposal < 5 minutes (target)

### Step 3: First Week Check-in

**Timing:** 5-7 days after onboarding.

**Owner asks:**
1. Have you used Taskdeck since the initial setup? (Y/N)
2. What did you try to do with it?
3. What was confusing or broken?
4. Would you use it again this week? (Y/N)

**Record responses** in a simple tracking sheet or GitHub Discussion thread.

### Step 4: Ongoing Cadence

| Cadence | Action |
| --- | --- |
| Weekly | Check GitHub issues/discussions for beta feedback |
| Bi-weekly | Reach out to active beta users for structured feedback |
| Monthly | Summarize feedback themes and update prioritization |

## Feedback Collection

### Structured Feedback Template

Share this with beta users after their first week:

```
1. What were you trying to do?
2. What happened?
3. What did you expect to happen?
4. How would you rate capture friction? (1=painful, 5=effortless)
5. How would you rate review clarity? (1=confusing, 5=clear)
6. Would you recommend Taskdeck to a peer? (Y/N, why?)
```

### Where Feedback Lives

- **Bugs:** GitHub Issues (label: `beta-feedback`)
- **Feature requests:** GitHub Discussions (category: Ideas)
- **General impressions:** GitHub Discussions (category: General)
- **Structured survey responses:** Private tracking sheet (not committed to repo)

## Retention Signals

**Healthy:**
- User returns after first week
- User files a bug or feature request (engagement signal)
- User completes multiple capture-to-board loops

**At risk:**
- No activity after first week check-in
- Reports confusion about core loop
- Says "I don't know when I'd use this"

**Owner action for at-risk users:**
- Offer a walkthrough call
- Ask what their current workflow looks like
- If they disengage, record the reason and move on (do not pressure)

## Graduation Criteria

Beta is "working" when:
- 5+ users have completed the capture-to-board loop independently
- Average time-to-first-value is under 5 minutes
- No critical bugs block the core loop
- At least 3 users return after the first week

At that point, consider broader channels (Hacker News, Reddit, blog posts).

## Anti-Patterns

- Do not launch on public channels before the demo recording is solid
- Do not promise features that are not shipped (check `docs/STATUS.md`)
- Do not collect email addresses without a clear plan to follow up
- Do not optimize for user count over feedback quality at this stage

## Related

- GTM strategy: `../InReview/HUMAN/04_GTM_AND_MARKETING.md`
- Landing copy: `LANDING_COPY.md`
- Demo script: `DEMO_SCRIPT.md`
- First-run guide: `FIRST_RUN_WORKFLOWS.md`
