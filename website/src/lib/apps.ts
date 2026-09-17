function gear(cx: number, cy: number, outer: number, inner: number, teeth: number): string {
  const step = (Math.PI * 2) / teeth;
  const pts: string[] = [];
  for (let i = 0; i < teeth; i++) {
    const a = i * step - Math.PI / 2;
    const angles = [a - step * 0.34, a - step * 0.17, a + step * 0.17, a + step * 0.34];
    const radii = [inner, outer, outer, inner];
    angles.forEach((ang, k) => {
      pts.push(`${pts.length ? 'L' : 'M'}${(cx + Math.cos(ang) * radii[k]).toFixed(2)} ${(cy + Math.sin(ang) * radii[k]).toFixed(2)}`);
    });
  }
  return `${pts.join(' ')} Z`;
}

const svg = (body: string) => `<svg viewBox="0 0 48 48" aria-hidden="true">${body}</svg>`;

/** Gradyanlar sayfadaki tek bir <svg> tanım bloğunda durur (AppDefs.astro); ikonlar id ile başvurur. */
export const appDefs = `
<linearGradient id="g-ex-back" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#F5B82E"/><stop offset="1" stop-color="#D9920F"/></linearGradient>
<linearGradient id="g-ex-front" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#FFE48F"/><stop offset="1" stop-color="#FFC73D"/></linearGradient>
<linearGradient id="g-br" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#3FD0F5"/><stop offset=".55" stop-color="#1F8FE8"/><stop offset="1" stop-color="#1450C4"/></linearGradient>
<radialGradient id="g-br-hi" cx=".32" cy=".26" r=".7"><stop offset="0" stop-color="#fff" stop-opacity=".45"/><stop offset="1" stop-color="#fff" stop-opacity="0"/></radialGradient>
<linearGradient id="g-tm" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#2C2C33"/><stop offset="1" stop-color="#101013"/></linearGradient>
<linearGradient id="g-np" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#FFFFFF"/><stop offset="1" stop-color="#DDE4EE"/></linearGradient>
<linearGradient id="g-ph" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#7ED8FF"/><stop offset="1" stop-color="#3C86EE"/></linearGradient>
<linearGradient id="g-mu" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#FF7BB9"/><stop offset="1" stop-color="#F0413A"/></linearGradient>
<linearGradient id="g-st" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#B9C6D8"/><stop offset="1" stop-color="#63748C"/></linearGradient>
<linearGradient id="g-ca" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#3B3F4A"/><stop offset="1" stop-color="#1D1F26"/></linearGradient>
<linearGradient id="g-bin" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#E9F1FB"/><stop offset="1" stop-color="#A9BCD6"/></linearGradient>
<clipPath id="c-ph"><rect x="5" y="8" width="38" height="32" rx="6.5"/></clipPath>
<linearGradient id="g-pc"x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#6FC4FF"/><stop offset="1" stop-color="#1E6FD9"/></linearGradient>
`;

export const appIcons = {
  explorer: svg(`
    <path d="M5 12.5A3.5 3.5 0 0 1 8.5 9h9.3a2 2 0 0 1 1.4.6L22.6 13H39.5A3.5 3.5 0 0 1 43 16.5v19A3.5 3.5 0 0 1 39.5 39h-31A3.5 3.5 0 0 1 5 35.5z" fill="url(#g-ex-back)"/>
    <path d="M5 20a3.5 3.5 0 0 1 3.5-3.5h31A3.5 3.5 0 0 1 43 20v15.5a3.5 3.5 0 0 1-3.5 3.5h-31A3.5 3.5 0 0 1 5 35.5z" fill="url(#g-ex-front)"/>
    <path d="M5 31h38v4.5a3.5 3.5 0 0 1-3.5 3.5h-31A3.5 3.5 0 0 1 5 35.5z" fill="#2F86E8"/>`),
  browser: svg(`
    <circle cx="24" cy="24" r="18.5" fill="url(#g-br)"/>
    <ellipse cx="24" cy="24" rx="7.5" ry="18.5" fill="none" stroke="#fff" stroke-opacity=".6" stroke-width="2"/>
    <path d="M5.5 24h37M8 15h32M8 33h32" stroke="#fff" stroke-opacity=".6" stroke-width="2"/>
    <circle cx="24" cy="24" r="18.5" fill="url(#g-br-hi)"/>`),
  terminal: svg(`
    <rect x="5" y="8" width="38" height="32" rx="6" fill="url(#g-tm)"/>
    <path d="M5 14a6 6 0 0 1 6-6h26a6 6 0 0 1 6 6v.5H5z" fill="#43434B"/>
    <path d="M12.5 21.5l6 5-6 5" stroke="#4CC2FF" stroke-width="2.8" fill="none" stroke-linecap="round" stroke-linejoin="round"/>
    <path d="M22 32h11" stroke="#fff" stroke-width="2.8" stroke-linecap="round"/>
    <rect x="5.5" y="8.5" width="37" height="31" rx="5.5" fill="none" stroke="#fff" stroke-opacity=".12"/>`),
  notepad: svg(`
    <rect x="9" y="6" width="30" height="36" rx="4.5" fill="url(#g-np)"/>
    <path d="M9 10.5A4.5 4.5 0 0 1 13.5 6h21A4.5 4.5 0 0 1 39 10.5V15H9z" fill="#2F7FE0"/>
    <path d="M15 22h18M15 27.5h18M15 33h11" stroke="#7F8BA0" stroke-width="2.2" stroke-linecap="round"/>`),
  photos: svg(`
    <rect x="5" y="8" width="38" height="32" rx="6.5" fill="url(#g-ph)"/>
    <circle cx="32.5" cy="17" r="4" fill="#FFE17A"/>
    <g clip-path="url(#c-ph)">
      <path d="M3 32l12.5-11.5 8.5 8.5 5-4.5L45 37v5H3z" fill="#1D56B8"/>
      <path d="M3 36l11.5-8 9 6.5 6-4L45 38v4H3z" fill="#35B07A"/>
    </g>`),
  music: svg(`
    <rect x="6" y="6" width="36" height="36" rx="10" fill="url(#g-mu)"/>
    <path d="M20 31.5V15.5l12.5-2.6v15.2" stroke="#fff" stroke-width="2.6" fill="none" stroke-linejoin="round"/>
    <circle cx="16.8" cy="31.5" r="3.6" fill="#fff"/><circle cx="29.3" cy="28.1" r="3.6" fill="#fff"/>`),
  settings: svg(`
    <path fill-rule="evenodd" fill="url(#g-st)" d="${gear(24, 24, 19, 15, 9)} M24 17.5a6.5 6.5 0 1 0 0 13 6.5 6.5 0 1 0 0-13z"/>
    <circle cx="24" cy="24" r="10.8" fill="none" stroke="#fff" stroke-opacity=".28" stroke-width="1.4"/>`),
  calculator: svg(`
    <rect x="9" y="5" width="30" height="38" rx="5" fill="url(#g-ca)"/>
    <rect x="13" y="9.5" width="22" height="8" rx="2" fill="#9FE3C1"/>
    <g fill="#5A5F6C"><rect x="13" y="21.5" width="6" height="5" rx="1.4"/><rect x="21" y="21.5" width="6" height="5" rx="1.4"/><rect x="13" y="28.5" width="6" height="5" rx="1.4"/><rect x="21" y="28.5" width="6" height="5" rx="1.4"/><rect x="13" y="35.5" width="14" height="4" rx="1.4"/></g>
    <rect x="29" y="21.5" width="6" height="18" rx="1.6" fill="#FF9F0A"/>`),
  bin: svg(`
    <path d="M13 15h22l-2.4 24.2A3 3 0 0 1 29.6 42H18.4a3 3 0 0 1-3-2.8z" fill="url(#g-bin)"/>
    <rect x="10.5" y="10" width="27" height="5.5" rx="2.2" fill="#DCE7F5"/>
    <path d="M20 21v15M24 21v15M28 21v15" stroke="#7F95B4" stroke-width="1.8" stroke-linecap="round"/>`),
  pc: svg(`
    <rect x="5" y="8" width="38" height="26" rx="3.5" fill="url(#g-pc)"/>
    <rect x="8" y="11" width="32" height="20" rx="1.5" fill="#0D2F66" fill-opacity=".35"/>
    <path d="M18 40h12M24 34v6" stroke="#B8C6D9" stroke-width="3" stroke-linecap="round"/>`),
} as const;

export type AppId = keyof typeof appIcons;

export interface AppDef {
  id: AppId;
  name: string;
}

export const dockApps: AppDef[] = [
  { id: 'explorer', name: 'Dosya Gezgini' },
  { id: 'browser', name: 'Tarayıcı' },
  { id: 'terminal', name: 'Terminal' },
  { id: 'notepad', name: 'Not Defteri' },
  { id: 'music', name: 'Medya Oynatıcı' },
];

export const appNames: Record<AppId, string> = {
  explorer: 'Dosya Gezgini',
  browser: 'Tarayıcı',
  terminal: 'Terminal',
  notepad: 'Not Defteri',
  photos: 'Fotoğraflar',
  music: 'Medya Oynatıcı',
  settings: 'Ayarlar',
  calculator: 'Hesap Makinesi',
  bin: 'Geri Dönüşüm Kutusu',
  pc: 'Bu bilgisayar',
};
