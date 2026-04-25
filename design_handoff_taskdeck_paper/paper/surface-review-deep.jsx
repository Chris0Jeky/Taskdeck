/* Review · DEEP — premium provenance, conflicts, history, decision-confidence,
   side-effects map, cited references, post-apply timeline. The single most important
   surface in the product. */
const PIrd = window.PaperIcons;

function ReviewDeep({ theme = "paper" }) {
  return (
    <div className={theme} style={{ display: "flex", height: "100%", minHeight: 1080, fontFamily: "var(--sans)" }}>
      <Sidebar active="R" theme={theme} />
      <div style={{ flex: 1, display: "flex", flexDirection: "column", minWidth: 0 }}>
        <TopBar crumb={["Workspace", "Review", "Proposal #014"]} />

        <div style={{ flex: 1, display: "grid", gridTemplateColumns: "280px 1fr 320px", minHeight: 0, background: "var(--paper)" }}>

          {/* QUEUE RAIL */}
          <aside style={{ borderRight: "1px solid var(--line)", background: "var(--paper-2)", padding: "20px 0", overflow: "auto" }}>
            <div style={{ padding: "0 18px 8px" }}>
              <div className="tk-eyebrow">Queue · 3 awaiting · 2 stale</div>
              <div style={{ display: "flex", gap: 6, marginTop: 8 }}>
                <button className="btn" style={{ padding: "4px 8px", fontSize: 10.5, background: "var(--paper-card)" }}>All</button>
                <button className="btn-ghost btn" style={{ padding: "4px 8px", fontSize: 10.5 }}>Mine</button>
                <button className="btn-ghost btn" style={{ padding: "4px 8px", fontSize: 10.5 }}>Stale</button>
              </div>
            </div>
            <Q active sn="#014" t="Split: Implement dark mode" who="haiku" conf={0.84} age="4s" reach="3 cards · 1 board" />
            <Q sn="#013" t="Triage 3 captures" who="capture" conf={null} age="32m" reach="—" />
            <Q sn="#012" t="Add 'Blocked' column to Sprint 12" who="haiku" conf={0.71} age="1d" reach="1 board" stale />
            <Q sn="#008" t="Merge 2 duplicate cards" who="haiku" conf={0.62} age="3d" reach="2 cards" stale />

            <div style={{ marginTop: 16, padding: "12px 18px", borderTop: "1px solid var(--line-soft)" }}>
              <div className="tk-eyebrow" style={{ marginBottom: 8 }}>Recently applied · undoable</div>
              <U sn="#011" t="Move 'Set up CI' → Done" left="5h 48m" />
              <U sn="#010" t="Rename board to 'Product Backlog'" left="—" expired />
              <U sn="#009" t="Apply 4-card split (Auth flow)" left="2h 14m" />
            </div>

            <div style={{ marginTop: 8, padding: "12px 18px", borderTop: "1px solid var(--line-soft)" }}>
              <div className="tk-eyebrow" style={{ marginBottom: 6 }}>This week</div>
              <MiniCadence />
              <div className="tk-meta" style={{ fontSize: 10, marginTop: 6 }}>
                Apply rate <b style={{ color: "var(--ink)" }}>71%</b> · undo rate <b style={{ color: "var(--ink)" }}>4%</b>
              </div>
            </div>
          </aside>

          {/* MAIN */}
          <div style={{ overflow: "auto", padding: "28px 36px 40px" }}>

            {/* Header */}
            <div style={{ display: "grid", gridTemplateColumns: "1fr auto", alignItems: "flex-start", gap: 24 }}>
              <div>
                <div style={{ display: "flex", gap: 10, alignItems: "center" }}>
                  <span className="tagstamp" style={{ color: "var(--ember)" }}>PROPOSED · DIFF</span>
                  <span className="tk-meta">#2026-04-25-014 · 11:42 PT · awaiting decision</span>
                </div>
                <h1 className="tk-h1" style={{ margin: "10px 0 6px", maxWidth: 660 }}>
                  Split <em>"Implement dark mode"</em> into <em>three smaller cards.</em>
                </h1>
                <p className="tk-lede" style={{ marginTop: 6 }}>
                  Haiku read the card body, the linked design doc, and 7 prior activity entries on this board. The split preserves the original assignee, labels, and source capture. Subtasks are reassigned to the most relevant child.
                </p>
              </div>

              <ConfDial v={0.84} />
            </div>

            {/* Decision rail (sticky) */}
            <div className="card-lift halo-ember" style={{
              marginTop: 18, padding: "12px 16px",
              display: "flex", alignItems: "center", gap: 12,
              position: "sticky", top: 0, zIndex: 2,
            }}>
              <span className="tagstamp" style={{ color: "var(--ember)" }}>DECISION</span>
              <span className="tk-meta">3 cards land · original archived 30d · undo 6h · atomic</span>
              <span style={{ flex: 1 }} />
              <HLBtn icon={PIrd.X} label="Reject" kbd="⌫" />
              <HLBtn icon={PIrd.Pages} label="Request edit" kbd="E" />
              <HLBtn label="Defer" kbd="D" />
              <HLBtn icon={PIrd.Stamp} label="Apply" kbd="⏎" ember />
            </div>

            {/* Diff section */}
            <section style={{ marginTop: 22 }}>
              <SectionHeader n="I" t="The change" sub="3 changes · Product Backlog · column In Progress" />
              <div className="card" style={{ padding: 0, overflow: "hidden" }}>
                <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr" }}>
                  <div style={{ padding: 22, borderRight: "1px solid var(--line-soft)" }}>
                    <div className="tk-eyebrow" style={{ marginBottom: 10 }}>Before · today</div>
                    <DiffBefore />
                  </div>
                  <div style={{ padding: 22, background: "linear-gradient(90deg, transparent 0%, var(--ember-bloom) 100%)" }}>
                    <div className="tk-eyebrow" style={{ marginBottom: 10, color: "var(--ember)" }}>After · on apply</div>
                    <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
                      <DiffAfter sn="C-090"  t="Tokens · darken & QA"          b="Migrate the token sheet; verify contrast at AA on every surface." status="kept" />
                      <DiffAfter sn="C-090a" t="Components · mode switch"      b="All components use semantic vars; ship a `data-theme` toggle with sticky preference." status="new" />
                      <DiffAfter sn="C-090b" t="Hand-off · screenshots & PR"   b="Capture every surface in both modes. PR with QA evidence and reviewer checklist." status="new" />
                    </div>
                  </div>
                </div>
                {/* Field-by-field diff strip */}
                <div style={{ borderTop: "1px solid var(--line-soft)", padding: "14px 22px", background: "var(--paper-2)" }}>
                  <div className="tk-eyebrow" style={{ marginBottom: 10 }}>Per-field changes</div>
                  <div style={{ display: "grid", gridTemplateColumns: "100px 1fr 1fr", gap: 8 }}>
                    <FieldDiff k="title"    a="Implement dark mode" b="Tokens · darken & QA · Components · mode switch · Hand-off" />
                    <FieldDiff k="subtasks" a="0/0"                  b="2/4 · 1/3 · 0/2 (3 + 3 + 2 = 8 total)" />
                    <FieldDiff k="labels"   a="theme"                b="theme · ui (added on hand-off card only)" />
                    <FieldDiff k="due"      a="—"                    b="kept blank · respects backlog convention" />
                    <FieldDiff k="assignee" a="Daniel L."            b="Daniel L. (preserved across all 3)" same />
                  </div>
                </div>
              </div>
            </section>

            {/* Provenance */}
            <section style={{ marginTop: 28 }}>
              <SectionHeader n="II" t="Provenance" sub="What haiku read · what it didn't · what it inferred" />
              <div className="card" style={{ padding: 0, overflow: "hidden" }}>
                <ProvRow icon="📄" k="C-090 body"          v="Card description · 178 words · last edited yesterday by Daniel" weight="primary" />
                <ProvRow icon="🔗" k="design-doc · #notion" v="Dark Mode QA checklist · 5 items · attached to C-090 last week" weight="primary" />
                <ProvRow icon="📜" k="board activity · 7 entries"   v="Recent moves on C-090 and adjacent cards · last 14 days" weight="contextual" />
                <ProvRow icon="⊘"  k="not read"           v="Other boards · private cards · capture #2026-04-23-021 (different scope)" weight="excluded" />
                <ProvRow icon="✦"  k="inferred"          v="Splitting threshold = 5+ subtasks OR >2 days estimated. Pattern matches C-082, C-064, C-051." weight="inferred" />
              </div>
              <p className="tk-meta" style={{ marginTop: 8, fontSize: 11 }}>
                Haiku ran <b style={{ color: "var(--ink)" }}>locally</b>. No data left this device. <a href="#" style={{ color: "var(--ember)", borderBottom: "1px solid var(--ember)", textDecoration: "none" }}>View full read-set →</a>
              </p>
            </section>

            {/* Side-effects map */}
            <section style={{ marginTop: 28 }}>
              <SectionHeader n="III" t="Side effects" sub="What lands · what doesn't · what archives" />
              <div style={{ display: "grid", gridTemplateColumns: "1.4fr 1fr", gap: 14 }}>
                <div className="card" style={{ padding: 0, overflow: "hidden" }}>
                  <SE k="Cards" v="3 created · 1 archived (30 days)" tone="active" />
                  <SE k="Subtasks" v="8 distributed · none lost · checkmarks preserved" tone="active" />
                  <SE k="Comments" v="Original 4 comments stay on the archived parent" tone="passive" />
                  <SE k="Activity log" v="Single entry: 'Daniel applied #014'" tone="active" />
                  <SE k="Notifications" v="Daniel only · no team notify (solo board)" tone="passive" />
                  <SE k="Webhooks" v="None · no integrations active on this board" tone="passive" />
                  <SE k="Calendar" v="Untouched · due dates preserved or blank" tone="passive" />
                </div>
                <div className="card" style={{ padding: 16, background: "var(--applied-tint)", borderColor: "var(--applied)" }}>
                  <div className="tk-eyebrow" style={{ color: "var(--applied)" }}>Reversibility</div>
                  <div style={{ fontFamily: "var(--serif)", fontSize: 24, fontWeight: 400, fontStyle: "italic", color: "var(--ink-deep)", margin: "6px 0 4px" }}>6 hours · single keystroke</div>
                  <p className="tk-body" style={{ margin: 0, fontSize: 12.5, color: "var(--ink-2)" }}>Undo restores all 3 cards to a single card with original body, subtasks, comments, and activity log. Nothing is lost.</p>
                  <div style={{ marginTop: 12, padding: "10px 0 0", borderTop: "1px solid var(--applied)" }}>
                    <div className="tk-eyebrow" style={{ marginBottom: 4 }}>Undo window</div>
                    <div style={{ height: 4, background: "var(--paper-card)", border: "1px solid var(--applied)", borderRadius: 2, position: "relative", overflow: "hidden" }}>
                      <div style={{ position: "absolute", inset: 0, width: "100%", background: "repeating-linear-gradient(90deg, var(--applied) 0 6px, transparent 6px 12px)" }} />
                    </div>
                    <div className="tk-meta" style={{ display: "flex", justifyContent: "space-between", marginTop: 4, fontSize: 10 }}>
                      <span>0h · apply</span><span>6h · sealed</span>
                    </div>
                  </div>
                </div>
              </div>
            </section>

            {/* Conflicts */}
            <section style={{ marginTop: 28 }}>
              <SectionHeader n="IV" t="Conflicts &amp; warnings" sub="What the system noticed · 1 minor" />
              <div className="card" style={{ padding: 0, overflow: "hidden" }}>
                <Conflict tone="warn" k="Stale assignment" v="Daniel L. was last active on C-090 9 days ago. Confirm before applying or reassign." />
                <Conflict tone="info" k="Linked capture is older" v='Source capture #2026-04-23-021 ("local-first conflict resolution") is 2 days old. Still relevant?' />
                <Conflict tone="ok"   k="No collisions" v="No other proposals touch C-090 right now." />
              </div>
            </section>

            {/* History · this card */}
            <section style={{ marginTop: 28 }}>
              <SectionHeader n="V" t="History · this card" sub="Every touch since creation" />
              <div className="rule-ledger" style={{ padding: 4, border: "1px solid var(--line)", borderRadius: 2, background: "var(--paper-card)" }}>
                {[
                  ["#014","haiku proposed split into 3","11:42","ember"],
                  ["#011","Daniel checked subtask · audit AA","09:18","applied"],
                  ["#009","capture linked: 'Paper at Night QA'","yest 16:04","mute"],
                  ["#007","Daniel rewrote body","yest 14:22","mute"],
                  ["#003","label · theme added","Mon 11:00","mute"],
                  ["#001","Daniel created card","wk 17 Mon","mute"],
                ].map(([sn, t, age, tone], i) => (
                  <div key={i} style={{ display: "grid", gridTemplateColumns: "70px 1fr 80px 120px", padding: "5px 12px", fontFamily: "var(--mono)", fontSize: 11, alignItems: "center" }}>
                    <span className="tk-serial">{sn}</span>
                    <span style={{ color: "var(--ink-2)" }}>{t}</span>
                    <span style={{ textAlign: "right", color: "var(--faint)" }}>{age}</span>
                    <span style={{ textAlign: "right", color: `var(--${tone === 'ember' ? 'ember' : tone === 'applied' ? 'applied' : 'faint'})`, letterSpacing: ".14em", textTransform: "uppercase", fontSize: 10 }}>
                      {tone === 'ember' ? 'PENDING' : tone === 'applied' ? 'APPLIED' : 'past'}
                    </span>
                  </div>
                ))}
              </div>
            </section>

            <footer style={{ marginTop: 36, paddingTop: 14, borderTop: "1px solid var(--line)", display: "flex", justifyContent: "space-between" }}>
              <span className="tk-serial">REVIEW · #014 · LOCAL-FIRST · LEDGER 2026-04-25</span>
              <span className="tk-serial">PRESS ⏎ TO APPLY · ⌫ TO REJECT</span>
            </footer>
          </div>

          {/* RIGHT RAIL */}
          <aside style={{ borderLeft: "1px solid var(--line)", background: "var(--paper-2)", padding: "20px 18px", overflow: "auto" }}>
            <div className="card" style={{ padding: 14, position: "relative" }}>
              <Stamp kind="proposed" date="Apr 25" time="11:42" num="014" style={{ position: "absolute", right: -6, top: -10, transform: "rotate(-9deg)" }} />
              <div className="tk-eyebrow" style={{ marginBottom: 8 }}>Author</div>
              <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
                <PIrd.Sparkle />
                <div>
                  <div style={{ fontWeight: 500, fontSize: 13 }}>Haiku · local</div>
                  <div className="tk-meta" style={{ fontSize: 10 }}>0.84 confidence · 4s · 1.2k tokens</div>
                </div>
              </div>
              <hr className="hr-soft" style={{ margin: "10px 0" }} />
              <div className="tk-eyebrow" style={{ marginBottom: 4 }}>Confidence breakdown</div>
              <ConfBar k="Pattern match" v={0.92} />
              <ConfBar k="Reach"          v={0.88} />
              <ConfBar k="Reversibility"  v={0.99} />
              <ConfBar k="Recency · ctx"  v={0.61} />
              <hr className="hr-soft" style={{ margin: "10px 0" }} />
              <div className="tk-meta" style={{ fontSize: 10.5, lineHeight: 1.5 }}>
                Lower-than-average on recency: source capture is 2 days old. Consider double-checking before apply.
              </div>
            </div>

            <div className="card" style={{ padding: 14, marginTop: 12 }}>
              <div className="tk-eyebrow" style={{ marginBottom: 8 }}>Why now</div>
              <p className="tk-body" style={{ margin: 0, fontSize: 12.5, color: "var(--ink-2)" }}>
                Haiku noticed C-090 has accumulated <b style={{ color: "var(--ink)" }}>5 distinct workstreams</b> in its body and crossed your "split this" threshold (Settings → Heuristics).
              </p>
              <a href="#" style={{ display: "inline-block", marginTop: 8, fontFamily: "var(--mono)", fontSize: 11, color: "var(--ember)", textDecoration: "none", borderBottom: "1px solid var(--ember)" }}>Tune heuristics →</a>
            </div>

            <div className="card" style={{ padding: 14, marginTop: 12 }}>
              <div className="tk-eyebrow" style={{ marginBottom: 8 }}>Similar past decisions</div>
              <Past sn="#984" t="Split 'Auth flow' → 4 cards" verdict="applied" date="wk 14" />
              <Past sn="#962" t="Split 'Onboarding' → 3"      verdict="rejected" date="wk 13" />
              <Past sn="#941" t="Merge dupes (C-082, C-083)"  verdict="applied" date="wk 12" />
              <div className="tk-meta" style={{ fontSize: 10, marginTop: 8 }}>
                Apply rate on similar: <b style={{ color: "var(--ink)" }}>3 of 4 (75%)</b>
              </div>
            </div>

            <div className="card" style={{ padding: 14, marginTop: 12, borderColor: "var(--ember)", background: "var(--ember-tint)" }}>
              <div className="tk-eyebrow" style={{ color: "var(--ember-ink)", marginBottom: 6 }}>Decide with keys</div>
              <div style={{ display: "flex", flexDirection: "column", gap: 5, fontSize: 12, color: "var(--ember-ink)" }}>
                <KbRow k="⏎"  l="Apply proposal" />
                <KbRow k="E"  l="Request edit · opens composer" />
                <KbRow k="⌫"  l="Reject · with optional reason" />
                <KbRow k="D"  l="Defer 1h" />
                <KbRow k="P"  l="Toggle provenance pane" />
                <KbRow k="space" l="Preview diff in card detail" />
              </div>
            </div>
          </aside>
        </div>
      </div>
    </div>
  );
}

/* --- Helpers ----------------------------------------------------------- */
function Q({ active, sn, t, who, conf, age, reach, stale }) {
  return (
    <a href="#" style={{
      display: "block", padding: "12px 18px",
      textDecoration: "none", color: "inherit",
      borderLeft: active ? "2px solid var(--ember)" : stale ? "2px solid var(--whisper)" : "2px solid transparent",
      background: active ? "linear-gradient(90deg, var(--ember-bloom) 0%, transparent 70%)" : "transparent",
      opacity: stale ? .7 : 1,
    }}>
      <div style={{ display: "flex", justifyContent: "space-between", marginBottom: 4 }}>
        <span className="tk-serial" style={{ color: active ? "var(--ember)" : "var(--faint)" }}>{sn}</span>
        <span className="tk-meta" style={{ fontSize: 9.5 }}>{age}</span>
      </div>
      <div style={{ fontFamily: "var(--serif)", fontSize: 13.5, fontWeight: 500, color: active ? "var(--ink-deep)" : "var(--ink)", lineHeight: 1.3, marginBottom: 4 }}>{t}</div>
      <div className="tk-meta" style={{ fontSize: 10 }}>
        {who} {conf != null && <>· conf {conf.toFixed(2)}</>} · {reach}
      </div>
    </a>
  );
}
function U({ sn, t, left, expired }) {
  return (
    <div style={{ padding: "8px 0", borderBottom: "1px solid var(--line-soft)", fontSize: 11.5, color: expired ? "var(--faint)" : "var(--ink-2)" }}>
      <div style={{ display: "flex", justifyContent: "space-between", marginBottom: 2 }}>
        <span className="tk-serial">{sn}</span>
        {!expired && <span className="tk-serial" style={{ color: "var(--ember)" }}>↶ {left}</span>}
        {expired && <span className="tk-serial" style={{ color: "var(--faint)" }}>sealed</span>}
      </div>
      <div style={{ lineHeight: 1.35 }}>{t}</div>
    </div>
  );
}
function MiniCadence() {
  const days = [4, 3, 5, 2, 4, 1, 3];
  return (
    <div style={{ display: "flex", alignItems: "flex-end", gap: 4, height: 36 }}>
      {days.map((d, i) => (
        <div key={i} style={{ flex: 1, height: "100%", display: "flex", alignItems: "flex-end" }}>
          <div style={{ width: "100%", height: `${(d/5)*100}%`, background: i === 6 ? "var(--ember)" : "var(--ink-deep)", opacity: i === 6 ? 1 : .65 }} />
        </div>
      ))}
    </div>
  );
}

function ConfDial({ v }) {
  const C = 2 * Math.PI * 28;
  const off = C - C * v;
  return (
    <div className="card" style={{ padding: 14, width: 200, display: "flex", flexDirection: "column", alignItems: "center" }}>
      <svg width="84" height="84" viewBox="0 0 70 70">
        <circle cx="35" cy="35" r="28" stroke="var(--line)" strokeWidth="2" fill="none" />
        <circle cx="35" cy="35" r="28" stroke="var(--ember)" strokeWidth="2" fill="none"
          strokeDasharray={C} strokeDashoffset={off}
          transform="rotate(-90 35 35)" strokeLinecap="round" />
        <text x="35" y="32" textAnchor="middle" fontFamily="var(--serif)" fontStyle="italic" fontSize="18" fill="var(--ink-deep)">{v.toFixed(2)}</text>
        <text x="35" y="44" textAnchor="middle" fontFamily="var(--mono)" fontSize="6.5" letterSpacing=".2em" fill="var(--mute)">CONF</text>
      </svg>
      <div className="tk-eyebrow" style={{ marginTop: 6 }}>Above your apply threshold</div>
      <div className="tk-meta" style={{ fontSize: 10, marginTop: 2 }}>(set 0.70 · Settings)</div>
    </div>
  );
}
function SectionHeader({ n, t, sub }) {
  return (
    <header style={{ display: "flex", alignItems: "baseline", gap: 14, marginBottom: 10, paddingBottom: 8, borderBottom: "1px solid var(--line-soft)" }}>
      <span className="tk-serial" style={{ color: "var(--faint)" }}>§ {n}</span>
      <h3 className="tk-h3" style={{ margin: 0 }}>{t}</h3>
      <span className="tk-meta" style={{ marginLeft: "auto" }}>{sub}</span>
    </header>
  );
}
function DiffBefore() {
  return (
    <div className="card" style={{ padding: 14 }}>
      <div className="tk-serial">C-090</div>
      <h4 style={{ margin: "4px 0 4px", fontFamily: "var(--serif)", fontSize: 16, fontWeight: 500 }}>Implement dark mode</h4>
      <p className="tk-body" style={{ margin: 0, fontSize: 12.5, color: "var(--ink-2)" }}>Apply Paper-at-Night tokens across all surfaces. Three-way variable swap with QA pass on every screen.</p>
      <div className="tk-meta" style={{ fontSize: 10, marginTop: 10 }}>· theme · 0/0 subtasks · 1d in column</div>
    </div>
  );
}
function DiffAfter({ sn, t, b, status }) {
  const isNew = status === "new";
  return (
    <div className="card" style={{
      padding: 12,
      borderColor: isNew ? "var(--applied)" : "var(--line)",
      borderLeft: `2px solid ${isNew ? "var(--applied)" : "var(--ember)"}`,
      background: "var(--paper-card)",
    }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
        <span className="tk-serial">{sn} {isNew && <span style={{ color: "var(--applied)", marginLeft: 4 }}>· new</span>}</span>
        {!isNew && <span className="tk-serial" style={{ color: "var(--ember)" }}>· kept</span>}
      </div>
      <h5 style={{ margin: "4px 0 4px", fontFamily: "var(--serif)", fontSize: 14.5, fontWeight: 500 }}>{t}</h5>
      <p className="tk-body" style={{ margin: 0, fontSize: 12, color: "var(--ink-2)" }}>{b}</p>
    </div>
  );
}
function FieldDiff({ k, a, b, same }) {
  return (
    <>
      <div className="tk-eyebrow">{k}</div>
      <div className="diff-rem" style={{ fontSize: 11.5, opacity: same ? .35 : 1, textDecoration: same ? "none" : "line-through" }}>{a}</div>
      <div className="diff-add" style={{ fontSize: 11.5, opacity: same ? .35 : 1 }}>{b}</div>
    </>
  );
}
function ProvRow({ icon, k, v, weight }) {
  const tone = weight === "primary" ? "var(--ink)" : weight === "excluded" ? "var(--faint)" : weight === "inferred" ? "var(--ember)" : "var(--ink-2)";
  return (
    <div style={{ display: "grid", gridTemplateColumns: "32px 200px 1fr", gap: 12, padding: "11px 16px", borderBottom: "1px solid var(--line-soft)", alignItems: "flex-start" }}>
      <span style={{ color: tone, fontSize: 14, lineHeight: 1.3 }}>{icon}</span>
      <span style={{ fontFamily: "var(--serif)", fontStyle: "italic", fontSize: 13, color: tone }}>{k}</span>
      <span style={{ fontSize: 12.5, color: "var(--ink-2)" }}>{v}</span>
    </div>
  );
}
function SE({ k, v, tone }) {
  return (
    <div style={{ display: "grid", gridTemplateColumns: "140px 1fr", gap: 12, padding: "10px 16px", borderBottom: "1px solid var(--line-soft)", alignItems: "center" }}>
      <span className="tk-eyebrow" style={{ color: tone === "active" ? "var(--ember)" : "var(--faint)" }}>{k}</span>
      <span style={{ fontSize: 13, color: "var(--ink-2)", fontFamily: tone === "active" ? "var(--serif)" : "var(--sans)", fontStyle: tone === "active" ? "italic" : "normal" }}>{v}</span>
    </div>
  );
}
function Conflict({ tone, k, v }) {
  const map = { warn: "var(--overdue)", info: "var(--mute)", ok: "var(--applied)" };
  const glyph = { warn: "‼", info: "·", ok: "✓" };
  return (
    <div style={{ display: "grid", gridTemplateColumns: "32px 200px 1fr", gap: 12, padding: "12px 16px", borderBottom: "1px solid var(--line-soft)", alignItems: "center" }}>
      <span style={{ fontFamily: "var(--serif)", fontSize: 18, color: map[tone], textAlign: "center" }}>{glyph[tone]}</span>
      <span className="tagstamp" style={{ color: map[tone], width: "fit-content" }}>{tone === "warn" ? "WARNING" : tone === "ok" ? "CLEAR" : "INFO"}</span>
      <div>
        <div style={{ fontFamily: "var(--serif)", fontSize: 14, fontWeight: 500, color: "var(--ink-deep)" }}>{k}</div>
        <p className="tk-body" style={{ margin: "2px 0 0", fontSize: 12.5, color: "var(--ink-2)" }}>{v}</p>
      </div>
    </div>
  );
}
function ConfBar({ k, v }) {
  return (
    <div style={{ display: "grid", gridTemplateColumns: "1fr 80px 28px", gap: 8, alignItems: "center", marginBottom: 4 }}>
      <span style={{ fontSize: 11, color: "var(--ink-2)" }}>{k}</span>
      <div style={{ height: 4, background: "var(--paper-2)", border: "1px solid var(--line-soft)", position: "relative" }}>
        <div style={{ position: "absolute", inset: 0, width: `${v*100}%`, background: v > .8 ? "var(--applied)" : v > .6 ? "var(--ember)" : "var(--overdue)" }} />
      </div>
      <span className="tk-serial" style={{ textAlign: "right" }}>{v.toFixed(2)}</span>
    </div>
  );
}
function Past({ sn, t, verdict, date }) {
  const c = verdict === "applied" ? "var(--applied)" : "var(--overdue)";
  return (
    <div style={{ display: "grid", gridTemplateColumns: "1fr auto", padding: "6px 0", borderBottom: "1px dashed var(--line-soft)", alignItems: "center" }}>
      <div>
        <span className="tk-serial">{sn}</span>
        <div style={{ fontSize: 12, color: "var(--ink-2)", lineHeight: 1.3 }}>{t}</div>
      </div>
      <div style={{ textAlign: "right" }}>
        <span className="tagstamp" style={{ color: c, fontSize: 8.5 }}>{verdict.toUpperCase()}</span>
        <div className="tk-meta" style={{ fontSize: 10, marginTop: 2 }}>{date}</div>
      </div>
    </div>
  );
}
function KbRow({ k, l }) {
  return (
    <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
      <span className="kbd" style={{ minWidth: 32, borderColor: "var(--ember)", color: "var(--ember-ink)" }}>{k}</span>
      <span>{l}</span>
    </div>
  );
}

window.ReviewDeep = ReviewDeep;
