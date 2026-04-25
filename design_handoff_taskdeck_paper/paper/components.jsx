/* Core Paper & Graphite components.
   All built from tokens.css. Components are scoped by a parent .paper or .paper-night class.
*/

const { useState, useEffect, useMemo, useRef } = React;
const PIc = window.PaperIcons;

/* ------------------ Sidebar ------------------ */
function Sidebar({ active = "B", theme = "paper", workspace = "Solo Workspace" }) {
  const primary = [
    { k: "H", n: "Home" },
    { k: "T", n: "Today" },
    { k: "R", n: "Review", badge: 3 },
    { k: "B", n: "Boards" },
    { k: "I", n: "Inbox", badge: 5 },
  ];
  const tools = [
    { k: "V", n: "Views" },
    { k: "N", n: "Notifications" },
    { k: "C", n: "Chat" },
    { k: "D", n: "Calendar" },
    { k: "M", n: "Metrics" },
    { k: "X", n: "Integrations" },
    { k: "Y", n: "Activity" },
    { k: "O", n: "Ops" },
  ];
  const meta = [
    { k: "S", n: "Settings" },
    { k: "K", n: "API Keys" },
    { k: "P", n: "Preferences" },
    { k: "?", n: "Shortcuts" },
    { k: "→", n: "Logout" },
  ];

  return (
    <nav style={{
      width: 232, flex: "none",
      background: "var(--paper-2)",
      borderRight: "1px solid var(--line)",
      padding: "20px 0 16px",
      display: "flex", flexDirection: "column",
      fontFamily: "var(--sans)",
      position: "relative",
    }}>
      {/* Header */}
      <div style={{ padding: "0 20px 18px", borderBottom: "1px solid var(--line-soft)" }}>
        <div className="tk-h3" style={{ fontFamily: "var(--serif)", fontWeight: 500, fontSize: 18, letterSpacing: "-.01em", color: "var(--ink-deep)" }}>
          Taskdeck
        </div>
        <div className="tk-eyebrow" style={{ marginTop: 4 }}>
          Precision Mode <span style={{ color: "var(--ember)" }}>· active</span>
        </div>
      </div>

      {/* Workspace switcher */}
      <button className="btn-ghost" style={{
        margin: "12px 12px 6px", padding: "8px 10px",
        display: "flex", alignItems: "center", gap: 10,
        background: "transparent", border: "1px solid var(--line-soft)", borderRadius: 4,
        cursor: "pointer", textAlign: "left",
      }}>
        <span style={{
          width: 22, height: 22, borderRadius: 2, border: "1px solid var(--line)",
          display: "grid", placeItems: "center",
          fontFamily: "var(--serif)", fontStyle: "italic", fontSize: 12, color: "var(--ink-deep)",
          background: "var(--paper-card)",
        }}>S</span>
        <span style={{ flex: 1, fontSize: 12, fontWeight: 500, color: "var(--ink)" }}>{workspace}</span>
        <PIc.ChevronD />
      </button>

      <SidebarGroup label="Primary loop" items={primary} active={active} />
      <SidebarGroup label="Workbench tools" items={tools} active={active} />
      <div style={{ flex: 1 }} />
      <SidebarGroup label="" items={meta} active={active} muted />

      {/* Status footer */}
      <div style={{
        margin: "8px 12px 0", padding: "10px 10px",
        borderTop: "1px solid var(--line-soft)",
        fontFamily: "var(--mono)", fontSize: 9.5, letterSpacing: ".14em",
        color: "var(--mute)", display: "flex", alignItems: "center", justifyContent: "space-between",
      }}>
        <span className="status live">SYSTEM LIVE</span>
        <span style={{ color: "var(--faint)" }}>v0.7.2</span>
      </div>
    </nav>
  );
}

function SidebarGroup({ label, items, active, muted }) {
  return (
    <div style={{ padding: "10px 0 4px" }}>
      {label && (
        <div className="tk-eyebrow" style={{ padding: "8px 20px 6px", color: "var(--faint)" }}>{label}</div>
      )}
      <ul style={{ listStyle: "none", margin: 0, padding: 0 }}>
        {items.map((it, i) => {
          const isActive = it.k === active;
          return (
            <li key={i}>
              <a href="#" style={{
                display: "flex", alignItems: "center", gap: 14,
                padding: "6px 20px",
                textDecoration: "none",
                color: isActive ? "var(--ink-deep)" : (muted ? "var(--mute)" : "var(--ink-2)"),
                fontSize: 12.5, fontWeight: isActive ? 600 : 400,
                fontFamily: "var(--sans)",
                borderLeft: isActive ? "2px solid var(--ember)" : "2px solid transparent",
                background: isActive ? "linear-gradient(90deg, var(--ember-bloom) 0%, transparent 70%)" : "transparent",
                position: "relative",
              }}>
                <span style={{
                  fontFamily: "var(--mono)", fontSize: 10.5, fontWeight: 500,
                  color: isActive ? "var(--ember)" : "var(--faint)",
                  width: 14, textAlign: "center", letterSpacing: 0,
                }}>{it.k}</span>
                <span style={{ flex: 1 }}>{it.n}</span>
                {it.badge != null && (
                  <span style={{
                    fontFamily: "var(--mono)", fontSize: 10,
                    color: isActive ? "var(--ember)" : "var(--mute)",
                  }}>· {it.badge}</span>
                )}
              </a>
            </li>
          );
        })}
      </ul>
    </div>
  );
}

/* ------------------ Top bar ------------------ */
function TopBar({ crumb = ["Workspace", "Boards", "Product Backlog"], theme = "paper" }) {
  return (
    <header style={{
      height: 48, borderBottom: "1px solid var(--line)",
      display: "flex", alignItems: "center", padding: "0 20px",
      background: "var(--paper)",
      gap: 18, position: "relative",
    }}>
      {/* crumb */}
      <div style={{ display: "flex", alignItems: "center", gap: 8, fontFamily: "var(--sans)", fontSize: 12.5 }}>
        {crumb.map((c, i) => (
          <React.Fragment key={i}>
            <span style={{ color: i === crumb.length - 1 ? "var(--ink)" : "var(--mute)", fontWeight: i === crumb.length - 1 ? 500 : 400 }}>{c}</span>
            {i < crumb.length - 1 && <span style={{ color: "var(--whisper)" }}>/</span>}
          </React.Fragment>
        ))}
      </div>

      <div style={{ flex: 1 }} />

      {/* command palette entry */}
      <button style={{
        display: "flex", alignItems: "center", gap: 10,
        padding: "5px 10px 5px 8px",
        border: "1px solid var(--line)",
        borderRadius: 4,
        background: "var(--paper-card)",
        fontFamily: "var(--sans)", fontSize: 12,
        color: "var(--mute)", cursor: "pointer",
        minWidth: 320,
      }}>
        <PIc.Search />
        <span>Go anywhere · capture · ask</span>
        <span style={{ flex: 1 }} />
        <span className="kbd">⌘</span><span className="kbd">K</span>
      </button>

      <span className="status live" style={{ marginLeft: 6 }}>SYNCED · LOCAL-FIRST</span>

      <div style={{ width: 1, height: 18, background: "var(--line)" }} />

      <button className="btn-ghost" style={{ padding: 6 }}><PIc.Bell /></button>
      <button className="btn-ghost" style={{ padding: 6 }}><PIc.Settings /></button>
      <div style={{
        width: 26, height: 26, borderRadius: "50%", border: "1px solid var(--line)",
        background: "var(--paper-card)", display: "grid", placeItems: "center",
        fontFamily: "var(--serif)", fontStyle: "italic", fontSize: 13, color: "var(--ink-deep)",
      }}>D</div>
    </header>
  );
}

/* ------------------ Hairline icon button ------------------ */
function HLBtn({ icon: Icon, label, kbd, primary, ember, onClick, style }) {
  const cls = primary ? "btn btn-primary" : ember ? "btn btn-ember" : "btn";
  return (
    <button className={cls} onClick={onClick} style={style}>
      {Icon && <Icon />}
      <span>{label}</span>
      {kbd && <span className="kbd" style={{ marginLeft: 4 }}>{kbd}</span>}
    </button>
  );
}

/* ------------------ Stamp ------------------ */
function Stamp({ kind = "applied", date = "Apr 25", time = "11:42", num = "014", style }) {
  const labels = {
    applied: { l: "Reviewed", color: "var(--applied)" },
    proposed: { l: "Proposed", color: "var(--ember)" },
    captured: { l: "Captured", color: "var(--mute)" },
    overdue: { l: "Overdue", color: "var(--overdue)" },
    draft:   { l: "Draft",   color: "var(--mute)" },
  };
  const m = labels[kind];
  return (
    <span className="stamp" style={{ color: m.color, ...style }}>
      <span>{m.l}</span>
      <b>{date}</b>
      <span className="stamp-num">{time} · #{num}</span>
    </span>
  );
}

/* ------------------ Mini ledger row (used in many surfaces) ------------------ */
function LedgerRow({ idx, title, meta, status, onClick }) {
  return (
    <div onClick={onClick} style={{
      display: "grid",
      gridTemplateColumns: "44px 1fr 200px 120px 24px",
      alignItems: "center",
      padding: "10px 14px",
      borderBottom: "1px solid var(--line-soft)",
      fontFamily: "var(--sans)", fontSize: 13,
      cursor: "pointer",
    }}>
      <span className="tk-serial">{idx}</span>
      <span style={{ color: "var(--ink)" }}>{title}</span>
      <span className="tk-meta" style={{ color: "var(--mute)" }}>{meta}</span>
      <span className={`status ${status?.kind || "draft"}`}>{status?.label || "—"}</span>
      <PIc.ChevronR />
    </div>
  );
}

Object.assign(window, { Sidebar, TopBar, HLBtn, Stamp, LedgerRow });
