/* Surface: Inbox / Capture — the product's signature input.
   Two variants:
   A · Single-line nib · a fountain-pen-feel single-input bar; magnetic, near-zero-friction
   B · Composer ledger · a structured multi-field, ledger-aligned form
*/
const PIi = window.PaperIcons;

function InboxSurface({ theme = "paper", variant = "A" }) {
  return (
    <div className={theme} style={{ display: "flex", height: "100%", minHeight: 820, fontFamily: "var(--sans)" }}>
      <Sidebar active="I" theme={theme} />
      <div style={{ flex: 1, display: "flex", flexDirection: "column", minWidth: 0 }}>
        <TopBar crumb={["Workspace", "Inbox"]} />

        <div style={{ flex: 1, padding: "32px 40px 40px", overflow: "auto" }}>
          <div style={{ display: "grid", gridTemplateColumns: "1fr 320px", gap: 32, alignItems: "start" }}>
            <div>
              {/* Frontispiece */}
              <div className="tk-eyebrow">Inbox · capture surface · 5 in queue</div>
              <h1 className="tk-h1" style={{ margin: "8px 0 6px" }}>What's on your mind, <em>quickly?</em></h1>
              <p className="tk-lede">Drop the thought. It will sit here, untouched, until you triage it. Nothing flows to the board without your approval.</p>

              {/* Capture variant */}
              <div style={{ marginTop: 20 }}>
                {variant === "B" ? <CaptureB /> : <CaptureA />}
              </div>

              {/* Queue */}
              <section style={{ marginTop: 32 }}>
                <div style={{ display: "flex", alignItems: "baseline", justifyContent: "space-between", marginBottom: 12 }}>
                  <h3 className="tk-h3">Today's captures</h3>
                  <span className="tk-meta">5 items · most recent first</span>
                </div>
                <div className="card" style={{ padding: 0, overflow: "hidden" }}>
                  <CaptureItem t="09:42" body="Look into the conflict-free replicated capture log idea I read about last night." tags={["arch","read-later"]} />
                  <CaptureItem t="09:17" body="Daniel mentioned the review-first thesis lands harder if the stamp 'un-embosses' on undo. Test." tags={["motion"]} />
                  <CaptureItem t="08:58" body="QA on dark mode AA contrast — Paper at Night specifically." tags={["qa","theme"]} hot />
                  <CaptureItem t="08:31" body="The empty-state for Inbox should still say something. A pen and a phrase." tags={["copy"]} />
                  <CaptureItem t="08:05" body="Check whether the shortcut overlay should be a sheet or a card." tags={["ui"]} />
                </div>
              </section>
            </div>

            {/* Aside — captured today / cadence */}
            <aside style={{ display: "flex", flexDirection: "column", gap: 14, position: "sticky", top: 32 }}>
              <div className="card" style={{ padding: 16 }}>
                <div className="tk-eyebrow" style={{ marginBottom: 8 }}>Cadence · last 7 days</div>
                <Cadence />
                <div className="tk-meta" style={{ marginTop: 8, fontSize: 10 }}>
                  Triaged within <b style={{ color: "var(--ink)" }}>median 14m</b>. Inbox zero <b style={{ color: "var(--ink)" }}>4 of 7</b> days.
                </div>
              </div>
              <div className="card" style={{ padding: 16 }}>
                <div className="tk-eyebrow" style={{ marginBottom: 8 }}>How capture works</div>
                <p className="tk-body" style={{ margin: "0 0 8px", fontSize: 12.5, color: "var(--ink-2)" }}>
                  Captures sit here until you triage them. Linking a capture to a board <span className="tk-ink-italic">creates a proposal</span>, not a card. Proposals require review.
                </p>
                <a href="#" style={{ fontFamily: "var(--mono)", fontSize: 11, color: "var(--ink)", textDecoration: "none", borderBottom: "1px solid var(--line)" }}>Read the loop →</a>
              </div>
              <div className="card" style={{ padding: 16, background: "var(--ember-tint)", borderColor: "var(--ember)" }}>
                <div className="tk-eyebrow" style={{ color: "var(--ember-ink)", marginBottom: 6 }}>Tip · keyboard</div>
                <p className="tk-body" style={{ margin: 0, fontSize: 12.5, color: "var(--ember-ink)" }}>
                  From anywhere, <span className="kbd" style={{ borderColor: "var(--ember)", color: "var(--ember-ink)" }}>⌘</span><span className="kbd" style={{ borderColor: "var(--ember)", color: "var(--ember-ink)" }}>;</span> opens this capture bar without leaving your view.
                </p>
              </div>
            </aside>
          </div>
        </div>
      </div>
    </div>
  );
}

/* ----------------- Capture variant A — single-line nib ----------------- */
function CaptureA() {
  return (
    <div style={{
      position: "relative",
      background: "var(--paper-card)",
      border: "1px solid var(--line)",
      borderRadius: 4,
      padding: "18px 22px 18px 60px",
      boxShadow: "var(--shadow-lift)",
    }}>
      {/* nib glyph */}
      <div style={{
        position: "absolute", left: 18, top: 16,
        width: 28, height: 28, color: "var(--ember)",
      }}>
        <PIi.Quill />
      </div>
      {/* underline rule */}
      <div style={{ position: "absolute", left: 60, right: 22, bottom: 14, height: 1, background: "var(--line)" }} />

      <div className="tk-eyebrow" style={{ marginBottom: 6 }}>Quick capture · ⌘;</div>
      <div style={{
        fontFamily: "var(--serif)", fontStyle: "italic", fontSize: 26,
        color: "var(--ink-deep)", letterSpacing: "-.005em", fontWeight: 400,
        minHeight: 38, display: "flex", alignItems: "center",
      }}>
        Look into local-first conflict resolution at apply-time
        <span style={{ display: "inline-block", width: 1, height: 26, background: "var(--ember)", marginLeft: 4, animation: "blink 1.1s steps(2) infinite" }} />
      </div>

      <div style={{ display: "flex", alignItems: "center", gap: 10, marginTop: 18 }}>
        <span className="kbd-light kbd">#</span><span className="tk-meta">tag</span>
        <span style={{ color: "var(--whisper)", margin: "0 4px" }}>·</span>
        <span className="kbd-light kbd">@</span><span className="tk-meta">link</span>
        <span style={{ color: "var(--whisper)", margin: "0 4px" }}>·</span>
        <span className="kbd-light kbd">/</span><span className="tk-meta">command</span>
        <span style={{ flex: 1 }} />
        <button className="btn-ghost btn" style={{ padding: "6px 10px", fontSize: 11 }}><PIi.Mic /> Hold to dictate</button>
        <HLBtn label="Capture" kbd="⏎" ember />
      </div>
      <style>{`@keyframes blink { 50% { opacity: 0; } }`}</style>
    </div>
  );
}

/* ----------------- Capture variant B — composer ledger ----------------- */
function CaptureB() {
  return (
    <div className="card-lift" style={{ padding: 0, overflow: "hidden" }}>
      <header style={{
        display: "flex", alignItems: "center", justifyContent: "space-between",
        padding: "12px 16px", borderBottom: "1px solid var(--line-soft)", background: "var(--paper-2)",
      }}>
        <span className="tagstamp" style={{ color: "var(--ember)" }}>CAPTURE · DRAFT</span>
        <span className="tk-meta">#2026-04-25-038 · 09:48 PT · local-only</span>
      </header>
      <div style={{ padding: 22, display: "grid", gridTemplateColumns: "1fr", gap: 12 }}>
        <Field label="Title" placeholder="What is it?" big serif value="Local-first conflict resolution at apply-time" />
        <Field label="Body" rows={3} placeholder="The thought, in plain language" value="If two devices propose conflicting changes to the same card, resolve at apply-time, not at sync-time. Surface the conflict in Review with both diffs." />
        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr", gap: 12 }}>
          <Field label="Tags" value="#arch · #read-later" mono />
          <Field label="Link" value="—" mono />
          <Field label="Suggested board" value="Product Backlog · Backlog column" mono />
        </div>
      </div>
      <footer style={{ padding: "12px 16px", borderTop: "1px solid var(--line-soft)", display: "flex", alignItems: "center", gap: 10 }}>
        <span className="tk-meta">Captures land in Inbox. Linking to a board creates a <span className="tk-ink-italic">proposal</span>, not a card.</span>
        <span style={{ flex: 1 }} />
        <HLBtn label="Save draft" kbd="⌘S" />
        <HLBtn label="Capture" kbd="⏎" ember />
      </footer>
    </div>
  );
}

function Field({ label, placeholder, rows, value, big, serif, mono }) {
  return (
    <label style={{ display: "block" }}>
      <div className="tk-eyebrow" style={{ marginBottom: 6 }}>{label}</div>
      <div style={{
        background: "var(--paper)",
        border: "1px solid var(--line-soft)",
        borderBottom: "1px solid var(--line)",
        borderRadius: 2,
        padding: rows ? "10px 12px" : "8px 12px",
        minHeight: rows ? 22 * rows : "auto",
        fontFamily: mono ? "var(--mono)" : serif ? "var(--serif)" : "var(--sans)",
        fontSize: big ? 20 : mono ? 12 : 13.5,
        fontStyle: serif ? "italic" : "normal",
        color: value ? "var(--ink-deep)" : "var(--mute)",
        fontWeight: serif ? 400 : 400,
      }}>
        {value || placeholder}
      </div>
    </label>
  );
}

function CaptureItem({ t, body, tags, hot }) {
  return (
    <div style={{
      display: "grid", gridTemplateColumns: "60px 1fr auto",
      alignItems: "flex-start", gap: 16,
      padding: "14px 18px", borderBottom: "1px solid var(--line-soft)",
      position: "relative",
      background: hot ? "linear-gradient(90deg, var(--ember-bloom) 0%, transparent 30%)" : "transparent",
    }}>
      {hot && <span style={{ position: "absolute", left: 0, top: 0, bottom: 0, width: 2, background: "var(--ember)" }} />}
      <span className="tk-serial">{t}</span>
      <div>
        <p className="tk-body" style={{ margin: 0, fontSize: 13.5, color: "var(--ink)" }}>{body}</p>
        <div style={{ display: "flex", gap: 12, marginTop: 6, fontFamily: "var(--mono)", fontSize: 10, letterSpacing: ".14em", textTransform: "uppercase", color: "var(--mute)" }}>
          {tags.map(tt => <span key={tt}>· {tt}</span>)}
        </div>
      </div>
      <div style={{ display: "flex", gap: 6 }}>
        <button className="btn-ghost btn" style={{ padding: "4px 8px", fontSize: 10.5 }}>Triage</button>
        <button className="btn-ghost btn" style={{ padding: "4px 8px", fontSize: 10.5, color: "var(--ember)" }}>Propose</button>
      </div>
    </div>
  );
}

function Cadence() {
  const days = [3, 6, 4, 5, 7, 2, 5];
  const max = 8;
  return (
    <div style={{ display: "flex", alignItems: "flex-end", gap: 6, height: 60 }}>
      {days.map((d, i) => (
        <div key={i} style={{ flex: 1, display: "flex", flexDirection: "column", alignItems: "center", gap: 4, height: "100%" }}>
          <div style={{ flex: 1, width: "100%", display: "flex", alignItems: "flex-end" }}>
            <div style={{ width: "100%", height: `${(d/max)*100}%`, background: i === days.length-1 ? "var(--ember)" : "var(--ink-deep)", opacity: i === days.length-1 ? 1 : 0.7 }} />
          </div>
          <span className="tk-serial" style={{ fontSize: 9 }}>{["M","T","W","T","F","S","S"][i]}</span>
        </div>
      ))}
    </div>
  );
}

window.InboxSurface = InboxSurface;
