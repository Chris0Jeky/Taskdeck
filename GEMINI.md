# Gemini CLI Specific Instructions

Because this repository already maintains `AGENTS.md` as the master context file for agents, this file is intentionally thin. The `.gemini/settings.json` is configured to read both `AGENTS.md` and this file.

* For repo conventions, branch strategy, testing policy, and definitions of done: refer to `AGENTS.md`.
* For high-frequency prompts, use commands in `.gemini/commands/`.
* For deep procedures (e.g., PR review flow, migrations), use skills in `.gemini/skills/`.

**Important defaults:**
- The default approval mode is `plan`, ensuring safety and architecture understanding before edits.
- You must ask clarifying questions and map the codebase before switching to edit mode.
- Use headless mode only for bounded automation (e.g., commit messages, PR summaries) rather than broad CI autonomy.
