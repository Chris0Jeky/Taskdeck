/**
 * Hairline icon path data for Paper & Graphite icons.
 * Drawn on a 16×16 grid; rendered at 14/16/20 px via the `.hl-icon` styles
 * in `paper-tokens.css`.  Each entry is either a single `d` string or an
 * ordered array of SVG primitives that the consumer renders inside a single
 * <svg>.  Ports the JSX in `design_handoff_taskdeck_paper/paper/icons.jsx`.
 */
export type PaperIconName =
  | 'plus'
  | 'search'
  | 'stamp'
  | 'sparkle'
  | 'arrowRight'
  | 'x'
  | 'check'
  | 'pages'
  | 'pen'
  | 'cursor'
  | 'tag'
  | 'dot'
  | 'eye'
  | 'bell'
  | 'chevronDown'
  | 'chevronRight'
  | 'settings'
  | 'sun'
  | 'moon'

export type PaperIconShape =
  | { kind: 'path'; d: string }
  | { kind: 'circle'; cx: number; cy: number; r: number }
  | { kind: 'rect'; x: number; y: number; width: number; height: number }

/**
 * The canonical path data for each named icon.  Values are arrays so an icon
 * may be composed of multiple primitives (path + circle, etc.).  The entries
 * mirror the JSX reference; `pen`, `cursor`, `dot`, `sun`, and `moon` are new
 * shapes added for the Paper component primitive set.
 */
export const PAPER_ICON_SHAPES: Record<PaperIconName, PaperIconShape[]> = {
  plus: [{ kind: 'path', d: 'M8 3v10 M3 8h10' }],
  search: [
    { kind: 'circle', cx: 7, cy: 7, r: 4.5 },
    { kind: 'path', d: 'M10.4 10.4 L13.5 13.5' },
  ],
  stamp: [
    { kind: 'rect', x: 3, y: 11, width: 10, height: 2 },
    {
      kind: 'path',
      d: 'M5 11v-2a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2 M7 8V5a1 1 0 0 1 2 0v3',
    },
  ],
  sparkle: [
    {
      kind: 'path',
      d: 'M8 2 v4 M8 10v4 M2 8h4 M10 8h4 M5 5l1.5 1.5 M9.5 9.5 L11 11 M5 11l1.5-1.5 M9.5 6.5 L11 5',
    },
  ],
  arrowRight: [{ kind: 'path', d: 'M3 8h10 M9 4l4 4-4 4' }],
  x: [{ kind: 'path', d: 'M4 4l8 8 M12 4l-8 8' }],
  check: [{ kind: 'path', d: 'M3.5 8.5 L6.5 11.5 L12.5 4.5' }],
  pages: [{ kind: 'path', d: 'M3 4h6l2 2v8H3z M5 4V2h6l2 2v8' }],
  // pen / quill — derived from the Quill JSX in icons.jsx
  pen: [
    { kind: 'path', d: 'M13 3 C 9 4, 5 7, 3 13' },
    { kind: 'path', d: 'M3 13 L 6 10' },
    { kind: 'path', d: 'M11 5 L 9 7' },
  ],
  // cursor — caret-style pointer shape
  cursor: [{ kind: 'path', d: 'M3 2 L13 7 L8 8 L7 13 z' }],
  tag: [{ kind: 'path', d: 'M3 3h5l5 5-5 5-5-5z M5.5 5.5h.01' }],
  // dot — small filled-look circle drawn as a tiny stroked circle
  dot: [{ kind: 'circle', cx: 8, cy: 8, r: 1.6 }],
  eye: [
    {
      kind: 'path',
      d: 'M1.5 8 C 4 4, 6 3, 8 3 s4 1, 6.5 5 C 12 12, 10 13, 8 13 s-4-1 -6.5-5z M8 6 a2 2 0 1 0 0 4 a2 2 0 0 0 0-4',
    },
  ],
  bell: [{ kind: 'path', d: 'M4 11V8 a4 4 0 0 1 8 0 v3 l1 1 H3z M7 13.5a1.5 1 0 0 0 2 0' }],
  chevronDown: [{ kind: 'path', d: 'M3 6l5 4 5-4' }],
  chevronRight: [{ kind: 'path', d: 'M6 3l4 5-4 5' }],
  settings: [
    {
      kind: 'path',
      d: 'M8 5.5 a2.5 2.5 0 1 0 0 5 a2.5 2.5 0 0 0 0-5 M8 1.5v2 M8 12.5v2 M1.5 8h2 M12.5 8h2 M3.4 3.4l1.4 1.4 M11.2 11.2l1.4 1.4 M3.4 12.6l1.4-1.4 M11.2 4.8l1.4-1.4',
    },
  ],
  // sun — center disc + 8 rays
  sun: [
    { kind: 'circle', cx: 8, cy: 8, r: 3 },
    {
      kind: 'path',
      d: 'M8 1v2 M8 13v2 M1 8h2 M13 8h2 M3.4 3.4l1.4 1.4 M11.2 11.2l1.4 1.4 M3.4 12.6l1.4-1.4 M11.2 4.8l1.4-1.4',
    },
  ],
  // moon — crescent
  moon: [{ kind: 'path', d: 'M12 9.5 A5 5 0 0 1 6.5 4 A5 5 0 1 0 12 9.5 z' }],
}
