/* Narrow companion: 768 (tablet) and 375 (smartphone) — Home + Capture */
const PIn = window.PaperIcons;

function NarrowSurface({ theme = "paper", width = 375 }) {
  const isPhone = width <= 420;
  return (
    <div className={theme} style={{
      width, height: isPhone ? 760 : 1000,
      borderRadius: 18, border: "1px solid var(--line)",
      overflow: "hidden", boxShadow: "var(--shadow-lift)",
      display: "flex", flexDirection: "column",
      fontFamily: "var(--sans)",
    }}>
      {/* Phone status bar */}
      {isPhone && (
        <div style={{ height: 24, background: "var(--paper)", display: "flex", alignItems: "center", justifyContent: "space-between", padding: "0 18px", fontFamily: "var(--mono)", fontSize: 10, color: "var(--ink-deep)" }}>
          <span>9:41</span>
          <span>· · ·</span>
        </div>
      )}
      {/* Top bar */}
      <header style={{
        padding: isPhone ? "12px 16px" : "16px 24px",
        borderBottom: "1px solid var(--line)",
        display: "flex", alignItems: "center", justifyContent: "space-between",
        background: "var(--paper)",
      }}>
        <div>
          <div style={{ fontFamily: "var(--serif)", fontWeight: 500, fontSize: 16, color: "var(--ink-deep)" }}>Taskdeck</div>
          <div className="tk-eyebrow" style={{ fontSize: 8.5, marginTop: 1 }}>09:42 PT · Friday</div>
        </div>
        <div style={{ display: "flex", gap: 6 }}>
          <button className="btn-ghost btn" style={{ padding: 6 }}><PIn.Bell /></button>
          <button className="btn-ghost btn" style={{ padding: 6 }}><PIn.Cmd /></button>
        </div>
      </header>

      <div style={{ flex: 1, padding: isPhone ? "20px 16px 80px" : "24px 24px 80px", overflow: "auto" }}>
        <div className="tk-eyebrow">Workspace</div>
        <h1 className="tk-h2" style={{ margin: "4px 0 4px", fontSize: isPhone ? 26 : 30 }}>
          Good morning, <em>Daniel.</em>
        </h1>
        <p className="tk-body" style={{ margin: "0 0 18px", fontSize: 13.5, color: "var(--ink-2)" }}>
          Three captures await. One proposal awaits your decision.
        </p>

        {/* Three large action tiles */}
        <div style={{ display: "grid", gridTemplateColumns: isPhone ? "1fr" : "1fr 1fr", gap: 10 }}>
          <NarrowAction n="01" title="Capture" sub="Drop the thought" k="C" />
          <NarrowAction n="02" title="Review" sub="One awaits decision" k="R" ember />
          <NarrowAction n="03" title="Apply" sub="Land approved work" k="A" />
        </div>

        {/* Needs attention list */}
        <section style={{ marginTop: 22 }}>
          <div className="tk-eyebrow" style={{ marginBottom: 8 }}>Needs attention</div>
          <div className="card" style={{ padding: 0, overflow: "hidden" }}>
            <NarrowRow sn="#014" title="Split: Implement dark mode → 3 cards" tag="proposal" emb />
            <NarrowRow sn="#013" title="Triage 3 captures from Inbox" tag="triage" />
            <NarrowRow sn="#010" title="Card overdue: Design landing page" tag="overdue" overdue />
          </div>
        </section>

        {/* Capture bar (sticky on phone) */}
        {isPhone && (
          <div style={{
            position: "absolute", left: 0, right: 0, bottom: 0,
            background: "var(--paper-card)", borderTop: "1px solid var(--line)",
            padding: "10px 16px 14px",
            display: "flex", alignItems: "center", gap: 10,
            boxShadow: "0 -8px 22px -10px #1a18141a",
          }}>
            <PIn.Quill />
            <div style={{ flex: 1, fontFamily: "var(--serif)", fontStyle: "italic", fontSize: 14, color: "var(--mute)" }}>What's on your mind?</div>
            <button className="btn btn-ember" style={{ padding: "6px 12px", fontSize: 11 }}>Capture</button>
          </div>
        )}
      </div>
    </div>
  );
}

function NarrowAction({ n, title, sub, k, ember }) {
  return (
    <a href="#" className={ember ? "card-lift halo-ember" : "card"} style={{
      padding: 14, textDecoration: "none", color: "inherit",
      display: "grid", gridTemplateColumns: "auto 1fr auto", alignItems: "center", gap: 12,
    }}>
      <span className="tk-serial">{n}</span>
      <div>
        <div style={{ fontFamily: "var(--serif)", fontWeight: 500, fontSize: 16, color: ember ? "var(--ember)" : "var(--ink-deep)" }}>{title}</div>
        <div className="tk-meta" style={{ fontSize: 10.5 }}>{sub}</div>
      </div>
      <span className="kbd">{k}</span>
    </a>
  );
}
function NarrowRow({ sn, title, tag, emb, overdue }) {
  return (
    <div style={{ padding: "12px 14px", borderBottom: "1px solid var(--line-soft)", display: "flex", alignItems: "center", gap: 12 }}>
      <span className="tk-serial" style={{ width: 40 }}>{sn}</span>
      <div style={{ flex: 1 }}>
        <div style={{ fontSize: 13, color: "var(--ink)", lineHeight: 1.3 }}>{title}</div>
        <div className="tk-meta" style={{ fontSize: 10, color: emb ? "var(--ember)" : overdue ? "var(--overdue)" : "var(--mute)", marginTop: 2 }}>· {tag}</div>
      </div>
      <PIn.ChevronR />
    </div>
  );
}

window.NarrowSurface = NarrowSurface;
