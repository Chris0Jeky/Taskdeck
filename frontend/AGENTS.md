# Repository Guidelines – Frontend

This guide applies to all code under `frontend/`, especially `frontend/taskdeck-web`.

## Tech Stack & Layout

- App root: `frontend/taskdeck-web`.
- Key directories:
  - `src/api`: API clients and Axios setup.
  - `src/store`: Pinia stores and shared state.
  - `src/router`: Vue Router configuration.
  - `src/views`: route-level pages.
  - `src/components`: reusable UI components.
  - `src/types`: shared TypeScript definitions.

## Coding Style

- Use Vue 3 with TypeScript; prefer `<script setup lang="ts">` where consistent with existing files.
- Name Vue components in `PascalCase.vue` (e.g., `BookingList.vue`) and export them using matching component names.
- Keep styling consistent with existing Tailwind and CSS usage in `style.css`; avoid introducing new global styles without discussion.
- Use meaningful, descriptive names for stores, routes, and components; avoid abbreviations that are not obvious.

## Development & Testing

- From `frontend/taskdeck-web`:
  - Install deps: `npm install`.
  - Dev server: `npm run dev`.
  - Production build: `npm run build`.
- When consuming backend APIs, centralize HTTP calls in `src/api` and reuse shared types from `src/types` instead of inlining response shapes.
- For new views or features, wire routes in `src/router`, state in `src/store`, and UI in `src/views`/`src/components`.

## Frontend-Specific PR Expectations

- For visual changes, include before/after screenshots or a short description of UI impact.
- Document any new configuration or environment variables (e.g., additions to `.env`) in the PR description.
- Keep PRs scoped by feature or bug; large refactors should be split or clearly labeled.

