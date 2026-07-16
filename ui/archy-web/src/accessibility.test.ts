import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

const app = readFileSync(new URL('./app.tsx', import.meta.url), 'utf8');
const graph = readFileSync(new URL('./graph.tsx', import.meta.url), 'utf8');
const timeline = readFileSync(new URL('./components/SessionTimeline.tsx', import.meta.url), 'utf8');
const search = readFileSync(new URL('./components/GraphSearch.tsx', import.meta.url), 'utf8');
const canvasV2 = readFileSync(new URL('./CanvasV2.tsx', import.meta.url), 'utf8');

describe('accessible UI contract', () => {
  it('makes unavailable and degraded workspace states perceivable as status text', () => {
    expect(app).toContain('role="status"');
    expect(app).toContain('role={alert ? \'alert\' : undefined}');
    expect(app).toContain('Some optional capabilities are unavailable.');
  });

  it('offers keyboard-operable graph alternatives beyond color and animation', () => {
    expect(graph).toContain('role="application"');
    expect(graph).toContain('tabIndex={0}');
    expect(graph).toContain("event.key.startsWith('Arrow')");
    expect(graph).toContain('Drag to travel');
    expect(graph).toContain('aria-describedby="map-instructions"');
  });

  it('gives replay controls an accessible name and announces event frames', () => {
    expect(timeline).toContain('aria-label="Replay position"');
    expect(timeline).toContain('aria-live="polite"');
  });

  it('keeps Canvas V2 commands and search keyboard-operable', () => {
    expect(graph).toContain('role="menu"');
    expect(graph).toContain('aria-pressed={active === mode}');
    expect(graph).toContain('click the minimap to navigate');
    expect(search).toContain("event.key === 'ArrowDown'");
    expect(search).toContain('aria-activedescendant');
    expect(search).toContain("event.key !== '/'");
  });

  it('provides a semantic, keyboard-operable module map without color-only selection', () => {
    expect(canvasV2).toContain('role="application"');
    expect(canvasV2).toContain('aria-describedby="canvas-v2-instructions"');
    expect(canvasV2).toContain('tabIndex={0}');
    expect(canvasV2).toContain('click a directory');
    expect(canvasV2).toContain('cross-directory relationships');
  });
});
