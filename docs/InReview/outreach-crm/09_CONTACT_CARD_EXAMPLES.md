# 09 — Contact Card Examples (copy/paste)

## Example: Warm contact → feedback ask
```yaml
---
type: contact
display_name: "Piotr N."
relationship_tier: "B"
company: "Google"
role: "SRE"
handles:
  linkedin_url: "https://www.linkedin.com/in/xxxxx"
tags: ["google","sre","feedback"]
status: "warm"
cadence_id: "warm-3-7-21"
last_touch_at: "2026-02-22"
next_touch_at: "2026-02-25"
notes_private: "Ask for 10 min feedback on Taskdeck trust-first loop."
---
```

## Timeline
- 2026-02-22 (comment): Asked about reliability trade-offs in local-first tools.
- 2026-02-23 (DM outbound): Sent demo + feedback ask.

---

## Example: A-tier contact → referral ask
```yaml
---
type: contact
display_name: "Former GE teammate"
relationship_tier: "A"
company: "GE Vernova"
role: "DevOps"
handles:
  email: "x@example.com"
tags: ["referral","platform","aws"]
status: "active"
cadence_id: "referral-3-10"
last_touch_at: "2026-02-18"
next_touch_at: "2026-02-28"
notes_private: "After warming, ask for intro to hiring manager at target company."
---
```
