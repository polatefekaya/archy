import { describe, expect, it } from 'vitest';
import { hitModule, layoutModules, moduleCenter } from './canvas-layout';
import type { ModuleScene } from './canvas-scene';

const modules = ['Billing', 'Orders', 'Users'].map((key, index): ModuleScene => ({ key, label: key, nodeIds: [], nodeCount: index + 1, crossModuleDependencyCount: 0 }));

describe('module layout', () => {
  it('uses stable, non-overlapping bounds for the same ordered scene', () => {
    const layout = layoutModules(modules);
    expect(layout.boundsByKey.get('Billing')).toEqual({ x: 72, y: 72, width: 280, height: 116 });
    expect(layout.boundsByKey.get('Orders')).toEqual({ x: 424, y: 72, width: 280, height: 116 });
    expect(layout.width).toBeGreaterThan(0);
  });

  it('uses the exact render geometry for hit testing', () => {
    const layout = layoutModules(modules);
    expect(hitModule(moduleCenter(layout.boundsByKey.get('Orders')!), layout)).toBe('Orders');
    expect(hitModule({ x: 1, y: 1 }, layout)).toBeNull();
  });
});
