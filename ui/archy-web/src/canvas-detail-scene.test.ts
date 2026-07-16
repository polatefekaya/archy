import { describe, expect, it } from 'vitest';
import type { GraphMapEdge, GraphMapNode } from './api';
import { buildModuleDetailScene, connectedFileDetail, directoryFileDetail } from './canvas-detail-scene';
import { buildModuleScene } from './canvas-scene';

const nodes: GraphMapNode[] = [
  { stableId: 'orders-type', nodeKind: 'type', canonicalKey: 'a', displayName: 'Order', filePath: 'src/Features/Orders/Order.cs', provider: 'csharp', confidence: 1 },
  { stableId: 'orders-method', nodeKind: 'method', canonicalKey: 'b', displayName: 'Save', filePath: 'src/Features/Orders/Order.cs', provider: 'csharp', confidence: 1 },
  { stableId: 'orders-handler', nodeKind: 'type', canonicalKey: 'c', displayName: 'Handler', filePath: 'src/Features/Orders/Handler.cs', provider: 'csharp', confidence: 1 },
  { stableId: 'billing', nodeKind: 'type', canonicalKey: 'd', displayName: 'Invoice', filePath: 'src/Features/Billing/Invoice.cs', provider: 'csharp', confidence: 1 },
];
const edges: GraphMapEdge[] = [
  { edgeId: 'internal', sourceStableId: 'orders-handler', targetStableId: 'orders-type', edgeKind: 'uses', confidence: 1 },
  { edgeId: 'external-a', sourceStableId: 'orders-type', targetStableId: 'billing', edgeKind: 'uses', confidence: 1 },
  { edgeId: 'external-b', sourceStableId: 'orders-method', targetStableId: 'billing', edgeKind: 'uses', confidence: 1 },
];

describe('module detail scene', () => {
  it('aggregates symbols into real files and preserves file-level connections', () => {
    const scene = buildModuleScene(nodes, edges);
    const detail = buildModuleDetailScene('Features/Orders', scene, nodes, edges)!;
    expect(detail.files.map(file => [file.label, file.symbolCount, file.externalConnectionCount])).toEqual([
      ['Order.cs', 2, 2],
      ['Handler.cs', 1, 0],
    ]);
    expect(detail.internalConnections).toHaveLength(1);
    expect(detail.connectedDirectories).toEqual([{ key: 'Features/Billing', label: 'Billing', incomingCount: 0, outgoingCount: 2, fileConnectionCount: 2 }]);
    expect(detail.directoryConnections).toHaveLength(1);
    expect(detail.directoryConnections[0].connectionCount).toBe(2);
    expect(detail.remoteFiles.map(file => [file.moduleLabel, file.label, file.connectionCount])).toEqual([['Billing', 'Invoice.cs', 2]]);
    expect(detail.crossDirectoryConnections).toHaveLength(1);
    expect(detail.crossDirectoryConnections[0]).toMatchObject({ localFileId: 'file:src/Features/Orders/Order.cs', remoteDirectoryKey: 'Features/Billing', direction: 'outgoing', connectionCount: 2 });
  });

  it('keeps externally connected files without pulling the entire internal graph into view', () => {
    const scene = buildModuleScene(nodes, edges);
    const detail = buildModuleDetailScene('Features/Orders', scene, nodes, edges)!;
    const focused = connectedFileDetail(detail);
    expect(focused.files.map(file => file.label)).toEqual(['Order.cs']);
    expect(focused.directoryConnections).toHaveLength(1);
  });

  it('isolates the files participating in one directory boundary', () => {
    const scene = buildModuleScene(nodes, edges);
    const detail = buildModuleDetailScene('Features/Orders', scene, nodes, edges)!;
    const focused = directoryFileDetail(detail, 'Features/Billing');
    expect(focused.files.map(file => file.label)).toEqual(['Order.cs']);
    expect(focused.connectedDirectories.map(directory => directory.label)).toEqual(['Billing']);
    expect(focused.remoteFiles.map(file => file.label)).toEqual(['Invoice.cs']);
    expect(focused.crossDirectoryConnections).toHaveLength(1);
  });
});
