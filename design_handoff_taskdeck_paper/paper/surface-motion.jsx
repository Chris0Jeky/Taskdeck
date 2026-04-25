/* Variant B · Ink Bleed — full motion spec
   Choreography: Capture → Bleed-in → Compose → Settle → Stamp.
   The bleed represents the model "drawing on the paper". Each token streams as a
   tiny droplet that lands, blooms, and dries into ink — fading from saturated
   ember to ink-deep. The headline reveals through a wet/dry mask.
*/
const PIm = window.PaperIcons;

function MotionSpec({ theme = "paper" }) {
  const [t, setT] = React.useState(2.0);
  const [playing, setPlaying] = React.useState(true);

  React.useEffect(() => {
    if (!playing) return;
    let raf;
    let last = performance.now();
    const tick = (now) => {
      const dt = (now - last) / 1000;
      last = now;
      setT(prev => {
        const next = prev + dt;
        return next > 4.6 ? 0 : next;
      });
      raf = requestAnimationFrame(tick);
    };
    raf = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(raf);
  }, [playing]);

  return (
    <div className={theme} style={{ height: "100%", padding: "32px 40px 36px", fontFamily: "var(--sans)", overflow: "auto" }}>
      {/* Frontispiece */}
      <header style={{ display: "grid", gridTemplateColumns: "1.2fr auto", alignItems: "end", gap: 24, marginBottom: 22 }}>
        <div>
          <div className="tk-eyebrow">Motion spec · signature · LLM thinking state</div>
          <h1 className="tk-h1" style={{ margin: "8px 0 4px" }}>
            Variant B · <em>ink bleed.</em>
          </h1>
          <p className="tk-lede">
            A drop of seal-red lands on cream, blooms through paper fibres, then dries to ink. The motion runs at the headline-of-proposal scale and replaces every "loading" state in the product. It is the system's voice — slow, warm, and a little messy.
          </p>
        </div>
        <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
          <span className="tk-meta">Total: <b style={{ color: "var(--ink)" }}>4.6s</b></span>
          <span className="tk-meta">·</span>
          <span className="tk-meta">FPS: <b style={{ color: "var(--ink)" }}>60</b></span>
          <span className="tk-meta">·</span>
          <span className="tk-meta">Reduced-motion: <b style={{ color: "var(--ink)" }}>fade only</b></span>
        </div>
      </header>

      {/* Live stage */}
      <section className="card-lift" style={{ padding: 0, overflow: "hidden", marginBottom: 22 }}>
        <div style={{ display: "grid", gridTemplateColumns: "1fr 320px" }}>
          <div style={{ borderRight: "1px solid var(--line-soft)", padding: 28, position: "relative", minHeight: 360, background: "var(--paper-card)" }}>
            <div className="tk-eyebrow" style={{ position: "absolute", left: 28, top: 28 }}>Stage · live · {t.toFixed(2)}s</div>
            <BleedStage t={t} />
          </div>
          <aside style={{ padding: 22, background: "var(--paper-2)" }}>
            <div className="tk-eyebrow">Controls</div>
            <hr className="hr-soft" style={{ margin: "8px 0 14px" }} />
            <div style={{ display: "flex", gap: 6, marginBottom: 12 }}>
              <button className="btn" onClick={() => setPlaying(p => !p)}>{playing ? "Pause" : "Play"}</button>
              <button className="btn" onClick={() => setT(0)}>Restart</button>
            </div>
            <input type="range" min="0" max="4.6" step="0.01" value={t}
              onChange={e => { setT(parseFloat(e.target.value)); setPlaying(false); }}
              style={{ width: "100%", accentColor: "var(--ember)" }} />
            <div className="tk-meta" style={{ display: "flex", justifyContent: "space-between", marginTop: 4 }}>
              <span>0.00s</span><span>4.60s</span>
            </div>

            <div style={{ marginTop: 18 }}>
              <div className="tk-eyebrow">Phase</div>
              <PhaseDot t={t} />
            </div>

            <hr className="hr-soft" style={{ margin: "16px 0" }} />
            <div className="tk-eyebrow">Materials</div>
            <ul style={{ margin: "6px 0 0", padding: 0, listStyle: "none", fontSize: 12, color: "var(--ink-2)", lineHeight: 1.7 }}>
              <li>Ink — seal red <span className="tk-serial">#a8421f</span></li>
              <li>Paper — cream <span className="tk-serial">#f3eee5</span></li>
              <li>Fibre — long, 47px stride</li>
              <li>Bloom — multiply, 8px blur</li>
              <li>Drip — irregular, 4 droplets</li>
              <li>Reveal — wet/dry mask</li>
            </ul>
          </aside>
        </div>
      </section>

      {/* Choreography timeline */}
      <section style={{ marginBottom: 22 }}>
        <h2 className="tk-h3" style={{ margin: "0 0 12px" }}>Choreography</h2>
        <div className="card" style={{ padding: 18 }}>
          <Timeline t={t} />
          <div className="tk-meta" style={{ marginTop: 10, display: "flex", justifyContent: "space-between" }}>
            <span>Drop · 0–0.4s</span>
            <span>Bloom · 0.4–1.4s</span>
            <span>Compose · 1.4–3.4s</span>
            <span>Settle · 3.4–4.2s</span>
            <span>Stamp · 4.2–4.6s</span>
          </div>
        </div>
      </section>

      {/* Phase grid */}
      <section style={{ marginBottom: 22 }}>
        <h2 className="tk-h3" style={{ margin: "0 0 12px" }}>Phase frames</h2>
        <div style={{ display: "grid", gridTemplateColumns: "repeat(5, 1fr)", gap: 12 }}>
          {[
            { t: 0.2, n: "01", k: "Drop", b: "A single droplet falls from above the headline. Subtle parallax — the page settles 1px on impact." },
            { t: 0.9, n: "02", k: "Bloom", b: "First bleed reaches ~40% of its final radius. Edge irregularity is hand-drawn, not radial." },
            { t: 2.4, n: "03", k: "Compose", b: "Subsequent droplets land on rhythm with token streaming. Headline reveals through bleed mask." },
            { t: 3.8, n: "04", k: "Settle", b: "Bleed desaturates from ember to ink-deep as it 'dries'. Diff cards crossfade in below." },
            { t: 4.5, n: "05", k: "Stamp", b: "Round seal embosses with a 1px shadow. Audible if sound enabled. Ready for decision." },
          ].map(p => (
            <div key={p.n} className="card" style={{ padding: 0, overflow: "hidden" }}>
              <div style={{ position: "relative", height: 160, background: "var(--paper-card)", overflow: "hidden", borderBottom: "1px solid var(--line-soft)" }}>
                <BleedStage t={p.t} mini />
              </div>
              <div style={{ padding: 12 }}>
                <div style={{ display: "flex", justifyContent: "space-between", alignItems: "baseline" }}>
                  <span className="tk-serial">{p.n}</span>
                  <span className="tk-meta">{p.t.toFixed(1)}s</span>
                </div>
                <div style={{ fontFamily: "var(--serif)", fontSize: 16, fontWeight: 500, margin: "2px 0 4px", color: "var(--ink-deep)" }}>{p.k}</div>
                <p className="tk-body" style={{ margin: 0, fontSize: 12, color: "var(--ink-2)" }}>{p.b}</p>
              </div>
            </div>
          ))}
        </div>
      </section>

      {/* Where it appears */}
      <section style={{ marginBottom: 22 }}>
        <h2 className="tk-h3" style={{ margin: "0 0 12px" }}>Where it appears</h2>
        <div className="card" style={{ padding: 0, overflow: "hidden" }}>
          {[
            ["Review", "Awaiting proposal · headline of the proposal card", "full · 4.6s · auto-pauses if user scrolls"],
            ["Inbox · capture", "After ⌘; while haiku is structuring a capture into title + tags", "compose only · 1.4–3.4s loop"],
            ["Command palette", "AI-action row, before the proposal preview renders", "drop + bloom · 0–1.4s, single droplet"],
            ["Card detail", "When opening a card with an attached pending proposal", "drop only · 0–0.4s flash on the badge"],
            ["Toasts", "On 'Proposed' notification — corner of the seal blooms once", "bloom only · 0.6s"],
          ].map(([k, where, dur], i) => (
            <div key={i} style={{ display: "grid", gridTemplateColumns: "120px 1fr 280px", padding: "12px 16px", borderBottom: i === 4 ? "none" : "1px solid var(--line-soft)", alignItems: "center" }}>
              <span style={{ fontFamily: "var(--serif)", fontSize: 14, fontWeight: 500, color: "var(--ink-deep)" }}>{k}</span>
              <span className="tk-body" style={{ fontSize: 13, color: "var(--ink-2)" }}>{where}</span>
              <span className="tk-meta" style={{ textAlign: "right" }}>{dur}</span>
            </div>
          ))}
        </div>
      </section>

      {/* Engineering specs */}
      <section style={{ marginBottom: 22 }}>
        <h2 className="tk-h3" style={{ margin: "0 0 12px" }}>Engineering specs</h2>
        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 16 }}>
          <SpecBlock title="Easing"
            rows={[
              ["Drop · y", "cubic-bezier(.45, 0, .15, 1)", "260ms"],
              ["Bloom · scale", "cubic-bezier(.2, .65, .25, 1)", "1000ms"],
              ["Bloom · opacity", "linear", "1400ms"],
              ["Reveal · mask", "cubic-bezier(.3, .8, .3, 1)", "2000ms"],
              ["Stamp · press", "cubic-bezier(.4, 0, .15, 1)", "320ms"],
            ]} />
          <SpecBlock title="Tokens & params"
            rows={[
              ["Droplets", "4 (irregular spacing)", "—"],
              ["Bloom radius", "min 80px, max 240px", "responsive"],
              ["Mix-blend-mode", "multiply", "fixed"],
              ["Filter", "blur(6px) → blur(10px)", "during dry"],
              ["Reduced motion", "fade-only · 200ms", "WCAG 2.3.3"],
              ["Fallback", "static stamp at t = 4.6s", "no JS"],
            ]} />
        </div>
      </section>

      {/* Don'ts */}
      <section>
        <h2 className="tk-h3" style={{ margin: "0 0 12px" }}>Don't</h2>
        <div style={{ display: "grid", gridTemplateColumns: "repeat(3, 1fr)", gap: 12 }}>
          <Dont>Don't loop the bloom indefinitely. The bleed must dry. If the model is still composing past 4.6s, hold the dried state and pulse the eyebrow only.</Dont>
          <Dont>Don't tint the bloom anything but ember. Other colors break the metaphor — ink is a single-pigment system.</Dont>
          <Dont>Don't run two bleeds simultaneously on the same view. They compete for the user's eye and dilute the meaning.</Dont>
        </div>
      </section>

      <footer style={{ marginTop: 30, paddingTop: 14, borderTop: "1px solid var(--line)", display: "flex", justifyContent: "space-between" }}>
        <span className="tk-serial">SPEC · MOTION-B · 2026-04-25 · v1</span>
        <span className="tk-serial">PAPER &amp; GRAPHITE · EMBER EDITION</span>
      </footer>
    </div>
  );
}

/* --- The bleed stage ---------------------------------------------------- */
function BleedStage({ t, mini }) {
  // Lifecycle markers
  const droplets = [
    { x: 38, y: 48, delay: 0.0,  r: 110 },
    { x: 56, y: 56, delay: 0.7,  r: 80 },
    { x: 30, y: 64, delay: 1.6,  r: 70 },
    { x: 64, y: 44, delay: 2.5,  r: 90 },
  ];

  const dryT = (tt, delay) => {
    // returns 0..1 where 1 = fully dried (desaturated to ink)
    const elapsed = Math.max(0, tt - delay);
    return Math.min(1, elapsed / 2.6);
  };
  const blob = (tt, delay) => {
    const elapsed = Math.max(0, tt - delay);
    const grow = Math.min(1, elapsed / 1.0);
    const fade = elapsed > 1.0 ? Math.max(0, 1 - (elapsed - 1.0) / 1.6) : 1;
    return { grow, fade };
  };

  const headlineReveal = Math.min(1, Math.max(0, (t - 0.4) / 2.6));
  const stampOn = t > 4.0;
  const stampPressed = t > 4.2 && t < 4.5;

  return (
    <div style={{
      position: "absolute", inset: 0,
      overflow: "hidden",
    }}>
      {droplets.map((d, i) => {
        const { grow, fade } = blob(t, d.delay);
        const dry = dryT(t, d.delay);
        if (grow === 0 && fade === 0) return null;
        // ink color shifts from ember to ink-deep as it dries
        const r0 = 0xa8, g0 = 0x42, b0 = 0x1f;
        const r1 = 0x1a, g1 = 0x18, b1 = 0x14;
        const cr = Math.round(r0 + (r1 - r0) * dry);
        const cg = Math.round(g0 + (g1 - g0) * dry);
        const cb = Math.round(b0 + (b1 - b0) * dry);
        const ink = `rgb(${cr},${cg},${cb})`;
        return (
          <div key={i} style={{
            position: "absolute",
            left: `${d.x}%`, top: `${d.y}%`,
            width: d.r * (mini ? 0.7 : 1), height: d.r * (mini ? 0.7 : 1),
            transform: `translate(-50%,-50%) scale(${0.2 + grow * 1.2})`,
            background: `radial-gradient(circle, ${ink} 0%, ${ink} 28%, transparent 72%)`,
            filter: `blur(${6 + dry * 4}px)`,
            opacity: 0.78 * fade * (1 - dry * 0.35),
            mixBlendMode: "multiply",
            transition: "none",
          }} />
        );
      })}

      {/* Headline with reveal mask */}
      <div style={{
        position: "absolute",
        left: mini ? 16 : 28,
        right: mini ? 16 : 28,
        bottom: mini ? 14 : 32,
      }}>
        <div className="tk-eyebrow" style={{ marginBottom: mini ? 4 : 8, opacity: t > 0.3 ? 1 : 0, transition: "opacity .4s" }}>
          {stampOn ? "Proposal · ready" : "haiku is composing…"}
        </div>
        <div style={{
          fontFamily: "var(--serif)", fontWeight: 400, fontStyle: "italic",
          fontSize: mini ? 16 : 36, lineHeight: 1.06, letterSpacing: "-.014em",
          color: "var(--ink-deep)",
          WebkitMaskImage: `linear-gradient(90deg, #000 ${headlineReveal * 100}%, transparent ${headlineReveal * 100 + 12}%)`,
          maskImage: `linear-gradient(90deg, #000 ${headlineReveal * 100}%, transparent ${headlineReveal * 100 + 12}%)`,
        }}>
          Split <span style={{ color: "var(--ember)" }}>"Implement dark mode"</span> into three smaller cards.
        </div>
        {!mini && (
          <div className="tk-meta" style={{ marginTop: 10, opacity: t > 1.5 ? 1 : 0, transition: "opacity .4s" }}>
            haiku · local · 0.84 confidence · reading 7 references
          </div>
        )}
      </div>

      {/* Stamp */}
      {stampOn && (
        <div style={{
          position: "absolute", right: mini ? 12 : 28, top: mini ? 12 : 28,
          transform: `rotate(-7deg) ${stampPressed ? 'scale(.96) translateY(1px)' : 'scale(1)'}`,
          transition: "transform 320ms cubic-bezier(.4,0,.15,1)",
          opacity: stampOn ? 1 : 0,
        }}>
          <div className="stamp ember" style={{ width: mini ? 50 : 84, height: mini ? 50 : 84, fontSize: mini ? 7 : 9 }}>
            <span style={{ fontSize: mini ? 6 : 9 }}>Proposed</span>
            <b style={{ fontSize: mini ? 9 : 13 }}>Apr 25</b>
            {!mini && <span className="stamp-num">11:42 · #014</span>}
          </div>
        </div>
      )}
    </div>
  );
}

function PhaseDot({ t }) {
  const phases = [
    { l: "Drop", s: 0,   e: 0.4 },
    { l: "Bloom", s: 0.4, e: 1.4 },
    { l: "Compose", s: 1.4, e: 3.4 },
    { l: "Settle", s: 3.4, e: 4.2 },
    { l: "Stamp", s: 4.2, e: 4.6 },
  ];
  const cur = phases.findIndex(p => t >= p.s && t < p.e);
  return (
    <div style={{ display: "flex", gap: 6, marginTop: 6 }}>
      {phases.map((p, i) => (
        <div key={i} style={{
          flex: 1, padding: "6px 4px", textAlign: "center",
          background: i === cur ? "var(--ember-tint)" : "var(--paper-card)",
          border: `1px solid ${i === cur ? "var(--ember)" : "var(--line-soft)"}`,
          borderRadius: 2,
          fontFamily: "var(--mono)", fontSize: 9, color: i === cur ? "var(--ember-ink)" : "var(--mute)",
          letterSpacing: ".1em", textTransform: "uppercase",
        }}>{p.l}</div>
      ))}
    </div>
  );
}

function Timeline({ t }) {
  const total = 4.6;
  const phases = [
    { l: "Drop",     s: 0,   e: 0.4, c: "var(--ember)" },
    { l: "Bloom",    s: 0.4, e: 1.4, c: "var(--ember)" },
    { l: "Compose",  s: 1.4, e: 3.4, c: "var(--ink-deep)" },
    { l: "Settle",   s: 3.4, e: 4.2, c: "var(--applied)" },
    { l: "Stamp",    s: 4.2, e: 4.6, c: "var(--ember)" },
  ];
  return (
    <div style={{ position: "relative", height: 32 }}>
      <div style={{ position: "absolute", inset: 0, display: "flex", borderRadius: 2, overflow: "hidden", border: "1px solid var(--line)" }}>
        {phases.map((p, i) => (
          <div key={i} style={{
            width: `${((p.e - p.s) / total) * 100}%`,
            background: i % 2 ? "var(--paper-2)" : "var(--paper-card)",
            borderRight: i === phases.length - 1 ? "none" : "1px solid var(--line-soft)",
            position: "relative",
          }}>
            <div style={{ position: "absolute", left: 6, top: 4, bottom: 4, width: 2, background: p.c, opacity: .35 }} />
          </div>
        ))}
      </div>
      {/* playhead */}
      <div style={{
        position: "absolute", top: -4, bottom: -4, left: `${(t / total) * 100}%`,
        width: 2, background: "var(--ember)", boxShadow: "0 0 0 3px var(--ember-bloom)",
      }} />
      <div style={{
        position: "absolute", top: -22, left: `${(t / total) * 100}%`, transform: "translateX(-50%)",
        fontFamily: "var(--mono)", fontSize: 10, color: "var(--ember)",
      }}>{t.toFixed(2)}s</div>
    </div>
  );
}

function SpecBlock({ title, rows }) {
  return (
    <div className="card" style={{ padding: 0, overflow: "hidden" }}>
      <header style={{ padding: "10px 14px", borderBottom: "1px solid var(--line-soft)", background: "var(--paper-2)" }}>
        <span className="tk-eyebrow">{title}</span>
      </header>
      <div>
        {rows.map(([k, v, d], i) => (
          <div key={i} style={{
            display: "grid", gridTemplateColumns: "1.1fr 1.4fr 80px", gap: 10,
            padding: "9px 14px", borderBottom: i === rows.length - 1 ? "none" : "1px dashed var(--line-soft)",
            alignItems: "center", fontSize: 12,
          }}>
            <span style={{ color: "var(--ink-2)" }}>{k}</span>
            <span className="tk-serial" style={{ color: "var(--ink)" }}>{v}</span>
            <span className="tk-meta" style={{ textAlign: "right" }}>{d}</span>
          </div>
        ))}
      </div>
    </div>
  );
}
function Dont({ children }) {
  return (
    <div className="card" style={{ padding: 14, borderColor: "var(--overdue)", background: "var(--overdue-tint)" }}>
      <div className="tk-eyebrow" style={{ color: "var(--overdue)", marginBottom: 6 }}>Don't</div>
      <p className="tk-body" style={{ margin: 0, fontSize: 12.5, color: "var(--ink-2)" }}>{children}</p>
    </div>
  );
}

window.MotionSpec = MotionSpec;
