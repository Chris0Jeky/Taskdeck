/* TODAY · End-of-day recap. Premium materials, stats, ledger.
   Designed as a one-page dossier the user reads at the close of work. */
const PIt = window.PaperIcons;

function TodaySurface({ theme = "paper" }) {
  return (
    <div className={theme} style={{ display: "flex", height: "100%", minHeight: 1100, fontFamily: "var(--sans)" }}>
      <Sidebar active="T" theme={theme} />
      <div style={{ flex: 1, display: "flex", flexDirection: "column", minWidth: 0 }}>
        <TopBar crumb={["Workspace", "Today", "Saturday · April 25"]} />
        <div style={{ flex: 1, overflow: "auto", padding: "0 0 60px", background: "var(--paper)" }}>

          {/* DOSSIER COVER */}
          <section style={{
            padding: "44px 56px 28px",
            background: "linear-gradient(180deg, var(--paper-2) 0%, var(--paper) 100%)",
            borderBottom: "1px solid var(--line)",
            position: "relative",
          }}>
            <div style={{ display: "grid", gridTemplateColumns: "1.4fr 1fr", gap: 32, alignItems: "flex-end" }}>
              <div>
                <div className="tk-eyebrow">Dossier · day's ledger · sealed at end of session</div>
                <h1 className="tk-h1" style={{ margin: "10px 0 6px", fontSize: 56, lineHeight: 1.02 }}>
                  Today, you moved <em>nine cards</em>.
                </h1>
                <p className="tk-lede" style={{ marginTop: 8, maxWidth: 620 }}>
                  A quiet Saturday. You triaged the morning inbox, applied two of haiku's proposals, and closed the dark-mode hand-off. Two cards drifted past their due — bring them up tomorrow.
                </p>
                <div style={{ display: "flex", gap: 14, marginTop: 18, alignItems: "center" }}>
                  <button className="btn" style={{ background: "var(--ember)", color: "var(--cream)", borderColor: "var(--ember)" }}>Seal day & archive</button>
                  <button className="btn">Write a note</button>
                  <span className="tk-meta" style={{ marginLeft: 6 }}>Auto-seals in 2h 18m</span>
                </div>
              </div>
              <div style={{ position: "relative" }}>
                <Stamp kind="proposed" date="Apr 25" time="end-of-day" num="W17·SAT" style={{ transform: "rotate(-4deg)" }} />
                <div className="tk-serial" style={{ marginTop: 10, color: "var(--faint)", textAlign: "right" }}>DOSSIER · 2026-04-25 · v01</div>
              </div>
            </div>
          </section>

          {/* KEY STATS */}
          <section style={{ padding: "28px 56px 12px" }}>
            <div style={{ display: "grid", gridTemplateColumns: "repeat(5, 1fr)", gap: 16 }}>
              <Stat n="9"      l="cards moved"           sub="+2 vs your wk avg" tone="ink" />
              <Stat n="3"      l="proposals applied"     sub="of 4 reviewed · 75%" tone="ember" />
              <Stat n="11"     l="captures triaged"      sub="0 left in inbox" tone="ink" />
              <Stat n="2h 14m" l="focus time · longest"  sub="13:02 — 15:16 · uninterrupted" tone="applied" />
              <Stat n="2"      l="overdue"               sub="C-072, C-061" tone="overdue" />
            </div>
          </section>

          {/* TWO COLUMN BODY */}
          <section style={{ padding: "20px 56px 0", display: "grid", gridTemplateColumns: "1.5fr 1fr", gap: 28 }}>

            {/* LEFT */}
            <div style={{ display: "flex", flexDirection: "column", gap: 24 }}>

              {/* Cadence */}
              <div className="card" style={{ padding: 22 }}>
                <SectionHead n="I" t="Cadence" sub="When you worked · 24h strip" />
                <CadenceStrip />
                <div style={{ display: "flex", justifyContent: "space-between", marginTop: 10 }}>
                  <Mini k="First action" v="08:42 · capture" />
                  <Mini k="Peak hour"   v="13:00 — 14:00 · 7 events" />
                  <Mini k="Last action" v="17:18 · seal" />
                </div>
              </div>

              {/* Ledger */}
              <div className="card" style={{ padding: 0, overflow: "hidden" }}>
                <header style={{ padding: "16px 22px", borderBottom: "1px solid var(--line-soft)", display: "flex", justifyContent: "space-between", alignItems: "baseline" }}>
                  <SectionHead n="II" t="Ledger" sub="Every meaningful event today · 18 entries" inline />
                  <span className="tk-meta">⌘L to open the full year ledger</span>
                </header>
                <div className="rule-ledger">
                  {LEDGER.map((e, i) => <Ledger key={i} {...e} />)}
                </div>
              </div>

              {/* Decisions */}
              <div className="card" style={{ padding: 22 }}>
                <SectionHead n="III" t="Decisions" sub="Proposals you weighed today" />
                <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12, marginTop: 10 }}>
                  <Decision sn="#014" t="Split: Implement dark mode" v="applied" conf={0.84} when="11:42" />
                  <Decision sn="#012" t="Add 'Blocked' column"        v="deferred" conf={0.71} when="10:14" />
                  <Decision sn="#011" t="Move 'Set up CI' → Done"     v="applied" conf={0.91} when="09:18" />
                  <Decision sn="#008" t="Merge duplicates"            v="rejected" conf={0.62} when="yest" stale />
                </div>
                <div style={{ marginTop: 14, padding: 12, background: "var(--paper-2)", borderRadius: 2, fontSize: 12, color: "var(--ink-2)", borderLeft: "2px solid var(--applied)" }}>
                  <span className="tk-eyebrow" style={{ color: "var(--applied)", marginRight: 8 }}>NOTE</span>
                  You applied at <b style={{ color: "var(--ink)" }}>0.84 avg confidence</b> today — slightly above your week. Apply rate <b style={{ color: "var(--ink)" }}>71%</b>. Undo rate <b style={{ color: "var(--ink)" }}>0%</b>.
                </div>
              </div>
            </div>

            {/* RIGHT */}
            <div style={{ display: "flex", flexDirection: "column", gap: 24 }}>

              {/* Boards touched */}
              <div className="card" style={{ padding: 22 }}>
                <SectionHead n="IV" t="Boards touched" sub="3 of 7" />
                <BoardLine n="Product Backlog"     active={6} prop={2} />
                <BoardLine n="Sprint 12"           active={3} prop={1} />
                <BoardLine n="Personal"            active={1} prop={0} />
                <BoardLine n="Side projects"       active={0} prop={0} dim />
                <BoardLine n="Notes & references"  active={0} prop={0} dim />
              </div>

              {/* Carry-over */}
              <div className="card" style={{ padding: 22, borderColor: "var(--overdue)", borderLeft: "2px solid var(--overdue)" }}>
                <SectionHead n="V" t="Carry-over" sub="Bring to tomorrow · 2 cards" />
                <Carry sn="C-072" t="Audit AA contrast on toasts"  age="3d overdue" why="rolled over twice" />
                <Carry sn="C-061" t="Reply: design system intro"   age="1d overdue" why="snoozed yesterday" />
                <button className="btn" style={{ marginTop: 12, width: "100%", justifyContent: "center" }}>
                  Pin both to tomorrow's morning
                </button>
              </div>

              {/* Streak / cadence */}
              <div className="card" style={{ padding: 22, background: "var(--paper-2)" }}>
                <SectionHead n="VI" t="Streak" sub="Days in a row · this quarter" />
                <Streak />
                <p className="tk-body" style={{ margin: "10px 0 0", fontSize: 12.5, color: "var(--ink-2)" }}>
                  <b style={{ color: "var(--ink-deep)" }}>17 days.</b> Your longest this year was 23. Sealed every weekday since 4 April.
                </p>
              </div>

              {/* End of day note */}
              <div className="card" style={{ padding: 22 }}>
                <SectionHead n="VII" t="A line for tomorrow" sub="A note your tomorrow-self will see at first open" />
                <textarea
                  defaultValue="Pick up the AA contrast audit first — it's been carried twice. Aim to seal Sprint 12 by Wednesday."
                  style={{
                    width: "100%", marginTop: 8, minHeight: 80,
                    border: "1px solid var(--line)", borderRadius: 2,
                    padding: 10, background: "var(--paper-card)",
                    fontFamily: "var(--serif)", fontSize: 14, lineHeight: 1.5,
                    color: "var(--ink-deep)", fontStyle: "italic", resize: "vertical",
                  }}
                />
                <div className="tk-meta" style={{ marginTop: 6, fontSize: 10.5, display: "flex", justifyContent: "space-between" }}>
                  <span>Saved · auto</span>
                  <span>shows on Sunday's open</span>
                </div>
              </div>
            </div>

          </section>

          <footer style={{ padding: "30px 56px 0", marginTop: 36, borderTop: "1px solid var(--line)", display: "flex", justifyContent: "space-between" }}>
            <span className="tk-serial">DOSSIER · DAY 116 · YEAR LEDGER</span>
            <span className="tk-serial">PRESS S TO SEAL · ⌘L FOR LEDGER</span>
          </footer>
        </div>
      </div>
    </div>
  );
}

const LEDGER = [
  { time: "17:18", who: "you",   what: "Sealed proposal #011 · Set up CI → Done", tone: "applied", sn: "L-018" },
  { time: "16:04", who: "haiku", what: "Proposed split · #014 · ready for review", tone: "ember", sn: "L-017" },
  { time: "15:42", who: "you",   what: "Triaged 7 captures into Product Backlog", tone: "active", sn: "L-016" },
  { time: "15:16", who: "you",   what: "End of focus block (2h 14m)", tone: "passive", sn: "L-015" },
  { time: "13:55", who: "haiku", what: "Proposed merge of duplicates · #008 · rejected", tone: "mute", sn: "L-014" },
  { time: "13:02", who: "you",   what: "Started focus block · DnD off · 2h 14m", tone: "active", sn: "L-013" },
  { time: "12:48", who: "you",   what: "Renamed board · 'Sprint 12 · QA'", tone: "active", sn: "L-012" },
  { time: "11:42", who: "you",   what: "Applied #014 · 3 cards land · undo 6h", tone: "applied", sn: "L-011" },
  { time: "11:38", who: "haiku", what: "Proposed split · #014 · 0.84 confidence", tone: "ember", sn: "L-010" },
  { time: "10:14", who: "you",   what: "Deferred #012 · 'Add Blocked column'",   tone: "passive", sn: "L-009" },
  { time: "09:18", who: "you",   what: "Applied #011 · 'Set up CI' → Done", tone: "applied", sn: "L-008" },
  { time: "09:00", who: "system", what: "Day opened · 5 cards on Today board",   tone: "passive", sn: "L-007" },
];

function SectionHead({ n, t, sub, inline }) {
  return (
    <div style={{
      display: "flex", alignItems: "baseline", gap: 14,
      marginBottom: inline ? 0 : 12,
      paddingBottom: inline ? 0 : 8,
      borderBottom: inline ? "none" : "1px solid var(--line-soft)",
    }}>
      <span className="tk-serial" style={{ color: "var(--faint)" }}>§ {n}</span>
      <h3 className="tk-h3" style={{ margin: 0, fontSize: 17 }}>{t}</h3>
      <span className="tk-meta" style={{ marginLeft: "auto" }}>{sub}</span>
    </div>
  );
}

function Stat({ n, l, sub, tone }) {
  const c = { ember: "var(--ember)", applied: "var(--applied)", overdue: "var(--overdue)", ink: "var(--ink-deep)" }[tone];
  return (
    <div className="card" style={{ padding: 16, position: "relative", overflow: "hidden" }}>
      <div style={{ position: "absolute", top: 0, left: 0, right: 0, height: 2, background: c, opacity: .8 }} />
      <div className="tk-eyebrow">{l}</div>
      <div style={{ fontFamily: "var(--serif)", fontSize: 38, fontWeight: 400, fontStyle: "italic", color: "var(--ink-deep)", lineHeight: 1, margin: "8px 0 4px" }}>{n}</div>
      <div className="tk-meta" style={{ fontSize: 10.5 }}>{sub}</div>
    </div>
  );
}

function CadenceStrip() {
  // 24 hours, with activity weights
  const weights = [0,0,0,0,0,0,0,0, 1,3,2,1,3,4,2,3,4,2, 0,0,0,0,0,0];
  const labels = ["00","","","","","06","","","","","12","","","","","18","","","","","23"];
  const max = 4;
  return (
    <div>
      <div style={{ display: "flex", alignItems: "flex-end", gap: 3, height: 64, padding: "8px 0", marginTop: 6 }}>
        {weights.map((w, i) => (
          <div key={i} style={{ flex: 1, height: "100%", display: "flex", alignItems: "flex-end", position: "relative" }}>
            <div style={{
              width: "100%", height: w === 0 ? 2 : `${(w/max)*100}%`,
              background: w === 0 ? "var(--line)" : (i === 13 ? "var(--ember)" : "var(--ink-deep)"),
              opacity: w === 0 ? 1 : i === 13 ? 1 : .8,
            }} />
            {i === 13 && (
              <div style={{ position: "absolute", top: -16, left: "50%", transform: "translateX(-50%)" }}>
                <span className="tk-serial" style={{ fontSize: 9, color: "var(--ember)" }}>peak</span>
              </div>
            )}
          </div>
        ))}
      </div>
      <div style={{ display: "flex", gap: 3 }}>
        {labels.map((l, i) => (
          <div key={i} style={{ flex: 1, textAlign: "center", fontFamily: "var(--mono)", fontSize: 9, color: "var(--faint)" }}>{l}</div>
        ))}
      </div>
    </div>
  );
}
function Mini({ k, v }) {
  return (
    <div>
      <div className="tk-eyebrow" style={{ marginBottom: 2 }}>{k}</div>
      <div style={{ fontFamily: "var(--serif)", fontStyle: "italic", fontSize: 14, color: "var(--ink-deep)" }}>{v}</div>
    </div>
  );
}

function Ledger({ time, who, what, tone, sn }) {
  const c = { ember: "var(--ember)", applied: "var(--applied)", active: "var(--ink-deep)", passive: "var(--ink-2)", mute: "var(--faint)" }[tone];
  return (
    <div style={{ display: "grid", gridTemplateColumns: "60px 60px 60px 1fr 60px", gap: 12, padding: "8px 22px", borderBottom: "1px solid var(--line-soft)", alignItems: "center", fontSize: 12 }}>
      <span className="tk-serial" style={{ color: "var(--faint)" }}>{sn}</span>
      <span className="tk-serial" style={{ color: "var(--ink-2)" }}>{time}</span>
      <span className="tagstamp" style={{ color: c, fontSize: 9 }}>{who.toUpperCase()}</span>
      <span style={{ color: "var(--ink-2)", lineHeight: 1.45 }}>{what}</span>
      <span style={{ textAlign: "right" }}>
        <span style={{ width: 6, height: 6, display: "inline-block", borderRadius: "50%", background: c }} />
      </span>
    </div>
  );
}

function Decision({ sn, t, v, conf, when, stale }) {
  const map = { applied: ["var(--applied)", "APPLIED"], rejected: ["var(--overdue)", "REJECTED"], deferred: ["var(--ember)", "DEFERRED"] };
  const [c, lbl] = map[v];
  return (
    <div className="card" style={{ padding: 12, opacity: stale ? .7 : 1 }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "baseline" }}>
        <span className="tk-serial">{sn}</span>
        <span className="tagstamp" style={{ color: c, fontSize: 9 }}>{lbl}</span>
      </div>
      <div style={{ fontFamily: "var(--serif)", fontSize: 13.5, fontWeight: 500, color: "var(--ink-deep)", margin: "4px 0", lineHeight: 1.3 }}>{t}</div>
      <div className="tk-meta" style={{ fontSize: 10 }}>conf {conf.toFixed(2)} · {when}</div>
    </div>
  );
}

function BoardLine({ n, active, prop, dim }) {
  return (
    <div style={{ display: "grid", gridTemplateColumns: "1fr auto auto", gap: 10, padding: "9px 0", borderBottom: "1px solid var(--line-soft)", alignItems: "center", opacity: dim ? .55 : 1 }}>
      <span style={{ fontFamily: "var(--serif)", fontSize: 14, fontWeight: 500, color: dim ? "var(--mute)" : "var(--ink-deep)" }}>{n}</span>
      <span className="tk-meta" style={{ fontSize: 11 }}>
        {active ? <><b style={{ color: "var(--ink)" }}>{active}</b> moves</> : "—"}
      </span>
      {prop > 0 ? (
        <span className="tagstamp" style={{ color: "var(--ember)", fontSize: 9 }}>{prop} PROP</span>
      ) : <span style={{ width: 36 }} />}
    </div>
  );
}

function Carry({ sn, t, age, why }) {
  return (
    <div style={{ padding: "10px 0", borderBottom: "1px solid var(--line-soft)" }}>
      <div style={{ display: "flex", justifyContent: "space-between", marginBottom: 2 }}>
        <span className="tk-serial">{sn}</span>
        <span className="tagstamp" style={{ color: "var(--overdue)", fontSize: 9 }}>{age.toUpperCase()}</span>
      </div>
      <div style={{ fontFamily: "var(--serif)", fontSize: 14, fontWeight: 500, color: "var(--ink-deep)", lineHeight: 1.3 }}>{t}</div>
      <div className="tk-meta" style={{ fontSize: 10.5, marginTop: 2 }}>{why}</div>
    </div>
  );
}

function Streak() {
  // 90-day grid (q)
  const cells = Array.from({ length: 90 }, (_, i) => {
    if (i < 73) return Math.floor(Math.random() * 4); // historical
    if (i === 73) return 0; // miss
    return 3 + (i % 2);
  });
  return (
    <div style={{ display: "grid", gridTemplateColumns: "repeat(30, 1fr)", gap: 2, padding: "8px 0" }}>
      {cells.map((v, i) => (
        <div key={i} style={{
          aspectRatio: "1",
          background: v === 0 ? "var(--line)" : v === 1 ? "var(--paper-card)" : v === 2 ? "var(--ember-bloom)" : v === 3 ? "var(--ember-tint)" : "var(--ember)",
          border: i === 89 ? "1px solid var(--ember)" : "none",
        }} />
      ))}
    </div>
  );
}

window.TodaySurface = TodaySurface;
