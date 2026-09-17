import { widgetById, makeInstance, track, type View, type Env, type Instance } from './widgets';
import { mount, unmount, refreshAll } from './views';
import { icons } from '../lib/icons';
import { setVisible } from './sim';

const env: Env = { refreshAll };

export function initGallery() {
  const grid = document.querySelector<HTMLElement>('[data-wgrid]');
  if (!grid || grid.dataset.ready) return;
  grid.dataset.ready = '1';

  const tiles = [...grid.querySelectorAll<HTMLElement>('.wtile')];
  const recs = new Map<HTMLElement, { inst: Instance; view?: View; visible: boolean }>();

  for (const tile of tiles) {
    const def = widgetById[tile.dataset.id!];
    const inst = makeInstance(def.id);
    track(inst, env);
    const host = tile.querySelector<HTMLElement>('[data-preview]')!;
    const chips = [...tile.querySelectorAll<HTMLButtonElement>('[data-variant]')];
    const rec = { inst, view: undefined as View | undefined, visible: false };
    recs.set(tile, rec);

    const draw = () => {
      unmount(rec.view);
      const v = def.card(inst, env);
      (v as View & { paused?: boolean }).paused = !rec.visible;
      host.replaceChildren(v.el);
      rec.view = mount(v);
      chips.forEach((c) => {
        const on = c.dataset.variant === inst.variant;
        c.setAttribute('aria-checked', String(on));
        c.tabIndex = on ? 0 : -1;
      });
    };
    draw();

    chips.forEach((c) =>
      c.addEventListener('click', () => {
        if (inst.variant === c.dataset.variant) return;
        inst.variant = c.dataset.variant!;
        draw();
      }),
    );
    tile.querySelector('[role="radiogroup"]')?.addEventListener('keydown', (e) => {
      const k = (e as KeyboardEvent).key;
      if (k !== 'ArrowRight' && k !== 'ArrowLeft') return;
      const i = chips.findIndex((c) => c.getAttribute('aria-checked') === 'true');
      const n = chips[(i + (k === 'ArrowRight' ? 1 : -1) + chips.length) % chips.length];
      n.focus();
      n.click();
    });

    const activate = (e: Event) => {
      if ((e.target as HTMLElement).closest('.mc-btn')) return;
      if (def.primary) def.primary(inst, env);
      else if (def.variants.length > 1) {
        const i = def.variants.findIndex((v) => v.id === inst.variant);
        inst.variant = def.variants[(i + 1) % def.variants.length].id;
        draw();
      }
    };
    host.addEventListener('click', activate);
    host.addEventListener('keydown', (e) => {
      if (e.key === 'Enter' || e.key === ' ') {
        e.preventDefault();
        activate(e);
      }
    });

    const add = tile.querySelector<HTMLButtonElement>('[data-add]')!;
    const label = add.querySelector('.add-label')!;
    const ic = add.querySelector('.add-ic')!;
    let t = 0;
    add.addEventListener('click', () => {
      document.dispatchEvent(new CustomEvent('cd:add-widget', { detail: { id: def.id, variant: inst.variant } }));
      add.classList.add('is-done');
      label.textContent = "Dock'a eklendi";
      ic.innerHTML = icons.check;
      clearTimeout(t);
      t = window.setTimeout(() => {
        add.classList.remove('is-done');
        label.textContent = 'Demoya ekle';
        ic.innerHTML = icons.plus;
      }, 1800);
    });
  }

  const io = new IntersectionObserver(
    (entries) => {
      entries.forEach((en) => {
        const rec = recs.get(en.target as HTMLElement);
        if (!rec) return;
        rec.visible = en.isIntersecting;
        if (rec.view) (rec.view as View & { paused?: boolean }).paused = !en.isIntersecting;
        if (en.isIntersecting) rec.view?.refresh?.();
      });
      setVisible('gallery', [...recs.values()].some((r) => r.visible));
    },
    { rootMargin: '60px' },
  );
  tiles.forEach((t) => io.observe(t));

  const filters = [...document.querySelectorAll<HTMLButtonElement>('[data-filters] [data-filter]')];
  const apply = (cat: string) => {
    filters.forEach((f) => f.setAttribute('aria-selected', String(f.dataset.filter === cat)));
    tiles.forEach((t) => (t.hidden = cat !== '*' && t.dataset.cat !== cat));
  };
  filters.forEach((f) =>
    f.addEventListener('click', () => {
      const cat = f.dataset.filter!;
      const doc = document as Document & { startViewTransition?: (cb: () => void) => unknown };
      const still = matchMedia('(prefers-reduced-motion: reduce)').matches;
      if (doc.startViewTransition && !still) {
        tiles.forEach((t, i) => ((t.style as CSSStyleDeclaration & { viewTransitionName: string }).viewTransitionName = `wt-${i}`));
        doc.startViewTransition(() => apply(cat));
      } else apply(cat);
    }),
  );
}
