/* Surface: Board (kanban) — with two card variants:
   A · Index-card (horizontal, dense, ledger-style serial number, stamp)
   B · Tag-card  (vertical, more breathing room, ribbon-style status)
*/
const PIb = window.PaperIcons;

function BoardSurface({ theme = "paper", cardVariant = "A" }) {
  return (
    <div className={theme} style={{ display: "flex", height: "100%", minHeight: 820, fontFamily: "var(--sans)" }}>
      <Sidebar active="B" theme={theme} />
      <div style={{ flex: 1, display: "flex", flexDirection: "column", minWidth: 0 }}>
        <TopBar crumb={["Workspace", "Boards", "Product Backlog"]} />

        {/* Board header */}
        <div style={{ padding: "24px 32px 0" }}>
          <div style={{ display: "flex", alignItems: "flex-end", justifyContent: "space-between", gap: 24 }}>
            <div>
              <div className="tk-eyebrow">Board · feature requests &amp; bug reports</div>
              <h1 className="tk-h1" style={{ margin: "6px 0 2px" }}>Product Backlog</h1>
              <div style={{ display: "flex", gap: 14, marginTop: 8, alignItems: "center" }}>
                <span className="status live">LIVE</span>
                <span className="tk-meta">12 cards · 3 columns · last applied 2:14pm</span>
                <span className="tk-meta">·</span>
                <span className="tk-meta" style={{ color: "var(--ember)" }}>1 proposal awaiting review</span>
              </div>
            </div>
            <div style={{ display: "flex", gap: 8 }}>
              <HLBtn icon={PIb.Filter} label="Filter" />
              <HLBtn icon={PIb.Tag}   label="Labels" />
              <HLBtn icon={PIb.Users} label="Members" />
              <HLBtn icon={PIb.Plus}  label="Capture here" kbd="C" />
              <HLBtn icon={PIb.Stamp} label="Review (1)" kbd="R" ember />
            </div>
          </div>

          {/* Board action rail */}
          <div style={{
            marginTop: 18, padding: "10px 14px",
            border: "1px solid var(--line)", borderRadius: 4,
            background: "var(--paper-card)",
            display: "flex", alignItems: "center", gap: 14, fontFamily: "var(--mono)", fontSize: 11, color: "var(--mute)",
          }}>
            <span className="tk-eyebrow" style={{ color: "var(--faint)" }}>Board actions</span>
            <span style={{ color: "var(--whisper)" }}>·</span>
            <a href="#" style={{ color: "var(--ink)", textDecoration: "none" }}>Capture here <span className="kbd-light kbd" style={{ marginLeft: 4 }}>C</span></a>
            <a href="#" style={{ color: "var(--ink)", textDecoration: "none" }}>Ask assistant <span className="kbd-light kbd" style={{ marginLeft: 4 }}>A</span></a>
            <a href="#" style={{ color: "var(--ember)", textDecoration: "none", fontWeight: 600 }}>Review proposals <span className="kbd-light kbd" style={{ marginLeft: 4 }}>R</span></a>
            <a href="#" style={{ color: "var(--ink)", textDecoration: "none" }}>Open Inbox <span className="kbd-light kbd" style={{ marginLeft: 4 }}>I</span></a>
            <span style={{ marginLeft: "auto", color: "var(--faint)" }}>Only approved changes land on this board.</span>
          </div>
        </div>

        {/* Columns */}
        <div style={{ flex: 1, padding: "24px 32px 32px", overflow: "auto" }}>
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr", gap: 18, alignItems: "flex-start" }}>
            <Column title="To Do" count={4} cards={[
              { sn: "C-104", title: "Set up CI pipeline", body: "Configure GitHub Actions for build and test.", labels: [["infra","var(--applied)"]], stamp: null, time: "2d", subtasks: [2,5] },
              { sn: "C-107", title: "Design landing page", body: "Create mockups for the new landing page.", labels: [["design","var(--ember)"]], stamp: "overdue", time: "30/05", overdue: true, subtasks: [0,3] },
              { sn: "C-112", title: "Audit ledger schema", body: "Review the current capture/proposal/applied table layout for v0.8.", labels: [["arch","var(--mute)"]], time: "wk 18" },
              { sn: "C-114", title: "Onboarding rewrite", body: "Three-step setup. No video. Cite the loop on each screen.", labels: [["copy","var(--ember)"]], time: "wk 19" },
            ]} variant={cardVariant} />

            <Column title="In Progress" count={3} cards={[
              { sn: "C-090", title: "Implement dark mode", body: "Apply Paper-at-Night tokens across all surfaces. Three-way variable swap.", labels: [["theme","var(--mute)"]], stamp: "proposed", time: "1d", subtasks: [3,5] },
              { sn: "C-101", title: "Local-first sync", body: "Conflict-free replicated capture log; resolve at apply-time.", labels: [["arch","var(--applied)"]], time: "3d", subtasks: [4,7] },
            ]} variant={cardVariant} />

            <Column title="Done" count={1} cards={[
              { sn: "C-082", title: "Write README", body: "Document setup and usage instructions.", labels: [["docs","var(--mute)"]], stamp: "applied", time: "yest" },
            ]} variant={cardVariant} />
          </div>
        </div>
      </div>
    </div>
  );
}

function Column({ title, count, cards, variant }) {
  return (
    <div className="well" style={{ padding: 14, minHeight: 540 }}>
      <header style={{ display: "flex", alignItems: "center", justifyContent: "space-between", padding: "0 4px 10px", borderBottom: "1px solid var(--line)" }}>
        <div style={{ display: "flex", alignItems: "baseline", gap: 8 }}>
          <span className="tk-eyebrow">{title}</span>
          <span className="tk-num" style={{ fontFamily: "var(--serif)", fontStyle: "italic", fontSize: 16, color: "var(--ink-deep)" }}>{count}</span>
        </div>
        <button className="btn-ghost" style={{ padding: 4 }}><PIb.Plus /></button>
      </header>
      <div style={{ paddingTop: 12, display: "flex", flexDirection: "column", gap: 10 }}>
        {cards.map((c, i) => variant === "B" ? <CardB key={i} {...c} /> : <CardA key={i} {...c} />)}
        <button className="btn" style={{ borderStyle: "dashed", color: "var(--mute)", justifyContent: "center", background: "transparent" }}>
          + Add card
        </button>
      </div>
    </div>
  );
}

/* CARD VARIANT A — Index card (horizontal serial number, dense) */
function CardA({ sn, title, body, labels, stamp, time, overdue, subtasks }) {
  const ringColor = stamp === "proposed" ? "var(--ember)" : stamp === "applied" ? "var(--applied)" : stamp === "overdue" ? "var(--overdue)" : null;
  return (
    <article className="card" style={{
      padding: "12px 14px", position: "relative",
      borderColor: ringColor || "var(--line)",
      boxShadow: stamp === "proposed" ? "0 0 0 4px var(--ember-bloom), var(--shadow-card)" : "var(--shadow-card)",
    }}>
      {/* serial across the top */}
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 6 }}>
        <span className="tk-serial">{sn}</span>
        <div style={{ display: "flex", gap: 6, alignItems: "center" }}>
          {stamp === "proposed" && <span className="tagstamp" style={{ color: "var(--ember)" }}>PROPOSED</span>}
          {stamp === "applied" && <span className="tagstamp" style={{ color: "var(--applied)" }}>APPLIED</span>}
          {stamp === "overdue" && <span className="tagstamp" style={{ color: "var(--overdue)" }}>OVERDUE</span>}
          <PIb.Drag style={{ color: "var(--whisper)" }} />
        </div>
      </div>
      <h4 style={{
        margin: "0 0 4px", fontFamily: "var(--serif)", fontWeight: 500,
        fontSize: 15.5, lineHeight: 1.18, letterSpacing: "-.005em",
        color: stamp === "applied" ? "var(--mute)" : "var(--ink-deep)",
        textDecoration: stamp === "applied" ? "line-through" : "none",
        textDecorationColor: "var(--applied)",
      }}>{title}</h4>
      <p className="tk-body" style={{ margin: 0, color: "var(--ink-2)", fontSize: 12.5, lineHeight: 1.45 }}>{body}</p>
      <div style={{ marginTop: 10, paddingTop: 8, borderTop: "1px dashed var(--line-soft)", display: "flex", alignItems: "center", gap: 10, fontFamily: "var(--mono)", fontSize: 10, color: "var(--mute)" }}>
        {labels.map(([l, c], i) => (
          <span key={i} style={{ color: c, letterSpacing: ".14em", textTransform: "uppercase" }}>· {l}</span>
        ))}
        <span style={{ flex: 1 }} />
        {subtasks && <span>{subtasks[0]}/{subtasks[1]}</span>}
        <span style={{ color: overdue ? "var(--overdue)" : "var(--mute)" }}>{time}</span>
      </div>
    </article>
  );
}

/* CARD VARIANT B — Tag card (ribbon top, more breathing) */
function CardB({ sn, title, body, labels, stamp, time, overdue, subtasks }) {
  const ribbonColor = stamp === "proposed" ? "var(--ember)" : stamp === "applied" ? "var(--applied)" : stamp === "overdue" ? "var(--overdue)" : "var(--ink-deep)";
  return (
    <article className="card" style={{
      padding: 0, overflow: "hidden",
      boxShadow: stamp === "proposed" ? "0 0 0 4px var(--ember-bloom), var(--shadow-card)" : "var(--shadow-card)",
      borderColor: stamp === "proposed" ? "var(--ember)" : "var(--line)",
    }}>
      {/* Ribbon */}
      <div style={{
        display: "flex", alignItems: "center", justifyContent: "space-between",
        padding: "8px 14px",
        background: stamp === "proposed" ? "var(--ember-tint)" : stamp === "applied" ? "var(--applied-tint)" : stamp === "overdue" ? "var(--overdue-tint)" : "var(--paper-2)",
        borderBottom: "1px solid var(--line-soft)",
        fontFamily: "var(--mono)", fontSize: 10, letterSpacing: ".14em", textTransform: "uppercase",
        color: ribbonColor,
      }}>
        <span style={{ display: "flex", alignItems: "center", gap: 8 }}>
          <span style={{ width: 4, height: 4, borderRadius: 4, background: ribbonColor }} />
          {stamp || "draft"} · <span style={{ color: "var(--mute)" }}>{sn}</span>
        </span>
        <PIb.Drag style={{ color: "var(--whisper)" }} />
      </div>

      <div style={{ padding: "14px 16px 14px" }}>
        <h4 style={{
          margin: 0,
          fontFamily: "var(--serif)", fontWeight: 500, fontSize: 17, lineHeight: 1.2,
          letterSpacing: "-.008em", color: "var(--ink-deep)",
          textDecoration: stamp === "applied" ? "line-through" : "none",
          textDecorationColor: "var(--applied)",
        }}>{title}</h4>
        <p className="tk-body" style={{ margin: "6px 0 0", fontSize: 12.5, color: "var(--ink-2)" }}>{body}</p>
      </div>

      <footer style={{
        display: "flex", alignItems: "center", gap: 12,
        padding: "8px 14px", borderTop: "1px solid var(--line-soft)",
        fontFamily: "var(--mono)", fontSize: 10, color: "var(--mute)",
      }}>
        {labels.map(([l, c], i) => (
          <span key={i} style={{ color: c, letterSpacing: ".14em", textTransform: "uppercase", display: "inline-flex", alignItems: "center", gap: 4 }}>
            <span style={{ width: 6, height: 6, border: `1px solid ${c}`, borderRadius: "50%" }} />{l}
          </span>
        ))}
        <span style={{ flex: 1 }} />
        {subtasks && <span style={{ display: "inline-flex", alignItems: "center", gap: 4 }}>
          <span style={{ width: 14, height: 4, background: "var(--line)", borderRadius: 2, position: "relative", overflow: "hidden" }}>
            <span style={{ position: "absolute", inset: 0, width: `${(subtasks[0]/subtasks[1])*100}%`, background: "var(--ink-deep)" }} />
          </span>
          {subtasks[0]}/{subtasks[1]}
        </span>}
        <span style={{ color: overdue ? "var(--overdue)" : "var(--mute)" }}>{time}</span>
      </footer>
    </article>
  );
}

window.BoardSurface = BoardSurface;
