# Gemini CLI Guide

This repository is optimized to use **Gemini CLI** as a powerful tool for planning, architecture research, and bounded automation. Instead of treating Gemini CLI as just "another terminal chatbot," you should think of it as a small agent platform tailored for Taskdeck.

To get the most out of Gemini CLI, follow these established best practices.

## 1. Start with Plan Mode
Gemini's standout feature for complex engineering work is **Plan Mode**. 
- Nontrivial work should **always start in `/plan`**.
- Let Gemini read the context, ask clarifying questions, and map dependencies and risks first.
- Only switch to edit mode once the plan is solid, relying on checkpointing before broad refactors. 
- *Note: Our [`settings.json`](../../../.gemini/settings.json) enforces `defaultApprovalMode: "plan"` by default.*

## 2. The Context Fallacy: Keep GEMINI.md Thin
`GEMINI.md` is always part of the active context hierarchy. Putting heavy procedures into this file makes it noisy and burns context window space.
Instead, we maintain a strict split:
- **[`AGENTS.md`](../../../AGENTS.md) and [`GEMINI.md`](../../../GEMINI.md)**: Reserved for repo conventions, guardrails, branch strategy, testing policies, and definitions of done. (We've configured settings to read both).
- **Commands (`.gemini/commands/`)**: Shortcuts for high-frequency workflows (e.g., `/review-pr`, `/write-tests-for`). Use commands for "same prompt shape, different input".
- **Skills (`.gemini/skills/`)**: Deep, on-demand procedures (e.g., PR reviews, migration planners). These are only loaded when explicitly needed.

## 3. Automation and Headless Mode
Gemini CLI's headless mode is excellent for CI adjuncts and automation wrappers.
**Good Candidates for Headless Mode:**
- Summarizing failing test logs.
- Drafting commit messages based on staged changes.
- Extracting structured data.
- Run repo-specific audit commands on demand.

**What to Avoid:**
- Unattended destructive edits in CI.
- Broad autonomous feature implementation in CI.

## 4. MCP Server Rules
Through MCP (Model Context Protocol), Gemini CLI can communicate with GitHub, internal APIs, and more. 
- Always enable local file/code tools.
- Enable GitHub MCP when doing operations on Issues/PRs.
- Avoid enabling random MCP servers if they aren't strictly required. More tools = more failure modes and prompt complexity.

## 5. Security and Guardrails
Taskdeck enforces strict guardrails.
Our baseline relies on:
- **Folder Trust (`folderTrust`)**: Enabled.
- **Checkpointing**: Enabled.
- **Plan Mode**: Default.
- **Policies ([`.gemini/policies/`](../../../.gemini/policies/))**: Restrictive blocking/allowing of tools and modes based on repository needs.

By adhering to this structure, Gemini CLI provides high-leverage assistance without "turning into configuration soup."
