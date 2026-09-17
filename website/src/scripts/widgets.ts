import { icons, deviceGlyph } from '../lib/icons';
import {
  sim,
  tracks,
  onFrame,
  hhmm,
  mmss,
  pad,
  fmt1,
  TR_DAYS,
  TR_MONTHS,
  TR_MONTHS_LONG,
  type WeatherCode,
} from './sim';

/* ------------------------------------------------------------------ DOM */

type Kid = Node | string | number | null | undefined | false;

export function h<K extends keyof HTMLElementTagNameMap>(
  tag: K,
  props: Record<string, any> = {},
  ...kids: Kid[]
): HTMLElementTagNameMap[K] {
  const el = document.createElement(tag);
  for (const [k, v] of Object.entries(props)) {
    if (v == null || v === false) continue;
    if (k === 'class') el.className = v;
    else if (k === 'html') el.innerHTML = v;
    else if (k === 'text') el.textContent = v;
    else if (k === 'style') el.setAttribute('style', v);
    else if (k.startsWith('on') && typeof v === 'function') el.addEventListener(k.slice(2).toLowerCase(), v);
    else el.setAttribute(k, v === true ? '' : String(v));
  }
  for (const kid of kids) {
    if (kid == null || kid === false) continue;
    el.append(kid instanceof Node ? kid : document.createTextNode(String(kid)));
  }
  return el;
}

/* ---------------------------------------------------------------- parts */

export function ring(size: number, sw: number, color: string, track: string) {
  const r = (size - sw) / 2;
  const el = h('div', { class: 'ring', style: `--size:${size}px` });
  el.innerHTML = `<svg viewBox="0 0 ${size} ${size}" aria-hidden="true">
    <circle cx="${size / 2}" cy="${size / 2}" r="${r}" fill="none" stroke-width="${sw}" style="stroke:${track}"/>
    <circle class="ring-fill" cx="${size / 2}" cy="${size / 2}" r="${r}" fill="none" stroke-width="${sw}" pathLength="100" stroke-dasharray="100 100" stroke-dashoffset="100" stroke-linecap="round" style="stroke:${color}"/>
  </svg>`;
  const inner = h('span', { class: 'ring-inner' });
  el.append(inner);
  const fill = el.querySelector('.ring-fill') as SVGCircleElement;
  let lastV = -1;
  return {
    el,
    inner,
    set(v: number) {
      const c = Math.max(0, Math.min(100, v));
      if (Math.abs(c - lastV) < 0.05) return;
      lastV = c;
      fill.setAttribute('stroke-dashoffset', String(100 - c));
      fill.style.opacity = c < 0.3 ? '0' : '1';
    },
    color(c: string) {
      fill.style.stroke = c;
    },
  };
}

export function analog(size: number) {
  const el = h('div', { class: 'analog', style: `--size:${size}px` });
  const ticks = Array.from({ length: 12 }, (_, i) => {
    const long = i % 3 === 0;
    return `<line x1="50" y1="${long ? 7 : 8}" x2="50" y2="${long ? 15 : 12}" stroke-width="${long ? 3.4 : 2}" transform="rotate(${i * 30} 50 50)"/>`;
  }).join('');
  el.innerHTML = `<svg viewBox="0 0 100 100" aria-hidden="true">
    <circle class="an-face" cx="50" cy="50" r="48"/>
    <g class="an-ticks" stroke-linecap="round">${ticks}</g>
    <line class="an-h" x1="50" y1="50" x2="50" y2="27" stroke-width="6" stroke-linecap="round"/>
    <line class="an-m" x1="50" y1="50" x2="50" y2="15" stroke-width="4.2" stroke-linecap="round"/>
    <line class="an-s" x1="50" y1="58" x2="50" y2="12" stroke-width="1.8" stroke-linecap="round"/>
    <circle class="an-pin" cx="50" cy="50" r="3.6"/>
  </svg>`;
  const hH = el.querySelector('.an-h')!;
  const hM = el.querySelector('.an-m')!;
  const hS = el.querySelector('.an-s')!;
  return {
    el,
    set(d: Date, offsetH = 0) {
      const hours = (d.getHours() + offsetH + 24) % 24;
      const m = d.getMinutes();
      const s = d.getSeconds();
      el.classList.toggle('is-night', hours < 7 || hours >= 19);
      hH.setAttribute('transform', `rotate(${(hours % 12) * 30 + m * 0.5} 50 50)`);
      hM.setAttribute('transform', `rotate(${m * 6 + s * 0.1} 50 50)`);
      hS.setAttribute('transform', `rotate(${s * 6} 50 50)`);
    },
  };
}

export function weatherIcon(code: WeatherCode, size = 28) {
  const sun = (cx: number, cy: number, r: number) =>
    `<g><circle cx="${cx}" cy="${cy}" r="${r}" fill="#FFD60A"/>${Array.from({ length: 8 }, (_, i) => {
      const a = (i * Math.PI) / 4;
      return `<line x1="${cx + Math.cos(a) * (r + 2.4)}" y1="${cy + Math.sin(a) * (r + 2.4)}" x2="${cx + Math.cos(a) * (r + 4.6)}" y2="${cy + Math.sin(a) * (r + 4.6)}" stroke="#FFD60A" stroke-width="1.8" stroke-linecap="round"/>`;
    }).join('')}</g>`;
  const cloud = (dx: number, dy: number, s: number, fill = 'var(--cloud, #D9DCE3)') =>
    `<path transform="translate(${dx} ${dy}) scale(${s})" d="M17.5 19H8a5 5 0 1 1 1.6-9.7A6 6 0 0 1 20.8 11.6 3.8 3.8 0 0 1 17.5 19Z" fill="${fill}"/>`;
  let body = '';
  switch (code) {
    case 'sun':
      body = sun(16, 16, 6.5);
      break;
    case 'moon':
      body = `<path d="M22 19.5A9 9 0 0 1 12.5 8a9 9 0 1 0 9.5 11.5Z" fill="#F2E7B6"/>`;
      break;
    case 'partly':
      body = `${sun(12, 11, 5)}${cloud(5, 5, 1.05)}`;
      break;
    case 'cloud':
      body = `${cloud(1, 3, 1.25, '#AEB5C2')}${cloud(5, 6, 1.05)}`;
      break;
    case 'rain':
      body = `${cloud(2, 0, 1.2)}<g stroke="#5AC8FA" stroke-width="2" stroke-linecap="round"><line x1="11" y1="25" x2="9.5" y2="29"/><line x1="16.5" y1="25" x2="15" y2="29"/><line x1="22" y1="25" x2="20.5" y2="29"/></g>`;
      break;
  }
  return `<svg class="wx" viewBox="0 0 32 32" width="${size}" height="${size}" aria-hidden="true">${body}</svg>`;
}

export function albumArt(index: number) {
  const [a, b, c] = tracks[index].art;
  const id = `art${index}`;
  return `<svg viewBox="0 0 60 60" aria-hidden="true" preserveAspectRatio="xMidYMid slice">
    <defs><linearGradient id="${id}" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="${a}"/><stop offset="1" stop-color="${c}"/></linearGradient></defs>
    <rect width="60" height="60" fill="url(#${id})"/>
    <circle cx="${18 + index * 9}" cy="${22 + index * 4}" r="${20 - index * 2}" fill="${b}" opacity=".85"/>
    <circle cx="${44 - index * 6}" cy="${40 - index * 3}" r="${12 + index * 2}" fill="${a}" opacity=".55"/>
    <path d="M0 ${46 - index * 4} Q30 ${34 + index * 3} 60 ${44 - index * 2} V60 H0Z" fill="${c}" opacity=".6"/>
  </svg>`;
}

function sparkline(values: number[], w: number, hgt: number, max?: number) {
  const m = max ?? Math.max(...values) * 1.15;
  const step = w / (values.length - 1);
  const pts = values.map((v, i) => `${(i * step).toFixed(1)},${(hgt - (v / m) * hgt).toFixed(1)}`);
  return { line: `M${pts.join(' L')}`, area: `M0,${hgt} L${pts.join(' L')} L${w},${hgt} Z` };
}

function tickBar(count: number) {
  const el = h('div', { class: 'tickbar' });
  const ticks = Array.from({ length: count }, () => h('i'));
  el.append(...ticks);
  return {
    el,
    set(pct: number) {
      const on = Math.round((pct / 100) * count);
      ticks.forEach((t, i) => t.classList.toggle('on', i < on));
    },
  };
}

/* ------------------------------------------------------------ registry */

export type Category = 'Saatler' | 'Hatırlatıcılar' | 'Notlar' | 'Medya' | 'Sistem' | 'Hava durumu';

export interface Instance {
  uid: string;
  id: string;
  variant: string;
  state: Record<string, any>;
}

export interface View {
  el: HTMLElement;
  tick?(now: Date): void;
  frame?(dt: number): void;
  refresh?(): void;
}

export interface Env {
  refreshAll(): void;
  toast?(title: string, body: string): void;
}

export interface WidgetDef {
  id: string;
  name: string;
  category: Category;
  description: string;
  icon: string;
  accent: string;
  variants: { id: string; name: string }[];
  init?(): Record<string, any>;
  card(i: Instance, env: Env): View;
  compact(i: Instance, env: Env): View;
  panel(i: Instance, env: Env): View;
  primary?(i: Instance, env: Env): void;
}

const card = (cls: string, ...kids: Kid[]) => h('div', { class: `wc ${cls}` }, ...kids);
const compactTile = (...kids: Kid[]) => h('div', { class: 'wt' }, ...kids);
const glyph = (path: string, color: string) =>
  h('span', {
    class: 'wt-glyph',
    style: `color:${color}`,
    html: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="${path}"/></svg>`,
  });
const panelShell = (title: string, ...kids: Kid[]) => h('div', { class: 'wp' }, h('div', { class: 'wp-title', text: title }), ...kids);
const monthGrid = (d: Date) => {
  const first = new Date(d.getFullYear(), d.getMonth(), 1);
  const lead = (first.getDay() + 6) % 7;
  const days = new Date(d.getFullYear(), d.getMonth() + 1, 0).getDate();
  const grid = h('div', { class: 'cal' }, ...['Pt', 'Sa', 'Ça', 'Pe', 'Cu', 'Ct', 'Pz'].map((x) => h('b', { text: x })));
  for (let i = 0; i < lead; i++) grid.append(h('span'));
  for (let day = 1; day <= days; day++) grid.append(h('span', { class: day === d.getDate() ? 'today' : '', text: day }));
  return grid;
};

const ACCENT = {
  green: 'var(--c-green)',
  orange: 'var(--c-orange)',
  blue: 'var(--c-blue)',
  cyan: 'var(--c-cyan)',
  magenta: 'var(--c-magenta)',
  pink: 'var(--c-pink)',
  purple: 'var(--c-purple)',
  yellow: 'var(--c-yellow)',
  red: 'var(--c-red)',
};
const TRACK = {
  green: 'var(--t-green)',
  orange: 'var(--t-orange)',
  blue: 'var(--t-blue)',
  magenta: 'var(--t-magenta)',
  neutral: 'var(--w-track)',
};

const ICON = {
  clock: 'M12,3 A9,9 0 1 1 11.99,3 Z M12,7 V12 L15,14',
  world: 'M12,3 A9,9 0 1 1 11.99,3 Z M3,12 H21 M12,3 C15,6 15,18 12,21 C9,18 9,6 12,3 Z',
  stopwatch: 'M12,5 A8,8 0 1 1 11.99,5 Z M12,9 V13 M10,2 H14 M19,6 L20.5,4.5',
  focus: 'M12,3 A9,9 0 1 1 11.99,3 Z M12,6 A6,6 0 0 1 18,12',
  countdown: 'M12,5 A8,8 0 1 1 11.99,5 Z M12,13 L15,10 M10,2 H14',
  alarm: 'M12,6 A7,7 0 1 1 11.99,6 Z M12,9 V13 L14,14 M4,5 L7,2.5 M20,5 L17,2.5',
  progress: 'M3,8 H21 V16 H3 Z M6,8 V16 M9,8 V16 M12,8 V16',
  drop: 'M12,3 C12,3 5.5,10 5.5,14.5 A6.5,6.5 0 0 0 18.5,14.5 C18.5,10 12,3 12,3 Z',
  list: 'M8,6 H20 M8,12 H20 M8,18 H20 M4,6 H4.5 M4,12 H4.5 M4,18 H4.5',
  note: 'M5,4 H19 V14 L14,20 H5 Z M14,20 V14 H19',
  music: 'M9,17 V5 L20,3 V15 M9,17 A2.5,2.5 0 1 1 4,17 A2.5,2.5 0 1 1 9,17 Z M20,15 A2.5,2.5 0 1 1 15,15 A2.5,2.5 0 1 1 20,15 Z',
  pulse: 'M3,12 H7 L10,4 L14,20 L17,12 H21',
  network: 'M8,4 V20 M4,16 L8,20 L12,16 M16,20 V4 M12,8 L16,4 L20,8',
  status: 'M12,4 A8,8 0 1 1 11.99,4 Z M12,8 A4,4 0 1 1 11.99,8 Z',
  weather: 'M17.5,19 H8 A5,5 0 1 1 9.6,9.3 A6,6 0 0 1 20.8,11.6 A3.8,3.8 0 0 1 17.5,19 Z',
};

const cities = [
  { name: 'İstanbul', short: 'İST', off: 0 },
  { name: 'Tokyo', short: 'TYO', off: 6 },
  { name: 'New York', short: 'NYC', off: -7 },
];

const noteColors: Record<string, string> = {
  yellow: '#F3CD5B',
  orange: '#F5A55B',
  red: '#F2848F',
  purple: '#D59BEA',
  blue: '#86BBF2',
  green: '#A3D48A',
};
const noteColorNames: Record<string, string> = {
  yellow: 'Sarı',
  orange: 'Turuncu',
  red: 'Kırmızı',
  purple: 'Mor',
  blue: 'Mavi',
  green: 'Yeşil',
};

function timerToggle(st: Record<string, any>) {
  st.running = !st.running;
}

function mediaControls(env: Env, size: 'sm' | 'lg') {
  const m = sim.media;
  const btn = (name: 'prev' | 'play' | 'next', label: string, fn: () => void) =>
    h('button', {
      class: `mc-btn mc-${name}`,
      'aria-label': label,
      html: icons[name],
      onclick: (e: Event) => {
        e.stopPropagation();
        fn();
        env.refreshAll();
      },
    });
  const play = btn('play', 'Oynat', () => (m.playing = !m.playing));
  const el = h(
    'div',
    { class: `mc mc-${size}` },
    btn('prev', 'Önceki', () => {
      if (m.pos > 3) m.pos = 0;
      else {
        m.index = (m.index + tracks.length - 1) % tracks.length;
        m.pos = 0;
      }
    }),
    play,
    btn('next', 'Sonraki', () => {
      m.index = (m.index + 1) % tracks.length;
      m.pos = 0;
    }),
  );
  return {
    el,
    sync() {
      play.innerHTML = m.playing ? icons.pause : icons.play;
      play.setAttribute('aria-label', m.playing ? 'Duraklat' : 'Oynat');
    },
  };
}

export const widgets: WidgetDef[] = [
  /* ----------------------------------------------------------- Saat */
  {
    id: 'clock',
    name: 'Saat',
    category: 'Saatler',
    description: 'Analog, dijital ya da takvim görünümü. Takvim sıradaki anımsatıcıyı da gösterir.',
    icon: ICON.clock,
    accent: ACCENT.orange,
    variants: [
      { id: 'analog', name: 'Analog' },
      { id: 'digital', name: 'Dijital' },
      { id: 'calendar', name: 'Takvim' },
    ],
    card(i) {
      if (i.variant === 'analog') {
        const a = analog(40);
        return { el: card('wc-square', a.el), tick: (d) => a.set(d) };
      }
      if (i.variant === 'digital') {
        const t = h('div', { class: 'wc-big num' });
        const s = h('div', { class: 'wc-sub' });
        return {
          el: card('wc-stack wc-digital', t, s),
          tick: (d) => {
            t.textContent = hhmm(d);
            s.textContent = `${TR_DAYS[d.getDay()]}, ${d.getDate()} ${TR_MONTHS[d.getMonth()]}`;
          },
        };
      }
      const dow = h('div', { class: 'cal-dow' });
      const day = h('div', { class: 'cal-day num' });
      const next = sim.reminders.find((r) => !r.done);
      return {
        el: card(
          'wc-calendar',
          h('div', { class: 'cal-date' }, dow, day),
          h('div', { class: 'wc-stack' }, h('div', { class: 'wc-title', text: next?.title ?? 'Boş gün' }), h('div', { class: 'wc-sub', text: next?.time ?? 'Anımsatıcı yok' })),
        ),
        tick: (d) => {
          dow.textContent = TR_DAYS[d.getDay()].toLocaleUpperCase('tr-TR');
          day.textContent = String(d.getDate());
        },
      };
    },
    compact(i) {
      if (i.variant === 'analog') {
        const a = analog(26);
        return { el: compactTile(a.el), tick: (d) => a.set(d) };
      }
      const t = h('div', { class: 'wt-label num' });
      return {
        el: compactTile(t),
        tick: (d) => {
          t.innerHTML = i.variant === 'calendar' ? `<small>${TR_DAYS[d.getDay()]}</small>${d.getDate()}` : `${pad(d.getHours())}<br>${pad(d.getMinutes())}`;
        },
      };
    },
    panel() {
      const a = analog(120);
      const date = h('div', { class: 'wp-hero-sub' });
      const time = h('div', { class: 'wp-hero num' });
      const grid = h('div');
      let lastDay = -1;
      return {
        el: panelShell('Saat', h('div', { class: 'wp-row wp-clock' }, a.el, h('div', {}, time, date)), grid),
        tick: (d) => {
          a.set(d);
          time.textContent = `${hhmm(d)}:${pad(d.getSeconds())}`;
          date.textContent = d.toLocaleDateString('tr-TR', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });
          if (d.getDate() !== lastDay) {
            lastDay = d.getDate();
            grid.replaceChildren(h('div', { class: 'wp-label', text: `${TR_MONTHS_LONG[d.getMonth()]} ${d.getFullYear()}` }), monthGrid(d));
          }
        },
      };
    },
  },
  /* ----------------------------------------------------- Dünya saati */
  {
    id: 'world-clock',
    name: 'Dünya saati',
    category: 'Saatler',
    description: 'Bir ya da birkaç şehrin saati, gündüz ve gece kadranıyla.',
    icon: ICON.world,
    accent: ACCENT.blue,
    variants: [
      { id: 'single', name: 'Tek şehir' },
      { id: 'multi', name: 'Çoklu şehir' },
    ],
    card(i) {
      const list = i.variant === 'single' ? [cities[1]] : cities;
      const cells = list.map((c) => {
        const a = analog(i.variant === 'single' ? 30 : 22);
        const t = h('div', { class: 'wc-title num' });
        return { c, a, t, el: h('div', { class: 'wc-city' }, a.el, h('div', { class: 'wc-stack' }, h('div', { class: 'wc-sub', text: i.variant === 'single' ? c.name : c.short }), t)) };
      });
      return {
        el: card('wc-world', ...cells.map((x) => x.el)),
        tick: (d) =>
          cells.forEach(({ c, a, t }) => {
            a.set(d, c.off);
            t.textContent = `${pad((d.getHours() + c.off + 24) % 24)}:${pad(d.getMinutes())}`;
          }),
      };
    },
    compact() {
      const t = h('div', { class: 'wt-text num' });
      return {
        el: compactTile(glyph(ICON.world, ACCENT.blue), t),
        tick: (d) => (t.textContent = `${pad((d.getHours() + 6) % 24)}:${pad(d.getMinutes())}`),
      };
    },
    panel() {
      const rows = cities.map((c) => {
        const a = analog(40);
        const t = h('div', { class: 'wp-num num' });
        return { c, a, t, el: h('div', { class: 'wp-list-row' }, a.el, h('div', { class: 'grow' }, h('div', { class: 'wp-strong', text: c.name }), h('div', { class: 'wp-muted', text: c.off === 0 ? 'Yerel saat' : `${c.off > 0 ? '+' : ''}${c.off} sa` })), t) };
      });
      return {
        el: panelShell('Dünya saati', h('div', { class: 'wp-list' }, ...rows.map((r) => r.el))),
        tick: (d) =>
          rows.forEach(({ c, a, t }) => {
            a.set(d, c.off);
            t.textContent = `${pad((d.getHours() + c.off + 24) % 24)}:${pad(d.getMinutes())}`;
          }),
      };
    },
  },
  /* ------------------------------------------------------ Kronometre */
  {
    id: 'stopwatch',
    name: 'Kronometre',
    category: 'Saatler',
    description: 'Tıkla başlat, tekrar tıkla duraklat. Sağ tık menüsünden sıfırla.',
    icon: ICON.stopwatch,
    accent: ACCENT.orange,
    variants: [{ id: 'default', name: 'Standart' }],
    init: () => ({ elapsed: 0, running: false }),
    primary(i, env) {
      timerToggle(i.state);
      env.refreshAll();
    },
    card(i) {
      const t = h('div', { class: 'wc-big num' });
      const dot = h('span', { class: 'live-dot' });
      const s = h('div', { class: 'wc-sub' }, dot, h('span'));
      const view: View = {
        el: card('wc-stack wc-stopwatch', t, s),
        frame: () => {
          const e = i.state.elapsed;
          t.textContent = `${pad(Math.floor(e / 60))}:${pad(Math.floor(e % 60))},${Math.floor((e * 10) % 10)}`;
        },
        refresh: () => {
          dot.classList.toggle('on', i.state.running);
          (s.lastChild as HTMLElement).textContent = i.state.running ? 'Çalışıyor' : i.state.elapsed > 0 ? 'Duraklatıldı' : 'Başlatmak için tıkla';
        },
      };
      return view;
    },
    compact(i) {
      const t = h('div', { class: 'wt-text num' });
      return {
        el: compactTile(glyph(ICON.stopwatch, ACCENT.orange), t),
        frame: () => (t.textContent = `${Math.floor(i.state.elapsed / 60)}:${pad(Math.floor(i.state.elapsed % 60))}`),
      };
    },
    panel(i, env) {
      const t = h('div', { class: 'wp-hero num' });
      const go = h('button', { class: 'wp-btn accent', onclick: () => { timerToggle(i.state); env.refreshAll(); } });
      const reset = h('button', { class: 'wp-btn', text: 'Sıfırla', onclick: () => { i.state.elapsed = 0; i.state.running = false; env.refreshAll(); } });
      return {
        el: panelShell('Kronometre', t, h('div', { class: 'wp-actions' }, go, reset)),
        frame: () => {
          const e = i.state.elapsed;
          t.textContent = `${pad(Math.floor(e / 60))}:${pad(Math.floor(e % 60))},${pad(Math.floor((e * 100) % 100))}`;
        },
        refresh: () => (go.textContent = i.state.running ? 'Duraklat' : 'Başlat'),
      };
    },
  },
  /* ------------------------------------------------ Odak zamanlayıcı */
  {
    id: 'focus',
    name: 'Odak zamanlayıcı',
    category: 'Saatler',
    description: 'Pomodoro: odak ve mola süreleri, bitince bildirim.',
    icon: ICON.focus,
    accent: ACCENT.orange,
    variants: [{ id: 'default', name: 'Standart' }],
    init: () => ({ total: 25 * 60, left: 25 * 60, running: false, phase: 'Odak' }),
    primary(i, env) {
      timerToggle(i.state);
      env.refreshAll();
    },
    card(i, env) {
      const r = ring(30, 3.2, ACCENT.orange, TRACK.orange);
      const t = h('div', { class: 'wc-title num' });
      const s = h('div', { class: 'wc-sub' });
      return {
        el: card('wc-focus', r.el, h('div', { class: 'wc-stack' }, t, s)),
        tick: () => {
          r.set((1 - i.state.left / i.state.total) * 100);
          t.textContent = mmss(i.state.left);
        },
        refresh: () => {
          s.textContent = i.state.running ? i.state.phase : `${i.state.phase} · başlat`;
          r.set((1 - i.state.left / i.state.total) * 100);
          t.textContent = mmss(i.state.left);
        },
      };
    },
    compact(i) {
      const r = ring(26, 2.6, ACCENT.orange, TRACK.orange);
      return {
        el: compactTile(r.el),
        tick: () => {
          r.set((1 - i.state.left / i.state.total) * 100);
          r.inner.textContent = String(Math.ceil(i.state.left / 60));
        },
      };
    },
    panel(i, env) {
      const r = ring(132, 8, ACCENT.orange, TRACK.orange);
      r.inner.classList.add('ring-inner-lg', 'num');
      const go = h('button', { class: 'wp-btn accent', onclick: () => { timerToggle(i.state); env.refreshAll(); } });
      const reset = h('button', {
        class: 'wp-btn',
        text: 'Sıfırla',
        onclick: () => {
          Object.assign(i.state, { left: i.state.total, running: false });
          env.refreshAll();
        },
      });
      const phase = h('div', { class: 'wp-muted center' });
      return {
        el: panelShell('Odak zamanlayıcı', h('div', { class: 'center' }, r.el), phase, h('div', { class: 'wp-actions' }, go, reset)),
        tick: () => {
          r.set((1 - i.state.left / i.state.total) * 100);
          r.inner.textContent = mmss(i.state.left);
        },
        refresh: () => {
          go.textContent = i.state.running ? 'Duraklat' : 'Başlat';
          phase.textContent = `${i.state.phase} · odak 25 dk, mola 5 dk`;
          r.set((1 - i.state.left / i.state.total) * 100);
          r.inner.textContent = mmss(i.state.left);
        },
      };
    },
  },
  /* ------------------------------------------------------ Geri sayım */
  {
    id: 'countdown',
    name: 'Geri sayım',
    category: 'Saatler',
    description: 'Hazır süreler ve etiket. Süre bitince bildirim gelir.',
    icon: ICON.countdown,
    accent: ACCENT.yellow,
    variants: [{ id: 'default', name: 'Standart' }],
    init: () => ({ total: 5 * 60, left: 5 * 60, running: false, label: 'Çay' }),
    primary(i, env) {
      if (i.state.left <= 0) i.state.left = i.state.total;
      timerToggle(i.state);
      env.refreshAll();
    },
    card(i, env) {
      const r = ring(30, 3.2, ACCENT.yellow, 'var(--t-yellow)');
      const t = h('div', { class: 'wc-title num' });
      const s = h('div', { class: 'wc-sub' });
      return {
        el: card('wc-focus', r.el, h('div', { class: 'wc-stack' }, t, s)),
        tick: () => {
          t.textContent = mmss(i.state.left);
          r.set((i.state.left / i.state.total) * 100);
        },
        refresh: () => {
          t.textContent = mmss(i.state.left);
          r.set((i.state.left / i.state.total) * 100);
          s.textContent = i.state.running ? i.state.label : `${i.state.label} · başlat`;
        },
      };
    },
    compact(i) {
      const r = ring(26, 2.6, ACCENT.yellow, 'var(--t-yellow)');
      return {
        el: compactTile(r.el),
        tick: () => {
          r.set((i.state.left / i.state.total) * 100);
          r.inner.textContent = String(Math.ceil(i.state.left / 60));
        },
      };
    },
    panel(i, env) {
      const t = h('div', { class: 'wp-hero num' });
      const go = h('button', { class: 'wp-btn accent', onclick: () => { if (i.state.left <= 0) i.state.left = i.state.total; timerToggle(i.state); env.refreshAll(); } });
      const presets = h(
        'div',
        { class: 'wp-chips' },
        ...[1, 3, 5, 10, 25].map((m) =>
          h('button', {
            class: 'wp-chip',
            text: `${m} dk`,
            onclick: () => {
              Object.assign(i.state, { total: m * 60, left: m * 60, running: true });
              env.refreshAll();
            },
          }),
        ),
      );
      return {
        el: panelShell('Geri sayım', t, h('div', { class: 'wp-label', text: 'Hazır süreler' }), presets, h('div', { class: 'wp-actions' }, go)),
        tick: () => (t.textContent = mmss(i.state.left)),
        refresh: () => {
          t.textContent = mmss(i.state.left);
          go.textContent = i.state.running ? 'Duraklat' : 'Başlat';
          presets.querySelectorAll('button').forEach((b) => b.classList.toggle('on', b.textContent === `${i.state.total / 60} dk`));
        },
      };
    },
  },
  /* ----------------------------------------------------------- Alarm */
  {
    id: 'alarm',
    name: 'Alarm',
    category: 'Saatler',
    description: 'Saat, etiket ve her gün tekrar. Bildirimi sesli ve kalıcıdır.',
    icon: ICON.alarm,
    accent: ACCENT.red,
    variants: [{ id: 'default', name: 'Standart' }],
    init: () => ({ time: '07:30', on: true, label: 'Uyan' }),
    card(i) {
      const s = h('div', { class: 'wc-sub' });
      return {
        el: card('wc-alarm', glyph(ICON.alarm, ACCENT.red), h('div', { class: 'wc-stack' }, h('div', { class: 'wc-title num', text: i.state.time }), s)),
        refresh: () => {
          s.textContent = i.state.on ? 'Her gün' : 'Kapalı';
          s.parentElement!.parentElement!.classList.toggle('is-off', !i.state.on);
        },
      };
    },
    compact(i) {
      return { el: compactTile(glyph(ICON.alarm, ACCENT.red), h('div', { class: 'wt-text num', text: i.state.time })) };
    },
    panel(i, env) {
      const sw = h('button', { class: 'switch', role: 'switch', 'aria-label': 'Alarm açık', onclick: () => { i.state.on = !i.state.on; env.refreshAll(); } });
      return {
        el: panelShell('Alarm', h('div', { class: 'wp-list-row' }, h('div', { class: 'grow' }, h('div', { class: 'wp-hero num', text: i.state.time }), h('div', { class: 'wp-muted', text: `${i.state.label} · her gün` })), sw)),
        refresh: () => sw.setAttribute('aria-checked', String(i.state.on)),
      };
    },
  },
  /* ----------------------------------------------- Zaman ilerlemesi */
  {
    id: 'time-progress',
    name: 'Zaman ilerlemesi',
    category: 'Saatler',
    description: 'Günün, haftanın, ayın ya da yılın ne kadarının geçtiği.',
    icon: ICON.progress,
    accent: ACCENT.purple,
    variants: [
      { id: 'bar', name: 'Çubuk' },
      { id: 'ring', name: 'Halka' },
    ],
    card(i) {
      if (i.variant === 'ring') {
        const r = ring(30, 3.2, ACCENT.purple, 'var(--t-purple)');
        r.inner.classList.add('num');
        return {
          el: card('wc-focus', r.el, h('div', { class: 'wc-stack' }, h('div', { class: 'wc-title', text: 'Gün' }), h('div', { class: 'wc-sub', text: 'geçti' }))),
          tick: (d) => {
            const p = dayPct(d);
            r.set(p);
            r.inner.textContent = String(Math.floor(p));
          },
        };
      }
      const tb = tickBar(16);
      const v = h('span', { class: 'num' });
      return {
        el: card('wc-stack wc-progress', h('div', { class: 'wc-row' }, h('span', { class: 'wc-title', text: 'Gün' }), v), tb.el),
        tick: (d) => {
          const p = dayPct(d);
          tb.set(p);
          v.textContent = `%${Math.floor(p)}`;
        },
      };
    },
    compact() {
      const r = ring(26, 2.6, ACCENT.purple, 'var(--t-purple)');
      return {
        el: compactTile(r.el),
        tick: (d) => {
          r.set(dayPct(d));
          r.inner.textContent = String(Math.floor(dayPct(d)));
        },
      };
    },
    panel() {
      const rows = ['Gün', 'Hafta', 'Ay', 'Yıl'].map((name) => {
        const tb = tickBar(24);
        const v = h('span', { class: 'num' });
        return { name, tb, v, el: h('div', { class: 'wp-progress' }, h('div', { class: 'wc-row' }, h('span', { class: 'wp-strong', text: name }), v), tb.el) };
      });
      return {
        el: panelShell('Zaman ilerlemesi', ...rows.map((r) => r.el)),
        tick: (d) =>
          rows.forEach((r) => {
            const p = periodPct(d, r.name);
            r.tb.set(p);
            r.v.textContent = `%${Math.floor(p)}`;
          }),
      };
    },
  },
  /* -------------------------------------------------------- Su içme */
  {
    id: 'hydration',
    name: 'Su içme',
    category: 'Hatırlatıcılar',
    description: 'Tıkla, bir bardak ekle. Aralıklı hatırlatmada "İçtim" düğmesi var.',
    icon: ICON.drop,
    accent: ACCENT.cyan,
    variants: [
      { id: 'timer', name: 'Zamanlayıcı' },
      { id: 'goal', name: 'Günlük hedef' },
    ],
    primary(_i, env) {
      const hy = sim.hydration;
      hy.count = hy.count >= hy.goal ? 0 : hy.count + 1;
      hy.next = 45 * 60;
      env.refreshAll();
      if (hy.count === hy.goal) env.toast?.('Günlük hedef tamam', `${hy.goal} bardak su içtin. Böyle devam.`);
    },
    card(i) {
      const hy = sim.hydration;
      if (i.variant === 'goal') {
        const drops = Array.from({ length: hy.goal }, () => h('i', { html: icons.drop }));
        const t = h('div', { class: 'wc-title num' });
        return {
          el: card('wc-hydration wc-stack', t, h('div', { class: 'drops' }, ...drops)),
          refresh: () => {
            t.textContent = `${hy.count} / ${hy.goal} bardak`;
            drops.forEach((d, k) => d.classList.toggle('on', k < hy.count));
          },
        };
      }
      const t = h('div', { class: 'wc-title num' });
      const s = h('div', { class: 'wc-sub num' });
      const el = card('wc-hydration', h('span', { class: 'wc-drop', html: icons.drop }), h('div', { class: 'wc-stack' }, t, s));
      return {
        el,
        tick: () => (s.textContent = hy.next > 0 ? `Sonraki ${Math.ceil(hy.next / 60)} dk` : 'Su zamanı'),
        refresh: () => {
          t.textContent = `${hy.count}/${hy.goal}`;
          el.classList.remove('splash');
          void el.offsetWidth;
          el.classList.add('splash');
        },
      };
    },
    compact() {
      const r = ring(26, 2.6, ACCENT.cyan, 'var(--t-cyan)');
      return {
        el: compactTile(r.el),
        refresh: () => {
          r.set((sim.hydration.count / sim.hydration.goal) * 100);
          r.inner.textContent = String(sim.hydration.count);
        },
      };
    },
    panel(_i, env) {
      const hy = sim.hydration;
      const drops = Array.from({ length: hy.goal }, () => h('i', { html: icons.drop }));
      const t = h('div', { class: 'wp-hero num' });
      return {
        el: panelShell(
          'Su içme',
          t,
          h('div', { class: 'drops drops-lg' }, ...drops),
          h('div', { class: 'wp-muted', text: 'Hatırlatma her 45 dakikada bir. Bildirimdeki "İçtim" düğmesi de sayar.' }),
          h('div', { class: 'wp-actions' }, h('button', { class: 'wp-btn accent', text: 'İçtim', onclick: () => { hy.count = Math.min(hy.goal, hy.count + 1); hy.next = 45 * 60; env.refreshAll(); } }), h('button', { class: 'wp-btn', text: 'Sıfırla', onclick: () => { hy.count = 0; env.refreshAll(); } })),
        ),
        refresh: () => {
          t.textContent = `${hy.count} / ${hy.goal} bardak`;
          drops.forEach((d, k) => d.classList.toggle('on', k < hy.count));
        },
      };
    },
  },
  /* -------------------------------------------------- Anımsatıcılar */
  {
    id: 'reminders',
    name: 'Anımsatıcılar',
    category: 'Hatırlatıcılar',
    description: 'Liste, sıradaki ya da sayı. Bildirimde "10 dk ertele" var.',
    icon: ICON.list,
    accent: ACCENT.blue,
    variants: [
      { id: 'next', name: 'Sıradaki' },
      { id: 'list', name: 'Liste' },
      { id: 'count', name: 'Sayı' },
    ],
    card(i) {
      const box = card(i.variant === 'count' ? 'wc-count' : 'wc-stack wc-reminders');
      return {
        el: box,
        refresh: () => {
          const open = sim.reminders.filter((r) => !r.done);
          if (i.variant === 'count') {
            box.replaceChildren(h('div', { class: 'wc-big num', text: open.length }), h('div', { class: 'wc-sub', html: 'bekleyen<br>anımsatıcı' }));
          } else if (i.variant === 'list') {
            box.replaceChildren(...(open.length ? open.slice(0, 2).map((r) => h('div', { class: 'wc-li' }, h('i'), h('span', { text: r.title }))) : [h('div', { class: 'wc-sub', text: 'Hepsi tamam' })]));
          } else {
            const n = open[0];
            box.replaceChildren(h('div', { class: 'wc-li' }, h('i'), h('span', { class: 'wc-title', text: n?.title ?? 'Hepsi tamam' })), h('div', { class: 'wc-sub', text: n?.time ?? 'Yeni anımsatıcı yok' }));
          }
        },
      };
    },
    compact() {
      const t = h('div', { class: 'wt-text num' });
      return {
        el: compactTile(glyph(ICON.list, ACCENT.blue), t),
        refresh: () => (t.textContent = String(sim.reminders.filter((r) => !r.done).length)),
      };
    },
    panel(_i, env) {
      const list = h('div', { class: 'wp-list' });
      return {
        el: panelShell('Anımsatıcılar', list),
        refresh: () =>
          list.replaceChildren(
            ...sim.reminders.map((r) =>
              h(
                'div',
                { class: `wp-list-row ${r.done ? 'is-done' : ''}` },
                h('button', { class: 'check', role: 'checkbox', 'aria-checked': String(r.done), 'aria-label': r.title, html: icons.check, onclick: () => { r.done = !r.done; env.refreshAll(); } }),
                h('div', { class: 'grow' }, h('div', { class: 'wp-strong', text: r.title }), h('div', { class: 'wp-muted', text: r.time })),
                h('button', { class: 'wp-chip', text: '10 dk ertele', onclick: () => env.toast?.('Ertelendi', `"${r.title}" 10 dakika sonra tekrar hatırlatılacak.`) }),
              ),
            ),
          ),
      };
    },
  },
  /* --------------------------------------------------- Yapışkan not */
  {
    id: 'notes',
    name: 'Yapışkan not',
    category: 'Notlar',
    description: 'Dock\'ta notun önizlemesi. Tıklayınca büyük kağıt açılır, kendiliğinden kaydedilir.',
    icon: ICON.note,
    accent: ACCENT.yellow,
    variants: [{ id: 'default', name: 'Standart' }],
    init: () => ({ text: 'Market: ekmek, zeytin, çay\nPazartesi sunum', color: 'yellow', size: 15 }),
    card(i) {
      const p = h('div', { class: 'note-preview' });
      const el = card('wc-note', p);
      return {
        el,
        refresh: () => {
          el.style.setProperty('--note', noteColors[i.state.color]);
          p.textContent = i.state.text || 'Boş not';
        },
      };
    },
    compact(i) {
      const n = h('div', { class: 'wt-note' });
      return {
        el: compactTile(n),
        refresh: () => {
          n.style.background = noteColors[i.state.color];
          n.textContent = (i.state.text || '').slice(0, 2);
        },
      };
    },
    panel(i, env) {
      const ta = h('textarea', { class: 'note-paper', 'aria-label': 'Not metni', spellcheck: 'false' }) as HTMLTextAreaElement;
      ta.value = i.state.text;
      ta.addEventListener('input', () => {
        i.state.text = ta.value;
        env.refreshAll();
      });
      const chips = h(
        'div',
        { class: 'note-colors', role: 'radiogroup', 'aria-label': 'Kağıt rengi' },
        ...Object.keys(noteColors).map((k) =>
          h('button', {
            class: 'note-color',
            role: 'radio',
            'aria-label': noteColorNames[k],
            style: `--c:${noteColors[k]}`,
            'data-color': k,
            onclick: () => {
              i.state.color = k;
              env.refreshAll();
            },
          }),
        ),
      );
      const wrap = h('div', { class: 'wp wp-note' }, ta, h('div', { class: 'note-foot' }, chips, h('span', { class: 'wp-muted', text: 'Kendiliğinden kaydedilir' })));
      return {
        el: wrap,
        refresh: () => {
          wrap.style.setProperty('--note', noteColors[i.state.color]);
          chips.querySelectorAll('button').forEach((b) => b.setAttribute('aria-checked', String(b.dataset.color === i.state.color)));
        },
      };
    },
  },
  /* -------------------------------------------------- Şu an çalıyor */
  {
    id: 'media',
    name: 'Şu an çalıyor',
    category: 'Medya',
    description: 'Spotify, tarayıcılar, VLC. Windows medya kontrollerini destekleyen her oynatıcı.',
    icon: ICON.music,
    accent: ACCENT.pink,
    variants: [
      { id: 'full', name: 'Tam' },
      { id: 'compact', name: 'Kompakt' },
      { id: 'mini', name: 'Mini' },
    ],
    card(i, env) {
      const m = sim.media;
      const art = h('div', { class: 'art' });
      const title = h('div', { class: 'wc-title' });
      const artist = h('div', { class: 'wc-sub' });
      const bar = h('div', { class: 'mbar' }, h('i'));
      const ctl = mediaControls(env, 'sm');
      let shown = -1;
      const parts: Kid[] =
        i.variant === 'mini'
          ? [art, ctl.el.children[1] as HTMLElement]
          : i.variant === 'compact'
            ? [art, h('div', { class: 'wc-stack grow' }, title, artist), ctl.el.children[1] as HTMLElement]
            : [art, h('div', { class: 'wc-stack grow' }, title, artist, bar), ctl.el];
      const el = card(`wc-media wc-media-${i.variant}`, ...parts);
      return {
        el,
        frame: () => {
          (bar.firstChild as HTMLElement).style.transform = `scaleX(${m.pos / tracks[m.index].length})`;
        },
        refresh: () => {
          if (shown !== m.index) {
            shown = m.index;
            art.innerHTML = albumArt(m.index);
            title.textContent = tracks[m.index].title;
            artist.textContent = tracks[m.index].artist;
          }
          el.classList.toggle('is-playing', m.playing);
          ctl.sync();
        },
      };
    },
    compact() {
      const art = h('div', { class: 'art art-sm' });
      return { el: compactTile(art), refresh: () => (art.innerHTML = albumArt(sim.media.index)) };
    },
    panel(_i, env) {
      const m = sim.media;
      const art = h('div', { class: 'art art-lg' });
      const title = h('div', { class: 'wp-strong wp-lg' });
      const artist = h('div', { class: 'wp-muted' });
      const bar = h('div', { class: 'mbar mbar-lg' }, h('i'));
      const a = h('span', { class: 'num' });
      const b = h('span', { class: 'num' });
      const ctl = mediaControls(env, 'lg');
      return {
        el: h('div', { class: 'wp wp-media' }, art, title, artist, bar, h('div', { class: 'wc-row wp-muted' }, a, b), ctl.el, h('div', { class: 'wp-foot', text: 'Oynatıcı: Medya Oynatıcı · kamu malı eserler' })),
        frame: () => {
          const len = tracks[m.index].length;
          (bar.firstChild as HTMLElement).style.transform = `scaleX(${m.pos / len})`;
          a.textContent = mmss(m.pos).replace(/^0/, '');
          b.textContent = mmss(len).replace(/^0/, '');
        },
        refresh: () => {
          art.innerHTML = albumArt(m.index);
          title.textContent = tracks[m.index].title;
          artist.textContent = tracks[m.index].artist;
          ctl.sync();
        },
      };
    },
  },
  /* -------------------------------------------------- CPU ve bellek */
  {
    id: 'system',
    name: 'CPU ve bellek',
    category: 'Sistem',
    description: 'İşlemci ve bellek kullanımı; sayı, halka ya da çubuk.',
    icon: ICON.pulse,
    accent: ACCENT.magenta,
    variants: [
      { id: 'rings', name: 'Halkalar' },
      { id: 'numbers', name: 'Sayılar' },
      { id: 'bars', name: 'Çubuklar' },
    ],
    card(i) {
      const rows = [
        { label: 'CPU', spring: sim.cpu, color: ACCENT.magenta, track: TRACK.magenta },
        { label: 'RAM', spring: sim.ram, color: ACCENT.blue, track: TRACK.blue },
      ];
      if (i.variant === 'rings') {
        const rs = rows.map((r) => {
          const g = ring(32, 3.2, r.color, r.track);
          g.inner.classList.add('num');
          return { r, g, el: h('div', { class: 'wc-ringcell' }, g.el, h('div', { class: 'wc-cap', text: r.label })) };
        });
        return {
          el: card('wc-rings', ...rs.map((x) => x.el)),
          frame: () =>
            rs.forEach(({ r, g }) => {
              g.set(r.spring.value);
              g.inner.textContent = String(Math.round(r.spring.value));
            }),
        };
      }
      if (i.variant === 'bars') {
        const bs = rows.map((r) => {
          const fill = h('i', { style: `background:${r.color}` });
          const v = h('span', { class: 'num' });
          return { r, fill, v, el: h('div', { class: 'wc-barrow' }, h('span', { class: 'wc-cap', text: r.label }), h('div', { class: 'hbar', style: `background:${r.track}` }, fill), v) };
        });
        return {
          el: card('wc-stack wc-bars', ...bs.map((x) => x.el)),
          frame: () =>
            bs.forEach(({ r, fill, v }) => {
              fill.style.transform = `scaleX(${r.spring.value / 100})`;
              v.textContent = `%${Math.round(r.spring.value)}`;
            }),
        };
      }
      const ns = rows.map((r) => {
        const v = h('span', { class: 'num' });
        return { r, v, el: h('div', { class: 'wc-numrow' }, h('span', { class: 'wc-cap', style: `color:${r.color}`, text: r.label }), v) };
      });
      return {
        el: card('wc-stack wc-numbers', ...ns.map((x) => x.el)),
        frame: () => ns.forEach(({ r, v }) => (v.textContent = `%${Math.round(r.spring.value)}`)),
      };
    },
    compact() {
      const r = ring(26, 2.6, ACCENT.magenta, TRACK.magenta);
      return {
        el: compactTile(r.el, h('div', { class: 'wt-text', text: 'CPU' })),
        frame: () => {
          r.set(sim.cpu.value);
          r.inner.textContent = String(Math.round(sim.cpu.value));
        },
      };
    },
    panel() {
      const svgWrap = h('div', { class: 'spark' });
      const cpu = h('span', { class: 'wp-hero num' });
      const ram = h('span', { class: 'wp-hero num' });
      const trend = h('span', { class: 'wp-muted' });
      let t = 0;
      return {
        el: panelShell(
          'CPU ve bellek',
          h('div', { class: 'wp-duo' }, h('div', {}, h('div', { class: 'wp-label', style: `color:${ACCENT.magenta}`, text: 'İşlemci' }), cpu), h('div', {}, h('div', { class: 'wp-label', style: `color:${ACCENT.blue}`, text: 'Bellek' }), ram)),
          svgWrap,
          trend,
          h('div', { class: 'wp-foot', text: 'Güncelleme aralığı 1 ile 10 saniye arasında seçilir.' }),
        ),
        frame: (dt) => {
          cpu.textContent = `%${Math.round(sim.cpu.value)}`;
          ram.textContent = `%${Math.round(sim.ram.value)}`;
          t += dt;
          if (t < 0.5 && svgWrap.firstChild) return;
          t = 0;
          const s = sparkline([...sim.cpuHistory, sim.cpu.value], 260, 64, 100);
          svgWrap.innerHTML = `<svg viewBox="0 0 260 64" preserveAspectRatio="none"><path d="${s.area}" fill="var(--t-magenta)"/><path d="${s.line}" fill="none" stroke="var(--c-magenta)" stroke-width="1.6" vector-effect="non-scaling-stroke"/></svg>`;
          const h8 = sim.cpuHistory.slice(-8);
          const diff = h8[h8.length - 1] - h8[0];
          trend.textContent = `Son 15 saniye: ${Math.abs(diff) < 4 ? 'dengeli' : diff > 0 ? 'yükseliyor' : 'düşüyor'}`;
        },
      };
    },
  },
  /* ------------------------------------------------------------- Ağ */
  {
    id: 'network',
    name: 'Ağ hızı',
    category: 'Sistem',
    description: 'Anlık indirme ve yükleme hızı, isterseniz grafikle.',
    icon: ICON.network,
    accent: ACCENT.blue,
    variants: [
      { id: 'numbers', name: 'Yalnızca sayılar' },
      { id: 'graph', name: 'Grafikli' },
    ],
    card(i) {
      const d = h('span', { class: 'num' });
      const u = h('span', { class: 'num' });
      const graph = h('div', { class: 'wc-graph' });
      const el = card(
        `wc-stack wc-net ${i.variant === 'graph' ? 'has-graph' : ''}`,
        i.variant === 'graph' ? graph : null,
        h('div', { class: 'wc-netrow' }, h('span', { class: 'ic-sm down', html: icons.arrowDown }), d),
        h('div', { class: 'wc-netrow' }, h('span', { class: 'ic-sm up', html: icons.arrowUp }), u),
      );
      let t = 1;
      return {
        el,
        frame: (dt) => {
          d.textContent = `${fmt1(sim.down.value)} MB/sn`;
          u.textContent = `${Math.round(sim.up.value * 1000)} KB/sn`;
          if (i.variant !== 'graph') return;
          t += dt;
          if (t < 0.5) return;
          t = 0;
          const s = sparkline([...sim.netHistory, sim.down.value], 100, 40);
          graph.innerHTML = `<svg viewBox="0 0 100 40" preserveAspectRatio="none"><path d="${s.area}" fill="var(--t-blue)"/></svg>`;
        },
      };
    },
    compact() {
      const t = h('div', { class: 'wt-text num' });
      return { el: compactTile(glyph(ICON.network, ACCENT.blue), t), frame: () => (t.textContent = fmt1(sim.down.value)) };
    },
    panel() {
      const d = h('div', { class: 'wp-hero num' });
      const u = h('div', { class: 'wp-hero num' });
      const g = h('div', { class: 'spark' });
      let t = 1;
      return {
        el: panelShell('Ağ hızı', h('div', { class: 'wp-duo' }, h('div', {}, h('div', { class: 'wp-label', text: 'İndirme' }), d), h('div', {}, h('div', { class: 'wp-label', text: 'Yükleme' }), u)), g, h('div', { class: 'wp-foot', text: 'Ölçüm yalnızca bu widget dock\'tayken çalışır.' })),
        frame: (dt) => {
          d.textContent = `${fmt1(sim.down.value)} MB/sn`;
          u.textContent = `${Math.round(sim.up.value * 1000)} KB/sn`;
          t += dt;
          if (t < 0.5) return;
          t = 0;
          const s = sparkline([...sim.netHistory, sim.down.value], 260, 64);
          g.innerHTML = `<svg viewBox="0 0 260 64" preserveAspectRatio="none"><path d="${s.area}" fill="var(--t-blue)"/><path d="${s.line}" fill="none" stroke="var(--c-blue)" stroke-width="1.6" vector-effect="non-scaling-stroke"/></svg>`;
        },
      };
    },
  },
  /* ---------------------------------------------------------- Durum */
  {
    id: 'status',
    name: 'Durum',
    category: 'Sistem',
    description: 'Pil, disk, bellek ve işlemci doluluk halkaları.',
    icon: ICON.status,
    accent: ACCENT.green,
    variants: [
      { id: 'rings', name: 'Halkalar' },
      { id: 'percent', name: 'Yüzde halkası' },
      { id: 'icons', name: 'Yalnızca ikon' },
    ],
    card(i) {
      const kinds = [
        { k: 'battery' as const, s: sim.battery, label: 'Pil', warn: (v: number) => v < 20 },
        { k: 'disk' as const, s: sim.disk, label: 'Disk', warn: (v: number) => v >= 90 },
        { k: 'memory' as const, s: sim.ram, label: 'Bellek', warn: (v: number) => v >= 90 },
      ];
      const size = i.variant === 'icons' ? 32 : 26;
      const cells = kinds.map((x) => {
        const g = ring(size, i.variant === 'icons' ? 3.4 : 3, ACCENT.green, TRACK.green);
        if (i.variant === 'percent') g.inner.classList.add('num');
        else g.inner.innerHTML = `<span class="dg">${deviceGlyph(x.k)}</span>`;
        const cap = h('div', { class: 'wc-cap num' });
        return { x, g, cap, el: h('div', { class: 'wc-ringcell' }, g.el, i.variant === 'icons' ? null : cap) };
      });
      return {
        el: card('wc-rings wc-status', ...cells.map((c) => c.el)),
        frame: () =>
          cells.forEach(({ x, g, cap }) => {
            const v = x.s.value;
            g.set(v);
            g.color(x.warn(v) ? ACCENT.red : ACCENT.green);
            if (i.variant === 'percent') {
              g.inner.textContent = String(Math.round(v));
              cap.textContent = x.label;
            } else cap.textContent = `%${Math.round(v)}`;
          }),
      };
    },
    compact() {
      const r = ring(26, 2.6, ACCENT.green, TRACK.green);
      return {
        el: compactTile(r.el, h('div', { class: 'wt-text', text: 'Pil' })),
        frame: () => {
          r.set(sim.battery.value);
          r.inner.textContent = String(Math.round(sim.battery.value));
        },
      };
    },
    panel(_i, env) {
      const rows = [
        { k: 'battery' as const, s: sim.battery, label: 'Pil', sub: 'Kalan yaklaşık 4 sa 20 dk' },
        { k: 'disk' as const, s: sim.disk, label: 'Disk (C:)', sub: '612 GB / 953 GB' },
        { k: 'memory' as const, s: sim.ram, label: 'Bellek', sub: '16 GB' },
        { k: 'cpu' as const, s: sim.cpu, label: 'İşlemci', sub: '8 çekirdek' },
      ].map((x) => {
        const g = ring(40, 4, ACCENT.green, TRACK.green);
        g.inner.innerHTML = `<span class="dg">${deviceGlyph(x.k)}</span>`;
        const v = h('div', { class: 'wp-num num' });
        return { x, g, v, el: h('div', { class: 'wp-list-row' }, g.el, h('div', { class: 'grow' }, h('div', { class: 'wp-strong', text: x.label }), h('div', { class: 'wp-muted', text: x.sub })), v) };
      });
      const drain = h('button', {
        class: 'wp-chip',
        text: 'Pili azalt',
        onclick: () => {
          sim.battery.target = sim.battery.target > 30 ? 14 : 86;
          env.refreshAll();
        },
      });
      return {
        el: panelShell('Durum', h('div', { class: 'wp-list' }, ...rows.map((r) => r.el)), h('div', { class: 'wp-note-row' }, h('span', { class: 'wp-muted', text: 'Pil %20 altına inince halka kırmızıya döner.' }), drain)),
        frame: () =>
          rows.forEach(({ x, g, v }) => {
            const val = x.s.value;
            g.set(val);
            g.color((x.k === 'battery' ? val < 20 : val >= 90) ? ACCENT.red : ACCENT.green);
            v.textContent = `%${Math.round(val)}`;
          }),
        refresh: () => (drain.textContent = sim.battery.target > 30 ? 'Pili azalt' : 'Pili doldur'),
      };
    },
  },
  /* --------------------------------------------------- Hava durumu */
  {
    id: 'weather',
    name: 'Hava durumu',
    category: 'Hava durumu',
    description: 'Güncel durum ya da saatlik tahmin. Open-Meteo ile, API anahtarı gerekmez.',
    icon: ICON.weather,
    accent: ACCENT.cyan,
    variants: [
      { id: 'current', name: 'Güncel' },
      { id: 'condition', name: 'Durum' },
      { id: 'hourly', name: 'Saatlik tahmin' },
    ],
    card(i) {
      const w = sim.weather;
      if (i.variant === 'hourly') {
        const cols = w.hourly.slice(0, 5).map((x) => ({ x, t: h('div', { class: 'wc-cap num' }) }));
        return {
          el: card('wc-hourly', ...cols.map(({ x, t }) => h('div', { class: 'wc-hcol' }, t, h('span', { html: weatherIcon(x.c, 18) }), h('div', { class: 'wc-htemp num', text: `${x.t}°` })))),
          tick: (d) => cols.forEach(({ x, t }) => (t.textContent = pad((d.getHours() + x.h) % 24))),
        };
      }
      if (i.variant === 'condition') {
        return {
          el: card('wc-weather', h('span', { html: weatherIcon(w.code, 30) }), h('div', { class: 'wc-stack' }, h('div', { class: 'wc-title', html: `<span class="num">${w.temp}°</span> ${w.text}` }), h('div', { class: 'wc-sub num', text: `Y ${w.hi}° D ${w.lo}° · ${w.city}` }))),
        };
      }
      return {
        el: card('wc-weather', h('span', { html: weatherIcon(w.code, 30) }), h('div', { class: 'wc-stack' }, h('div', { class: 'wc-big num', text: `${w.temp}°` }), h('div', { class: 'wc-sub', text: w.city }))),
      };
    },
    compact() {
      return { el: compactTile(h('span', { class: 'wt-wx', html: weatherIcon(sim.weather.code, 22) }), h('div', { class: 'wt-text num', text: `${sim.weather.temp}°` })) };
    },
    panel() {
      const w = sim.weather;
      const hours = h('div', { class: 'wp-hours' });
      return {
        el: panelShell(
          w.city,
          h('div', { class: 'wp-row' }, h('span', { html: weatherIcon(w.code, 56) }), h('div', {}, h('div', { class: 'wp-hero num', text: `${w.temp}°` }), h('div', { class: 'wp-muted num', text: `${w.text} · Y ${w.hi}° D ${w.lo}°` }))),
          hours,
          h('div', { class: 'wp-days' }, ...w.daily.map((x) => h('div', { class: 'wp-day' }, h('span', { class: 'wp-strong', text: x.d }), h('span', { html: weatherIcon(x.c, 22) }), h('span', { class: 'num wp-muted', text: `${x.lo}°` }), h('span', { class: 'tempbar' }, h('i', { style: `left:${(x.lo - 10) * 6}%;right:${100 - (x.hi - 10) * 6}%` })), h('span', { class: 'num', text: `${x.hi}°` })))),
          h('div', { class: 'wp-foot', text: 'Örnek veri. Uygulamada Open-Meteo kullanılır, veri 30 dakikada bir yenilenir.' }),
        ),
        tick: (d) =>
          hours.replaceChildren(
            ...w.hourly.map((x) => h('div', { class: 'wp-hour' }, h('span', { class: 'wp-muted num', text: pad((d.getHours() + x.h) % 24) }), h('span', { html: weatherIcon(x.c, 24) }), h('span', { class: 'num', text: `${x.t}°` }))),
          ),
      };
    },
  },
];

export const widgetById = Object.fromEntries(widgets.map((w) => [w.id, w])) as Record<string, WidgetDef>;

function dayPct(d: Date) {
  return ((d.getHours() * 3600 + d.getMinutes() * 60 + d.getSeconds()) / 86400) * 100;
}

function periodPct(d: Date, name: string) {
  if (name === 'Gün') return dayPct(d);
  if (name === 'Hafta') return ((((d.getDay() + 6) % 7) + dayPct(d) / 100) / 7) * 100;
  if (name === 'Ay') {
    const days = new Date(d.getFullYear(), d.getMonth() + 1, 0).getDate();
    return ((d.getDate() - 1 + dayPct(d) / 100) / days) * 100;
  }
  const start = new Date(d.getFullYear(), 0, 1).getTime();
  const end = new Date(d.getFullYear() + 1, 0, 1).getTime();
  return ((d.getTime() - start) / (end - start)) * 100;
}

function focusStep(i: Instance, dt: number, env: Env) {
  const st = i.state;
  if (!st.running) return;
  st.left -= dt;
  if (st.left <= 0) {
    const toBreak = st.phase === 'Odak';
    st.phase = toBreak ? 'Mola' : 'Odak';
    st.total = toBreak ? 5 * 60 : 25 * 60;
    st.left = st.total;
    st.running = false;
    env.toast?.(toBreak ? 'Odak süresi bitti' : 'Mola bitti', toBreak ? '5 dakikalık mola zamanı.' : 'Yeni bir odak turuna hazırsın.');
    env.refreshAll();
  }
}

function countdownStep(i: Instance, dt: number, env: Env) {
  const st = i.state;
  if (!st.running) return;
  st.left -= dt;
  if (st.left <= 0) {
    st.left = 0;
    st.running = false;
    env.toast?.('Geri sayım bitti', `${st.label} hazır.`);
    env.refreshAll();
  }
}

const live = new Map<Instance, Env>();
export const track = (i: Instance, env: Env) => live.set(i, env);
export const untrack = (i: Instance) => live.delete(i);

onFrame((dt) =>
  live.forEach((env, i) => {
    if (i.id === 'focus') focusStep(i, dt, env);
    else if (i.id === 'countdown') countdownStep(i, dt, env);
    else if (i.id === 'stopwatch' && i.state.running) i.state.elapsed += dt;
  }),
);

let uidSeq = 0;
export function makeInstance(id: string, variant?: string): Instance {
  const def = widgetById[id];
  return { uid: `w${++uidSeq}`, id, variant: variant ?? def.variants[0].id, state: def.init?.() ?? {} };
}
