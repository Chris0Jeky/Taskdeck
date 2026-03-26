# Taskdeck UI Mock

This folder contains a frontend-only static mock of the Taskdeck UI.

Purpose:
- give a lightweight feel for the current shell and major surfaces
- keep example data local to the frontend only
- stay simple to host on GitHub Pages or any static file host

Entry point:
- `index.html`

Files:
- `styles.css`: self-contained design tokens and mock-specific styles, derived from the real frontend's current visual language
- `data.js`: example workspace, proposal, capture, and board data
- `app.js`: tiny client-side router and local interactions for the mock

Suggested local usage:
- open `frontend/taskdeck-web/public/mock/index.html` via a simple static server
- or run the normal frontend dev server and open `/mock/`

Suggested GitHub Pages usage:
- publish the built `frontend/taskdeck-web/dist/mock/` directory
- or copy this folder as-is to any static host
