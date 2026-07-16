import { describe, expect, it } from 'vitest';
import { readCanvasTraceSelection, readCanvasViewState, writeCanvasTraceSelection, writeCanvasViewState } from './canvas-state';
import { deriveVisibleGraph, findCycleNodeIds, findHighCouplingNodeIds, shortestPath } from './graph';

describe('canvas view state', () => {
  it('uses safe defaults for malformed URL input', () => {
    expect(readCanvasViewState('?canvasMode=unknown&impactDepth=99')).toMatchObject({ mode: 'explore', impactDepth: 2, scope: 'all' });
  });

  it('round-trips supported view controls without discarding unrelated URL state', () => {
    const state = readCanvasViewState('?session=abc&canvasMode=impact&canvasScope=hop-2&impactDepth=3&edges=0');
    const search = writeCanvasViewState('?session=abc', state);
    expect(search).toContain('session=abc');
    expect(readCanvasViewState(`?${search}`)).toMatchObject({ mode: 'impact', scope: 'hop-2', impactDepth: 3, showEdges: false });
  });

  it('round-trips trace selections without retaining empty values', () => {
    const search = writeCanvasTraceSelection('?session=abc', 'node-a', 'node-b');
    expect(readCanvasTraceSelection(`?${search}`)).toEqual({ start: 'node-a', end: 'node-b' });
    expect(writeCanvasTraceSelection(`?${search}`, null, null)).toBe('session=abc');
  });
});

describe('canvas investigation views', () => {
  const nodes = ['a', 'b', 'c', 'd'].map(stableId => ({ stableId, nodeKind: 'type', canonicalKey: stableId, displayName: stableId, filePath: null, provider: 'test', confidence: 1 }));
  const edges = [{ edgeId: 'ab', sourceStableId: 'a', targetStableId: 'b', edgeKind: 'uses', confidence: 1 }, { edgeId: 'bc', sourceStableId: 'b', targetStableId: 'c', edgeKind: 'uses', confidence: 1 }, { edgeId: 'ad', sourceStableId: 'a', targetStableId: 'd', edgeKind: 'uses', confidence: 1 }];

  it('finds directed trace paths without inventing a reverse dependency', () => {
    expect(shortestPath('a', 'c', edges)).toEqual(new Set(['a', 'b', 'c']));
    expect(shortestPath('c', 'a', edges)).toBeNull();
  });

  it('limits impact mode to downstream dependents at the requested depth', () => {
    const result = deriveVisibleGraph(nodes, edges, { kind: 'node', node: nodes[2] }, { mode: 'impact', scope: 'all', nodeKind: 'all', provider: 'all', module: 'all', confidence: 'all', referenceScope: 'all', showEdges: true, showLabels: true, impactDepth: 2 }, null, null);
    expect(result.nodes.map(node => node.stableId).sort()).toEqual(['a', 'b', 'c']);
    expect(result.edges.map(edge => edge.edgeId).sort()).toEqual(['ab', 'bc']);
  });

  it('applies provider, confidence, and reference filters together', () => {
    const filteredNodes = [
      { ...nodes[0], provider: 'source', confidence: .9, filePath: 'src/a.ts' },
      { ...nodes[1], provider: 'source', confidence: .4, filePath: null },
      { ...nodes[2], provider: 'generated', confidence: .9, filePath: 'src/c.ts' },
    ];
    const result = deriveVisibleGraph(filteredNodes, edges, null, { mode: 'explore', scope: 'all', nodeKind: 'all', provider: 'source', module: 'all', confidence: 'high', referenceScope: 'internal', showEdges: true, showLabels: true, impactDepth: 2 }, null, null);
    expect(result.nodes.map(node => node.stableId)).toEqual(['a']);
  });

  it('finds deterministic cycle and high-coupling signals without server inference', () => {
    const signalEdges = [...edges, { edgeId: 'ca', sourceStableId: 'c', targetStableId: 'a', edgeKind: 'uses', confidence: 1 }, { edgeId: 'ae', sourceStableId: 'a', targetStableId: 'e', edgeKind: 'uses', confidence: 1 }];
    const signalNodes = [...nodes, { stableId: 'e', nodeKind: 'type', canonicalKey: 'e', displayName: 'e', filePath: null, provider: 'test', confidence: 1 }];
    expect(findCycleNodeIds(signalNodes, signalEdges)).toEqual(new Set(['a', 'b', 'c']));
    expect(findHighCouplingNodeIds(signalNodes, signalEdges)).toContain('a');
  });
});
