export class Spring {
  value: number;
  target: number;
  constructor(v: number, private k = 3.2) {
    this.value = v;
    this.target = v;
  }
  step(dt: number) {
    this.value += (this.target - this.value) * (1 - Math.exp(-dt * this.k));
    return this.value;
  }
}

const clamp = (v: number, a: number, b: number) => Math.min(b, Math.max(a, v));
const walk = (v: number, amp: number, lo: number, hi: number) => clamp(v + (Math.random() - 0.5) * amp, lo, hi);

export const tracks = [
  { title: 'Gymnopédie No. 1', artist: 'Erik Satie', length: 185, art: ['#f7b267', '#f25c54', '#6a2c70'] },
  { title: 'Clair de Lune', artist: 'Claude Debussy', length: 302, art: ['#8fb8ff', '#3a57d6', '#131b4d'] },
  { title: 'Für Elise', artist: 'Ludwig van Beethoven', length: 175, art: ['#9ff0c0', '#2a9d8f', '#16324f'] },
];

export const sim = {
  cpu: new Spring(23),
  ram: new Spring(61),
  battery: new Spring(86),
  disk: new Spring(64),
  cpuHistory: Array.from({ length: 32 }, (_, i) => 20 + Math.sin(i / 3) * 6 + Math.random() * 6),
  down: new Spring(4.2, 2),
  up: new Spring(0.31, 2),
  netHistory: Array.from({ length: 32 }, (_, i) => 2.5 + Math.sin(i / 2.6) * 1.4 + Math.random()),
  media: { index: 0, playing: true, pos: 48 },
  hydration: { count: 4, goal: 8, next: 25 * 60 },
  reminders: [
    { id: 'r1', title: 'Faturayı öde', time: '15:00', done: false },
    { id: 'r2', title: 'Annemi ara', time: '18:30', done: false },
    { id: 'r3', title: 'Tasarım incelemesi', time: 'Yarın 10:30', done: false },
  ],
  weather: {
    city: 'İstanbul',
    temp: 18,
    hi: 21,
    lo: 14,
    code: 'partly' as WeatherCode,
    text: 'Parçalı bulutlu',
    hourly: [
      { h: 1, t: 18, c: 'partly' as WeatherCode },
      { h: 2, t: 19, c: 'sun' as WeatherCode },
      { h: 3, t: 20, c: 'sun' as WeatherCode },
      { h: 4, t: 19, c: 'cloud' as WeatherCode },
      { h: 5, t: 17, c: 'rain' as WeatherCode },
      { h: 6, t: 16, c: 'rain' as WeatherCode },
    ],
    daily: [
      { d: 'Bugün', hi: 21, lo: 14, c: 'partly' as WeatherCode },
      { d: 'Cum', hi: 19, lo: 13, c: 'rain' as WeatherCode },
      { d: 'Cmt', hi: 22, lo: 14, c: 'sun' as WeatherCode },
      { d: 'Paz', hi: 23, lo: 15, c: 'sun' as WeatherCode },
      { d: 'Pzt', hi: 20, lo: 14, c: 'cloud' as WeatherCode },
    ],
  },
};

export type WeatherCode = 'sun' | 'partly' | 'cloud' | 'rain' | 'moon';

let lastRetarget = 0;
export function stepSim(dt: number, now: number) {
  if (now - lastRetarget > 1800) {
    lastRetarget = now;
    sim.cpu.target = walk(sim.cpu.target, 22, 6, 78);
    sim.ram.target = walk(sim.ram.target, 3, 52, 74);
    sim.down.target = walk(sim.down.target, 3.2, 0.4, 11.5);
    sim.up.target = walk(sim.up.target, 0.3, 0.05, 1.4);
    sim.cpuHistory.push(sim.cpu.target);
    sim.cpuHistory.shift();
    sim.netHistory.push(sim.down.target);
    sim.netHistory.shift();
  }
  sim.cpu.step(dt);
  sim.ram.step(dt);
  sim.down.step(dt);
  sim.up.step(dt);
  sim.battery.step(dt);
  sim.disk.step(dt);
  const m = sim.media;
  if (m.playing) {
    m.pos += dt;
    if (m.pos >= tracks[m.index].length) {
      m.index = (m.index + 1) % tracks.length;
      m.pos = 0;
    }
  }
  if (sim.hydration.next > 0) sim.hydration.next = Math.max(0, sim.hydration.next - dt);
}

type FrameFn = (dt: number, now: number) => void;
type SecondFn = (now: Date) => void;
const frameSubs = new Set<FrameFn>();
const secondSubs = new Set<SecondFn>();
let running = false;
let raf = 0;
let last = 0;
let lastSecond = -1;
let active = true;

function loop(t: number) {
  const dt = Math.min(0.1, (t - last) / 1000 || 0);
  last = t;
  stepSim(dt, t);
  frameSubs.forEach((f) => f(dt, t));
  const d = new Date();
  const s = d.getSeconds();
  if (s !== lastSecond) {
    lastSecond = s;
    secondSubs.forEach((f) => f(d));
  }
  raf = requestAnimationFrame(loop);
}

const visible = new Map<string, boolean>();
export function setVisible(key: string, on: boolean) {
  visible.set(key, on);
  active = [...visible.values()].some(Boolean);
  sync();
}

function sync() {
  if (typeof document === 'undefined') return;
  const should = active && document.visibilityState === 'visible';
  if (should && !running) {
    running = true;
    last = performance.now();
    raf = requestAnimationFrame(loop);
  } else if (!should && running) {
    running = false;
    cancelAnimationFrame(raf);
  }
}

if (typeof document !== 'undefined') {
  document.addEventListener('visibilitychange', sync);
}

export function onFrame(f: FrameFn) {
  frameSubs.add(f);
  sync();
  return () => frameSubs.delete(f);
}

export function onSecond(f: SecondFn) {
  secondSubs.add(f);
  sync();
  return () => secondSubs.delete(f);
}

export const TR_DAYS = ['Paz', 'Pzt', 'Sal', 'Çar', 'Per', 'Cum', 'Cmt'];
export const TR_DAYS_LONG = ['Pazar', 'Pazartesi', 'Salı', 'Çarşamba', 'Perşembe', 'Cuma', 'Cumartesi'];
export const TR_MONTHS = ['Oca', 'Şub', 'Mar', 'Nis', 'May', 'Haz', 'Tem', 'Ağu', 'Eyl', 'Eki', 'Kas', 'Ara'];
export const TR_MONTHS_LONG = ['Ocak', 'Şubat', 'Mart', 'Nisan', 'Mayıs', 'Haziran', 'Temmuz', 'Ağustos', 'Eylül', 'Ekim', 'Kasım', 'Aralık'];

export const pad = (n: number) => String(n).padStart(2, '0');
export const hhmm = (d: Date) => `${pad(d.getHours())}:${pad(d.getMinutes())}`;
export const mmss = (sec: number) => {
  const s = Math.max(0, Math.ceil(sec));
  return `${pad(Math.floor(s / 60))}:${pad(s % 60)}`;
};
export const fmt1 = (v: number) => v.toLocaleString('tr-TR', { minimumFractionDigits: 1, maximumFractionDigits: 1 });
