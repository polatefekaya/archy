import type { ModuleScene } from './canvas-scene';

export type ScenePoint = { x: number; y: number };
export type ModuleBounds = ScenePoint & { width: number; height: number };
export type ModuleLayout = { boundsByKey: ReadonlyMap<string, ModuleBounds>; width: number; height: number };

const CARD_WIDTH = 280;
const CARD_HEIGHT = 116;
const GAP = 72;
const PADDING = 72;

/** Stable grid layout; sort order is supplied by the scene and never depends on viewport size. */
export function layoutModules(modules: readonly ModuleScene[], maxColumns = 4): ModuleLayout {
  const columns = Math.max(1, Math.min(maxColumns, Math.ceil(Math.sqrt(Math.max(1, modules.length)))));
  const boundsByKey = new Map<string, ModuleBounds>();
  modules.forEach((module, index) => {
    const column = index % columns; const row = Math.floor(index / columns);
    boundsByKey.set(module.key, { x: PADDING + column * (CARD_WIDTH + GAP), y: PADDING + row * (CARD_HEIGHT + GAP), width: CARD_WIDTH, height: CARD_HEIGHT });
  });
  const rows = Math.max(1, Math.ceil(modules.length / columns));
  return { boundsByKey, width: PADDING * 2 + columns * CARD_WIDTH + (columns - 1) * GAP, height: PADDING * 2 + rows * CARD_HEIGHT + (rows - 1) * GAP };
}

export function moduleCenter(bounds: ModuleBounds): ScenePoint { return { x: bounds.x + bounds.width / 2, y: bounds.y + bounds.height / 2 }; }

export function hitModule(point: ScenePoint, layout: ModuleLayout): string | null {
  for (const [key, bounds] of layout.boundsByKey) if (point.x >= bounds.x && point.x <= bounds.x + bounds.width && point.y >= bounds.y && point.y <= bounds.y + bounds.height) return key;
  return null;
}
