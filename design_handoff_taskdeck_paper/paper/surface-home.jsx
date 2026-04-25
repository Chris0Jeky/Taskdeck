/* Surface: Home / reset
   "Where do I begin?" — setup loop, needs-attention tiles, recent boards.
*/
const PIh = window.PaperIcons;

function HomeSurface({ theme = "paper", variant = "A" }) {
  return (
    <div className={theme} style={{ display: "flex", height: "100%", minHeight: 760, fontFamily: "var(--sans)" }}>
      <Sidebar active="H" theme={theme} />
      <div style={{ flex: 1, display: "flex", flexDirection: "column", minWidth: 0 }}>
        <TopBar crumb={["Workspace", "Home"]} />
        <div style={{ flex: 1, padding: "32px 40px 56px", overflow: "hidden" }}>

          {/* Frontispiece */}
          <div style={{ display: "grid", gridTemplateColumns: "1fr auto", alignItems: "end", gap: 32, marginBottom: 8 }}>
            <div>
              <div className="tk-eyebrow">Workspace · 09:42 PT · Friday</div>
              <h1 className="tk-display" style={{ margin: "10px 0 6px" }}>
                Good morning, <em>Daniel.</em>
              </h1>
              <p className="tk-lede" style={{ marginTop: 4 }}>
                Three captures await triage. One proposal awaits your decision.
                Begin in <span className="tk-ink-italic">Inbox</span>, end on the <span className="tk-ink-italic">Board</span>.
              </p>
            </div>
            <div style={{ display: "flex", gap: 10 }}>
              <HLBtn icon={PIh.Inbox} label="Open Inbox" kbd="I" />
              <HLBtn icon={PIh.Quill} label="Capture a note" kbd="C" />
              <HLBtn icon={PIh.Stamp} label="Review proposals" kbd="R" ember />
            </div>
          </div>

          <hr className="hr-double" style={{ margin: "28px 0 0" }} />

          {/* Setup loop */}
          <section style={{ marginTop: 28 }}>
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "baseline", marginBottom: 14 }}>
              <h2 className="tk-h2">The loop</h2>
              <span className="tk-meta">Capture <span style={{ color: "var(--whisper)", margin: "0 6px" }}>·</span> Review <span style={{ color: "var(--whisper)", margin: "0 6px" }}>·</span> Apply</span>
            </div>
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr", gap: 16 }}>
              <LoopStep n="01" title="Capture" status="done" body="Drop a quick thought into the Inbox. Three items captured this morning." action="Captured · 3 items" />
              <LoopStep n="02" title="Review" status="now" body="One proposal awaits your decision. Approve or reject before it touches the board." action="One waiting · ⏎ to open" highlight />
              <LoopStep n="03" title="Apply" status="next" body="Approved changes land on the board, with a six-hour reversibility window." action="—" />
            </div>
          </section>

          {/* Two-column: Needs attention + Boards */}
          <div style={{ display: "grid", gridTemplateColumns: "1.4fr 1fr", gap: 24, marginTop: 32 }}>

            {/* Needs attention */}
            <section className="card" style={{ padding: 0, overflow: "hidden" }}>
              <header style={{ padding: "14px 18px", borderBottom: "1px solid var(--line-soft)", display: "flex", alignItems: "center", justifyContent: "space-between" }}>
                <div>
                  <div className="tk-eyebrow">III · Needs attention</div>
                  <h3 className="tk-h3" style={{ margin: "2px 0 0" }}>Open ledger</h3>
                </div>
                <span className="tk-meta">7 entries · <span style={{ color: "var(--ember)" }}>1 awaiting review</span></span>
              </header>
              <div>
                <LedgerRow idx="#014" title="Split: Implement dark mode → 3 cards"
                           meta="haiku · 4s ago · 0.84 conf"
                           status={{ kind: "proposed", label: "Proposed" }} />
                <LedgerRow idx="#013" title="Triage: 3 captures from this morning"
                           meta="capture · 09:42 PT"
                           status={{ kind: "draft", label: "Awaits triage" }} />
                <LedgerRow idx="#012" title="Add column 'Blocked' to Sprint 12"
                           meta="haiku · yesterday · 0.71 conf"
                           status={{ kind: "draft", label: "Awaits decision" }} />
                <LedgerRow idx="#011" title="Move 'Set up CI pipeline' to Done"
                           meta="applied · 2:14pm · 6h undo"
                           status={{ kind: "applied", label: "Applied" }} />
                <LedgerRow idx="#010" title="Card overdue: Design landing page"
                           meta="due 30/05 · still in To Do"
                           status={{ kind: "overdue", label: "Overdue" }} />
              </div>
              <footer style={{ padding: "10px 18px", borderTop: "1px solid var(--line-soft)", display: "flex", justifyContent: "space-between" }}>
                <span className="tk-meta">Most recent first · ledger #2026-04-25</span>
                <a href="#" style={{ fontFamily: "var(--mono)", fontSize: 11, color: "var(--ink)", textDecoration: "none", borderBottom: "1px solid var(--line)" }}>Open full ledger →</a>
              </footer>
            </section>

            {/* Boards */}
            <section style={{ display: "flex", flexDirection: "column", gap: 16 }}>
              <header style={{ display: "flex", alignItems: "baseline", justifyContent: "space-between" }}>
                <div>
                  <div className="tk-eyebrow">II · Recent boards</div>
                  <h3 className="tk-h3" style={{ margin: "2px 0 0" }}>Where work lives</h3>
                </div>
                <span className="tk-meta">2 active</span>
              </header>
              <BoardCard title="Product Backlog" sub="Feature requests &amp; bug reports" stats={[["12","To Do"],["3","In Progress"],["28","Done"]]} live />
              <BoardCard title="Sprint 12" sub="Current sprint work items" stats={[["6","To Do"],["4","In Progress"],["2","Done"]]} />
              <button className="btn" style={{ alignSelf: "stretch", justifyContent: "center", padding: "12px", borderStyle: "dashed", color: "var(--mute)" }}>
                <PIh.Plus /> New board
              </button>
            </section>
          </div>

          {/* Footer mark */}
          <footer style={{ marginTop: 40, paddingTop: 16, borderTop: "1px solid var(--line-soft)", display: "flex", justifyContent: "space-between" }}>
            <span className="tk-serial">TASKDECK · LEDGER 2026-W17 · ENTRY #014 · LOCAL-FIRST</span>
            <span className="tk-serial">PAPER &amp; GRAPHITE / EMBER EDITION</span>
          </footer>
        </div>
      </div>
    </div>
  );
}

function LoopStep({ n, title, status, body, action, highlight }) {
  const statusMap = { done: "Captured", now: "Awaiting review", next: "Up next" };
  return (
    <div className={highlight ? "card-lift halo-ember" : "card"} style={{ padding: 18, position: "relative" }}>
      <div style={{ display: "flex", alignItems: "baseline", gap: 12, marginBottom: 8 }}>
        <span className="tk-serial" style={{ color: "var(--faint)" }}>{n}</span>
        <h3 className="tk-h3" style={{ margin: 0 }}>{title}</h3>
        {status === "now" && (
          <span className="tagstamp" style={{ color: "var(--ember)", marginLeft: "auto" }}>NOW</span>
        )}
        {status === "done" && (
          <span className="tagstamp" style={{ color: "var(--applied)", marginLeft: "auto" }}>DONE</span>
        )}
      </div>
      <p className="tk-body" style={{ margin: 0, color: "var(--ink-2)", fontSize: 13 }}>{body}</p>
      <div style={{ marginTop: 14, paddingTop: 12, borderTop: "1px solid var(--line-soft)", display: "flex", alignItems: "center", justifyContent: "space-between" }}>
        <span className="tk-meta">{action}</span>
        {highlight && <span className="kbd">⏎</span>}
      </div>
    </div>
  );
}

function BoardCard({ title, sub, stats, live }) {
  return (
    <a href="#" className="card" style={{ padding: 16, textDecoration: "none", color: "inherit", display: "block" }}>
      <div style={{ display: "flex", alignItems: "baseline", justifyContent: "space-between", marginBottom: 4 }}>
        <h4 className="tk-h3" style={{ margin: 0, fontSize: 18 }}>{title}</h4>
        {live && <span className="status live">LIVE</span>}
      </div>
      <p className="tk-meta" style={{ margin: "0 0 12px", color: "var(--mute)" }}>{sub}</p>
      <div style={{ display: "flex", gap: 18, paddingTop: 10, borderTop: "1px solid var(--line-soft)" }}>
        {stats.map(([n, l]) => (
          <div key={l}>
            <div className="tk-num" style={{ fontFamily: "var(--serif)", fontWeight: 400, fontSize: 22, color: "var(--ink-deep)", lineHeight: 1 }}>{n}</div>
            <div className="tk-eyebrow" style={{ marginTop: 2 }}>{l}</div>
          </div>
        ))}
      </div>
    </a>
  );
}

window.HomeSurface = HomeSurface;
