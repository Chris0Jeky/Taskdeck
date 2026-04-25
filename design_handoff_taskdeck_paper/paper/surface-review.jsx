/* Surface: Review — the most important screen.
   Two variants:
   A · Letterpress · the proposal card embosses; before/after diff stacked
   B · Side-by-side ledger · two columns, before/after, with stamp seal
*/
const PIr = window.PaperIcons;

function ReviewSurface({ theme = "paper", variant = "A" }) {
  return (
    <div className={theme} style={{ display: "flex", height: "100%", minHeight: 820, fontFamily: "var(--sans)" }}>
      <Sidebar active="R" theme={theme} />
      <div style={{ flex: 1, display: "flex", flexDirection: "column", minWidth: 0 }}>
        <TopBar crumb={["Workspace", "Review", "Proposal #014"]} />

        <div style={{ flex: 1, display: "grid", gridTemplateColumns: "260px 1fr", minHeight: 0 }}>
          {/* Queue rail */}
          <aside style={{ borderRight: "1px solid var(--line)", padding: "20px 0", background: "var(--paper-2)", overflow: "auto" }}>
            <div className="tk-eyebrow" style={{ padding: "0 18px 10px" }}>Queue · 3 awaiting</div>
            <QueueItem active sn="#014" title="Split: Implement dark mode" author="haiku" conf="0.84" age="4s" />
            <QueueItem sn="#013" title="Triage 3 captures from Inbox" author="capture" conf="—" age="32m" />
            <QueueItem sn="#012" title="Add column 'Blocked' to Sprint 12" author="haiku" conf="0.71" age="1d" />
            <div style={{ marginTop: 16, padding: "10px 18px", borderTop: "1px solid var(--line-soft)" }}>
              <div className="tk-eyebrow" style={{ marginBottom: 6 }}>Recently applied · undoable</div>
              <UndoItem sn="#011" title="Move 'Set up CI' → Done" t="2:14pm · 5h 48m left" />
              <UndoItem sn="#009" title="Rename board to 'Product Backlog'" t="yest · expired" expired />
            </div>
          </aside>

          {/* Main review pane */}
          {variant === "B" ? <ReviewPaneB /> : <ReviewPaneA />}
        </div>
      </div>
    </div>
  );
}

function QueueItem({ active, sn, title, author, conf, age }) {
  return (
    <a href="#" style={{
      display: "block", padding: "12px 18px",
      textDecoration: "none", color: "var(--ink)",
      borderLeft: active ? "2px solid var(--ember)" : "2px solid transparent",
      background: active ? "linear-gradient(90deg, var(--ember-bloom) 0%, transparent 70%)" : "transparent",
    }}>
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 4 }}>
        <span className="tk-serial" style={{ color: active ? "var(--ember)" : "var(--faint)" }}>{sn}</span>
        <span className="tk-meta" style={{ fontSize: 9.5 }}>{age}</span>
      </div>
      <div style={{ fontFamily: "var(--serif)", fontSize: 14, fontWeight: 500, lineHeight: 1.25, marginBottom: 4, color: active ? "var(--ink-deep)" : "var(--ink)" }}>
        {title}
      </div>
      <div className="tk-meta" style={{ fontSize: 10 }}>{author} · conf {conf}</div>
    </a>
  );
}
function UndoItem({ sn, title, t, expired }) {
  return (
    <div style={{ padding: "8px 0", borderBottom: "1px solid var(--line-soft)", fontSize: 11.5, color: expired ? "var(--faint)" : "var(--ink-2)" }}>
      <div style={{ display: "flex", justifyContent: "space-between", marginBottom: 2 }}>
        <span className="tk-serial">{sn}</span>
        {!expired && <a href="#" style={{ fontFamily: "var(--mono)", fontSize: 10, color: "var(--ember)", textDecoration: "none", borderBottom: "1px solid var(--ember)" }}>↶ Undo</a>}
      </div>
      <div style={{ lineHeight: 1.35 }}>{title}</div>
      <div className="tk-meta" style={{ fontSize: 9.5, marginTop: 2, color: expired ? "var(--faint)" : "var(--mute)" }}>{t}</div>
    </div>
  );
}

/* ----------------- Variant A · Letterpress ----------------- */
function ReviewPaneA() {
  return (
    <div style={{ padding: "32px 40px 40px", overflow: "auto", background: "var(--paper)", position: "relative" }}>
      {/* Frontispiece */}
      <div style={{ display: "flex", alignItems: "flex-start", justifyContent: "space-between", gap: 32, marginBottom: 24 }}>
        <div style={{ flex: 1 }}>
          <div className="tk-eyebrow">Proposal · #2026-04-25-014 · awaiting decision</div>
          <h1 className="tk-h1" style={{ margin: "8px 0 4px", maxWidth: 640 }}>
            Split <em>"Implement dark mode"</em> into <em>three smaller cards.</em>
          </h1>
          <p className="tk-lede" style={{ marginTop: 8 }}>
            Haiku read the card body, the linked design doc, and the recent activity log on this board. It's confident this card spans more than a single afternoon's work; the proposal below preserves the original assignee and labels.
          </p>
        </div>

        {/* Author seal */}
        <div className="card" style={{ padding: 14, width: 240, flex: "none" }}>
          <div className="tk-eyebrow" style={{ marginBottom: 8 }}>Provenance</div>
          <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
            <PIr.Sparkle />
            <div>
              <div style={{ fontWeight: 500, fontSize: 13 }}>Haiku · local</div>
              <div className="tk-meta" style={{ fontSize: 10 }}>4s ago · 0.84 confidence</div>
            </div>
          </div>
          <hr className="hr-soft" style={{ margin: "10px 0" }} />
          <ul style={{ margin: 0, padding: 0, listStyle: "none", fontFamily: "var(--mono)", fontSize: 10.5, color: "var(--mute)", lineHeight: 1.7 }}>
            <li>· Read 1 card body</li>
            <li>· Read 1 linked doc</li>
            <li>· Read 7 prior activity entries</li>
            <li>· No private context surfaced</li>
          </ul>
          <hr className="hr-soft" style={{ margin: "10px 0" }} />
          <div className="tk-meta" style={{ fontSize: 10 }}>Reach: <b style={{ color: "var(--ink)" }}>3 cards · 1 board</b></div>
          <div className="tk-meta" style={{ fontSize: 10 }}>Reversible: <b style={{ color: "var(--ink)" }}>6h · single keystroke</b></div>
        </div>
      </div>

      {/* Diff card — letterpress impression */}
      <div className="card-lift halo-ember" style={{ padding: 0, overflow: "hidden" }}>
        <header style={{ padding: "14px 22px", borderBottom: "1px solid var(--line-soft)", display: "flex", alignItems: "center", gap: 12 }}>
          <span className="tagstamp" style={{ color: "var(--ember)" }}>PROPOSED · DIFF</span>
          <span className="tk-meta">Product Backlog · column "In Progress"</span>
          <span style={{ flex: 1 }} />
          <span className="tk-meta">3 changes</span>
        </header>

        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", borderBottom: "1px solid var(--line-soft)" }}>
          {/* Before */}
          <div style={{ padding: 22, borderRight: "1px solid var(--line-soft)" }}>
            <div className="tk-eyebrow" style={{ marginBottom: 10 }}>Before</div>
            <div className="card" style={{ padding: 12 }}>
              <div className="tk-serial">C-090</div>
              <h4 style={{ margin: "4px 0 4px", fontFamily: "var(--serif)", fontSize: 15, fontWeight: 500 }}>Implement dark mode</h4>
              <p className="tk-body" style={{ margin: 0, fontSize: 12.5, color: "var(--ink-2)" }}>Apply Paper-at-Night tokens across all surfaces. Three-way variable swap.</p>
              <div className="tk-meta" style={{ fontSize: 10, marginTop: 8 }}>· theme · 1d · 0/0</div>
            </div>
          </div>
          {/* After */}
          <div style={{ padding: 22, position: "relative" }}>
            <div className="tk-eyebrow" style={{ marginBottom: 10, color: "var(--ember)" }}>After</div>
            <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
              <DiffCard sn="C-090" title="Tokens · darken & QA" body="Migrate the token sheet; verify contrast at AA on every surface." />
              <DiffCard sn="C-090a" title="Components · mode switch" body="All components use semantic vars; ship a `data-theme` toggle." add />
              <DiffCard sn="C-090b" title="Hand-off · screenshots & PR" body="Capture every surface in both modes. PR with QA evidence." add />
            </div>
          </div>
        </div>

        {/* Decision rail */}
        <footer style={{ padding: "16px 22px", display: "flex", alignItems: "center", gap: 14, background: "var(--paper-2)" }}>
          <span className="tk-meta">
            On apply, <b style={{ color: "var(--ink)" }}>3 cards land</b> · the original card is <span className="erase-line">archived for 30 days</span> · undo for 6 hours.
          </span>
          <span style={{ flex: 1 }} />
          <HLBtn icon={PIr.X} label="Reject" kbd="⌫" />
          <HLBtn icon={PIr.Pages} label="Request edit" kbd="E" />
          <HLBtn icon={PIr.Stamp} label="Apply proposal" kbd="⏎" ember />
        </footer>
      </div>

      {/* Trust strip */}
      <div style={{ marginTop: 24, padding: "16px 0", borderTop: "1px solid var(--line)", borderBottom: "1px solid var(--line)", display: "flex", gap: 28 }}>
        <Trust k="Provenance" v="haiku · local · model card linked" />
        <Trust k="Reach" v="3 cards · 1 board · no other tables" />
        <Trust k="Reversibility" v="6 hours · single keystroke" />
        <Trust k="Side effects" v="None — applies are atomic" />
      </div>
    </div>
  );
}

function DiffCard({ sn, title, body, add }) {
  return (
    <div className={`card ${add ? "" : ""}`} style={{
      padding: 12,
      borderColor: add ? "var(--applied)" : "var(--line)",
      background: add ? "linear-gradient(90deg, #d8e0ce40 0%, var(--paper-card) 70%)" : "var(--paper-card)",
      borderLeft: add ? "2px solid var(--applied)" : "1px solid var(--line)",
    }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
        <span className="tk-serial">{sn} {add && <span style={{ color: "var(--applied)", marginLeft: 4 }}>· new</span>}</span>
      </div>
      <h4 style={{ margin: "4px 0 4px", fontFamily: "var(--serif)", fontSize: 14.5, fontWeight: 500 }}>{title}</h4>
      <p className="tk-body" style={{ margin: 0, fontSize: 12, color: "var(--ink-2)" }}>{body}</p>
    </div>
  );
}
function Trust({ k, v }) {
  return (
    <div style={{ flex: 1 }}>
      <div className="tk-eyebrow" style={{ marginBottom: 4 }}>{k}</div>
      <div style={{ fontFamily: "var(--serif)", fontSize: 14, color: "var(--ink-deep)", fontWeight: 400, fontStyle: "italic" }}>{v}</div>
    </div>
  );
}

/* ----------------- Variant B · Side-by-side with stamp ----------------- */
function ReviewPaneB() {
  return (
    <div style={{ padding: "32px 40px 40px", overflow: "auto", background: "var(--paper)", position: "relative" }}>
      <div style={{ display: "grid", gridTemplateColumns: "1fr 280px", gap: 32, marginBottom: 24 }}>
        <div>
          <div className="tk-eyebrow">Proposal · #2026-04-25-014</div>
          <h1 className="tk-h1" style={{ margin: "8px 0 8px" }}>Split <em>"Implement dark mode"</em></h1>
          <p className="tk-lede">Haiku proposes three replacements. The board on the left is what's there; the board on the right is what would land.</p>
        </div>
        <div className="card" style={{ padding: 16, position: "relative" }}>
          <Stamp kind="proposed" date="Apr 25" time="11:42" num="014" style={{ position: "absolute", right: 16, top: 16, transform: "rotate(-7deg)" }} />
          <div className="tk-eyebrow" style={{ marginBottom: 8 }}>Decision</div>
          <p className="tk-body" style={{ fontSize: 12, margin: "0 0 10px", color: "var(--ink-2)" }}>
            On apply, 3 cards land. Reversible for 6 hours. Atomic.
          </p>
          <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
            <HLBtn icon={PIr.Stamp} label="Apply proposal" kbd="⏎" ember />
            <HLBtn icon={PIr.Pages} label="Request edit" kbd="E" />
            <HLBtn icon={PIr.X} label="Reject" kbd="⌫" />
          </div>
        </div>
      </div>

      {/* Side by side boards */}
      <div style={{ display: "grid", gridTemplateColumns: "1fr 14px 1fr", gap: 0, alignItems: "stretch" }}>
        <BoardSnap title="Before · today" cards={[
          { sn: "C-090", title: "Implement dark mode", body: "Apply Paper-at-Night tokens across all surfaces.", state: "now" },
        ]} />
        <div style={{ display: "flex", alignItems: "center", justifyContent: "center", color: "var(--ember)" }}>
          <PIr.Arrow />
        </div>
        <BoardSnap title="After · on apply" cards={[
          { sn: "C-090",  title: "Tokens · darken & QA",            body: "Migrate the token sheet; verify contrast at AA.", state: "kept" },
          { sn: "C-090a", title: "Components · mode switch",        body: "All components use semantic vars; ship a `data-theme` toggle.", state: "new" },
          { sn: "C-090b", title: "Hand-off · screenshots & PR",      body: "Capture every surface in both modes. PR with QA evidence.", state: "new" },
        ]} />
      </div>
    </div>
  );
}

function BoardSnap({ title, cards }) {
  return (
    <div className="well" style={{ padding: 14 }}>
      <div className="tk-eyebrow" style={{ padding: "0 4px 10px", borderBottom: "1px solid var(--line)", marginBottom: 10 }}>{title}</div>
      <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
        {cards.map((c, i) => (
          <div key={i} className="card" style={{
            padding: 12,
            borderColor: c.state === "new" ? "var(--applied)" : "var(--line)",
            borderLeft: c.state === "new" ? "2px solid var(--applied)" : c.state === "now" ? "2px solid var(--ember)" : "1px solid var(--line)",
          }}>
            <div style={{ display: "flex", justifyContent: "space-between" }}>
              <span className="tk-serial">{c.sn}</span>
              <span className="tk-serial" style={{ color: c.state === "new" ? "var(--applied)" : c.state === "now" ? "var(--ember)" : "var(--faint)" }}>
                {c.state === "new" ? "new" : c.state === "now" ? "current" : "kept"}
              </span>
            </div>
            <h5 style={{ margin: "4px 0 4px", fontFamily: "var(--serif)", fontSize: 14, fontWeight: 500 }}>{c.title}</h5>
            <p className="tk-body" style={{ margin: 0, fontSize: 12, color: "var(--ink-2)" }}>{c.body}</p>
          </div>
        ))}
      </div>
    </div>
  );
}

window.ReviewSurface = ReviewSurface;
