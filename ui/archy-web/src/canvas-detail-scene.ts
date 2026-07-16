import type { GraphMapEdge, GraphMapNode } from './api';
import type { CanvasScene } from './canvas-scene';

export type FileScene = {
  id: string;
  label: string;
  path: string | null;
  nodeIds: readonly string[];
  representativeNodeId: string;
  symbolCount: number;
  internalConnectionCount: number;
  externalConnectionCount: number;
};

export type FileConnectionScene = {
  id: string;
  sourceFileId: string;
  targetFileId: string;
  connectionCount: number;
};

export type DirectoryConnectionScene = {
  id: string;
  fileId: string;
  directoryKey: string;
  direction: 'incoming' | 'outgoing';
  connectionCount: number;
};

export type ConnectedDirectoryScene = {
  key: string;
  label: string;
  incomingCount: number;
  outgoingCount: number;
  fileConnectionCount: number;
};

/** A real file in a neighbouring directory that has at least one direct edge to this module. */
export type RemoteFileScene = {
  id: string;
  moduleKey: string;
  moduleLabel: string;
  label: string;
  path: string | null;
  nodeIds: readonly string[];
  representativeNodeId: string;
  symbolCount: number;
  connectionCount: number;
  incomingConnectionCount: number;
  outgoingConnectionCount: number;
};

/** A concrete file-to-file dependency crossing the selected directory boundary. */
export type CrossDirectoryFileConnection = {
  id: string;
  localFileId: string;
  remoteFileId: string;
  remoteDirectoryKey: string;
  direction: 'incoming' | 'outgoing';
  connectionCount: number;
};

export type ModuleDetailScene = {
  moduleKey: string;
  moduleLabel: string;
  files: readonly FileScene[];
  internalConnections: readonly FileConnectionScene[];
  directoryConnections: readonly DirectoryConnectionScene[];
  connectedDirectories: readonly ConnectedDirectoryScene[];
  remoteFiles: readonly RemoteFileScene[];
  crossDirectoryConnections: readonly CrossDirectoryFileConnection[];
  fileIdByNodeId: ReadonlyMap<string, string>;
};

export function buildModuleDetailScene(
  moduleKey: string,
  scene: CanvasScene,
  nodes: readonly GraphMapNode[],
  edges: readonly GraphMapEdge[],
): ModuleDetailScene | null {
  const module = scene.modules.find(item => item.key === moduleKey);
  if (!module) return null;

  const visibleNodes = new Map(nodes.filter(node => scene.visibleNodeIds.has(node.stableId)).map(node => [node.stableId, node]));
  const moduleByNodeId = new Map(scene.modules.flatMap(item => item.nodeIds.map(id => [id, item.key] as const)));
  const grouped = new Map<string, GraphMapNode[]>();
  for (const nodeId of module.nodeIds) {
    const node = visibleNodes.get(nodeId);
    if (!node) continue;
    const fileId = node.filePath ? `file:${node.filePath}` : `node:${node.stableId}`;
    const group = grouped.get(fileId) ?? [];
    group.push(node);
    grouped.set(fileId, group);
  }

  const fileIdByNodeId = new Map<string, string>();
  for (const [fileId, group] of grouped) for (const node of group) fileIdByNodeId.set(node.stableId, fileId);
  const internalCounts = new Map<string, number>();
  const externalCounts = new Map<string, number>();
  const directoryCounts = new Map<string, { incoming: number; outgoing: number; fileConnections: number }>();
  const fileDirectoryCounts = new Map<string, number>();
  const remoteGroups = new Map<string, { moduleKey: string; nodes: GraphMapNode[] }>();
  const crossConnectionCounts = new Map<string, number>();

  for (const edge of edges) {
    if (!scene.visibleNodeIds.has(edge.sourceStableId) || !scene.visibleNodeIds.has(edge.targetStableId)) continue;
    const sourceModule = moduleByNodeId.get(edge.sourceStableId);
    const targetModule = moduleByNodeId.get(edge.targetStableId);
    if (sourceModule === moduleKey && targetModule === moduleKey) {
      const sourceFile = fileIdByNodeId.get(edge.sourceStableId);
      const targetFile = fileIdByNodeId.get(edge.targetStableId);
      if (!sourceFile || !targetFile || sourceFile === targetFile) continue;
      const key = `${sourceFile}\u0000${targetFile}`;
      internalCounts.set(key, (internalCounts.get(key) ?? 0) + 1);
      continue;
    }
    if (sourceModule !== moduleKey && targetModule !== moduleKey) continue;
    const outgoing = sourceModule === moduleKey;
    const localNodeId = outgoing ? edge.sourceStableId : edge.targetStableId;
    const remoteNodeId = outgoing ? edge.targetStableId : edge.sourceStableId;
    const directoryKey = outgoing ? targetModule : sourceModule;
    const fileId = fileIdByNodeId.get(localNodeId);
    if (!fileId || !directoryKey) continue;
    externalCounts.set(fileId, (externalCounts.get(fileId) ?? 0) + 1);
    const directory = directoryCounts.get(directoryKey) ?? { incoming: 0, outgoing: 0, fileConnections: 0 };
    if (outgoing) directory.outgoing += 1; else directory.incoming += 1;
    directory.fileConnections += 1;
    directoryCounts.set(directoryKey, directory);
    const key = `${fileId}\u0000${directoryKey}\u0000${outgoing ? 'outgoing' : 'incoming'}`;
    fileDirectoryCounts.set(key, (fileDirectoryCounts.get(key) ?? 0) + 1);

    const remoteNode = visibleNodes.get(remoteNodeId);
    if (!remoteNode) continue;
    const remoteFileId = `remote:${directoryKey}:${remoteNode.filePath ?? remoteNode.stableId}`;
    const group = remoteGroups.get(remoteFileId) ?? { moduleKey: directoryKey, nodes: [] };
    if (!group.nodes.some(node => node.stableId === remoteNode.stableId)) group.nodes.push(remoteNode);
    remoteGroups.set(remoteFileId, group);
    const crossKey = `${fileId}\u0000${remoteFileId}\u0000${directoryKey}\u0000${outgoing ? 'outgoing' : 'incoming'}`;
    crossConnectionCounts.set(crossKey, (crossConnectionCounts.get(crossKey) ?? 0) + 1);
  }

  const internalByFile = new Map<string, number>();
  for (const [key, count] of internalCounts) {
    const [source, target] = key.split('\u0000');
    internalByFile.set(source, (internalByFile.get(source) ?? 0) + count);
    internalByFile.set(target, (internalByFile.get(target) ?? 0) + count);
  }
  const files = [...grouped.entries()].map(([id, group]) => {
    const path = group[0].filePath;
    return {
      id,
      label: path?.split('/').pop() ?? group[0].displayName,
      path,
      nodeIds: group.map(node => node.stableId).sort(),
      representativeNodeId: [...group].sort((a, b) => a.startLine == null ? 1 : b.startLine == null ? -1 : a.startLine - b.startLine)[0].stableId,
      symbolCount: group.length,
      internalConnectionCount: internalByFile.get(id) ?? 0,
      externalConnectionCount: externalCounts.get(id) ?? 0,
    };
  }).sort((a, b) => b.externalConnectionCount - a.externalConnectionCount || b.internalConnectionCount - a.internalConnectionCount || a.label.localeCompare(b.label));

  const internalConnections = [...internalCounts.entries()].map(([key, connectionCount]) => {
    const [sourceFileId, targetFileId] = key.split('\u0000');
    return { id: `${sourceFileId}→${targetFileId}`, sourceFileId, targetFileId, connectionCount };
  }).sort((a, b) => b.connectionCount - a.connectionCount || a.id.localeCompare(b.id));
  const directoryConnections = [...fileDirectoryCounts.entries()].map(([key, connectionCount]) => {
    const [fileId, directoryKey, direction] = key.split('\u0000');
    return { id: `${fileId}→${directoryKey}:${direction}`, fileId, directoryKey, direction: direction as 'incoming' | 'outgoing', connectionCount };
  }).sort((a, b) => b.connectionCount - a.connectionCount || a.id.localeCompare(b.id));
  const connectedDirectories = [...directoryCounts.entries()].map(([key, counts]) => ({
    key,
    label: scene.modules.find(item => item.key === key)?.label ?? key,
    incomingCount: counts.incoming,
    outgoingCount: counts.outgoing,
    fileConnectionCount: counts.fileConnections,
  })).sort((a, b) => b.fileConnectionCount - a.fileConnectionCount || a.label.localeCompare(b.label));

  const remoteConnectionCounts = new Map<string, { total: number; incoming: number; outgoing: number }>();
  for (const [key, count] of crossConnectionCounts) {
    const [, remoteFileId,, direction] = key.split('\u0000');
    const totals = remoteConnectionCounts.get(remoteFileId) ?? { total: 0, incoming: 0, outgoing: 0 };
    totals.total += count;
    if (direction === 'incoming') totals.incoming += count; else totals.outgoing += count;
    remoteConnectionCounts.set(remoteFileId, totals);
  }
  const remoteFiles = [...remoteGroups.entries()].map(([id, group]) => {
    const ordered = [...group.nodes].sort((a, b) => a.startLine == null ? 1 : b.startLine == null ? -1 : a.startLine - b.startLine);
    const representative = ordered[0];
    const totals = remoteConnectionCounts.get(id) ?? { total: 0, incoming: 0, outgoing: 0 };
    return {
      id,
      moduleKey: group.moduleKey,
      moduleLabel: scene.modules.find(item => item.key === group.moduleKey)?.label ?? group.moduleKey,
      label: representative.filePath?.split('/').pop() ?? representative.displayName,
      path: representative.filePath,
      nodeIds: group.nodes.map(node => node.stableId).sort(),
      representativeNodeId: representative.stableId,
      symbolCount: group.nodes.length,
      connectionCount: totals.total,
      incomingConnectionCount: totals.incoming,
      outgoingConnectionCount: totals.outgoing,
    };
  }).sort((a, b) => b.connectionCount - a.connectionCount || a.moduleLabel.localeCompare(b.moduleLabel) || a.label.localeCompare(b.label));
  const crossDirectoryConnections = [...crossConnectionCounts.entries()].map(([key, connectionCount]) => {
    const [localFileId, remoteFileId, remoteDirectoryKey, direction] = key.split('\u0000');
    return { id: `${localFileId}→${remoteFileId}:${direction}`, localFileId, remoteFileId, remoteDirectoryKey, direction: direction as 'incoming' | 'outgoing', connectionCount };
  }).sort((a, b) => b.connectionCount - a.connectionCount || a.id.localeCompare(b.id));

  return { moduleKey, moduleLabel: module.label, files, internalConnections, directoryConnections, connectedDirectories, remoteFiles, crossDirectoryConnections, fileIdByNodeId };
}

export function connectedFileDetail(detail: ModuleDetailScene, selectedFileId: string | null = null): ModuleDetailScene {
  const connectedIds = new Set(detail.files.filter(file => file.externalConnectionCount > 0).map(file => file.id));
  if (selectedFileId) connectedIds.add(selectedFileId);
  if (!connectedIds.size) return detail;
  const directoryConnections = detail.directoryConnections.filter(connection => connectedIds.has(connection.fileId));
  const directoryKeys = new Set(directoryConnections.map(connection => connection.directoryKey));
  const crossDirectoryConnections = detail.crossDirectoryConnections.filter(connection => connectedIds.has(connection.localFileId));
  const remoteIds = new Set(crossDirectoryConnections.map(connection => connection.remoteFileId));
  return {
    ...detail,
    files: detail.files.filter(file => connectedIds.has(file.id)),
    internalConnections: detail.internalConnections.filter(connection => connectedIds.has(connection.sourceFileId) && connectedIds.has(connection.targetFileId)),
    directoryConnections,
    connectedDirectories: detail.connectedDirectories.filter(directory => directoryKeys.has(directory.key)),
    remoteFiles: detail.remoteFiles.filter(file => remoteIds.has(file.id)),
    crossDirectoryConnections,
  };
}

export function directoryFileDetail(detail: ModuleDetailScene, directoryKey: string, selectedFileId: string | null = null): ModuleDetailScene {
  const fileIds = new Set(detail.directoryConnections.filter(connection => connection.directoryKey === directoryKey).map(connection => connection.fileId));
  if (selectedFileId) fileIds.add(selectedFileId);
  if (!fileIds.size) return connectedFileDetail(detail, selectedFileId);
  const directoryConnections = detail.directoryConnections.filter(connection => fileIds.has(connection.fileId) && connection.directoryKey === directoryKey);
  const crossDirectoryConnections = detail.crossDirectoryConnections.filter(connection => fileIds.has(connection.localFileId) && connection.remoteDirectoryKey === directoryKey);
  const remoteIds = new Set(crossDirectoryConnections.map(connection => connection.remoteFileId));
  return {
    ...detail,
    files: detail.files.filter(file => fileIds.has(file.id)),
    internalConnections: detail.internalConnections.filter(connection => fileIds.has(connection.sourceFileId) && fileIds.has(connection.targetFileId)),
    directoryConnections,
    connectedDirectories: detail.connectedDirectories.filter(directory => directory.key === directoryKey),
    remoteFiles: detail.remoteFiles.filter(file => remoteIds.has(file.id)),
    crossDirectoryConnections,
  };
}
