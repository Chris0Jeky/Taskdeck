import { mockData } from "./data.js";

const app = document.getElementById("app");

const state = {
  route: getRouteFromHash(),
  mode: loadMode(),
  selectedCaptureId: mockData.captures[0]?.id ?? null,
  expandedProposalId: null,
  currentBoardId: mockData.boards[0]?.id ?? null,
  proposals: structuredClone(mockData.proposals),
  captures: structuredClone(mockData.captures),
};

window.addEventListener("hashchange", () => {
  state.route = getRouteFromHash();
  render();
});

render();

function getRouteFromHash() {
  const value = window.location.hash.replace(/^#/, "").trim();
  const allowed = new Set(["home", "today", "review", "inbox", "board"]);
  return allowed.has(value) ? value : "home";
}

function loadMode() {
  const stored = window.localStorage.getItem("taskdeck-mock-mode");
  return mockData.workspaceModes.some((mode) => mode.id === stored) ? stored : "guided";
}

function saveMode(mode) {
  window.localStorage.setItem("taskdeck-mock-mode", mode);
}

function navigate(route) {
  window.location.hash = route;
}

function updateMode(mode) {
  state.mode = mode;
  saveMode(mode);
  render();
}

function getCurrentMode() {
  return mockData.workspaceModes.find((mode) => mode.id === state.mode) ?? mockData.workspaceModes[0];
}

function getCurrentBoard() {
  return mockData.boards.find((board) => board.id === state.currentBoardId) ?? mockData.boards[0];
}

function getSelectedCapture() {
  return state.captures.find((capture) => capture.id === state.selectedCaptureId) ?? null;
}

function setSelectedCapture(captureId) {
  state.selectedCaptureId = captureId;
  render();
}

function toggleProposalDiff(proposalId) {
  state.expandedProposalId = state.expandedProposalId === proposalId ? null : proposalId;
  render();
}

function updateProposalStatus(proposalId, nextStatus) {
  state.proposals = state.proposals.map((proposal) =>
    proposal.id === proposalId ? { ...proposal, status: nextStatus } : proposal,
  );
  render();
}

function triageCapture(captureId) {
  state.captures = state.captures.map((capture) =>
    capture.id === captureId ? { ...capture, status: "Triaged" } : capture,
  );
  render();
}

function ignoreCapture(captureId) {
  state.captures = state.captures.map((capture) =>
    capture.id === captureId ? { ...capture, status: "Ignored" } : capture,
  );
  render();
}

function cardCount(board) {
  return board.columns.reduce((count, column) => count + column.cards.length, 0);
}

function render() {
  const mode = getCurrentMode();
  const currentBoard = getCurrentBoard();

  app.innerHTML = `
    <div class="td-shell">
      <aside class="td-sidebar" role="navigation" aria-label="Main navigation">
        <div class="td-sidebar__header">
          <div>
            <div class="td-sidebar__title">Taskdeck</div>
            <div class="td-sidebar__subtitle">Frontend-only mock</div>
          </div>
        </div>

        <nav class="td-sidebar__nav">
          ${renderNavItem("home", "Home", "H")}
          ${renderNavItem("today", "Today", "T")}
          ${renderNavItem("review", "Review", "R")}
          ${renderNavItem("board", "Boards", "B")}
          ${renderNavItem("inbox", "Inbox", "I")}

          <div class="td-sidebar__section">
            <div class="td-sidebar__section-label">Secondary</div>
            <button class="td-nav-item td-nav-item--secondary" type="button">
              <span class="td-nav-item__icon">A</span>
              <span class="td-nav-item__label">Advanced surfaces stay mocked out</span>
            </button>
          </div>
        </nav>

        <div class="td-sidebar__footer">
          <div class="td-sidebar__footer-note">
            Static preview for GitHub Pages. All data lives in <code>public/mock/</code>.
          </div>
        </div>
      </aside>

      <div class="td-main-container">
        <header class="td-topbar">
          <div class="td-topbar__left">
            <div class="td-topbar__mode">
              <label class="td-topbar__mode-label" for="workspace-mode-select">Workspace mode</label>
              <div class="td-topbar__mode-controls">
                <select id="workspace-mode-select" class="td-topbar__mode-select">
                  ${mockData.workspaceModes
                    .map(
                      (workspaceMode) =>
                        `<option value="${workspaceMode.id}" ${
                          workspaceMode.id === state.mode ? "selected" : ""
                        }>${workspaceMode.label}</option>`,
                    )
                    .join("")}
                </select>
                <span class="td-topbar__mode-copy">${escapeHtml(mode.description)}</span>
              </div>
            </div>
            <button class="td-topbar__palette-trigger" type="button" data-route="home">
              <span class="td-topbar__search-icon">/</span>
              <span class="td-topbar__search-text">Mock command palette entry point</span>
            </button>
          </div>

          <div class="td-topbar__right">
            <span class="td-topbar__badge">Static</span>
            <span class="td-topbar__user">${escapeHtml(mockData.user.username)}</span>
          </div>
        </header>

        <main class="td-content" role="main">
          ${renderBanner()}
          ${renderView(currentBoard)}
        </main>
      </div>
    </div>
  `;

  bindEvents();
}

function renderBanner() {
  return `
    <section class="td-banner">
      <div>
        <strong>Taskdeck UI mock.</strong>
        This is not wired to the backend. It is a visual/static walkthrough of the current product direction.
      </div>
      <div class="td-banner__meta">Suggested GitHub Pages entry: site root <code>/</code></div>
    </section>
  `;
}

function renderNavItem(route, label, icon) {
  const activeClass = state.route === route ? "td-nav-item--active" : "";
  return `
    <button class="td-nav-item ${activeClass}" type="button" data-route="${route}">
      <span class="td-nav-item__icon">${icon}</span>
      <span class="td-nav-item__label">${label}</span>
    </button>
  `;
}

function renderView(currentBoard) {
  switch (state.route) {
    case "today":
      return renderTodayView();
    case "review":
      return renderReviewView();
    case "inbox":
      return renderInboxView();
    case "board":
      return renderBoardView(currentBoard);
    case "home":
    default:
      return renderHomeView();
  }
}

function renderHelpCallout(title, description, actions) {
  return `
    <section class="td-help-callout">
      <div class="td-help-callout__header">
        <div class="td-help-callout__copy">
          <span class="td-help-callout__eyebrow">What is this?</span>
          <h2 class="td-help-callout__title">${escapeHtml(title)}</h2>
          <p class="td-help-callout__description">${escapeHtml(description)}</p>
        </div>
      </div>
      <div class="td-help-callout__actions">${actions}</div>
    </section>
  `;
}

function renderHomeView() {
  return `
    <div class="td-page">
      <header class="td-panel td-hero">
        <div class="td-hero__copy">
          <span class="td-eyebrow">Workspace</span>
          <h1 class="td-page-title">Home</h1>
          <p class="td-subtitle">
            Keep the loop clear: shape the day in Today, decide change in Review, and let boards stay where work lands.
          </p>
        </div>
        <div class="td-hero__actions">
          <button class="td-btn td-btn--primary" type="button" data-route="today">Open Today</button>
          <button class="td-btn td-btn--secondary" type="button" data-route="inbox">Capture to Inbox</button>
          <button class="td-btn td-btn--secondary" type="button" data-route="review">Open Review</button>
        </div>
      </header>

      ${renderHelpCallout(
        "What is Home for?",
        "Home is the reset surface for the product loop: see what needs attention, restart setup when the loop feels unclear, and jump into Today, Inbox, or Review without guessing where to begin.",
        `
          <button class="td-btn td-btn--secondary td-btn--sm" type="button" data-route="today">Open Today</button>
          <button class="td-btn td-btn--secondary td-btn--sm" type="button" data-route="review">Open Review</button>
        `,
      )}

      <section class="td-panel td-section">
        <div class="td-section__head">
          <div>
            <h2 class="td-section-title">Setup loop</h2>
            <p class="td-section-desc">Start from a useful board, capture one real item, then review before anything reaches a board.</p>
          </div>
          <div class="td-badge">${mockData.home.onboarding.completedSteps}/${mockData.home.onboarding.totalSteps} steps</div>
        </div>
        <div class="td-step-grid">
          ${mockData.home.onboarding.steps
            .map(
              (step) => `
                <article class="td-step ${step.state === "Done" ? "td-step--complete" : ""}">
                  <span class="td-step__status">${escapeHtml(step.state)}</span>
                  <span class="td-step__title">${escapeHtml(step.title)}</span>
                  <span class="td-step__description">${escapeHtml(step.description)}</span>
                </article>
              `,
            )
            .join("")}
        </div>
      </section>

      <section class="td-grid td-grid--three">
        <article class="td-panel td-card">
          <div class="td-card__header">
            <h2 class="td-section-title">Needs attention</h2>
            <span class="td-badge">4 awaiting review</span>
          </div>
          <div class="td-stat-grid">
            ${mockData.home.workload
              .map(
                (item) => `
                  <div class="td-stat-card">
                    <span class="td-stat-card__value">${item.value}</span>
                    <span class="td-stat-card__label">${escapeHtml(item.label)}</span>
                    <span class="td-stat-card__helper">${escapeHtml(item.helper)}</span>
                  </div>
                `,
              )
              .join("")}
          </div>
        </article>

        <article class="td-panel td-card">
          <div class="td-card__header">
            <h2 class="td-section-title">Next step</h2>
            <span class="td-badge">Review-first</span>
          </div>
          <div class="td-stack">
            ${mockData.home.recommendedActions
              .map(
                (action) => `
                  <button class="td-action td-action--${action.tone}" type="button" data-route="${action.route}">
                    <span class="td-action__title">
                      ${escapeHtml(action.title)}
                      ${action.count ? `<span class="td-action__count">${action.count}</span>` : ""}
                    </span>
                    <span class="td-action__description">${escapeHtml(action.description)}</span>
                  </button>
                `,
              )
              .join("")}
          </div>
        </article>

        <article class="td-panel td-card">
          <div class="td-card__header">
            <h2 class="td-section-title">Boards</h2>
            <span class="td-badge">${mockData.boards.length} active</span>
          </div>
          <div class="td-stack">
            ${mockData.boards
              .map(
                (board) => `
                  <button class="td-list-card" type="button" data-board-id="${board.id}" data-route="board">
                    <span class="td-list-card__title">${escapeHtml(board.name)}</span>
                    <span class="td-list-card__description">${escapeHtml(board.description)}</span>
                    <span class="td-list-card__meta">${escapeHtml(board.recentActivity)}</span>
                  </button>
                `,
              )
              .join("")}
          </div>
        </article>
      </section>
    </div>
  `;
}

function renderTodayView() {
  return `
    <div class="td-page">
      <header class="td-panel td-hero">
        <div class="td-hero__copy">
          <span class="td-eyebrow">Daily Agenda</span>
          <h1 class="td-page-title">Today</h1>
          <p class="td-subtitle">
            See what needs a decision, what needs shaping, and what board work is due before the day gets away from you.
          </p>
        </div>
        <div class="td-hero__actions">
          <button class="td-btn td-btn--primary" type="button" data-route="review">Open Review</button>
          <button class="td-btn td-btn--secondary" type="button" data-route="inbox">Open Inbox</button>
          <button class="td-btn td-btn--secondary" type="button" data-route="board">Start Useful Board</button>
        </div>
      </header>

      ${renderHelpCallout(
        "What is Today for?",
        "Today keeps the daily path legible: decide proposals first, shape fresh captures second, and only then dive back into board work that is overdue, due now, or blocked.",
        `
          <button class="td-btn td-btn--secondary td-btn--sm" type="button" data-route="review">Open Review</button>
          <button class="td-btn td-btn--secondary td-btn--sm" type="button" data-route="inbox">Open Inbox</button>
        `,
      )}

      <section class="td-grid td-grid--stats">
        ${mockData.today.stats
          .map(
            (stat) => `
              <article class="td-panel td-stat-block">
                <span class="td-stat-block__label">${escapeHtml(stat.label)}</span>
                <span class="td-stat-block__value">${stat.value}</span>
                <span class="td-stat-block__helper">${escapeHtml(stat.helper)}</span>
              </article>
            `,
          )
          .join("")}
      </section>

      <section class="td-grid td-grid--three">
        ${mockData.today.agenda
          .map(
            (section) => `
              <article class="td-panel td-card">
                <div class="td-card__header">
                  <div>
                    <h2 class="td-section-title">${escapeHtml(section.title)}</h2>
                    <p class="td-section-desc">${escapeHtml(section.helper)}</p>
                  </div>
                  <span class="td-badge">${section.count}</span>
                </div>
                ${
                  section.items.length === 0
                    ? `
                      <div class="td-empty">
                        <p>${escapeHtml(section.empty)}</p>
                        ${section.route ? `<button class="td-btn td-btn--secondary td-btn--sm" type="button" data-route="${section.route}">Open ${escapeHtml(section.title)}</button>` : ""}
                      </div>
                    `
                    : `
                      <div class="td-stack">
                        ${section.items
                          .map(
                            (item) => `
                              <button class="td-list-card" type="button" data-route="board">
                                <span class="td-list-card__title">${escapeHtml(item.title)}</span>
                                <span class="td-list-card__description">${escapeHtml(item.boardName)}</span>
                                <span class="td-list-card__meta">${escapeHtml(item.meta)}</span>
                              </button>
                            `,
                          )
                          .join("")}
                      </div>
                    `
                }
              </article>
            `,
          )
          .join("")}
      </section>

      <section class="td-panel td-section">
        <div class="td-section__head">
          <div>
            <h2 class="td-section-title">Recommended next moves</h2>
            <p class="td-section-desc">Keep the loop moving without leaving Today to figure out where to go next.</p>
          </div>
        </div>
        <div class="td-stack">
          ${mockData.today.recommendedActions
            .map(
              (action) => `
                <button class="td-list-card" type="button" data-route="${action.route}">
                  <span class="td-list-card__title">${escapeHtml(action.title)}</span>
                  <span class="td-list-card__description">${escapeHtml(action.description)}</span>
                </button>
              `,
            )
            .join("")}
        </div>
      </section>
    </div>
  `;
}

function renderReviewView() {
  const pendingCount = state.proposals.filter((proposal) => proposal.status === "Pending Review").length;
  const approvedCount = state.proposals.filter((proposal) => proposal.status === "Approved").length;
  const appliedCount = state.proposals.filter((proposal) => proposal.status === "Applied").length;
  const captureLinkedCount = state.proposals.filter((proposal) => !!proposal.captureId).length;

  return `
    <div class="td-page">
      <header class="td-panel td-hero">
        <div class="td-hero__copy">
          <span class="td-eyebrow">Review</span>
          <h1 class="td-page-title">Review</h1>
          <p class="td-subtitle">
            Review proposed changes before anything touches a board. Queue and chat remain advanced/operator surfaces when you need manual control.
          </p>
        </div>
        <div class="td-hero__actions">
          <button class="td-btn td-btn--primary" type="button" data-route="review">Refresh Review</button>
          <button class="td-btn td-btn--secondary" type="button" data-route="inbox">Open Inbox</button>
          <button class="td-btn td-btn--secondary" type="button" data-route="board">Open Board</button>
        </div>
      </header>

      ${renderHelpCallout(
        "What is Review for?",
        "Review is the trust gate. Proposed changes stop here before they touch a board, while queue and chat remain advanced/operator surfaces when you need to drive the workflow manually.",
        `
          <button class="td-btn td-btn--secondary td-btn--sm" type="button" data-route="inbox">Open Inbox</button>
          <button class="td-btn td-btn--secondary td-btn--sm" type="button" data-route="board">Open Boards</button>
        `,
      )}

      <section class="td-grid td-grid--stats">
        ${[
          { label: "Pending review", value: pendingCount, helper: "Changes waiting for an explicit decision." },
          { label: "Ready to execute", value: approvedCount, helper: "Approved proposals that can now land on boards." },
          { label: "Capture-linked", value: captureLinkedCount, helper: "Review items that came through the inbox loop." },
          { label: "Applied", value: appliedCount, helper: "Proposals already executed successfully." },
        ]
          .map(
            (card) => `
              <article class="td-panel td-stat-block">
                <span class="td-stat-block__value">${card.value}</span>
                <span class="td-stat-block__label">${escapeHtml(card.label)}</span>
                <span class="td-stat-block__helper">${escapeHtml(card.helper)}</span>
              </article>
            `,
          )
          .join("")}
      </section>

      <section class="td-stack">
        ${state.proposals
          .map(
            (proposal) => `
              <article class="td-panel td-review-card">
                <div class="td-card__header td-review-card__header">
                  <div>
                    <h2 class="td-review-card__title">${escapeHtml(proposal.title)}</h2>
                    <div class="td-review-card__meta">
                      <span>Risk: ${escapeHtml(proposal.risk)}</span>
                      <span>Created: ${escapeHtml(proposal.createdAt)}</span>
                      <span>Source: ${escapeHtml(proposal.source)}</span>
                      <span>Board: ${escapeHtml(proposal.boardName)}</span>
                    </div>
                  </div>
                  <span class="td-status-chip td-status-chip--${proposal.status.toLowerCase().replace(/\s+/g, "-")}">${escapeHtml(proposal.status)}</span>
                </div>

                <div class="td-stack td-stack--tight">
                  <p class="td-review-card__summary">${escapeHtml(proposal.summary)}</p>
                  <div class="td-chip-row">
                    <span class="td-cue">${escapeHtml(proposal.impact)}</span>
                    <span class="td-cue">Risk: ${escapeHtml(proposal.risk)}</span>
                    <span class="td-cue">Source: ${escapeHtml(proposal.source)}</span>
                  </div>
                </div>

                ${
                  proposal.captureId
                    ? `
                      <div class="td-chip-row">
                        <span class="td-cue">Capture-linked</span>
                        <button class="td-btn td-btn--secondary td-btn--sm" type="button" data-route="inbox" data-capture-id="${proposal.captureId}">Open Capture</button>
                        <button class="td-btn td-btn--secondary td-btn--sm" type="button" data-route="board">Open Board</button>
                      </div>
                    `
                    : ""
                }

                <div class="td-chip-row">
                  <button class="td-btn td-btn--secondary td-btn--sm" type="button" data-toggle-diff="${proposal.id}">
                    ${state.expandedProposalId === proposal.id ? "Hide Diff" : "View Diff"}
                  </button>
                  <button class="td-btn td-btn--primary td-btn--sm" type="button" data-approve-proposal="${proposal.id}" ${
                    proposal.status !== "Pending Review" ? "disabled" : ""
                  }>
                    Approve
                  </button>
                  <button class="td-btn td-btn--danger td-btn--sm" type="button" data-reject-proposal="${proposal.id}" ${
                    proposal.status !== "Pending Review" ? "disabled" : ""
                  }>
                    Reject
                  </button>
                  <button class="td-btn td-btn--secondary td-btn--sm" type="button" data-execute-proposal="${proposal.id}" ${
                    proposal.status !== "Approved" ? "disabled" : ""
                  }>
                    Execute
                  </button>
                </div>

                ${
                  state.expandedProposalId === proposal.id
                    ? `<pre class="td-diff">${escapeHtml(proposal.diff)}</pre>`
                    : ""
                }
              </article>
            `,
          )
          .join("")}
      </section>
    </div>
  `;
}

function renderInboxView() {
  const selectedCapture = getSelectedCapture();

  return `
    <div class="td-page">
      <header class="td-page-header">
        <div>
          <h1 class="td-page-title">Inbox</h1>
          <p class="td-subtitle">Capture artifacts and triage-ready context.</p>
        </div>
        <button class="td-btn td-btn--secondary" type="button" data-route="inbox">Refresh</button>
      </header>

      ${renderHelpCallout(
        "What is Inbox for?",
        "Inbox is where notes, pasted text, and follow-ups get shaped into reviewable proposals. Use triage here when you want help preparing a change, then switch to Review before anything reaches a board.",
        `
          <button class="td-btn td-btn--secondary td-btn--sm" type="button" data-route="home">Open Home</button>
          <button class="td-btn td-btn--secondary td-btn--sm" type="button" data-route="review">Open Review</button>
        `,
      )}

      <div class="td-two-pane">
        <section class="td-panel td-pane">
          <div class="td-pane__header">
            <h2>Items</h2>
            <span class="td-badge">${state.captures.length}</span>
          </div>
          <div class="td-stack">
            ${state.captures
              .map(
                (capture) => `
                  <button class="td-inbox-row ${
                    capture.id === state.selectedCaptureId ? "td-inbox-row--selected" : ""
                  }" type="button" data-capture-id="${capture.id}">
                    <div class="td-chip-row">
                      <span class="td-cue">${escapeHtml(capture.status)}</span>
                      <span class="td-cue">${escapeHtml(capture.source)}</span>
                    </div>
                    <span class="td-inbox-row__title">${escapeHtml(capture.title)}</span>
                    <span class="td-inbox-row__excerpt">${escapeHtml(capture.excerpt)}</span>
                    <span class="td-inbox-row__meta">${escapeHtml(capture.createdAt)} | ${escapeHtml(capture.boardName)}</span>
                  </button>
                `,
              )
              .join("")}
          </div>
        </section>

        <section class="td-panel td-pane td-pane--detail">
          ${
            !selectedCapture
              ? `<div class="td-empty"><p>Select a capture to inspect the raw note and decide what to do next.</p></div>`
              : `
                <article class="td-stack">
                  <div class="td-card__header">
                    <div>
                      <h2 class="td-section-title">Capture Detail</h2>
                      <p class="td-section-desc">${escapeHtml(selectedCapture.status)} | ${escapeHtml(selectedCapture.source)} | ${escapeHtml(selectedCapture.createdAt)}</p>
                    </div>
                  </div>
                  <pre class="td-detail-text">${escapeHtml(selectedCapture.rawText)}</pre>
                  ${
                    selectedCapture.linkedProposalId
                      ? `
                        <div class="td-chip-row">
                          <span class="td-cue">Linked proposal is ready for review.</span>
                          <button class="td-btn td-btn--primary td-btn--sm" type="button" data-route="review">Open Proposal</button>
                        </div>
                      `
                      : ""
                  }
                  <div class="td-chip-row">
                    <button class="td-btn td-btn--secondary" type="button" data-triage-capture="${selectedCapture.id}">Start Triage</button>
                    <button class="td-btn td-btn--danger" type="button" data-ignore-capture="${selectedCapture.id}">Ignore</button>
                    <button class="td-btn td-btn--secondary" type="button" data-route="review">Open Review</button>
                  </div>
                </article>
              `
          }
        </section>
      </div>
    </div>
  `;
}

function renderBoardView(currentBoard) {
  return `
    <div class="td-page td-page--board">
      <header class="td-panel td-board-header">
        <div class="td-board-header__title">
          <button class="td-back-button" type="button" data-route="home" aria-label="Back to Home">&larr;</button>
          <div>
            <h1 class="td-page-title">${escapeHtml(currentBoard.name)}</h1>
            <p class="td-subtitle">${escapeHtml(currentBoard.description)}</p>
            <div class="td-chip-row">
              <span class="td-cue">Live</span>
              <span class="td-cue">3 active collaborators</span>
              <span class="td-cue">${cardCount(currentBoard)} cards</span>
            </div>
          </div>
        </div>
        <div class="td-hero__actions">
          ${mockData.boards
            .map(
              (board) => `
                <button class="td-btn td-btn--secondary td-btn--sm" type="button" data-board-id="${board.id}" data-route="board">
                  ${escapeHtml(board.name)}
                </button>
              `,
            )
            .join("")}
        </div>
      </header>

      <section class="td-board-action-rail">
        <span class="td-board-action-rail__label">Board actions</span>
        <button class="td-btn td-btn--secondary td-btn--sm" type="button" data-route="inbox">Capture here</button>
        <button class="td-btn td-btn--secondary td-btn--sm" type="button" data-route="review">Review proposals</button>
        <button class="td-btn td-btn--secondary td-btn--sm" type="button" data-route="today">Open Today</button>
        <button class="td-btn td-btn--primary td-btn--sm" type="button">Add card</button>
      </section>

      ${renderHelpCallout(
        "What should happen on a board?",
        "Boards are where approved work lands. Capture here when new input belongs to this board, review proposals before applying changes, and use the board action rail to keep work anchored instead of bouncing between disconnected screens.",
        `
          <button class="td-btn td-btn--secondary td-btn--sm" type="button" data-route="inbox">Capture here</button>
          <button class="td-btn td-btn--secondary td-btn--sm" type="button" data-route="review">Review proposals</button>
        `,
      )}

      <section class="td-board-columns">
        ${currentBoard.columns
          .map(
            (column) => `
              <article class="td-board-column">
                <div class="td-board-column__header">
                  <div>
                    <h2 class="td-board-column__title">${escapeHtml(column.name)}</h2>
                    <span class="td-board-column__meta">${escapeHtml(column.wip)}</span>
                  </div>
                  <button class="td-handle" type="button">Drag</button>
                </div>
                <div class="td-stack">
                  ${column.cards
                    .map(
                      (card) => `
                        <div class="td-board-card">
                          <div class="td-board-card__drag">Drag card</div>
                          <h3 class="td-board-card__title">${escapeHtml(card.title)}</h3>
                          <p class="td-board-card__description">${escapeHtml(card.description)}</p>
                          <div class="td-chip-row">
                            ${card.labels
                              .map(
                                (label) =>
                                  `<span class="td-label" style="background:${label.colorHex}">${escapeHtml(label.name)}</span>`,
                              )
                              .join("")}
                          </div>
                          ${card.dueDate ? `<div class="td-board-card__due">${escapeHtml(card.dueDate)}</div>` : ""}
                        </div>
                      `,
                    )
                    .join("")}
                </div>
              </article>
            `,
          )
          .join("")}
      </section>
    </div>
  `;
}

function bindEvents() {
  document.querySelectorAll("[data-route]").forEach((element) => {
    element.addEventListener("click", () => {
      const route = element.getAttribute("data-route");
      const boardId = element.getAttribute("data-board-id");
      const captureId = element.getAttribute("data-capture-id");

      if (boardId) {
        state.currentBoardId = boardId;
      }

      if (captureId) {
        state.selectedCaptureId = captureId;
      }

      if (route) {
        navigate(route);
      }
    });
  });

  document.querySelectorAll("[data-capture-id]").forEach((element) => {
    if (element.hasAttribute("data-route")) {
      return;
    }

    element.addEventListener("click", () => {
      const captureId = element.getAttribute("data-capture-id");
      if (captureId) {
        setSelectedCapture(captureId);
      }
    });
  });

  document.querySelectorAll("[data-toggle-diff]").forEach((element) => {
    element.addEventListener("click", () => {
      const proposalId = element.getAttribute("data-toggle-diff");
      if (proposalId) {
        toggleProposalDiff(proposalId);
      }
    });
  });

  document.querySelectorAll("[data-approve-proposal]").forEach((element) => {
    element.addEventListener("click", () => {
      const proposalId = element.getAttribute("data-approve-proposal");
      if (proposalId) {
        updateProposalStatus(proposalId, "Approved");
      }
    });
  });

  document.querySelectorAll("[data-reject-proposal]").forEach((element) => {
    element.addEventListener("click", () => {
      const proposalId = element.getAttribute("data-reject-proposal");
      if (proposalId) {
        updateProposalStatus(proposalId, "Rejected");
      }
    });
  });

  document.querySelectorAll("[data-execute-proposal]").forEach((element) => {
    element.addEventListener("click", () => {
      const proposalId = element.getAttribute("data-execute-proposal");
      if (proposalId) {
        updateProposalStatus(proposalId, "Applied");
      }
    });
  });

  document.querySelectorAll("[data-triage-capture]").forEach((element) => {
    element.addEventListener("click", () => {
      const captureId = element.getAttribute("data-triage-capture");
      if (captureId) {
        triageCapture(captureId);
      }
    });
  });

  document.querySelectorAll("[data-ignore-capture]").forEach((element) => {
    element.addEventListener("click", () => {
      const captureId = element.getAttribute("data-ignore-capture");
      if (captureId) {
        ignoreCapture(captureId);
      }
    });
  });

  const modeSelect = document.getElementById("workspace-mode-select");
  if (modeSelect) {
    modeSelect.addEventListener("change", (event) => {
      updateMode(event.target.value);
    });
  }
}

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}
