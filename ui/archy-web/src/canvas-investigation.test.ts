import { describe, expect, it } from 'vitest';
import { findDirectedTrace, findDownstreamImpact } from './canvas-investigation';
import type { GraphMapEdge, GraphMapNode } from './api';

const nodes: GraphMapNode[] = ['a', 'b', 'c', 'd'].map(id => ({ stableId: id, nodeKind: 'type', canonicalKey: id, displayName: id, filePath: `src/Features/${id}/item.cs`, provider: 'test', confidence: 1 }));
const edges: GraphMapEdge[] = [{ edgeId: 'ab', sourceStableId: 'a', targetStableId: 'b', edgeKind: 'uses', confidence: 1 }, { edgeId: 'bc', sourceStableId: 'b', targetStableId: 'c', edgeKind: 'uses', confidence: 1 }, { edgeId: 'dc', sourceStableId: 'd', targetStableId: 'c', edgeKind: 'uses', confidence: 1 }];

describe('canvas investigation model', () => {
  it('returns only directed trace paths and reports a genuine no-path state', () => {
    expect(findDirectedTrace('a', 'c', edges)).toEqual({ kind: 'path', nodeIds: ['a', 'b', 'c'], edgeIds: ['ab', 'bc'] });
    expect(findDirectedTrace('c', 'a', edges)).toEqual({ kind: 'no_path' });
  });

  it('groups bounded downstream impact using graph direction', () => {
    const result = findDownstreamImpact('c', 2, nodes, edges);
    expect([...result.nodeIds].sort()).toEqual(['a', 'b', 'c', 'd']);
    expect([...result.edgeIds].sort()).toEqual(['ab', 'bc', 'dc']);
    expect(result.moduleCounts.get('Features/a')).toBe(1);
  });
});
