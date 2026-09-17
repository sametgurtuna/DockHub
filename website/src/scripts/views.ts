import { onFrame, onSecond } from './sim';
import type { View } from './widgets';

const views = new Set<View & { paused?: boolean }>();

export function mount<T extends View>(v: T): T {
  views.add(v);
  v.refresh?.();
  v.tick?.(new Date());
  v.frame?.(0);
  return v;
}

export function unmount(v: View | undefined) {
  if (v) views.delete(v);
}

export function refreshAll() {
  const now = new Date();
  views.forEach((v) => {
    v.refresh?.();
    v.tick?.(now);
    v.frame?.(0);
  });
}

onFrame((dt) =>
  views.forEach((v) => {
    if (!v.paused) v.frame?.(dt);
  }),
);
onSecond((d) =>
  views.forEach((v) => {
    if (!v.paused) v.tick?.(d);
  }),
);
