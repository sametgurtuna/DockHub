import { h, widgets, widgetById, makeInstance, track, untrack, albumArt, type Instance, type View, type Env, type Category } from './widgets';
import { mount, unmount, refreshAll } from './views';
import { icons, windowsLogo, dockLogo } from '../lib/icons';
import { appIcons, appNames, type AppId } from '../lib/apps';
import { onSecond, setVisible, pad, sim, tracks } from './sim';

type Edge = 'bottom' | 'top' | 'left' | 'right';
type Item =
  | { key: string; kind: 'app'; app: AppId }
  | { key: string; kind: 'sep' }
  | { key: string; kind: 'widget'; inst: Instance };

type Entry =
  | 'sep'
  | { head: string }
  | { label: string; icon?: keyof typeof icons; run?: () => void; checked?: boolean; danger?: boolean; sub?: Entry[] };

type WinKey = AppId | 'dock-settings';

const PRIMARY_ON_CARD = new Set(['hydration', 'stopwatch']);
const reduced = () => matchMedia('(prefers-reduced-motion: reduce)').matches || document.documentElement.classList.contains('static');

let keySeq = 0;
const nextKey = () => `k${++keySeq}`;

export function initDemo() {
  const stage = document.querySelector<HTMLElement>('[data-stage]');
  if (!stage || stage.dataset.ready) return;
  stage.dataset.ready = '1';

  const $ = <T extends HTMLElement>(sel: string) => stage.querySelector<T>(sel)!;
  const dock = $('[data-dock]');
  const itemsEl = $('[data-dock-items]');
  const scroller = $('[data-dock-scroll]');
  const center = dock.querySelector<HTMLElement>('.dock-center')!;
  const layer = $('[data-layer]');
  const toasts = $('[data-toasts]');
  const winLayer = $('[data-windows]');
  const bar = document.querySelector<HTMLElement>('[data-demo-bar]');

  const state = {
    edge: 'bottom' as Edge,
    layout: 'floating',
    theme: 'dark',
    size: stage.dataset.size || 'small',
    mode: 'replace',
    backdrop: 'blur',
    autohide: false,
    fullscreenHide: true,
  };

  const env: Env = { refreshAll, toast: (t, b) => toast(t, b) };

  const w = (id: string, variant?: string): Item => {
    const inst = makeInstance(id, variant);
    track(inst, env);
    return { key: nextKey(), kind: 'widget', inst };
  };
  const app = (id: AppId): Item => ({ key: nextKey(), kind: 'app', app: id });

  let items: Item[] = [
    app('explorer'),
    app('browser'),
    app('terminal'),
    app('notepad'),
    { key: nextKey(), kind: 'sep' },
    w('clock', 'analog'),
    w('media', 'full'),
    w('weather', 'current'),
    w('system', 'rings'),
    w('hydration', 'timer'),
    w('notes'),
  ];

  const vertical = () => state.edge === 'left' || state.edge === 'right';

  /* ------------------------------------------------------------ items */

  interface Rec {
    el: HTMLElement;
    btn: HTMLElement;
    view?: View;
    mode?: 'card' | 'compact';
  }
  const recs = new Map<string, Rec>();

  function buildWidget(rec: Rec, it: Extract<Item, { kind: 'widget' }>, mode: 'card' | 'compact') {
    unmount(rec.view);
    const def = widgetById[it.inst.id];
    const view = mode === 'card' ? def.card(it.inst, env) : def.compact(it.inst, env);
    rec.btn.replaceChildren(view.el);
    rec.view = mount(view);
    rec.mode = mode;
  }

  function createRec(it: Item): Rec {
    if (it.kind === 'sep') {
      const el = h('div', { class: 'dock-item dock-sep', role: 'separator', 'data-key': it.key });
      return { el, btn: el };
    }
    if (it.kind === 'app') {
      const btn = h(
        'button',
        { class: 'db app-btn', type: 'button', 'aria-label': appNames[it.app], title: appNames[it.app], 'data-app': it.app },
        h('span', { class: 'app-ico', html: appIcons[it.app] }),
      );
      btn.addEventListener('click', () => toggleApp(it.app, btn));
      btn.addEventListener('contextmenu', (e) => {
        e.preventDefault();
        e.stopPropagation();
        appMenu(it, e);
      });
      return { el: h('div', { class: 'dock-item', 'data-key': it.key }, btn), btn };
    }
    const def = widgetById[it.inst.id];
    const btn = h('div', { class: 'widget-btn', role: 'button', tabindex: '0', 'aria-label': `${def.name} widget'ı`, 'data-widget': def.id });
    const rec: Rec = { el: h('div', { class: 'dock-item', 'data-key': it.key }, btn), btn };
    const activate = () => {
      const compact = rec.mode === 'compact';
      if (def.primary && (compact || PRIMARY_ON_CARD.has(def.id))) def.primary(it.inst, env);
      else openWidgetPanel(it, btn);
    };
    btn.addEventListener('click', (e) => {
      if ((e.target as HTMLElement).closest('.mc-btn')) return;
      activate();
    });
    btn.addEventListener('keydown', (e) => {
      if (e.key === 'Enter' || e.key === ' ') {
        e.preventDefault();
        activate();
      }
    });
    btn.addEventListener('contextmenu', (e) => {
      e.preventDefault();
      e.stopPropagation();
      widgetMenu(it, btn, e);
    });
    return rec;
  }

  function render(newKey?: string) {
    const mode = vertical() ? 'compact' : 'card';
    const nodes: HTMLElement[] = [];
    items.forEach((it, idx) => {
      let rec = recs.get(it.key);
      if (!rec) {
        rec = createRec(it);
        recs.set(it.key, rec);
      }
      if (it.kind === 'widget' && rec.mode !== mode) buildWidget(rec, it, mode);
      rec.el.style.setProperty('--i', String(idx + 2));
      rec.el.classList.toggle('is-new', it.key === newKey);
      nodes.push(rec.el);
    });
    recs.forEach((rec, key) => {
      if (!items.some((i) => i.key === key)) {
        unmount(rec.view);
        rec.el.remove();
        recs.delete(key);
      }
    });
    itemsEl.replaceChildren(...nodes);
    updateRunning();
    requestAnimationFrame(updateFades);
  }

  function removeItem(key: string) {
    const rec = recs.get(key);
    const it = items.find((i) => i.key === key);
    if (it?.kind === 'widget') untrack(it.inst);
    const done = () => {
      items = items.filter((i) => i.key !== key);
      render();
    };
    if (!rec || reduced()) return done();
    rec.el.classList.add('is-leaving');
    setTimeout(done, 220);
  }

  function insertIndex() {
    return items.length;
  }

  function addWidget(id: string, variant?: string) {
    const it = w(id, variant);
    items.splice(insertIndex(), 0, it);
    render(it.key);
    const rec = recs.get(it.key)!;
    requestAnimationFrame(() => rec.el.scrollIntoView({ behavior: reduced() ? 'auto' : 'smooth', block: 'nearest', inline: 'nearest' }));
    setTimeout(() => rec.el.classList.remove('is-new'), 700);
    if (stage!.dataset.phase === 'exited') startDock();
    return it;
  }

  function pinApp(id: AppId) {
    if (items.some((i) => i.kind === 'app' && i.app === id)) {
      toast('Zaten sabitli', `${appNames[id]} dock'ta duruyor.`);
      return;
    }
    const lastApp = items.map((i) => i.kind).lastIndexOf('app');
    const it = app(id);
    items.splice(lastApp + 1, 0, it);
    render(it.key);
    setTimeout(() => recs.get(it.key)?.el.classList.remove('is-new'), 700);
    toast('Custom Dock\'a sabitlendi', `${appNames[id]} sabitlenmiş uygulamaların sonuna eklendi.`);
  }

  /* ----------------------------------------------------------- fades */

  function updateFades() {
    const v = vertical();
    const pos = v ? scroller.scrollTop : scroller.scrollLeft;
    const max = v ? scroller.scrollHeight - scroller.clientHeight : scroller.scrollWidth - scroller.clientWidth;
    center.classList.toggle('fade-start', pos > 2);
    center.classList.toggle('fade-end', pos < max - 2);
  }
  scroller.addEventListener('scroll', updateFades, { passive: true });
  new ResizeObserver(updateFades).observe(scroller);
  new ResizeObserver(updateFades).observe(itemsEl);
  center.querySelectorAll<HTMLElement>('[data-scroll]').forEach((b) =>
    b.addEventListener('click', () => {
      const dir = Number(b.dataset.scroll);
      const amount = (vertical() ? scroller.clientHeight : scroller.clientWidth) * 0.6 * dir;
      scroller.scrollBy(vertical() ? { top: amount } : { left: amount });
    }),
  );
  scroller.addEventListener(
    'wheel',
    (e) => {
      if (vertical()) return;
      const max = scroller.scrollWidth - scroller.clientWidth;
      if (max <= 0 || Math.abs(e.deltaX) > Math.abs(e.deltaY)) return;
      e.preventDefault();
      scroller.scrollBy({ left: e.deltaY });
    },
    { passive: false },
  );

  /* ------------------------------------------------------------ drag */

  let suppressClick = false;
  itemsEl.addEventListener(
    'click',
    (e) => {
      if (suppressClick) {
        e.stopPropagation();
        e.preventDefault();
        suppressClick = false;
      }
    },
    true,
  );

  itemsEl.addEventListener('pointerdown', (e) => {
    if (e.button !== 0) return;
    const target = e.target as HTMLElement;
    const dragEl = target.closest<HTMLElement>('.dock-item');
    if (!dragEl || target.closest('.mc-btn')) return;
    const axisX = !vertical();
    const pos = (el: HTMLElement) => (axisX ? el.offsetLeft + el.offsetWidth / 2 : el.offsetTop + el.offsetHeight / 2);
    const pointer = (ev: PointerEvent) => (axisX ? ev.clientX : ev.clientY);
    const scrollPos = () => (axisX ? scroller.scrollLeft : scroller.scrollTop);
    const start = pointer(e);
    const startScroll = scrollPos();
    let startCenter = 0;
    let dragging = false;
    let longPress = 0;
    const touch = e.pointerType !== 'mouse';
    const pointerId = e.pointerId;

    const begin = () => {
      dragging = true;
      startCenter = pos(dragEl);
      dragEl.classList.add('is-dragging');
      closeFlyout();
      try {
        itemsEl.setPointerCapture(pointerId);
      } catch {
        /* pointer may be gone */
      }
      if (touch && navigator.vibrate) navigator.vibrate(8);
    };
    if (touch) longPress = window.setTimeout(begin, 380);

    const move = (ev: PointerEvent) => {
      if (ev.pointerId !== pointerId) return;
      const delta = pointer(ev) - start;
      if (!dragging) {
        if (touch) {
          if (Math.abs(delta) > 8) cleanup();
          return;
        }
        if (Math.abs(delta) < 6) return;
        begin();
      }
      ev.preventDefault();
      const desired = startCenter + delta + (scrollPos() - startScroll);
      const kids = [...itemsEl.children] as HTMLElement[];
      const others = kids.filter((k) => k !== dragEl);
      let idx = others.findIndex((k) => pos(k) > desired);
      if (idx < 0) idx = others.length;
      if (kids.indexOf(dragEl) !== idx) {
        const before = new Map(others.map((k) => [k, pos(k)]));
        itemsEl.insertBefore(dragEl, others[idx] ?? null);
        others.forEach((k) => {
          const d = before.get(k)! - pos(k);
          if (d) k.animate([{ transform: axisX ? `translateX(${d}px)` : `translateY(${d}px)` }, { transform: 'none' }], { duration: 220, easing: 'cubic-bezier(0.1,0.9,0.2,1)' });
        });
      }
      const off = desired - pos(dragEl);
      dragEl.style.transform = axisX ? `translateX(${off}px)` : `translateY(${off}px)`;
    };

    const cleanup = () => {
      clearTimeout(longPress);
      window.removeEventListener('pointermove', move);
      window.removeEventListener('pointerup', up);
      window.removeEventListener('pointercancel', up);
    };

    const up = (ev: PointerEvent) => {
      if (ev.pointerId !== pointerId) return;
      cleanup();
      if (!dragging) return;
      suppressClick = true;
      setTimeout(() => (suppressClick = false), 50);
      const t = dragEl.style.transform;
      dragEl.style.transform = '';
      dragEl.classList.remove('is-dragging');
      if (t) dragEl.animate([{ transform: t }, { transform: 'none' }], { duration: 250, easing: 'cubic-bezier(0.1,0.9,0.2,1)' });
      const order = [...itemsEl.children].map((k) => (k as HTMLElement).dataset.key);
      items = order.map((k) => items.find((i) => i.key === k)!).filter(Boolean);
      items.forEach((it, i) => recs.get(it.key)?.el.style.setProperty('--i', String(i + 2)));
      evalHide();
    };

    window.addEventListener('pointermove', move, { passive: false });
    window.addEventListener('pointerup', up);
    window.addEventListener('pointercancel', up);
  });

  /* --------------------------------------------------------- flyouts */

  interface Fly {
    el: HTMLElement;
    kind: string;
    anchor: HTMLElement | null;
    views: View[];
    onClose?: () => void;
  }
  let fly: Fly | null = null;
  let sub: HTMLElement | null = null;

  function stageScale() {
    const r = stage!.getBoundingClientRect();
    return { r, s: r.width / stage!.offsetWidth || 1 };
  }

  function place(el: HTMLElement, anchor: HTMLElement | null, point?: { x: number; y: number }, side?: Edge) {
    const { r, s } = stageScale();
    const sw = stage!.offsetWidth;
    const sh = stage!.offsetHeight;
    const fw = el.offsetWidth;
    const fh = el.offsetHeight;
    const gap = 10;
    let left: number;
    let top: number;
    let origin = '50% 100%';
    let fx = '0px';
    let fy = '12px';
    if (point) {
      left = point.x;
      top = point.y;
      if (left + fw > sw - 8) left = Math.max(8, left - fw);
      if (top + fh > sh - 8) top = Math.max(8, top - fh);
      origin = 'top left';
      fy = '-4px';
    } else if (anchor) {
      const a = anchor.getBoundingClientRect();
      const ax = (a.left - r.left) / s;
      const ay = (a.top - r.top) / s;
      const aw = a.width / s;
      const ah = a.height / s;
      const e = side ?? state.edge;
      if (e === 'bottom') {
        left = ax + aw / 2 - fw / 2;
        top = ay - fh - gap;
      } else if (e === 'top') {
        left = ax + aw / 2 - fw / 2;
        top = ay + ah + gap;
        origin = '50% 0%';
        fy = '-12px';
      } else if (e === 'left') {
        left = ax + aw + gap;
        top = ay + ah / 2 - fh / 2;
        origin = '0% 50%';
        fx = '-12px';
        fy = '0px';
      } else {
        left = ax - fw - gap;
        top = ay + ah / 2 - fh / 2;
        origin = '100% 50%';
        fx = '12px';
        fy = '0px';
      }
    } else {
      const d = dock.getBoundingClientRect();
      const dl = (d.left - r.left) / s;
      const dt = (d.top - r.top) / s;
      left = sw / 2 - fw / 2;
      top = sh / 2 - fh / 2;
      if (state.edge === 'bottom') top = dt - fh - 12;
      else if (state.edge === 'top') top = dt + d.height / s + 12;
      else if (state.edge === 'left') left = dl + d.width / s + 12;
      else left = dl - fw - 12;
    }
    left = Math.min(Math.max(10, left), sw - fw - 10);
    top = Math.min(Math.max(10, top), sh - fh - 10);
    el.style.left = `${left}px`;
    el.style.top = `${top}px`;
    el.style.setProperty('--origin', origin);
    el.style.setProperty('--fx', fx);
    el.style.setProperty('--fy', fy);
  }

  function openFlyout(content: HTMLElement, opts: { kind: string; anchor?: HTMLElement | null; views?: View[]; cls?: string; point?: { x: number; y: number }; onClose?: () => void; focus?: string }) {
    const anchor = opts.anchor ?? null;
    if (fly && fly.kind === opts.kind && fly.anchor === anchor && !opts.point) {
      closeFlyout();
      return null;
    }
    closeFlyout(true);
    const el = h('div', { class: `flyout ${opts.cls ?? ''}`, role: 'dialog' }, content);
    el.style.visibility = 'hidden';
    layer.append(el);
    place(el, anchor, opts.point);
    el.style.visibility = '';
    anchor?.classList.add('is-open');
    fly = { el, kind: opts.kind, anchor, views: opts.views ?? [], onClose: opts.onClose };
    fly.views.forEach(mount);
    const f = opts.focus ? el.querySelector<HTMLElement>(opts.focus) : null;
    if (f) f.focus({ preventScroll: true });
    evalHide();
    return el;
  }

  function closeFlyout(immediate = false) {
    closeSub();
    if (!fly) return;
    const f = fly;
    fly = null;
    f.views.forEach(unmount);
    f.anchor?.classList.remove('is-open');
    f.onClose?.();
    if (immediate || reduced()) f.el.remove();
    else {
      f.el.classList.add('is-closing');
      setTimeout(() => f.el.remove(), 150);
    }
    evalHide();
  }

  function closeSub() {
    sub?.remove();
    sub = null;
    layer.querySelectorAll('.menu-item.is-open').forEach((x) => x.classList.remove('is-open'));
  }

  stage.addEventListener('pointerdown', (e) => {
    const t = e.target as HTMLElement;
    if (!fly) return;
    if (t.closest('.flyout') || (fly.anchor && fly.anchor.contains(t))) return;
    closeFlyout();
  });
  document.addEventListener('pointerdown', (e) => {
    if (fly && !stage!.contains(e.target as Node) && !(e.target as HTMLElement).closest('[data-demo-bar]')) closeFlyout();
  });
  stage.addEventListener('keydown', (e) => {
    if (e.key === 'Escape' && fly) {
      const a = fly.anchor;
      closeFlyout();
      a?.focus({ preventScroll: true });
    }
  });

  /* ------------------------------------------------------------ menus */

  function menuEl(entries: Entry[], isSub = false): HTMLElement {
    const m = h('div', { class: 'menu', role: 'menu' });
    for (const en of entries) {
      if (en === 'sep') {
        m.append(h('div', { class: 'menu-sep', role: 'separator' }));
        continue;
      }
      if ('head' in en) {
        m.append(h('div', { class: 'menu-head', text: en.head }));
        continue;
      }
      const b = h(
        'button',
        {
          class: `menu-item ${en.danger ? 'is-danger' : ''}`,
          type: 'button',
          role: en.checked === undefined ? 'menuitem' : 'menuitemradio',
          'aria-checked': en.checked === undefined ? null : String(en.checked),
          'aria-haspopup': en.sub ? 'menu' : null,
        },
        h('span', { class: 'mi-ic', html: en.icon ? icons[en.icon] : '' }),
        h('span', { class: 'mi-label', text: en.label }),
        en.sub ? h('span', { class: 'mi-end', html: icons.chevronRight }) : null,
      );
      if (en.sub) {
        const openSub = () => {
          if (b.classList.contains('is-open')) return;
          closeSub();
          b.classList.add('is-open');
          const sm = menuEl(en.sub!, true);
          const wrap = h('div', { class: 'flyout menu-flyout' }, sm);
          wrap.style.visibility = 'hidden';
          layer.append(wrap);
          const { r, s } = stageScale();
          const br = b.getBoundingClientRect();
          let x = (br.right - r.left) / s + 2;
          if (x + wrap.offsetWidth > stage!.offsetWidth - 8) x = (br.left - r.left) / s - wrap.offsetWidth - 2;
          const y = Math.min((br.top - r.top) / s - 4, stage!.offsetHeight - wrap.offsetHeight - 8);
          wrap.style.left = `${Math.max(8, x)}px`;
          wrap.style.top = `${Math.max(8, y)}px`;
          wrap.style.setProperty('--fy', '0px');
          wrap.style.setProperty('--fx', '-6px');
          wrap.style.visibility = '';
          sub = wrap;
        };
        b.addEventListener('pointerenter', openSub);
        b.addEventListener('click', (e) => {
          e.stopPropagation();
          openSub();
          sub?.querySelector<HTMLElement>('.menu-item')?.focus();
        });
        b.addEventListener('keydown', (e) => {
          if (e.key === 'ArrowRight') {
            openSub();
            sub?.querySelector<HTMLElement>('.menu-item')?.focus();
          }
        });
      } else {
        if (!isSub) b.addEventListener('pointerenter', closeSub);
        b.addEventListener('click', () => {
          closeFlyout();
          en.run?.();
        });
      }
      m.append(b);
    }
    m.addEventListener('keydown', (e) => {
      const list = [...m.querySelectorAll<HTMLElement>(':scope > .menu-item')];
      const i = list.indexOf(document.activeElement as HTMLElement);
      if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
        e.preventDefault();
        const n = (i + (e.key === 'ArrowDown' ? 1 : -1) + list.length) % list.length;
        list[n]?.focus();
      } else if (e.key === 'ArrowLeft' && isSub) {
        const opener = layer.querySelector<HTMLElement>('.menu-item.is-open');
        closeSub();
        opener?.focus();
      }
    });
    return m;
  }

  function openMenu(entries: Entry[], e: MouseEvent | null, anchor?: HTMLElement) {
    let point: { x: number; y: number } | undefined;
    if (e && e.clientX) {
      const { r, s } = stageScale();
      point = { x: (e.clientX - r.left) / s, y: (e.clientY - r.top) / s };
    }
    const el = openFlyout(menuEl(entries), { kind: 'menu', anchor: point ? null : anchor, point, cls: 'menu-flyout' });
    el?.querySelector<HTMLElement>('.menu-item')?.focus({ preventScroll: true });
  }

  function variantsEntry(it: Extract<Item, { kind: 'widget' }>): Entry | null {
    const def = widgetById[it.inst.id];
    if (def.variants.length < 2) return null;
    return {
      label: 'Görünüm',
      icon: 'widgets',
      sub: def.variants.map((v) => ({
        label: v.name,
        checked: it.inst.variant === v.id,
        run: () => {
          it.inst.variant = v.id;
          const rec = recs.get(it.key)!;
          rec.mode = undefined;
          render();
          rec.el.classList.add('is-new');
          setTimeout(() => rec.el.classList.remove('is-new'), 600);
        },
      })),
    };
  }

  function widgetMenu(it: Extract<Item, { kind: 'widget' }>, btn: HTMLElement, e: MouseEvent | null) {
    const def = widgetById[it.inst.id];
    const entries: Entry[] = [{ head: def.name }];
    const v = variantsEntry(it);
    if (v) entries.push(v);
    entries.push({ label: "Widget ayarları…", icon: 'settings', run: () => openWidgetPanel(it, btn) });
    if (def.id === 'stopwatch')
      entries.push({
        label: 'Sıfırla',
        icon: 'restore',
        run: () => {
          it.inst.state.elapsed = 0;
          it.inst.state.running = false;
          refreshAll();
        },
      });
    entries.push('sep', { label: 'Dock\'tan kaldır', icon: 'close', danger: true, run: () => removeItem(it.key) });
    openMenu(entries, e, btn);
  }

  function appMenu(it: Extract<Item, { kind: 'app' }>, e: MouseEvent | null) {
    const open = wins.get(it.app);
    const btn = recs.get(it.key)?.btn;
    const entries: Entry[] = [
      { head: appNames[it.app] },
      { label: open ? 'Pencereye geç' : 'Aç', icon: 'external', run: () => openApp(it.app) },
      { label: 'Yönetici olarak çalıştır', icon: 'shield', run: () => { openApp(it.app); toast('Yönetici olarak çalıştırıldı', `${appNames[it.app]} yükseltilmiş izinle açıldı. Custom Dock'un kendisi yönetici izni istemez.`); } },
      { label: 'Dosya konumunu aç', icon: 'pin', run: () => openApp('explorer') },
      'sep',
      { label: 'Custom Dock\'tan kaldır', icon: 'close', run: () => removeItem(it.key) },
    ];
    if (open) entries.push({ label: 'Tüm pencereleri kapat', icon: 'close', danger: true, run: () => closeWin(it.app) });
    openMenu(entries, e, btn);
  }

  function dockMenu(e: MouseEvent | null) {
    const pick = <T extends string>(label: string, icon: keyof typeof icons, key: 'edge' | 'size' | 'backdrop', opts: [T, string][]): Entry => ({
      label,
      icon,
      sub: opts.map(([v, l]) => ({ label: l, checked: state[key] === v, run: () => set(key, v) })),
    });
    openMenu(
      [
        { label: 'Widget ekle', icon: 'widgets', run: () => openGallery() },
        { label: 'Uygulama sabitle', icon: 'pin', run: () => openApp('explorer') },
        {
          label: 'Ayraç ekle',
          icon: 'minus',
          run: () => {
            const it: Item = { key: nextKey(), kind: 'sep' };
            items.push(it);
            render(it.key);
          },
        },
        'sep',
        { label: 'Otomatik gizle', icon: 'autohide', checked: state.autohide, run: () => set('autohide', !state.autohide) },
        pick('Konum', 'monitor', 'edge', [
          ['bottom', 'Alt'],
          ['top', 'Üst'],
          ['left', 'Sol'],
          ['right', 'Sağ'],
        ]),
        pick('Boyut', 'grip', 'size', [
          ['small', 'Küçük (48)'],
          ['medium', 'Orta (56)'],
          ['large', 'Büyük (66)'],
        ]),
        pick('Arka plan', 'sparkle', 'backdrop', [
          ['blur', 'Bulanık cam'],
          ['acrylic', 'Acrylic'],
          ['solid', 'Düz'],
        ]),
        'sep',
        { label: 'Ayarlar', icon: 'settings', run: () => openApp('dock-settings') },
        { label: 'Çıkış', icon: 'power', danger: true, run: () => exitDock() },
      ],
      e,
    );
  }

  dock.addEventListener('contextmenu', (e) => {
    if ((e.target as HTMLElement).closest('.dock-item, [data-start]')) return;
    e.preventDefault();
    dockMenu(e);
  });

  $('[data-start]').addEventListener('contextmenu', (e) => {
    e.preventDefault();
    openMenu(
      [
        { label: 'Terminal', icon: 'terminal', run: () => openApp('terminal') },
        { label: 'Dosya Gezgini', icon: 'monitor', run: () => openApp('explorer') },
        { label: 'Ayarlar', icon: 'settings', run: () => openApp('dock-settings') },
        { label: 'Masaüstü', icon: 'monitor', run: () => showDesktop() },
        'sep',
        { label: 'Kapat veya oturumu kapat', icon: 'power', sub: [{ label: 'Oturumu kapat', run: () => toast('Oturum kapatılıyor', 'Custom Dock, oturum kapanmadan önce Windows görev çubuğunu geri getirir.') }] },
      ],
      e,
    );
  });

  /* ------------------------------------------------------ widget panel */

  function openWidgetPanel(it: Extract<Item, { kind: 'widget' }>, anchor: HTMLElement) {
    const def = widgetById[it.inst.id];
    const view = def.panel(it.inst, env);
    openFlyout(view.el, { kind: `w-${it.key}`, anchor, views: [view], cls: def.id === 'notes' ? 'note-fly' : '', focus: def.id === 'notes' ? 'textarea' : undefined });
  }

  /* ------------------------------------------------------------ start */

  function startMenu(anchor: HTMLElement, focusSearch = false) {
    const pinned: [AppId | 'dock-settings', string, string][] = [
      ['explorer', appNames.explorer, appIcons.explorer],
      ['browser', appNames.browser, appIcons.browser],
      ['terminal', appNames.terminal, appIcons.terminal],
      ['notepad', appNames.notepad, appIcons.notepad],
      ['music', appNames.music, appIcons.music],
      ['photos', appNames.photos, appIcons.photos],
      ['calculator', appNames.calculator, appIcons.calculator],
      ['dock-settings', 'Custom Dock', dockLogo('dl-start')],
      ['settings', appNames.settings, appIcons.settings],
      ['pc', appNames.pc, appIcons.pc],
      ['bin', 'Geri Dönüşüm', appIcons.bin],
    ];
    const grid = h(
      'div',
      { class: 'start-grid' },
      ...pinned.map(([id, name, ic]) =>
        h('button', { class: 'start-app', type: 'button', onclick: () => { closeFlyout(); openApp(id); } }, h('span', { html: ic }), h('span', { text: name })),
      ),
    );
    const input = h('input', { type: 'search', placeholder: 'Uygulama, ayar ve belge arayın', 'aria-label': 'Ara' }) as HTMLInputElement;
    input.addEventListener('input', () => {
      const q = input.value.toLocaleLowerCase('tr-TR');
      grid.querySelectorAll<HTMLElement>('.start-app').forEach((b) => (b.hidden = !!q && !b.textContent!.toLocaleLowerCase('tr-TR').includes(q)));
    });
    input.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') grid.querySelector<HTMLElement>('.start-app:not([hidden])')?.click();
    });
    const rec = (ic: string, title: string, subText: string, run: () => void) =>
      h('button', { type: 'button', onclick: () => { closeFlyout(); run(); } }, h('span', { class: 'rec-ic', html: ic }), h('span', {}, title, h('small', { text: subText })));
    const content = h(
      'div',
      { class: 'start' },
      h(
        'div',
        { class: 'start-body' },
        h('label', { class: 'start-search' }, h('span', { html: icons.search }), input),
        h('div', { class: 'start-head' }, 'Sabitlenmiş', h('span', { text: 'Tüm uygulamalar' })),
        grid,
        h('div', { class: 'start-head' }, 'Önerilen'),
        h(
          'div',
          { class: 'start-rec' },
          rec(dockLogo('dl-rec'), 'Custom Dock', 'Yeni eklendi', () => openApp('dock-settings')),
          rec(appIcons.notepad, 'config.json', '%AppData%\\CustomDock', () => openApp('notepad')),
          rec(appIcons.terminal, 'Görev çubuğunu geri getir', 'CustomDock.exe --restore-taskbar', () => openApp('terminal')),
          rec(appIcons.photos, 'Masaüstü düzeni.png', 'Resimler', () => openApp('photos')),
        ),
      ),
      h(
        'div',
        { class: 'start-foot' },
        h('span', { class: 'start-user' }, h('span', { class: 'avatar', html: icons.user }), 'Kullanıcı'),
        h('button', { class: 'start-power', type: 'button', 'aria-label': 'Güç', html: icons.power, onclick: () => { closeFlyout(); exitDock(); } }),
      ),
    );
    openFlyout(content, { kind: 'start', anchor, cls: 'start-fly', focus: focusSearch ? 'input' : undefined });
  }

  $('[data-start]').addEventListener('click', (e) => startMenu(e.currentTarget as HTMLElement));
  $('[data-search]').addEventListener('click', (e) => startMenu(e.currentTarget as HTMLElement, true));

  /* ------------------------------------------------------------- tray */

  $('[data-tray-more]').addEventListener('click', (e) => {
    const trayApps: [AppId, string][] = [
      ['music', 'Medya Oynatıcı: çalıyor'],
      ['photos', 'Fotoğraflar: eşitleniyor'],
      ['calculator', 'Hesap Makinesi'],
    ];
    const grid = h(
      'div',
      { class: 'tray-grid' },
      h('button', { type: 'button', title: 'Custom Dock', 'aria-label': 'Custom Dock', onclick: (ev: MouseEvent) => dockMenu(ev) }, h('span', { html: dockLogo('dl-tray') })),
      ...trayApps.map(([id, t]) => h('button', { type: 'button', title: t, 'aria-label': t, onclick: () => { closeFlyout(); openApp(id); } }, h('span', { html: appIcons[id] }))),
    );
    openFlyout(grid, { kind: 'tray', anchor: e.currentTarget as HTMLElement });
  });

  $('[data-quick]').addEventListener('click', (e) => {
    const tile = (ic: keyof typeof icons, label: string, on: boolean) => {
      const b = h('button', { class: `qt ${on ? 'on' : ''}`, type: 'button', 'aria-pressed': String(on) }, h('span', { html: icons[ic] }), label);
      b.addEventListener('click', () => {
        const v = !b.classList.contains('on');
        b.classList.toggle('on', v);
        b.setAttribute('aria-pressed', String(v));
      });
      return b;
    };
    const batt = h('span', { class: 'num' });
    const view: View = { el: batt, frame: () => (batt.textContent = `%${Math.round(sim.battery.value)}`) };
    const content = h(
      'div',
      { class: 'quick' },
      h('div', { class: 'quick-tiles' }, tile('wifi', 'Wi-Fi', true), tile('moon', 'Odak', false), tile('battery', 'Enerji tasarrufu', false), tile('sun', 'Gece ışığı', false), tile('shield', 'Güvenlik', true), tile('monitor', 'Yansıt', false)),
      h('label', { class: 'quick-slider' }, h('span', { html: icons.sun }), h('input', { type: 'range', min: '0', max: '100', value: '70', 'aria-label': 'Parlaklık' })),
      h('label', { class: 'quick-slider' }, h('span', { html: icons.volume }), h('input', { type: 'range', min: '0', max: '100', value: '45', 'aria-label': 'Ses' })),
      h('div', { class: 'quick-foot' }, h('span', {}, h('span', { html: icons.battery }), batt), h('button', { class: 'gallery-close', type: 'button', 'aria-label': 'Ayarlar', html: icons.settings, onclick: () => { closeFlyout(); openApp('dock-settings'); } })),
    );
    openFlyout(content, { kind: 'quick', anchor: e.currentTarget as HTMLElement, views: [view] });
  });

  const clockBtn = $('[data-clock]');
  clockBtn.addEventListener('click', () => {
    const cal = widgetById.clock.panel(makeInstance('clock'), env);
    const content = h(
      'div',
      { class: 'notif' },
      h('div', { class: 'notif-top' }, h('div', { class: 'start-head' }, 'Bildirimler', h('span', { text: 'Rahatsız etme' })), h('div', { class: 'wp-muted', text: 'Yeni bildirim yok' })),
      cal.el,
    );
    openFlyout(content, { kind: 'clock', anchor: clockBtn, views: [cal] });
  });
  clockBtn.addEventListener('contextmenu', (e) => {
    e.preventDefault();
    e.stopPropagation();
    openMenu(
      [
        { label: 'Hızlı ayarlar', icon: 'settings', run: () => $<HTMLElement>('[data-quick]').click() },
        { label: 'Tarih ve saati ayarla', icon: 'monitor', run: () => openApp('dock-settings') },
        'sep',
        { label: 'Bildirim merkezi', icon: 'bell', run: () => clockBtn.click() },
      ],
      e,
    );
  });

  const clockTime = $('[data-clock-time]');
  const clockDate = $('[data-clock-date]');
  const tbClock = stage.querySelector('[data-clock-tb]');
  const tickClock = (d: Date) => {
    const t = `${pad(d.getHours())}:${pad(d.getMinutes())}`;
    const ds = `${pad(d.getDate())}.${pad(d.getMonth() + 1)}.${d.getFullYear()}`;
    clockTime.textContent = t;
    clockDate.textContent = ds;
    if (tbClock) tbClock.innerHTML = `<span>${t}</span><span>${ds}</span>`;
  };
  tickClock(new Date());
  onSecond(tickClock);

  $('[data-show-desktop]').addEventListener('click', showDesktop);

  /* ---------------------------------------------------------- gallery */

  function openGallery(anchor: HTMLElement | null = null) {
    const cats: (Category | 'Tümü')[] = ['Tümü', 'Saatler', 'Hatırlatıcılar', 'Notlar', 'Medya', 'Sistem', 'Hava durumu'];
    let cat: string = 'Tümü';
    const list = h('div', { class: 'gallery-list' });
    const tabs = h('div', { class: 'gallery-tabs', role: 'tablist' });
    const draw = () => {
      tabs.querySelectorAll('button').forEach((b) => b.setAttribute('aria-selected', String(b.dataset.cat === cat)));
      list.replaceChildren(
        ...widgets
          .filter((d) => cat === 'Tümü' || d.category === cat)
          .map((d) => {
            const sel = d.variants.length > 1 ? (h('select', { 'aria-label': `${d.name} görünümü` }, ...d.variants.map((v) => h('option', { value: v.id, text: v.name }))) as HTMLSelectElement) : null;
            const add = h('button', { class: 'add-btn', type: 'button', 'aria-label': `${d.name} ekle`, html: icons.plus });
            add.addEventListener('click', () => {
              addWidget(d.id, sel?.value);
              add.innerHTML = icons.check;
              add.classList.add('is-done');
              setTimeout(() => {
                add.innerHTML = icons.plus;
                add.classList.remove('is-done');
              }, 1200);
            });
            return h(
              'div',
              { class: 'gallery-row' },
              h('span', { class: 'gallery-ic', style: `color:${d.accent}`, html: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="${d.icon}"/></svg>` }),
              h('div', {}, h('b', { text: d.name }), h('p', { text: d.description }), sel),
              add,
            );
          }),
      );
    };
    cats.forEach((c) =>
      tabs.append(
        h('button', {
          type: 'button',
          role: 'tab',
          'data-cat': c,
          text: c,
          onclick: () => {
            cat = c;
            draw();
          },
        }),
      ),
    );
    draw();
    const content = h(
      'div',
      { class: 'gallery' },
      h('div', { class: 'gallery-head' }, 'Widget galerisi', h('button', { class: 'gallery-close', type: 'button', 'aria-label': 'Kapat', html: icons.close, onclick: () => closeFlyout() })),
      tabs,
      list,
    );
    openFlyout(content, { kind: 'gallery', anchor, cls: 'gallery-fly' });
  }

  /* ---------------------------------------------------------- windows */

  interface Win {
    key: WinKey;
    el: HTMLElement;
    min: boolean;
    views: View[];
  }
  const wins = new Map<WinKey, Win>();
  let activeWin: Win | null = null;
  let zTop = 1;
  let cascade = 0;

  const winSizes: Partial<Record<WinKey, [number, number]>> = {
    explorer: [640, 400],
    pc: [640, 400],
    bin: [520, 320],
    browser: [640, 420],
    terminal: [600, 340],
    notepad: [520, 360],
    music: [320, 470],
    photos: [560, 380],
    calculator: [300, 420],
    'dock-settings': [720, 480],
    settings: [720, 480],
  };

  function dockBtnFor(key: WinKey) {
    const it = items.find((i) => i.kind === 'app' && i.app === key);
    return it ? recs.get(it.key)?.btn : undefined;
  }

  function updateRunning() {
    recs.forEach((rec, key) => {
      const it = items.find((i) => i.key === key);
      if (it?.kind !== 'app') return;
      const wv = wins.get(it.app);
      rec.btn.classList.toggle('is-running', !!wv);
      rec.btn.classList.toggle('is-active', !!wv && !wv.min && activeWin === wv);
    });
  }

  function focusWin(wv: Win) {
    activeWin = wv;
    wv.el.style.zIndex = String(++zTop);
    wins.forEach((o) => o.el.classList.toggle('is-inactive', o !== wv));
    updateRunning();
  }

  function minimizeWin(wv: Win) {
    const b = dockBtnFor(wv.key);
    const { r, s } = stageScale();
    if (b && stage!.dataset.phase === 'ready') {
      const br = b.getBoundingClientRect();
      const tx = (br.left - r.left) / s + br.width / s / 2 - (wv.el.offsetLeft + wv.el.offsetWidth / 2);
      const ty = (br.top - r.top) / s - (wv.el.offsetTop + wv.el.offsetHeight / 2);
      wv.el.style.setProperty('--mx', `${tx}px`);
      wv.el.style.setProperty('--my', `${ty}px`);
    }
    wv.min = true;
    wv.el.classList.add('is-min');
    if (activeWin === wv) activeWin = null;
    updateRunning();
  }

  function restoreWin(wv: Win) {
    wv.min = false;
    wv.el.classList.remove('is-min');
    focusWin(wv);
  }

  function closeWin(key: WinKey) {
    const wv = wins.get(key);
    if (!wv) return;
    wins.delete(key);
    wv.views.forEach(unmount);
    if (activeWin === wv) activeWin = null;
    wv.el.classList.add('is-closing');
    setTimeout(() => wv.el.remove(), 170);
    updateRunning();
  }

  function showDesktop() {
    const open = [...wins.values()].filter((x) => !x.min);
    if (open.length) open.forEach(minimizeWin);
    else wins.forEach(restoreWin);
  }

  function workArea() {
    const sw = stage!.offsetWidth;
    const sh = stage!.offsetHeight;
    const d = dock.offsetHeight;
    const dw = dock.offsetWidth;
    const pad = 12;
    let x = pad;
    let y = pad;
    let wdt = sw - pad * 2;
    let hgt = sh - pad * 2;
    if (state.mode === 'both') hgt -= 48;
    if (state.edge === 'bottom') hgt -= d + 8;
    if (state.edge === 'top') {
      y += d + 8;
      hgt -= d + 8;
    }
    if (state.edge === 'left') {
      x += dw + 8;
      wdt -= dw + 8;
    }
    if (state.edge === 'right') wdt -= dw + 8;
    return { x, y, w: wdt, h: hgt };
  }

  function openApp(key: WinKey) {
    closeFlyout();
    if (key === 'settings') key = 'dock-settings';
    const existing = wins.get(key);
    if (existing) {
      if (existing.min) restoreWin(existing);
      else focusWin(existing);
      return;
    }
    createWin(key);
  }

  function toggleApp(key: AppId, btn: HTMLElement) {
    const wv = wins.get(key);
    closeFlyout();
    if (!wv) {
      btn.classList.remove('is-bouncing');
      void btn.offsetWidth;
      btn.classList.add('is-bouncing');
      setTimeout(() => btn.classList.remove('is-bouncing'), 750);
      createWin(key);
    } else if (wv.min) restoreWin(wv);
    else if (activeWin === wv) minimizeWin(wv);
    else focusWin(wv);
  }

  function createWin(key: WinKey) {
    const area = workArea();
    const [pw, ph] = winSizes[key] ?? [560, 380];
    const ww = Math.min(pw, area.w);
    const wh = Math.min(ph, area.h);
    const off = (cascade++ % 5) * 26;
    const left = area.x + Math.max(0, (area.w - ww) / 2 - 60 + off);
    const top = area.y + Math.max(0, Math.min((area.h - wh) / 2 - 20 + off, area.h - wh));
    const title = key === 'dock-settings' ? 'Custom Dock Ayarları' : appNames[key as AppId];
    const icon = key === 'dock-settings' ? dockLogo(`dl-w${cascade}`) : appIcons[key as AppId];
    const content = winContent(key);
    const bodyEl = h('div', { class: 'win-body' }, content.el);
    const ctrl = (ic: string, label: string, cls: string, fn: () => void) => h('button', { type: 'button', class: cls, 'aria-label': label, html: ic, onclick: (e: Event) => { e.stopPropagation(); fn(); } });
    const barEl = h(
      'div',
      { class: 'win-bar' },
      h('div', { class: 'win-title' }, h('span', { html: icon }), h('span', { text: title })),
      h(
        'div',
        { class: 'win-ctrls' },
        ctrl(icons.minus, 'Simge durumuna küçült', '', () => minimizeWin(wv)),
        ctrl('<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.3"><rect x="6" y="6" width="12" height="12" rx="1.5"/></svg>', 'Ekranı kapla', '', () => maximize()),
        ctrl(icons.close, 'Kapat', 'close', () => closeWin(key)),
      ),
    );
    const el = h('div', { class: 'win', role: 'dialog', 'aria-label': title, style: `left:${left}px;top:${top}px;width:${ww}px;height:${wh}px` }, barEl, bodyEl);
    const wv: Win = { key, el, min: false, views: content.views };
    let maxed: string | null = null;
    const maximize = () => {
      if (maxed) {
        el.setAttribute('style', maxed);
        maxed = null;
      } else {
        maxed = el.getAttribute('style');
        const a = workArea();
        Object.assign(el.style, { left: `${a.x - 4}px`, top: `${a.y - 4}px`, width: `${a.w + 8}px`, height: `${a.h + 8}px` });
      }
    };
    el.addEventListener('pointerdown', () => focusWin(wv));
    barEl.addEventListener('dblclick', maximize);
    barEl.addEventListener('pointerdown', (e) => {
      if ((e.target as HTMLElement).closest('button')) return;
      const { s } = stageScale();
      const sx = e.clientX;
      const sy = e.clientY;
      const ox = el.offsetLeft;
      const oy = el.offsetTop;
      el.classList.add('is-dragging');
      barEl.setPointerCapture(e.pointerId);
      const mv = (ev: PointerEvent) => {
        const nx = Math.min(Math.max(-el.offsetWidth + 80, ox + (ev.clientX - sx) / s), stage!.offsetWidth - 80);
        const ny = Math.min(Math.max(0, oy + (ev.clientY - sy) / s), stage!.offsetHeight - 40);
        el.style.left = `${nx}px`;
        el.style.top = `${ny}px`;
      };
      const upd = () => {
        el.classList.remove('is-dragging');
        barEl.removeEventListener('pointermove', mv);
        barEl.removeEventListener('pointerup', upd);
        barEl.removeEventListener('pointercancel', upd);
      };
      barEl.addEventListener('pointermove', mv);
      barEl.addEventListener('pointerup', upd);
      barEl.addEventListener('pointercancel', upd);
    });
    winLayer.append(el);
    wins.set(key, wv);
    wv.views.forEach(mount);
    focusWin(wv);
    content.after?.();
  }

  function winContent(key: WinKey): { el: HTMLElement; views: View[]; after?: () => void } {
    switch (key) {
      case 'explorer':
      case 'pc':
        return explorerContent();
      case 'bin':
        return { el: h('div', { class: 'br-page' }, h('span', { class: 'logo', html: appIcons.bin }), h('p', { text: 'Geri Dönüşüm Kutusu boş.' })), views: [] };
      case 'browser':
        return {
          el: h(
            'div',
            { class: 'br' },
            h('div', { class: 'br-bar' }, h('div', { class: 'br-url' }, h('span', { html: icons.search }), 'Arayın ya da bir adres yazın')),
            h('div', { class: 'br-page' }, h('span', { class: 'logo', html: dockLogo('dl-br') }), h('h4', { text: 'Yeni sekme' }), h('p', { text: 'Bu pencere demo içinde. Başlıktan tutup sürükleyebilir, dock\'taki simgeye tıklayarak küçültebilirsin.' })),
          ),
          views: [],
        };
      case 'terminal':
        return terminalContent();
      case 'notepad': {
        const ta = h('textarea', { class: 'np', spellcheck: 'false', 'aria-label': 'Not Defteri' }) as HTMLTextAreaElement;
        ta.value = `{\n  "version": 2,\n  "taskbarMode": "Replace",\n  "edge": "${cap(state.edge)}",\n  "theme": "${cap(state.theme)}",\n  "backdrop": "${cap(state.backdrop)}",\n  "size": "${cap(state.size)}",\n  "layout": "${cap(state.layout)}",\n  "autoHide": ${state.autohide},\n  "items": [ … ]\n}\n\n%AppData%\\CustomDock\\config.json`;
        return { el: ta, views: [] };
      }
      case 'music': {
        const v = widgetById.media.panel(makeInstance('media'), env);
        v.el.style.width = '100%';
        return { el: v.el, views: [v] };
      }
      case 'photos':
        return { el: h('div', { class: 'ph' }, ...Array.from({ length: 12 }, (_, i) => h('span', { html: albumArt(i % tracks.length).replace(/id="art(\d)"/, `id="ph${i}"`).replace(/url\(#art\d\)/, `url(#ph${i})`) }))), views: [] };
      case 'calculator':
        return calcContent();
      default:
        return settingsContent();
    }
  }

  const cap = (s: string) => s[0].toUpperCase() + s.slice(1);

  function explorerContent() {
    const files: { name: string; ic: string; pin?: AppId; lnk?: boolean }[] = [
      { name: 'Belgeler', ic: appIcons.explorer },
      { name: 'İndirilenler', ic: appIcons.explorer },
      { name: 'Resimler', ic: appIcons.explorer },
      { name: 'Müzik', ic: appIcons.explorer },
      { name: 'Hesap Makinesi', ic: appIcons.calculator, pin: 'calculator', lnk: true },
      { name: 'Fotoğraflar', ic: appIcons.photos, pin: 'photos', lnk: true },
      { name: 'Medya Oynatıcı', ic: appIcons.music, pin: 'music', lnk: true },
      { name: 'CustomDock.exe', ic: dockLogo('dl-ex') },
    ];
    const grid = h('div', { class: 'ex-grid' });
    files.forEach((f) => {
      const b = h('button', { class: 'ex-file', type: 'button' }, h('span', { html: f.ic }, f.lnk ? h('span', { class: 'lnk', html: icons.arrowUp.replace('M12 19V5M6.5 10.5L12 5l5.5 5.5', 'M8 16L16 8M9.5 8H16v6.5') }) : null), h('span', { text: f.name }));
      const menu = (e: MouseEvent | null) => {
        grid.querySelectorAll('.is-selected').forEach((x) => x.classList.remove('is-selected'));
        b.classList.add('is-selected');
        const entries: Entry[] = [{ head: f.name }, { label: 'Aç', icon: 'external', run: () => (f.pin ? openApp(f.pin) : f.name === 'CustomDock.exe' ? startDock() : undefined) }];
        if (f.pin) entries.push({ label: 'Custom Dock\'a sabitle', icon: 'pin', run: () => pinApp(f.pin!) });
        entries.push('sep', { label: 'Özellikler', icon: 'settings' });
        openMenu(entries, e, b);
      };
      b.addEventListener('contextmenu', (e) => {
        e.preventDefault();
        e.stopPropagation();
        menu(e);
      });
      b.addEventListener('click', () => {
        grid.querySelectorAll('.is-selected').forEach((x) => x.classList.remove('is-selected'));
        b.classList.add('is-selected');
      });
      b.addEventListener('dblclick', () => (f.pin ? openApp(f.pin) : undefined));
      let lp = 0;
      b.addEventListener('pointerdown', (e) => {
        if (e.pointerType !== 'mouse') lp = window.setTimeout(() => menu(null), 450);
      });
      ['pointerup', 'pointercancel', 'pointerleave'].forEach((ev) => b.addEventListener(ev, () => clearTimeout(lp)));
      grid.append(b);
    });
    const side = h(
      'div',
      { class: 'ex-side' },
      ...['Giriş', 'Masaüstü', 'Belgeler', 'İndirilenler', 'Bu bilgisayar'].map((n, i) => h('button', { type: 'button', class: i === 0 ? 'on' : '' }, h('span', { html: i === 4 ? appIcons.pc : appIcons.explorer }), n)),
    );
    return {
      el: h(
        'div',
        { class: 'ex' },
        side,
        h(
          'div',
          { class: 'ex-main' },
          h('div', { class: 'ex-crumb', text: 'Giriş  ›  Hızlı erişim' }),
          h('div', { class: 'ex-tip' }, h('span', { html: icons.pin }), h('span', { text: 'Bir kısayola sağ tıklayıp (dokunmatikte basılı tutup) "Custom Dock\'a sabitle" komutunu dene.' })),
          grid,
        ),
      ),
      views: [],
    };
  }

  function terminalContent() {
    const out = h('div');
    const input = h('input', { type: 'text', 'aria-label': 'Komut', spellcheck: 'false', autocomplete: 'off' }) as HTMLInputElement;
    const prompt = 'PS C:\\Users\\sen> ';
    const term = h('div', { class: 'term' }, out, h('div', { class: 'term-line' }, h('span', { text: prompt }), input));
    const print = (text: string, cls = '') => out.append(h('p', { class: cls, text }));
    print('Windows PowerShell', 't-dim');
    print('Denemek için: CustomDock.exe --exit, CustomDock.exe, --restore-taskbar, --pin calc, yardım', 't-dim');
    print('');
    const history: string[] = [];
    let hi = 0;
    input.addEventListener('keydown', (e) => {
      if (e.key === 'ArrowUp' && history.length) {
        hi = Math.max(0, hi - 1);
        input.value = history[hi];
        return;
      }
      if (e.key === 'ArrowDown' && history.length) {
        hi = Math.min(history.length, hi + 1);
        input.value = history[hi] ?? '';
        return;
      }
      if (e.key !== 'Enter') return;
      const raw = input.value.trim();
      input.value = '';
      print(prompt + raw);
      if (!raw) return;
      history.push(raw);
      hi = history.length;
      const cmd = raw.toLocaleLowerCase('tr-TR').replace(/^\.\\/, '').replace(/customdock(\.exe)?/, 'customdock');
      if (cmd === 'cls' || cmd === 'clear') out.replaceChildren();
      else if (cmd === 'yardım' || cmd === 'yardim' || cmd === 'help') {
        print('CustomDock.exe                     Başlatır ya da ayarları öne getirir');
        print('CustomDock.exe --exit              Düzgünce kapatır, görev çubuğu geri gelir');
        print('CustomDock.exe --restore-taskbar   Acil durumda görev çubuğunu geri getirir');
        print('CustomDock.exe --pin "<dosya>"     Uygulamayı dock\'a sabitler (calc, photos, music)');
      } else if (cmd === 'customdock') {
        if (stage!.dataset.phase === 'exited') {
          startDock();
          print('Custom Dock başlatıldı. Görev çubuğu gizlendi.', 't-ok');
        } else {
          openApp('dock-settings');
          print('Zaten çalışıyor, ayarlar penceresi öne getirildi.', 't-ok');
        }
      } else if (cmd === 'customdock --exit') {
        if (stage!.dataset.phase === 'exited') print('Custom Dock zaten kapalı.', 't-dim');
        else {
          exitDock();
          print('Custom Dock kapatıldı. Windows görev çubuğu geri geldi.', 't-ok');
        }
      } else if (cmd === 'customdock --restore-taskbar') {
        if (stage!.dataset.phase !== 'exited') exitDock(true);
        print('session.json okundu, görev çubuğunun orijinal durumu geri yüklendi.', 't-ok');
        print('TaskbarCreated yayını gönderildi, tepsi simgeleri yeniden kaydoldu.', 't-ok');
      } else if (cmd.startsWith('customdock --pin')) {
        const arg = cmd.slice('customdock --pin'.length).replace(/"/g, '').trim();
        const target: AppId | null = /calc|hesap/.test(arg) ? 'calculator' : /photo|foto/.test(arg) ? 'photos' : /music|müzik|media|medya/.test(arg) ? 'music' : /note|not/.test(arg) ? 'notepad' : /term/.test(arg) ? 'terminal' : null;
        if (!target) print(`"${arg || '?'}" bulunamadı. Örnek: CustomDock.exe --pin calc`, 't-err');
        else {
          pinApp(target);
          print(`${appNames[target]} dock'a sabitlendi.`, 't-ok');
        }
      } else print(`'${raw}' tanınmadı. Komutlar için "yardım" yaz.`, 't-err');
      term.scrollTop = term.scrollHeight;
    });
    return { el: term, views: [], after: () => setTimeout(() => input.focus({ preventScroll: true }), 50) };
  }

  function calcContent() {
    let cur = '0';
    let acc: number | null = null;
    let op: string | null = null;
    let fresh = false;
    const screen = h('div', { class: 'calc-screen num' });
    const hist = h('small');
    const show = () => {
      screen.replaceChildren(hist, cur.replace('.', ','));
    };
    const calc = (a: number, b: number, o: string) => (o === '+' ? a + b : o === '−' ? a - b : o === '×' ? a * b : b === 0 ? NaN : a / b);
    const press = (k: string) => {
      if (/\d/.test(k)) {
        cur = fresh || cur === '0' ? k : cur + k;
        fresh = false;
      } else if (k === ',') {
        if (fresh) cur = '0';
        if (!cur.includes('.')) cur += '.';
        fresh = false;
      } else if (k === 'C') {
        cur = '0';
        acc = null;
        op = null;
        hist.textContent = '';
      } else if (k === '⌫') cur = cur.length > 1 ? cur.slice(0, -1) : '0';
      else if (k === '=') {
        if (op !== null && acc !== null) {
          const r = calc(acc, parseFloat(cur), op);
          hist.textContent = `${String(acc).replace('.', ',')} ${op} ${cur.replace('.', ',')} =`;
          cur = Number.isFinite(r) ? String(+r.toFixed(10)) : 'Sıfıra bölünemez';
          acc = null;
          op = null;
          fresh = true;
        }
      } else {
        const v = parseFloat(cur);
        acc = acc !== null && op && !fresh ? calc(acc, v, op) : Number.isNaN(v) ? 0 : v;
        op = k;
        hist.textContent = `${String(acc).replace('.', ',')} ${op}`;
        cur = String(acc);
        fresh = true;
      }
      show();
    };
    const keys = ['C', '⌫', '÷', '×', '7', '8', '9', '−', '4', '5', '6', '+', '1', '2', '3', '=', '0', ','];
    const pad = h(
      'div',
      { class: 'calc-keys' },
      ...keys.map((k) => {
        const b = h('button', { type: 'button', class: k === '=' ? 'eq' : /[÷×−+C⌫]/.test(k) ? 'op' : '', text: k, onclick: () => press(k) });
        if (k === '=') b.style.gridRow = 'span 2';
        if (k === '0') b.style.gridColumn = 'span 2';
        return b;
      }),
    );
    show();
    return { el: h('div', { class: 'calc' }, screen, pad), views: [] };
  }

  function settingsContent() {
    const pages = ['Genel', 'Görünüm', 'Görev çubuğu', 'Widget galerisi'];
    let page = 'Görünüm';
    const main = h('div', { class: 'set-main' });
    const side = h('div', { class: 'set-side' });
    const select = (key: keyof typeof state, opts: [string, string][]) => {
      const s = h('select', { 'aria-label': String(key) }, ...opts.map(([v, l]) => h('option', { value: v, text: l }))) as HTMLSelectElement;
      s.value = String(state[key]);
      s.addEventListener('change', () => set(key, s.value));
      return s;
    };
    const toggle = (key: 'autohide' | 'fullscreenHide') => {
      const b = h('button', { class: 'switch', role: 'switch', type: 'button', 'aria-checked': String(state[key]), 'aria-label': key });
      b.addEventListener('click', () => set(key, !state[key]));
      return b;
    };
    const row = (title: string, desc: string, ctl: HTMLElement) => h('div', { class: 'set-row' }, h('div', {}, h('b', { text: title }), h('small', { text: desc })), ctl);
    const draw = () => {
      const focused = main.contains(document.activeElement) ? (document.activeElement as HTMLElement).getAttribute('aria-label') : null;
      side.replaceChildren(
        h('div', { class: 'set-app' }, h('span', { html: dockLogo('dl-set') }), h('div', {}, h('b', { text: 'Custom Dock' }), h('small', { text: 'Ayarlar' }))),
        ...pages.map((p) =>
          h('button', { type: 'button', class: p === page ? 'on' : '', onclick: () => { page = p; draw(); } }, h('span', { html: icons[(['settings', 'sparkle', 'monitor', 'widgets'] as const)[pages.indexOf(p)]] }), p),
        ),
      );
      const rows: HTMLElement[] = [];
      if (page === 'Genel') {
        rows.push(
          row('Görev çubuğunun yerini al', 'Değiştirince uygulama kendini yeniden başlatır', select('mode', [['replace', 'Custom Dock'], ['both', 'İkisi birlikte']])),
          row('Windows ile başlat', 'Oturum açınca dock hazır olsun', h('button', { class: 'switch', role: 'switch', type: 'button', 'aria-checked': 'true', onclick: (e: Event) => { const b = e.currentTarget as HTMLElement; b.setAttribute('aria-checked', String(b.getAttribute('aria-checked') !== 'true')); } })),
          row('Windows görev çubuğunu geri getir', 'Acil durum: her koşulda çalışır', h('button', { class: 'wp-btn', type: 'button', text: 'Geri getir', onclick: () => exitDock(true) })),
        );
      } else if (page === 'Görünüm') {
        rows.push(
          h('div', { class: 'set-group', text: 'Dock' }),
          row('Konum', 'Dock\'un duracağı ekran kenarı', select('edge', [['bottom', 'Alt'], ['top', 'Üst'], ['left', 'Sol'], ['right', 'Sağ']])),
          row('Biçim', 'Yüzen ya da klasik yapışık', select('layout', [['floating', 'Yüzen'], ['attached', 'Yapışık']])),
          row('Boyut', 'Küçük, Windows görev çubuğuyla aynı kalınlıktadır', select('size', [['small', 'Küçük (48)'], ['medium', 'Orta (56)'], ['large', 'Büyük (66)']])),
          h('div', { class: 'set-group', text: 'Görünüm' }),
          row('Tema', 'Koyu, açık ya da sisteme uyan', select('theme', [['dark', 'Koyu'], ['light', 'Açık']])),
          row('Arka plan', 'Bulanık cam, Acrylic ya da düz', select('backdrop', [['blur', 'Bulanık cam'], ['acrylic', 'Acrylic'], ['solid', 'Düz']])),
        );
      } else if (page === 'Görev çubuğu') {
        rows.push(
          row('Otomatik gizle', 'Dock kenara kayar, imleç gelince döner', toggle('autohide')),
          row('Tam ekranda gizle', 'Oyun, video ve F11 modunda', toggle('fullscreenHide')),
          row('Başlat düğmesi', 'Windows Başlat menüsünü açar', h('button', { class: 'switch', role: 'switch', type: 'button', 'aria-checked': 'true', disabled: true })),
        );
      } else {
        rows.push(
          ...widgets.map((d) =>
            row(d.name, d.variants.map((v) => v.name).join(', '), h('button', { class: 'add-btn', type: 'button', 'aria-label': `${d.name} ekle`, html: icons.plus, onclick: () => addWidget(d.id) })),
          ),
        );
      }
      main.replaceChildren(h('h4', { text: page }), ...rows);
      if (focused) main.querySelector<HTMLElement>(`[aria-label="${focused}"]`)?.focus({ preventScroll: true });
    };
    draw();
    const view: View = { el: main, refresh: draw };
    return { el: h('div', { class: 'set' }, side, main), views: [view] };
  }

  /* ------------------------------------------------------------ toasts */

  function toast(title: string, body: string, opts: { actions?: { label: string; run: () => void; accent?: boolean }[]; timeout?: number } = {}) {
    const close = () => {
      el.classList.add('is-closing');
      setTimeout(() => el.remove(), 200);
    };
    const el = h(
      'div',
      { class: 'toast', role: 'status' },
      h('div', { class: 'toast-app' }, h('span', { html: dockLogo(`dl-t${Date.now()}`) }), 'Custom Dock', h('button', { class: 'toast-close', type: 'button', 'aria-label': 'Kapat', html: icons.close, onclick: close })),
      h('b', { text: title }),
      h('p', { text: body }),
      opts.actions
        ? h(
            'div',
            { class: 'toast-actions' },
            ...opts.actions.map((a) => h('button', { type: 'button', class: `wp-btn ${a.accent ? 'accent' : ''}`, text: a.label, onclick: () => { close(); a.run(); } })),
          )
        : null,
    );
    toasts.append(el);
    while (toasts.children.length > 2) toasts.firstElementChild?.remove();
    if (opts.timeout !== 0) setTimeout(close, opts.timeout ?? 6000);
  }

  /* ------------------------------------------------------------ phases */

  function enter() {
    if (reduced()) return;
    stage!.classList.add('is-entering');
    setTimeout(() => stage!.classList.remove('is-entering'), 1500);
  }

  function startDock() {
    stage!.dataset.phase = 'ready';
    enter();
    evalHide();
    requestAnimationFrame(updateFades);
  }

  function exitDock(restore = false) {
    closeFlyout();
    stage!.dataset.phase = 'exited';
    stage!.classList.remove('is-hidden');
    toast(restore ? 'Görev çubuğu geri getirildi' : 'Custom Dock kapatıldı', 'Windows görev çubuğu ve tepsi simgeleri geri geldi. Hiçbir şey kaybolmadı.', {
      actions: [{ label: 'Custom Dock\'u yeniden başlat', accent: true, run: startDock }],
      timeout: 0,
    });
  }

  let booted = false;
  function boot() {
    if (booted) return;
    booted = true;
    if (reduced()) {
      stage!.dataset.phase = 'ready';
      return;
    }
    setTimeout(() => {
      stage!.dataset.phase = 'ready';
      enter();
      requestAnimationFrame(updateFades);
    }, 1000);
    setTimeout(() => toast('Custom Dock çalışıyor', 'Görev çubuğunun yerini aldı. Kapattığında Windows görev çubuğu kendiliğinden geri gelir.'), 2400);
  }

  /* ---------------------------------------------------------- autohide */

  let nearEdge = false;
  let hovered = false;
  let hideTimer = 0;
  function evalHide() {
    clearTimeout(hideTimer);
    if (!state.autohide || stage!.dataset.phase !== 'ready') {
      stage!.classList.remove('is-hidden');
      return;
    }
    if (hovered || nearEdge || fly) {
      stage!.classList.remove('is-hidden');
      return;
    }
    hideTimer = window.setTimeout(() => stage!.classList.add('is-hidden'), 650);
  }
  dock.addEventListener('pointerenter', () => {
    hovered = true;
    evalHide();
  });
  dock.addEventListener('pointerleave', () => {
    hovered = false;
    evalHide();
  });
  dock.addEventListener('focusin', () => {
    hovered = true;
    evalHide();
  });
  dock.addEventListener('focusout', () => {
    hovered = false;
    evalHide();
  });

  const onPointer = (e: PointerEvent) => {
    const { r, s } = stageScale();
    const x = (e.clientX - r.left) / s;
    const y = (e.clientY - r.top) / s;
    const sw = stage!.offsetWidth;
    const sh = stage!.offsetHeight;
    stage!.style.setProperty('--px', ((x / sw) * 2 - 1).toFixed(3));
    stage!.style.setProperty('--py', ((y / sh) * 2 - 1).toFixed(3));
    if (!state.autohide) return;
    const bottomLimit = sh - (state.mode === 'both' ? 48 : 0);
    const dist = state.edge === 'bottom' ? bottomLimit - y : state.edge === 'top' ? y : state.edge === 'left' ? x : sw - x;
    const hidden = stage!.classList.contains('is-hidden');
    const near = dist >= -2 && dist < (hidden ? 18 : dock.offsetHeight + 30);
    if (near !== nearEdge) {
      nearEdge = near;
      evalHide();
    }
  };
  stage.addEventListener('pointermove', onPointer);
  stage.addEventListener('pointerdown', onPointer);
  stage.addEventListener('pointerleave', () => {
    nearEdge = false;
    evalHide();
  });

  /* -------------------------------------------------------------- set */

  function set(key: keyof typeof state, value: any) {
    if ((state as any)[key] === value) return;
    const layoutChange = key === 'edge' || key === 'layout' || key === 'size' || key === 'mode';
    const apply = () => {
      (state as any)[key] = value;
      if (key === 'autohide' || key === 'fullscreenHide') {
        if (key === 'autohide') {
          stage!.classList.toggle('show-hint', !!value);
          if (value) setTimeout(() => stage!.classList.remove('show-hint'), 4200);
          evalHide();
        }
      } else stage!.dataset[key] = String(value);
      if (key === 'edge') render();
      syncBar();
      refreshAll();
      requestAnimationFrame(updateFades);
    };
    closeFlyout(true);
    if (layoutChange && !reduced() && stage!.dataset.phase === 'ready') {
      const anim = dock.animate([{ opacity: 1 }, { opacity: 0 }], { duration: 140, easing: 'ease-in', fill: 'forwards' });
      anim.onfinish = () => {
        apply();
        anim.cancel();
        dock.animate([{ opacity: 0 }, { opacity: 1 }], { duration: 200, easing: 'ease-out' });
        enter();
      };
    } else apply();
    if (key === 'mode') {
      toast(
        value === 'both' ? 'İkisi birlikte' : 'Custom Dock görev çubuğunun yerini aldı',
        value === 'both' ? 'Windows görev çubuğuna dokunulmaz, dock onun üstünde durur. Tepsi devralınmaz.' : 'Windows görev çubuğu gizlendi, tepsi simgeleri dock\'a taşındı.',
      );
    }
  }

  /* --------------------------------------------------------------- bar */

  const segs = bar ? [...bar.querySelectorAll<HTMLElement>('[data-control]')] : [];
  segs.forEach((seg) => {
    const thumb = h('span', { class: 'seg-thumb', 'aria-hidden': 'true' });
    seg.prepend(thumb);
    seg.querySelectorAll<HTMLButtonElement>('button').forEach((b) =>
      b.addEventListener('click', () => {
        const key = seg.dataset.control as keyof typeof state;
        if (key === 'mode' && stage!.dataset.phase === 'exited') startDock();
        set(key, b.dataset.value);
      }),
    );
    seg.addEventListener('keydown', (e) => {
      if (e.key !== 'ArrowRight' && e.key !== 'ArrowLeft') return;
      const btns = [...seg.querySelectorAll<HTMLButtonElement>('button')];
      const i = btns.findIndex((b) => b.getAttribute('aria-checked') === 'true');
      const n = btns[(i + (e.key === 'ArrowRight' ? 1 : -1) + btns.length) % btns.length];
      n.focus();
      n.click();
    });
  });

  function syncBar() {
    segs.forEach((seg) => {
      const key = seg.dataset.control as keyof typeof state;
      const btns = [...seg.querySelectorAll<HTMLButtonElement>('button')];
      btns.forEach((b) => {
        const on = String(state[key]) === b.dataset.value;
        b.setAttribute('aria-checked', String(on));
        b.tabIndex = on ? 0 : -1;
      });
      const active = btns.find((b) => b.getAttribute('aria-checked') === 'true');
      const thumb = seg.querySelector<HTMLElement>('.seg-thumb');
      if (active && thumb) {
        thumb.style.width = `${active.offsetWidth}px`;
        thumb.style.transform = `translateX(${active.offsetLeft}px)`;
      }
    });
  }
  new ResizeObserver(syncBar).observe(bar ?? document.body);

  bar?.querySelector('[data-add-widget]')?.addEventListener('click', () => {
    if (stage.dataset.phase !== 'ready') startDock();
    openGallery();
  });
  bar?.querySelector('[data-open-settings]')?.addEventListener('click', () => openApp('dock-settings'));

  document.querySelectorAll<HTMLElement>('[data-demo-set]').forEach((b) =>
    b.addEventListener('click', () => {
      const [key, value] = b.dataset.demoSet!.split(':');
      stage.scrollIntoView({ behavior: reduced() ? 'auto' : 'smooth', block: 'center' });
      setTimeout(() => {
        if (stage.dataset.phase !== 'ready') startDock();
        set(key as keyof typeof state, value);
      }, reduced() ? 0 : 650);
    }),
  );

  document.addEventListener('cd:add-widget', (e) => {
    const { id, variant } = (e as CustomEvent).detail;
    addWidget(id, variant);
  });

  /* ------------------------------------------------------ desk icons */

  stage.querySelectorAll<HTMLElement>('[data-open]').forEach((b) => {
    const open = () => {
      const key = b.dataset.open as WinKey;
      if (key === 'dock-settings' && stage.dataset.phase === 'exited') startDock();
      else openApp(key);
    };
    b.addEventListener('click', (e) => {
      stage.querySelectorAll('.desk-icon').forEach((x) => x.classList.toggle('is-selected', x === b));
      if ((e as PointerEvent).pointerType !== 'mouse' || e.detail === 0) open();
    });
    b.addEventListener('dblclick', open);
  });
  stage.addEventListener('pointerdown', (e) => {
    if (!(e.target as HTMLElement).closest('.desk-icon')) stage.querySelectorAll('.desk-icon').forEach((x) => x.classList.remove('is-selected'));
  });

  /* ------------------------------------------------------------ start */

  render();
  syncBar();
  document.fonts?.ready.then(syncBar);

  const io = new IntersectionObserver(
    (entries) => {
      const vis = entries.some((en) => en.isIntersecting);
      setVisible('stage', vis);
      if (vis) boot();
    },
    { threshold: 0.2 },
  );
  io.observe(stage);
}
