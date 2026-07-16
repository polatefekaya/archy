import { describe, expect, it } from 'vitest';
import { buildModuleScene, defaultCanvasFilter } from './canvas-scene';
import type { GraphMapEdge, GraphMapNode } from './api';

const nodes: GraphMapNode[] = [
  { stableId: 'orders-a', nodeKind: 'type', canonicalKey: 'a', displayName: 'Order', filePath: 'src/Features/Orders/Order.cs', provider: 'csharp', confidence: .95 },
  { stableId: 'orders-b', nodeKind: 'type', canonicalKey: 'b', displayName: 'OrderHandler', filePath: 'src/Features/Orders/OrderHandler.cs', provider: 'csharp', confidence: .9 },
  { stableId: 'billing-a', nodeKind: 'type', canonicalKey: 'c', displayName: 'Invoice', filePath: 'src/Features/Billing/Invoice.cs', provider: 'csharp', confidence: .8 },
  { stableId: 'external', nodeKind: 'unresolved_reference', canonicalKey: 'd', displayName: 'External', filePath: null, provider: 'csharp', confidence: .4 },
];
const edges: GraphMapEdge[] = [
  { edgeId: 'one', sourceStableId: 'orders-a', targetStableId: 'billing-a', edgeKind: 'uses', confidence: 1 },
  { edgeId: 'two', sourceStableId: 'orders-b', targetStableId: 'billing-a', edgeKind: 'uses', confidence: 1 },
  { edgeId: 'three', sourceStableId: 'orders-b', targetStableId: 'orders-a', edgeKind: 'uses', confidence: 1 },
];

describe('module canvas scene', () => {
  it('aggregates cross-module relationships while preserving readable module metrics', () => {
    const scene = buildModuleScene(nodes, edges);
    expect(scene.modules.map(module => [module.key, module.nodeCount, module.crossModuleDependencyCount])).toEqual([['Features/Billing', 1, 2], ['Features/Orders', 2, 2], ['References/External', 1, 0]]);
    expect(scene.edges).toEqual([{ id: 'Features/Orders→Features/Billing', sourceModuleKey: 'Features/Orders', targetModuleKey: 'Features/Billing', dependencyCount: 2 }]);
  });

  it('uses one deterministic filter for module cards, edges, and empty state', () => {
    const scene = buildModuleScene(nodes, edges, { ...defaultCanvasFilter, references: 'external' });
    expect(scene.modules.map(module => module.key)).toEqual(['References/External']);
    expect(scene.edges).toEqual([]);
    expect(scene.filteredOutNodeCount).toBe(3);
  });

  it('applies confidence and reference filters before aggregation', () => {
    const scene = buildModuleScene(nodes, edges, { ...defaultCanvasFilter, minConfidence: .9, references: 'internal' });
    expect(scene.modules.map(module => [module.key, module.nodeCount])).toEqual([['Features/Orders', 2]]);
    expect(scene.edges).toEqual([]);
    expect(scene.filteredOutNodeCount).toBe(2);
  });
});
