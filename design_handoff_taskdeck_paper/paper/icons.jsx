/* Hairline / engraving-register icon set for Paper & Graphite.
   1px strokes, square cap, no fill. Drawn at 16×16 grid; render at 14/16/20.
*/
const HLIcon = ({ d, size = 14, vb = "0 0 16 16", style }) => (
  <svg viewBox={vb} className={`hl-icon ${size === 16 ? 'hl-icon-md' : size === 20 ? 'hl-icon-lg' : ''}`} style={style} aria-hidden="true">
    {typeof d === 'string' ? <path d={d} /> : d}
  </svg>
);
const I = HLIcon;
const PaperIconsMap = {
  // nav letterforms are typographic — these are action icons
  Search:   () => <I d={<><circle cx="7" cy="7" r="4.5" /><path d="M10.4 10.4 L13.5 13.5" /></>} />,
  Plus:     () => <I d="M8 3v10 M3 8h10" />,
  Check:    () => <I d="M3.5 8.5 L6.5 11.5 L12.5 4.5" />,
  X:        () => <I d="M4 4l8 8 M12 4l-8 8" />,
  Arrow:    () => <I d="M3 8h10 M9 4l4 4-4 4" />,
  Return:   () => <I d="M13 4v3a2 2 0 0 1-2 2H4 M7 6 3 9l4 3" />,
  Stamp:    () => <I d={<><rect x="3" y="11" width="10" height="2" /><path d="M5 11v-2a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2 M7 8V5a1 1 0 0 1 2 0v3" /></>} />,
  Quill:    () => <I d={<><path d="M13 3 C 9 4, 5 7, 3 13" /><path d="M3 13 L 6 10" /><path d="M11 5 L 9 7" /></>} />,
  Inkdrop:  () => <I d="M8 2 C 5 6, 4 8, 4 10 a4 4 0 0 0 8 0 c0-2 -1-4 -4-8 z" />,
  Page:     () => <I d="M4 2h6l3 3v9H4z M10 2v3h3" />,
  Pages:    () => <I d="M3 4h6l2 2v8H3z M5 4V2h6l2 2v8" />,
  Card:     () => <I d="M2 4h12v8H2z M2 7h12" />,
  Inbox:    () => <I d={<><path d="M2 9 L4 4 h8 l2 5 V13 H2z" /><path d="M2 9 h4 l1 1 h2 l1-1 h4" /></>} />,
  Bell:     () => <I d="M4 11V8 a4 4 0 0 1 8 0 v3 l1 1 H3z M7 13.5a1.5 1 0 0 0 2 0" />,
  Cmd:      () => <I d="M5 5h6v6H5z M5 5a1.5 1.5 0 1 1-1.5 1.5H5 M11 5a1.5 1.5 0 1 0 1.5 1.5H11 M5 11a1.5 1.5 0 1 0-1.5-1.5H5 M11 11a1.5 1.5 0 1 1 1.5-1.5H11" />,
  Esc:      () => <I d={<><rect x="2" y="5" width="12" height="6" rx="1"/><text x="8" y="9.5" fontSize="4.4" textAnchor="middle" fontFamily="var(--mono)" fill="currentColor" stroke="none">esc</text></>} />,
  Drag:     () => <I d="M6 4v8 M10 4v8" />,
  More:     () => <I d="M3 8h.01 M8 8h.01 M13 8h.01" />,
  Undo:     () => <I d="M3 8 a5 5 0 0 1 9-3 M3 8 V4 M3 8h4" />,
  Time:     () => <I d="M8 3 a5 5 0 1 0 0 10 a5 5 0 0 0 0-10 M8 5v3l2 1.5" />,
  Calendar: () => <I d="M3 5h10v8H3z M3 7h10 M5 3v3 M11 3v3" />,
  Tag:      () => <I d="M3 3h5l5 5-5 5-5-5z M5.5 5.5h.01" />,
  Eye:      () => <I d="M1.5 8 C 4 4, 6 3, 8 3 s4 1, 6.5 5 C 12 12, 10 13, 8 13 s-4-1 -6.5-5z M8 6 a2 2 0 1 0 0 4 a2 2 0 0 0 0-4" />,
  Filter:   () => <I d="M2 4h12 l-4 5 v4 l-4-1 V9z" />,
  Sparkle:  () => <I d="M8 2 v4 M8 10v4 M2 8h4 M10 8h4 M5 5l1.5 1.5 M9.5 9.5 L11 11 M5 11l1.5-1.5 M9.5 6.5 L11 5" />,
  ChevronR: () => <I d="M6 3l4 5-4 5" />,
  ChevronL: () => <I d="M10 3 L6 8l4 5" />,
  ChevronD: () => <I d="M3 6l5 4 5-4" />,
  Settings: () => <I d="M8 5.5 a2.5 2.5 0 1 0 0 5 a2.5 2.5 0 0 0 0-5 M8 1.5v2 M8 12.5v2 M1.5 8h2 M12.5 8h2 M3.4 3.4l1.4 1.4 M11.2 11.2l1.4 1.4 M3.4 12.6l1.4-1.4 M11.2 4.8l1.4-1.4" />,
  User:     () => <I d="M8 2.5 a2.5 2.5 0 1 1 0 5 a2.5 2.5 0 0 1 0-5 M3 14 a5 5 0 0 1 10 0" />,
  Users:    () => <I d="M5 4 a2 2 0 1 1 0 4 a2 2 0 0 1 0-4 M2 13 a3 3 0 0 1 6 0 M11 5 a1.7 1.7 0 1 1 0 3.4 M9 13 a3 3 0 0 1 5 0" />,
  Link:     () => <I d="M6 10 L4.5 11.5 a2.1 2.1 0 1 1-3-3 L3 7 M10 6 l1.5-1.5 a2.1 2.1 0 1 1 3 3 L13 9 M5.5 10.5 l5-5" />,
  Layers:   () => <I d="M8 2 l6 3 -6 3 -6-3z M2 8 l6 3 6-3 M2 11 l6 3 6-3" />,
  Folder:   () => <I d="M2 5 V12 a1 1 0 0 0 1 1 H13 a1 1 0 0 0 1-1 V6 a1 1 0 0 0-1-1 H7 L6 4 H3 a1 1 0 0 0-1 1z" />,
  Logout:   () => <I d="M9 3 H4 a1 1 0 0 0-1 1 V12 a1 1 0 0 0 1 1 H9 M11 5 l3 3 -3 3 M14 8 H7" />,
  Help:     () => <I d="M8 2.5 a5.5 5.5 0 1 0 0 11 a5.5 5.5 0 0 0 0-11 M6 6.5 a2 2 0 1 1 2 2 V10 M8 12 v.01" />,
  Mic:      () => <I d="M8 3 a2 2 0 0 0-2 2 v3 a2 2 0 0 0 4 0 V5 a2 2 0 0 0-2-2 M5 8 a3 3 0 0 0 6 0 M8 11 v2.5" />,
  Activity: () => <I d="M2 8 H5 L7 4 L9 12 L11 8 H14" />,
  Diff:     () => <I d="M5 3 v8 M3 5 h4 M9 9 a2 2 0 1 1 4 0 a2 2 0 0 1-4 0 M5 11 l5-5" />,
  // Three-line "ledger" mark for review
  Ledger:   () => <I d="M3 3 H13 V13 H3z M5 6 H11 M5 8.5 H11 M5 11 H8" />,
  // Section mark / pilcrow-ish (decorative dingbat)
  Pilcrow:  () => <I d="M11 3 H6 a2.5 2.5 0 0 0 0 5 H8 V13 M10 3 V13" />,
  // Quote glyph (decorative)
  Quote:    () => <I d="M3 7 v3 h3 V7 H3 M3 7 a3 3 0 0 1 3-3 M9 7 v3 h3 V7 H9 M9 7 a3 3 0 0 1 3-3" />,
};

window.PaperIcons = PaperIconsMap;
