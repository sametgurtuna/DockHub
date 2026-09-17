const line = (body: string, extra = '') =>
  `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"${extra}>${body}</svg>`;

function gearPath(): string {
  const cx = 12;
  const cy = 12;
  const teeth = 8;
  const outer = 9.2;
  const inner = 7.2;
  const pts: string[] = [];
  for (let i = 0; i < teeth; i++) {
    const a = (i / teeth) * Math.PI * 2;
    const step = (Math.PI * 2) / teeth;
    const angles = [a - step * 0.36, a - step * 0.18, a + step * 0.18, a + step * 0.36];
    const radii = [inner, outer, outer, inner];
    angles.forEach((ang, k) => {
      const x = (cx + Math.cos(ang) * radii[k]).toFixed(2);
      const y = (cy + Math.sin(ang) * radii[k]).toFixed(2);
      pts.push(`${pts.length === 0 ? 'M' : 'L'}${x} ${y}`);
    });
  }
  return `${pts.join(' ')} Z`;
}

export const icons = {
  search: line('<circle cx="10.5" cy="10.5" r="6"/><path d="M15 15l5 5"/>'),
  taskView: line('<rect x="3.5" y="6" width="11" height="12" rx="2"/><path d="M17.5 8h1a2 2 0 0 1 2 2v4a2 2 0 0 1-2 2h-1"/>'),
  chevronUp: line('<path d="M6 15l6-6 6 6"/>'),
  chevronDown: line('<path d="M6 9l6 6 6-6"/>'),
  chevronRight: line('<path d="M9 6l6 6-6 6"/>'),
  chevronLeft: line('<path d="M15 6l-6 6 6 6"/>'),
  wifi: line('<path d="M2.5 9.5a14 14 0 0 1 19 0"/><path d="M5.5 12.8a9.5 9.5 0 0 1 13 0"/><path d="M8.6 16a5 5 0 0 1 6.8 0"/><circle cx="12" cy="19" r="1.1" fill="currentColor" stroke="none"/>'),
  volume: line('<path d="M4 9.5h3l4.5-4v13L7 14.5H4z"/><path d="M15.5 9a4.5 4.5 0 0 1 0 6"/><path d="M18 6.5a8 8 0 0 1 0 11"/>'),
  battery: line('<rect x="2.5" y="7.5" width="17" height="9" rx="2"/><path d="M21.5 10.5v3"/><rect x="4.5" y="9.5" width="10.5" height="5" rx="1" fill="currentColor" stroke="none"/>'),
  play: line('<path d="M8 5.8v12.4a.8.8 0 0 0 1.2.7l9.8-6.2a.8.8 0 0 0 0-1.4L9.2 5.1A.8.8 0 0 0 8 5.8z" fill="currentColor" stroke="none"/>'),
  pause: line('<rect x="6.5" y="5" width="3.6" height="14" rx="1.2" fill="currentColor" stroke="none"/><rect x="13.9" y="5" width="3.6" height="14" rx="1.2" fill="currentColor" stroke="none"/>'),
  next: line('<path d="M5.5 6.6v10.8a.7.7 0 0 0 1.1.6l8-5.4a.7.7 0 0 0 0-1.2l-8-5.4a.7.7 0 0 0-1.1.6z" fill="currentColor" stroke="none"/><path d="M18.5 6v12"/>'),
  prev: line('<path d="M18.5 6.6v10.8a.7.7 0 0 1-1.1.6l-8-5.4a.7.7 0 0 1 0-1.2l8-5.4a.7.7 0 0 1 1.1.6z" fill="currentColor" stroke="none"/><path d="M5.5 6v12"/>'),
  plus: line('<path d="M12 5v14M5 12h14"/>'),
  minus: line('<path d="M5 12h14"/>'),
  close: line('<path d="M6.5 6.5l11 11M17.5 6.5l-11 11"/>'),
  check: line('<path d="M5 12.5l4.5 4.5L19 7.5"/>'),
  download: line('<path d="M12 4v11"/><path d="M7 10.5l5 5 5-5"/><path d="M5 19.5h14"/>'),
  arrowDown: line('<path d="M12 5v14M6.5 13.5L12 19l5.5-5.5"/>'),
  arrowUp: line('<path d="M12 19V5M6.5 10.5L12 5l5.5 5.5"/>'),
  arrowRight: line('<path d="M5 12h14M13 6l6 6-6 6"/>'),
  external: line('<path d="M14 5h5v5"/><path d="M19 5l-8 8"/><path d="M17 13.5V18a1.5 1.5 0 0 1-1.5 1.5h-9A1.5 1.5 0 0 1 5 18V9a1.5 1.5 0 0 1 1.5-1.5H11"/>'),
  drop: line('<path d="M12 3.5s-6 6.5-6 10.5a6 6 0 0 0 12 0c0-4-6-10.5-6-10.5z"/>'),
  power: line('<path d="M12 3.5v8"/><path d="M7 6.5a7.5 7.5 0 1 0 10 0"/>'),
  settings: line(`<path d="${gearPath()}"/><circle cx="12" cy="12" r="3"/>`),
  shield: line('<path d="M12 3l7 2.6v5.6c0 4.4-2.9 8-7 9.6-4.1-1.6-7-5.2-7-9.6V5.6z"/><path d="M9 12l2.2 2.2L15.5 10"/>'),
  pin: line('<path d="M9.5 3.5h5l-.7 5.4 3.2 3.1v1.6H7.1V12l3.1-3.1z"/><path d="M12 13.6v6.9"/>'),
  grip: line('<g fill="currentColor" stroke="none"><circle cx="9" cy="6" r="1.4"/><circle cx="15" cy="6" r="1.4"/><circle cx="9" cy="12" r="1.4"/><circle cx="15" cy="12" r="1.4"/><circle cx="9" cy="18" r="1.4"/><circle cx="15" cy="18" r="1.4"/></g>'),
  monitor: line('<rect x="3" y="4" width="18" height="12.5" rx="2"/><path d="M9 20h6M12 16.5V20"/>'),
  autohide: line('<rect x="3" y="4" width="18" height="16" rx="2"/><path d="M7 16.5h10" stroke-dasharray="1.5 2.5"/><path d="M12 8v5M9.5 10.5L12 13l2.5-2.5"/>'),
  restore: line('<path d="M4.5 12a7.5 7.5 0 1 0 2.2-5.3"/><path d="M4.5 4.5v4h4"/>'),
  copy: line('<rect x="8.5" y="8.5" width="11" height="11" rx="2"/><path d="M15.5 8.5V6.5a2 2 0 0 0-2-2h-7a2 2 0 0 0-2 2v7a2 2 0 0 0 2 2h2"/>'),
  more: line('<g fill="currentColor" stroke="none"><circle cx="6" cy="12" r="1.5"/><circle cx="12" cy="12" r="1.5"/><circle cx="18" cy="12" r="1.5"/></g>'),
  bell: line('<path d="M6.5 16.5V11a5.5 5.5 0 0 1 11 0v5.5l1.5 2H5z"/><path d="M10 20.5a2 2 0 0 0 4 0"/>'),
  widgets: line('<rect x="3.5" y="3.5" width="7" height="7" rx="2"/><rect x="13.5" y="3.5" width="7" height="7" rx="3.5"/><rect x="3.5" y="13.5" width="7" height="7" rx="2"/><path d="M17 14v6M14 17h6"/>'),
  sun: line('<circle cx="12" cy="12" r="4"/><path d="M12 2.5v2M12 19.5v2M2.5 12h2M19.5 12h2M5.3 5.3l1.4 1.4M17.3 17.3l1.4 1.4M5.3 18.7l1.4-1.4M17.3 6.7l1.4-1.4"/>'),
  moon: line('<path d="M19.5 14.5A8 8 0 0 1 9.5 4.5a8 8 0 1 0 10 10z"/>'),
  user: line('<circle cx="12" cy="8.5" r="3.6"/><path d="M5 19.5a7 7 0 0 1 14 0"/>'),
  terminal: line('<rect x="3" y="4.5" width="18" height="15" rx="2.5"/><path d="M7 10l3 2.5L7 15M12.5 15.5H17"/>'),
  sparkle: line('<path d="M12 3.5l1.9 5.6 5.6 1.9-5.6 1.9L12 18.5l-1.9-5.6L4.5 11l5.6-1.9z"/>'),
  heart: line('<path d="M12 19.5s-7.5-4.4-7.5-9.7A4.3 4.3 0 0 1 12 7.2a4.3 4.3 0 0 1 7.5 2.6c0 5.3-7.5 9.7-7.5 9.7z"/>'),
  github:
    '<svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M12 .3a12 12 0 0 0-3.8 23.4c.6.1.8-.3.8-.6v-2c-3.3.7-4-1.6-4-1.6-.6-1.4-1.4-1.8-1.4-1.8-1-.7.1-.7.1-.7 1.2.1 1.8 1.2 1.8 1.2 1 1.8 2.8 1.3 3.5 1 0-.8.4-1.3.7-1.6-2.7-.3-5.5-1.3-5.5-6 0-1.2.5-2.3 1.3-3.1-.2-.4-.6-1.6 0-3.2 0 0 1-.3 3.4 1.2a11.5 11.5 0 0 1 6 0c2.3-1.5 3.3-1.2 3.3-1.2.6 1.6.2 2.8 0 3.2.9.8 1.3 1.9 1.3 3.2 0 4.6-2.8 5.6-5.5 5.9.5.4.9 1.1.9 2.2v3.3c0 .3.1.7.8.6A12 12 0 0 0 12 .3"/></svg>',
  windows:
    '<svg viewBox="0 0 24 24" aria-hidden="true"><path fill="currentColor" d="M3 3h8.5v8.5H3zM12.5 3H21v8.5h-8.5zM3 12.5h8.5V21H3zM12.5 12.5H21V21h-8.5z"/></svg>',
} as const;

export type IconName = keyof typeof icons;

export const windowsLogo = (id = 'wl') => `<svg viewBox="0 0 24 24" aria-hidden="true">
  <defs><linearGradient id="${id}" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#4CC2FF"/><stop offset="1" stop-color="#0067C0"/></linearGradient></defs>
  <g fill="url(#${id})"><rect x="2" y="2" width="9.5" height="9.5" rx=".6"/><rect x="12.5" y="2" width="9.5" height="9.5" rx=".6"/><rect x="2" y="12.5" width="9.5" height="9.5" rx=".6"/><rect x="12.5" y="12.5" width="9.5" height="9.5" rx=".6"/></g>
</svg>`;

export const dockLogo = (id = 'dl') => `<svg viewBox="0 0 64 64" aria-hidden="true">
  <defs><linearGradient id="${id}" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#3A3A42"/><stop offset="1" stop-color="#121216"/></linearGradient></defs>
  <rect x="2.5" y="2.5" width="59" height="59" rx="14" fill="url(#${id})"/>
  <rect x="2.5" y="2.5" width="59" height="59" rx="14" fill="none" stroke="#fff" stroke-opacity=".1"/>
  <rect x="9" y="33.3" width="46" height="21.8" rx="6.5" fill="#fff" fill-opacity=".27"/>
  <rect x="12.9" y="37.2" width="10.1" height="13.9" rx="3.4" fill="#34C759"/>
  <rect x="26.9" y="37.2" width="10.1" height="13.9" rx="3.4" fill="#FF9F0A"/>
  <rect x="41" y="37.2" width="10.1" height="13.9" rx="3.4" fill="#0A84FF"/>
  <path d="M15.4 16.6H35.8" stroke="#fff" stroke-opacity=".9" stroke-width="4.5" stroke-linecap="round"/>
</svg>`;

export const deviceGlyphs = {
  laptop: 'M3,5 H13 V11 H3 Z M1,13 H15',
  battery: 'M2,5 H13 V11 H2 Z M14,7 V9',
  disk: 'M3,4 C3,2 13,2 13,4 V12 C13,14 3,14 3,12 Z M3,4 C3,6 13,6 13,4',
  memory: 'M2,5 H14 V10 H2 Z M4,10 V12 M7,10 V12 M10,10 V12 M13,10 V12 M5,7 H6 M8,7 H9 M11,7 H12',
  cpu: 'M4,4 H12 V12 H4 Z M6.5,6.5 H9.5 V9.5 H6.5 Z M6,2 V4 M10,2 V4 M6,12 V14 M10,12 V14 M2,6 H4 M2,10 H4 M12,6 H14 M12,10 H14',
} as const;

export const deviceGlyph = (kind: keyof typeof deviceGlyphs) =>
  `<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.3" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="${deviceGlyphs[kind]}"/></svg>`;
