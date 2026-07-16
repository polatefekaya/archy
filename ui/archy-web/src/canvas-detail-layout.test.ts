import { describe, expect, it } from 'vitest';
import { layoutModuleDetail } from './canvas-detail-layout';
import type { ModuleDetailScene } from './canvas-detail-scene';

const detail: ModuleDetailScene = {
  moduleKey: 'A', moduleLabel: 'A', fileIdByNodeId: new Map(), internalConnections: [], directoryConnections: [], crossDirectoryConnections: [],
  files: Array.from({ length: 7 }, (_, index) => ({ id: `f${index}`, label: `F${index}`, path: null, nodeIds: [], representativeNodeId: '', symbolCount: 1, internalConnectionCount: 0, externalConnectionCount: 0 })),
  connectedDirectories: [],
  remoteFiles: [
    { id: 'incoming', moduleKey: 'Incoming', moduleLabel: 'Incoming', label: 'Incoming.cs', path: null, nodeIds: [], representativeNodeId: '', symbolCount: 1, connectionCount: 2, incomingConnectionCount: 2, outgoingConnectionCount: 0 },
    { id: 'outgoing', moduleKey: 'Outgoing', moduleLabel: 'Outgoing', label: 'Outgoing.cs', path: null, nodeIds: [], representativeNodeId: '', symbolCount: 1, connectionCount: 3, incomingConnectionCount: 0, outgoingConnectionCount: 3 },
  ],
};

describe('module detail layout', () => {
  it('keeps local files readable between real incoming and outgoing file rails', () => {
    const layout = layoutModuleDetail(detail);
    expect(layout.fileBoundsById.size).toBe(7);
    expect(layout.remoteFileBoundsById.get('incoming')!.y + 72).toBeLessThan(layout.focusBounds.y);
    expect(layout.remoteFileBoundsById.get('outgoing')!.y).toBeGreaterThan(layout.focusBounds.y + layout.focusBounds.height);
    for (const bounds of layout.fileBoundsById.values()) {
      expect(bounds.x).toBeGreaterThan(layout.focusBounds.x);
      expect(bounds.y).toBeGreaterThan(layout.focusBounds.y);
    }
  });
});
