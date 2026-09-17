import { h, widgetById, makeInstance, track, type View, type Env } from './widgets';
import { mount, unmount, refreshAll } from './views';
import { icons, windowsLogo } from '../lib/icons';
import { appIcons, appNames, type AppId } from '../lib/apps';
import { onSecond, pad, setVisible } from './sim';

const env: Env = { refreshAll };
const reduced = () => matchMedia('(prefers-reduced-motion: reduce)').matches || document.documentElement.classList.contains('static');
let seq = 0;

export interface MiniDock {
  stage: HTMLElement;
  dock: HTMLElement;
  items: HTMLElement;
  rebuild(): void;
  views: View[];
  setPaused(p: boolean): void;
  addApp(id: AppId): HTMLElement;
}

export function buildMiniDock(stage: HTMLElement): MiniDock {
  const spec = (stage.dataset.spec ?? '').split(/\s+/).filter(Boolean);
  const dock = h('div', { class: 'dock', 'aria-hidden': 'true' });
  const startZone = h('div', { class: 'dock-zone dock-start' });
  const center = h('div', { class: 'dock-center' });
  const scroll = h('div', { class: 'dock-scroll' });
  const items = h('div', { class: 'dock-items' });
  const endZone = h('div', { class: 'dock-zone dock-end' });
  scroll.append(items);
  center.append(scroll);
  let views: View[] = [];
  let paused = false;
  const insts = new Map<string, ReturnType<typeof makeInstance>>();

  const clock = () => {
    const b = h('div', { class: 'db db-clock' }, h('span', { class: 't' }), h('span', { class: 'd', 'data-clock-date': '' }));
    const set = (d: Date) => {
      b.firstElementChild!.textContent = `${pad(d.getHours())}:${pad(d.getMinutes())}`;
      b.lastElementChild!.textContent = `${pad(d.getDate())}.${pad(d.getMonth() + 1)}.${d.getFullYear()}`;
    };
    set(new Date());
    onSecond(set);
    return b;
  };

  const appBtn = (id: AppId) => h('div', { class: 'dock-item' }, h('div', { class: 'db app-btn', title: appNames[id] }, h('span', { class: 'app-ico', html: appIcons[id] })));

  let rebuild = function () {
    views.forEach(unmount);
    views = [];
    startZone.replaceChildren();
    items.replaceChildren();
    endZone.replaceChildren();
    const vertical = stage.dataset.edge === 'left' || stage.dataset.edge === 'right';
    let i = 2;
    for (const tok of spec) {
      const [kind, a, b] = tok.split(':');
      if (kind === 'start') {
        startZone.append(h('div', { class: 'db db-start', html: windowsLogo(`wl-m${++seq}`) }), h('div', { class: 'db', html: icons.search }));
      } else if (kind === 'app') {
        const el = appBtn(a as AppId);
        if (b === 'run') el.firstElementChild!.classList.add('is-running');
        if (b === 'active') el.firstElementChild!.classList.add('is-running', 'is-active');
        items.append(el);
      } else if (kind === 'sep') items.append(h('div', { class: 'dock-item dock-sep' }));
      else if (kind === 'w') {
        const key = `${a}:${b ?? ''}:${i}`;
        let inst = insts.get(key);
        if (!inst) {
          inst = makeInstance(a, b || undefined);
          track(inst, env);
          insts.set(key, inst);
        }
        const def = widgetById[a];
        const v = vertical ? def.compact(inst, env) : def.card(inst, env);
        views.push(v);
        items.append(h('div', { class: 'dock-item', 'data-w': a }, h('div', { class: 'widget-btn' }, v.el)));
      } else if (kind === 'tray') {
        endZone.append(
          h('div', { class: 'dock-tray' }, h('div', { class: 'db db-tray', html: icons.chevronUp }), h('div', { class: 'db db-tray' }, h('span', { html: icons.wifi }), h('span', { html: icons.volume }), h('span', { html: icons.battery }))),
        );
      } else if (kind === 'clock') endZone.append(clock(), h('div', { class: 'db-desktop' }));
      (items.lastElementChild as HTMLElement | null)?.style.setProperty('--i', String(i++));
    }
    dock.replaceChildren(...[startZone.childElementCount ? startZone : null, center, endZone.childElementCount ? endZone : null].filter(Boolean) as HTMLElement[]);
    views.forEach((v) => {
      mount(v);
      (v as View & { paused?: boolean }).paused = paused;
    });
  };

  const fit = () => {
    dock.style.zoom = '';
    dock.style.maxWidth = '';
    dock.style.maxHeight = '';
    const vertical = stage.dataset.edge === 'left' || stage.dataset.edge === 'right';
    const zones = [startZone, endZone].filter((z) => z.isConnected);
    const need = vertical
      ? zones.reduce((a, z) => a + z.offsetHeight + 8, 0) + items.offsetHeight + 14
      : zones.reduce((a, z) => a + z.offsetWidth + 8, 0) + items.offsetWidth + 14;
    const avail = (vertical ? stage.clientHeight : stage.clientWidth) - 20;
    const z = Math.min(1, avail / need);
    if (z < 0.995) {
      dock.style.maxWidth = 'none';
      dock.style.maxHeight = 'none';
      dock.style.zoom = z.toFixed(3);
    }
  };

  const origRebuild = rebuild;
  rebuild = () => {
    origRebuild();
    fit();
  };

  origRebuild();
  const anchor = stage.querySelector('[data-dock-slot]');
  if (anchor) anchor.replaceWith(dock);
  else stage.append(dock);
  fit();
  new ResizeObserver(fit).observe(stage);

  return {
    stage,
    dock,
    items,
    rebuild: () => rebuild(),
    get views() {
      return views;
    },
    setPaused(p: boolean) {
      paused = p;
      views.forEach((v) => ((v as View & { paused?: boolean }).paused = p));
    },
    addApp(id: AppId) {
      const el = appBtn(id);
      items.append(el);
      return el;
    },
  };
}

type Cycle = (m: MiniDock) => { tick(): void; interval: number };

const cycles: Record<string, Cycle> = {
  edge(m) {
    const edges = ['bottom', 'left', 'top', 'right'];
    let i = 0;
    const card = m.stage.closest('[data-card]');
    const mark = () => card?.querySelectorAll<HTMLElement>('[data-edge-opt]').forEach((o) => o.classList.toggle('on', o.dataset.edgeOpt === edges[i]));
    mark();
    return {
      interval: 2600,
      tick() {
        m.dock.classList.add('mini-out');
        setTimeout(() => {
          i = (i + 1) % edges.length;
          m.stage.dataset.edge = edges[i];
          m.rebuild();
          mark();
          m.dock.classList.remove('mini-out');
          m.stage.classList.add('is-entering');
          setTimeout(() => m.stage.classList.remove('is-entering'), 900);
        }, 260);
      },
    };
  },
  autohide(m) {
    const cursor = m.stage.querySelector<HTMLElement>('[data-cursor]');
    let hidden = false;
    return {
      interval: 1900,
      tick() {
        hidden = !hidden;
        m.stage.classList.toggle('is-hidden', hidden);
        cursor?.classList.toggle('at-edge', !hidden);
      },
    };
  },
  reorder(m) {
    return {
      interval: 2300,
      tick() {
        const kids = [...m.items.children].filter((k) => (k as HTMLElement).dataset.w) as HTMLElement[];
        if (kids.length < 2) return;
        const [a, b] = [kids[0], kids[1]];
        const before = new Map(kids.map((k) => [k, k.offsetLeft]));
        a.classList.add('is-dragging');
        m.items.insertBefore(b, a);
        kids.forEach((k) => {
          const d = before.get(k)! - k.offsetLeft;
          if (d) k.animate([{ transform: `translateX(${d}px)` }, { transform: 'none' }], { duration: 620, easing: 'cubic-bezier(0.1,0.9,0.2,1)' });
        });
        setTimeout(() => a.classList.remove('is-dragging'), 640);
      },
    };
  },
  pin(m) {
    const card = m.stage.closest('[data-card]');
    const menu = card?.querySelector<HTMLElement>('[data-pin-menu]');
    let added: HTMLElement | null = null;
    let step = 0;
    return {
      interval: 1500,
      tick() {
        step = (step + 1) % 4;
        if (step === 1) menu?.classList.add('is-hot');
        if (step === 2) {
          menu?.classList.remove('is-hot');
          menu?.classList.add('is-gone');
          added = m.addApp('calculator');
          added.classList.add('is-new');
        }
        if (step === 0) {
          added?.remove();
          added = null;
          menu?.classList.remove('is-gone');
        }
      },
    };
  },
};

export function initMinis() {
  const stages = [...document.querySelectorAll<HTMLElement>('[data-mini]')];
  const tbClocks = document.querySelectorAll<HTMLElement>('[data-mini-clock]');
  const setClocks = (d: Date) =>
    tbClocks.forEach((c) => (c.innerHTML = `<span>${pad(d.getHours())}:${pad(d.getMinutes())}</span><span>${pad(d.getDate())}.${pad(d.getMonth() + 1)}.${d.getFullYear()}</span>`));
  setClocks(new Date());
  onSecond(setClocks);
  const minis = new Map<HTMLElement, { m: MiniDock; timer: number; cycle?: ReturnType<Cycle> }>();
  for (const st of stages) {
    const m = buildMiniDock(st);
    const cyc = st.dataset.cycle && !reduced() ? cycles[st.dataset.cycle]?.(m) : undefined;
    minis.set(st, { m, timer: 0, cycle: cyc });
    if (st.dataset.panel) {
      const def = widgetById[st.dataset.panel];
      const v = def.panel(makeInstance(def.id), env);
      const fly = h('div', { class: 'flyout mini-panel' }, v.el);
      st.append(fly);
      mount(v);
      m.views.push(v);
    }
  }
  const io = new IntersectionObserver(
    (entries) => {
      let any = false;
      entries.forEach((en) => {
        const rec = minis.get(en.target as HTMLElement);
        if (!rec) return;
        rec.m.setPaused(!en.isIntersecting);
        clearInterval(rec.timer);
        if (en.isIntersecting && rec.cycle) rec.timer = window.setInterval(rec.cycle.tick, rec.cycle.interval);
      });
      minis.forEach((_, el) => {
        const r = el.getBoundingClientRect();
        if (r.bottom > 0 && r.top < innerHeight) any = true;
      });
      setVisible('minis', any);
    },
    { rootMargin: '80px' },
  );
  stages.forEach((s) => io.observe(s));
}
