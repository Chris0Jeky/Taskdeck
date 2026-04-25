/* Surfaces: Card Detail, Command Palette, Toasts, Empty States, Shortcuts overlay */
const PIx = window.PaperIcons;

/* ============== CARD DETAIL ============== */
function CardDetailSurface({ theme = "paper" }) {
  return (
    <div className={theme} style={{ display: "flex", height: "100%", minHeight: 820, fontFamily: "var(--sans)" }}>
      <Sidebar active="B" theme={theme} />
      <div style={{ flex: 1, display: "flex", flexDirection: "column", minWidth: 0, position: "relative" }}>
        <TopBar crumb={["Workspace", "Boards", "Product Backlog", "C-090"]} />
        {/* dimmed board behind */}
        <div style={{ flex: 1, position: "relative", background: "var(--paper)", overflow: "hidden" }}>
          <div style={{ position: "absolute", inset: 0, padding: 24, opacity: .4, filter: "blur(2px)", pointerEvents: "none" }}>
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr", gap: 18, height: "100%" }}>
              <div className="well" /><div className="well" /><div className="well" />
            </div>
          </div>
          {/* card detail panel */}
          <div className="card-lift" style={{
            position: "relative", margin: "32px auto", maxWidth: 880,
            padding: 0, overflow: "hidden",
          }}>
            <header style={{ padding: "22px 28px", borderBottom: "1px solid var(--line-soft)", display: "flex", alignItems: "flex-start", gap: 18 }}>
              <div style={{ flex: 1 }}>
                <div className="tk-eyebrow">C-090 · in progress · Product Backlog</div>
                <h1 className="tk-h2" style={{ margin: "6px 0 0" }}>Implement <em>dark mode</em></h1>
              </div>
              <div style={{ display: "flex", gap: 6 }}>
                <button className="btn"><PIx.Link /></button>
                <button className="btn"><PIx.More /></button>
                <button className="btn"><PIx.X /></button>
              </div>
            </header>
            <div style={{ display: "grid", gridTemplateColumns: "1fr 240px", gap: 0 }}>
              <div style={{ padding: "22px 28px" }}>
                <p className="tk-lede" style={{ marginTop: 0 }}>Apply Paper-at-Night tokens across all surfaces. Three-way variable swap, AA contrast verified per surface.</p>
                <div className="tk-eyebrow" style={{ marginTop: 22, marginBottom: 8 }}>Subtasks · 3 of 5</div>
                <ul style={{ listStyle: "none", margin: 0, padding: 0 }}>
                  {[
                    ["Migrate token sheet", true],
                    ["Audit ladder contrast at AA", true],
                    ["Add data-theme switch", true],
                    ["Capture screenshots of every surface", false],
                    ["Open PR with QA evidence", false],
                  ].map(([s, done], i) => (
                    <li key={i} style={{ display: "flex", alignItems: "center", gap: 10, padding: "8px 0", borderBottom: "1px solid var(--line-soft)" }}>
                      <span style={{ width: 14, height: 14, border: "1.5px solid var(--ink-deep)", borderRadius: 2, display: "grid", placeItems: "center", color: "var(--applied)" }}>
                        {done && <PIx.Check />}
                      </span>
                      <span style={{ flex: 1, textDecoration: done ? "line-through" : "none", textDecorationColor: "var(--applied)", color: done ? "var(--mute)" : "var(--ink)" }}>{s}</span>
                    </li>
                  ))}
                </ul>
                <div className="tk-eyebrow" style={{ marginTop: 22, marginBottom: 8 }}>Activity ledger</div>
                <div className="rule-ledger" style={{ padding: "0 0 6px" }}>
                  {[
                    ["#014","haiku proposed split into 3 cards","4s ago"],
                    ["#013","Daniel checked subtask · audit AA","42m"],
                    ["#012","capture linked: 'Paper at Night QA'","1h"],
                    ["#011","Daniel created card","yesterday"],
                  ].map(([sn, t, age], i) => (
                    <div key={i} style={{ display: "grid", gridTemplateColumns: "70px 1fr 80px", padding: "5px 0", fontFamily: "var(--mono)", fontSize: 11, color: "var(--mute)" }}>
                      <span className="tk-serial">{sn}</span>
                      <span style={{ color: "var(--ink-2)" }}>{t}</span>
                      <span style={{ textAlign: "right", color: "var(--faint)" }}>{age}</span>
                    </div>
                  ))}
                </div>
              </div>
              <aside style={{ padding: "22px 22px 22px 0" }}>
                <div className="card" style={{ padding: 14 }}>
                  {[
                    ["Status","In Progress"],
                    ["Assignee","Daniel L."],
                    ["Due","wk 19"],
                    ["Labels","theme · ui"],
                    ["Subtasks","3/5"],
                    ["Source","Capture #2026-04-23-021"],
                  ].map(([k,v], i) => (
                    <div key={i} style={{ display: "flex", justifyContent: "space-between", padding: "6px 0", borderBottom: i === 5 ? "none" : "1px solid var(--line-soft)" }}>
                      <span className="tk-eyebrow">{k}</span>
                      <span style={{ fontFamily: "var(--serif)", fontStyle: "italic", fontSize: 13 }}>{v}</span>
                    </div>
                  ))}
                </div>
                <div className="card" style={{ padding: 14, marginTop: 10, borderColor: "var(--ember)", background: "var(--ember-tint)" }}>
                  <div className="tk-eyebrow" style={{ color: "var(--ember-ink)" }}>Pending proposal</div>
                  <p className="tk-body" style={{ margin: "6px 0 8px", fontSize: 12.5, color: "var(--ember-ink)" }}>Haiku suggests splitting this card into 3.</p>
                  <HLBtn icon={PIx.Stamp} label="Open in Review" kbd="⏎" ember />
                </div>
              </aside>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

/* ============== COMMAND PALETTE ============== */
function CommandPaletteSurface({ theme = "paper" }) {
  return (
    <div className={theme} style={{ position: "relative", height: "100%", minHeight: 820, fontFamily: "var(--sans)" }}>
      {/* dimmed surface */}
      <div style={{ display: "flex", height: "100%", filter: "blur(3px)", opacity: .55, pointerEvents: "none" }}>
        <Sidebar active="B" theme={theme} />
        <div style={{ flex: 1 }}><TopBar /></div>
      </div>
      <div style={{ position: "absolute", inset: 0, background: "rgba(26,24,20,.18)" }} />

      {/* palette */}
      <div className="card-lift" style={{
        position: "absolute", left: "50%", top: 120, transform: "translateX(-50%)",
        width: 640, padding: 0, overflow: "hidden",
      }}>
        <header style={{ padding: "16px 18px", borderBottom: "1px solid var(--line)", display: "flex", alignItems: "center", gap: 12 }}>
          <PIx.Cmd />
          <input style={{
            flex: 1, border: 0, outline: 0, background: "transparent",
            fontFamily: "var(--serif)", fontStyle: "italic", fontSize: 22, color: "var(--ink-deep)",
          }} defaultValue="split implement dark mode" />
          <span className="kbd">esc</span>
        </header>
        <div>
          <PaletteSection title="Action · ai">
            <PaletteRow active glyph="◆" title="Propose: split into 3 cards" sub="darken tokens · migrate components · QA · ~4s" tag="haiku" />
          </PaletteSection>
          <PaletteSection title="Cards">
            <PaletteRow glyph="·" title="Open card · Implement dark mode" sub="C-090 · Product Backlog · In Progress" tag="jump" />
            <PaletteRow glyph="·" title="Open card · Tokens audit" sub="C-114 · Product Backlog · To Do" tag="jump" />
          </PaletteSection>
          <PaletteSection title="Capture">
            <PaletteRow glyph="✎" title='Capture note: "split implement dark mode"' sub="goes to Inbox · ⏎ to commit" tag="capture" />
          </PaletteSection>
        </div>
        <footer style={{ padding: "10px 18px", borderTop: "1px solid var(--line)", display: "flex", justifyContent: "space-between", alignItems: "center" }}>
          <span className="tk-meta">↑↓ to move · ⏎ to commit · ⌘. for filters</span>
          <span className="tk-meta" style={{ color: "var(--ember)" }}>· live · 12ms</span>
        </footer>
      </div>
    </div>
  );
}

function PaletteSection({ title, children }) {
  return (
    <section>
      <div className="tk-eyebrow" style={{ padding: "10px 18px 4px", color: "var(--faint)" }}>{title}</div>
      {children}
    </section>
  );
}
function PaletteRow({ glyph, title, sub, tag, active }) {
  return (
    <div style={{
      display: "grid", gridTemplateColumns: "20px 1fr 80px",
      gap: 12, alignItems: "center",
      padding: "8px 18px",
      background: active ? "linear-gradient(90deg, var(--ember-bloom) 0%, transparent 70%)" : "transparent",
      borderLeft: active ? "2px solid var(--ember)" : "2px solid transparent",
    }}>
      <span style={{ fontFamily: "var(--serif)", fontSize: 14, color: active ? "var(--ember)" : "var(--faint)", textAlign: "center" }}>{glyph}</span>
      <div>
        <div style={{ fontSize: 13.5, fontWeight: active ? 500 : 400, color: "var(--ink)" }}>{title}</div>
        <div className="tk-meta" style={{ fontSize: 10.5, marginTop: 1 }}>{sub}</div>
      </div>
      <span className="tk-meta" style={{ textAlign: "right", color: active ? "var(--ember)" : "var(--mute)" }}>{tag}</span>
    </div>
  );
}

/* ============== TOAST LAYER ============== */
function ToastSurface({ theme = "paper" }) {
  return (
    <div className={theme} style={{ position: "relative", height: "100%", minHeight: 560, padding: 32, fontFamily: "var(--sans)" }}>
      <div className="tk-eyebrow" style={{ marginBottom: 10 }}>Toast layer · stacked</div>
      <h2 className="tk-h2" style={{ margin: "0 0 24px" }}>Confirmations, in <em>paper voice</em></h2>
      <div style={{ display: "flex", flexDirection: "column", gap: 14, maxWidth: 460 }}>
        <Toast kind="applied" title="3 cards applied" body="Original card archived for 30 days." action="Undo · 6h" />
        <Toast kind="proposed" title="Proposal received" body="Haiku · #2026-04-25-014 · 0.84 confidence." action="Open · ⏎" />
        <Toast kind="captured" title="Captured to Inbox" body='"Look into local-first conflict resolution at apply-time."' action="Triage · I" />
        <Toast kind="overdue" title="Card overdue" body='"Design landing page" was due Mon 30/05.' action="Open · O" />
        <Toast kind="undo" title="Apply reversed" body="Cards restored to their previous shape." action="—" />
      </div>
    </div>
  );
}
function Toast({ kind, title, body, action }) {
  const map = {
    applied:  { c: "var(--applied)", l: "Applied",  glyph: "✓", bg: "var(--paper-card)" },
    proposed: { c: "var(--ember)",   l: "Proposed", glyph: "◆", bg: "var(--ember-tint)" },
    captured: { c: "var(--ink-deep)",l: "Captured", glyph: "✎", bg: "var(--paper-card)" },
    overdue:  { c: "var(--overdue)", l: "Overdue",  glyph: "‼", bg: "var(--overdue-tint)" },
    undo:     { c: "var(--mute)",    l: "Reversed", glyph: "↶", bg: "var(--paper-card)" },
  };
  const m = map[kind];
  return (
    <div className="card-lift" style={{ display: "flex", padding: 0, background: m.bg, borderColor: kind === "proposed" ? "var(--ember)" : "var(--line)" }}>
      <div style={{ width: 44, display: "grid", placeItems: "center", borderRight: "1px solid var(--line-soft)", color: m.c, fontFamily: "var(--serif)", fontSize: 18 }}>{m.glyph}</div>
      <div style={{ flex: 1, padding: "10px 14px" }}>
        <div style={{ display: "flex", alignItems: "baseline", gap: 8 }}>
          <span className="tagstamp" style={{ color: m.c, fontSize: 9 }}>{m.l}</span>
          <span style={{ fontFamily: "var(--serif)", fontSize: 14.5, fontWeight: 500, color: "var(--ink-deep)" }}>{title}</span>
        </div>
        <p className="tk-body" style={{ margin: "2px 0 0", fontSize: 12.5, color: "var(--ink-2)" }}>{body}</p>
      </div>
      <div style={{ padding: "10px 14px", borderLeft: "1px solid var(--line-soft)", display: "grid", placeItems: "center", color: m.c, fontFamily: "var(--mono)", fontSize: 10.5, letterSpacing: ".14em", textTransform: "uppercase" }}>{action}</div>
    </div>
  );
}

/* ============== EMPTY STATES ============== */
function EmptyStatesSurface({ theme = "paper" }) {
  return (
    <div className={theme} style={{ height: "100%", minHeight: 720, padding: 32, fontFamily: "var(--sans)" }}>
      <div className="tk-eyebrow">Empty states · per surface</div>
      <h2 className="tk-h2" style={{ margin: "8px 0 24px" }}>Quiet rooms, <em>not blank pages</em></h2>
      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr", gap: 18 }}>
        <Empty title="Inbox is clean" body="Nothing waits to be triaged. Capture with ⌘; from anywhere." mark="✎" />
        <Empty title="No proposals to review" body="The board is yours. Decisions only happen here." mark="◇" />
        <Empty title="No boards yet" body="A board is a small ledger. Start one for the work you're doing today." mark="▱" cta="New board · B" />
        <Empty title="Today is empty" body="Nothing scheduled. Open a board, or capture the next thought." mark="○" />
        <Empty title="No notifications" body="The system isn't tugging at your sleeve." mark="·" />
        <Empty title="Search returned nothing" body='Try "split", "dark", or a card serial like C-090.' mark="?" />
      </div>
    </div>
  );
}
function Empty({ title, body, mark, cta }) {
  return (
    <div className="card" style={{ padding: 22, minHeight: 200, display: "flex", flexDirection: "column", justifyContent: "space-between" }}>
      <div style={{ fontFamily: "var(--serif)", fontStyle: "italic", fontSize: 36, color: "var(--ember)", lineHeight: 1, opacity: .6 }}>{mark}</div>
      <div>
        <h3 style={{ margin: "0 0 4px", fontFamily: "var(--serif)", fontSize: 17, fontWeight: 500, color: "var(--ink-deep)" }}>{title}</h3>
        <p className="tk-body" style={{ margin: 0, fontSize: 12.5, color: "var(--ink-2)" }}>{body}</p>
        {cta && <div style={{ marginTop: 12 }}><HLBtn label={cta.split("·")[0].trim()} kbd={cta.split("·")[1]?.trim()} /></div>}
      </div>
    </div>
  );
}

/* ============== SHORTCUTS OVERLAY ============== */
function ShortcutsSurface({ theme = "paper" }) {
  return (
    <div className={theme} style={{ position: "relative", height: "100%", minHeight: 820, padding: 0, fontFamily: "var(--sans)" }}>
      <div style={{ display: "flex", height: "100%", filter: "blur(3px)", opacity: .5, pointerEvents: "none" }}>
        <Sidebar active="B" theme={theme} />
        <div style={{ flex: 1 }}><TopBar /></div>
      </div>
      <div style={{ position: "absolute", inset: 0, background: "rgba(26,24,20,.2)" }} />
      <div className="card-lift" style={{
        position: "absolute", left: "50%", top: 60, transform: "translateX(-50%)",
        width: 760, padding: 0, overflow: "hidden",
      }}>
        <header style={{ padding: "18px 24px", borderBottom: "1px solid var(--line)", display: "flex", alignItems: "baseline", justifyContent: "space-between" }}>
          <div>
            <div className="tk-eyebrow">Help · keyboard map</div>
            <h2 className="tk-h2" style={{ margin: "4px 0 0" }}>The full <em>keystroke ledger</em></h2>
          </div>
          <span className="kbd">?</span>
        </header>
        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 0 }}>
          <ShortGroup title="Navigation" rows={[
            ["H","Home"],["T","Today"],["R","Review"],["B","Boards"],["I","Inbox"],
            ["⌘K","Command palette"],["⌘;","Quick capture (anywhere)"],["G then T","Go to Today"],
          ]} />
          <ShortGroup title="Actions" rows={[
            ["⏎","Apply / commit decision"],["⌫","Reject / dismiss"],["E","Request edit"],
            ["⌘Z","Undo applied change"],["O","Open card"],["⎇","Hold for action menu"],
          ]} />
          <ShortGroup title="Board" rows={[
            ["C","Capture here"],["A","Ask assistant"],["F","Filter"],["L","Labels"],
            ["1–9","Jump to column"],["J/K","Move between cards"],
          ]} />
          <ShortGroup title="Review" rows={[
            ["↑↓","Move queue"],["space","Preview diff"],["U","Undo last apply"],
            ["P","Provenance pane"],
          ]} />
        </div>
        <footer style={{ padding: "10px 24px", borderTop: "1px solid var(--line)", display: "flex", justifyContent: "space-between" }}>
          <span className="tk-meta">Bindings are remappable · Settings → Keyboard</span>
          <span className="tk-meta">Press <span className="kbd">?</span> at any time</span>
        </footer>
      </div>
    </div>
  );
}
function ShortGroup({ title, rows }) {
  return (
    <div style={{ padding: "16px 24px", borderRight: "1px solid var(--line-soft)", borderBottom: "1px solid var(--line-soft)" }}>
      <div className="tk-eyebrow" style={{ marginBottom: 8 }}>{title}</div>
      <div>
        {rows.map(([k, l], i) => (
          <div key={i} style={{ display: "flex", alignItems: "center", gap: 12, padding: "5px 0", borderBottom: "1px dashed var(--line-soft)" }}>
            <span className="kbd" style={{ minWidth: 30 }}>{k}</span>
            <span style={{ flex: 1, fontSize: 13, color: "var(--ink)" }}>{l}</span>
          </div>
        ))}
      </div>
    </div>
  );
}

/* ============== THINKING-STATE COMPARATORS ============== */
function ThinkingComparators({ theme = "paper" }) {
  return (
    <div className={theme} style={{ padding: 32, minHeight: 600, fontFamily: "var(--sans)" }}>
      <div className="tk-eyebrow">Signature motion · LLM thinking state</div>
      <h2 className="tk-h2" style={{ margin: "8px 0 8px" }}>Three ways the system <em>composes</em></h2>
      <p className="tk-lede" style={{ marginTop: 0, marginBottom: 20 }}>Pick one to develop further. Each runs at the proposal-card scale; none use a spinner or a chrome animation.</p>
      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr", gap: 20 }}>
        <ThinkA />
        <ThinkB />
        <ThinkC />
      </div>
    </div>
  );
}

/* A · Letterpress impression */
function ThinkA() {
  return (
    <div className="card" style={{ padding: 18, position: "relative" }}>
      <div className="tk-eyebrow" style={{ marginBottom: 10 }}>Variant A · letterpress</div>
      <div style={{
        position: "relative", height: 200, borderRadius: 3,
        background: "var(--paper-card)",
        border: "1px solid var(--line)",
        boxShadow: "inset 0 2px 0 #1a18141a, inset 0 -1px 0 #ffffff80",
        overflow: "hidden",
      }}>
        <style>{`
          @keyframes thA-press { 0%,100% { box-shadow: inset 0 1px 0 #1a181410, inset 0 -1px 0 #ffffff80; transform: translateY(0); } 45% { box-shadow: inset 0 4px 0 #1a181430, inset 0 -1px 0 #ffffff80; transform: translateY(.5px); } 60% { transform: translateY(-1px); } }
        `}</style>
        <div style={{ position: "absolute", inset: 12, animation: "thA-press 2.4s var(--ease-press) infinite" }}>
          <div style={{ fontFamily: "var(--serif)", fontStyle: "italic", fontSize: 18, color: "var(--ink-deep)", lineHeight: 1.2 }} className="letterpress">
            Splitting "Implement<br/>dark mode"…
          </div>
          <div className="tk-meta" style={{ marginTop: 10, fontSize: 10 }}>3 cards · 7 references · ~4s</div>
          <div style={{ position: "absolute", left: 0, right: 0, bottom: 6 }}>
            <div className="erase-line" style={{ height: 2, background: "var(--ember)", width: "62%" }} />
          </div>
        </div>
      </div>
      <p className="tk-body" style={{ marginTop: 12, fontSize: 12.5, color: "var(--ink-2)" }}>The proposal card debosses into the page while haiku composes; it pops back up on commit. Mechanical, satisfying, doubles as the Review motion.</p>
    </div>
  );
}

/* B · Ink bleed */
function ThinkB() {
  return (
    <div className="card" style={{ padding: 18 }}>
      <div className="tk-eyebrow" style={{ marginBottom: 10 }}>Variant B · ink bleed</div>
      <div style={{ position: "relative", height: 200, borderRadius: 3, background: "var(--paper-card)", border: "1px solid var(--line)", overflow: "hidden" }}>
        <style>{`
          @keyframes thB-bleed { 0% { transform: scale(.4); opacity: .9; filter: blur(0px); } 100% { transform: scale(2.6); opacity: 0; filter: blur(8px); } }
          @keyframes thB-bleed2 { 0% { transform: scale(.4); opacity: .9; filter: blur(0px); } 100% { transform: scale(3.2); opacity: 0; filter: blur(10px); } }
        `}</style>
        {[0, 1.4].map((delay, i) => (
          <div key={i} style={{
            position: "absolute", left: "50%", top: "55%", width: 80, height: 80,
            transform: "translate(-50%,-50%) scale(.4)",
            borderRadius: "50%",
            background: `radial-gradient(circle, var(--ember) 0%, var(--ember) 30%, transparent 70%)`,
            animation: `thB-bleed${i ? '2' : ''} 2.8s ease-out infinite`,
            animationDelay: `${delay}s`,
            mixBlendMode: "multiply",
          }} />
        ))}
        <div style={{ position: "absolute", inset: 0, padding: 12, display: "flex", flexDirection: "column", justifyContent: "flex-end" }}>
          <div style={{ fontFamily: "var(--serif)", fontStyle: "italic", fontSize: 16, color: "var(--ink-deep)" }}>haiku is composing…</div>
          <div className="tk-meta" style={{ fontSize: 10 }}>ink bleed · ~4s</div>
        </div>
      </div>
      <p className="tk-body" style={{ marginTop: 12, fontSize: 12.5, color: "var(--ink-2)" }}>A drop of seal-red spreads through paper fibers, irregularly. Slow, warm, ambient. Works as a per-token streaming texture beneath the headline.</p>
    </div>
  );
}

/* C · Nib tracing a dotted line */
function ThinkC() {
  return (
    <div className="card" style={{ padding: 18 }}>
      <div className="tk-eyebrow" style={{ marginBottom: 10 }}>Variant C · nib stroke</div>
      <div style={{ position: "relative", height: 200, borderRadius: 3, background: "var(--paper-card)", border: "1px solid var(--line)", overflow: "hidden" }}>
        <style>{`
          @keyframes thC-nib { 0% { stroke-dashoffset: 320; } 100% { stroke-dashoffset: 0; } }
          @keyframes thC-pen { 0% { transform: translateX(20px) rotate(-12deg); } 100% { transform: translateX(280px) rotate(-12deg); } }
        `}</style>
        <svg viewBox="0 0 320 200" style={{ position: "absolute", inset: 0, width: "100%", height: "100%" }}>
          <path d="M 20 130 C 80 110, 140 150, 200 120 S 280 110, 300 130"
                stroke="var(--ember)" strokeWidth="1.4" fill="none" strokeLinecap="round"
                strokeDasharray="4 4"
                style={{ strokeDashoffset: 320, animation: "thC-nib 3.6s linear infinite" }} />
        </svg>
        <div style={{
          position: "absolute", left: 0, top: 110,
          width: 24, height: 24, color: "var(--ember)",
          animation: "thC-pen 3.6s linear infinite",
        }}>
          <svg viewBox="0 0 24 24" style={{ width: "100%", height: "100%" }}>
            <path d="M 20 4 C 14 6, 8 12, 4 22 L 8 18 L 12 16 Z" stroke="var(--ink-deep)" strokeWidth="1" fill="var(--ember)" />
          </svg>
        </div>
        <div style={{ position: "absolute", left: 12, top: 12, fontFamily: "var(--serif)", fontStyle: "italic", fontSize: 16, color: "var(--ink-deep)" }}>tracing the proposal…</div>
      </div>
      <p className="tk-body" style={{ marginTop: 12, fontSize: 12.5, color: "var(--ink-2)" }}>A nib draws a dotted underline across the headline as haiku streams its tokens. Old-fashioned, rustic, precise. Lightest of the three on perf.</p>
    </div>
  );
}

window.CardDetailSurface = CardDetailSurface;
window.CommandPaletteSurface = CommandPaletteSurface;
window.ToastSurface = ToastSurface;
window.EmptyStatesSurface = EmptyStatesSurface;
window.ShortcutsSurface = ShortcutsSurface;
window.ThinkingComparators = ThinkingComparators;
