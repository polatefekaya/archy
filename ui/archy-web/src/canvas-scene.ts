import type { GraphMapEdge, GraphMapNode } from './api';

export type CanvasFilter = {
  nodeKinds: readonly string[];
  providers: readonly string[];
  minConfidence: number | null;
  references: 'all' | 'internal' | 'external';
};

export type ModuleScene = {
  key: string;
  label: string;
  nodeIds: readonly string[];
  nodeCount: number;
  crossModuleDependencyCount: number;
};

export type ModuleEdgeScene = {
  id: string;
  sourceModuleKey: string;
  targetModuleKey: string;
  dependencyCount: number;
};

export type CanvasScene = {
  modules: readonly ModuleScene[];
  edges: readonly ModuleEdgeScene[];
  visibleNodeIds: ReadonlySet<string>;
  filteredOutNodeCount: number;
  isEmpty: boolean;
};

export const defaultCanvasFilter: CanvasFilter = { nodeKinds: [], providers: [], minConfidence: null, references: 'all' };

export function moduleKeyFor(node: GraphMapNode) {
  if (!node.filePath) return node.nodeKind === 'unresolved_reference' ? 'References/External' : `Generated/${node.provider}`;
  const segments = node.filePath.split('/').filter(Boolean);
  const featureIndex = segments.indexOf('Features');
  if (featureIndex >= 0) return segments.slice(featureIndex, featureIndex + 2).join('/');
  return segments.slice(0, 2).join('/') || 'Workspace';
}

export function moduleLabel(key: string) { return key.replace('Features/', '').replace('Generated/', ''); }

export function matchesCanvasFilter(node: GraphMapNode, filter: CanvasFilter) {
  if (filter.nodeKinds.length && !filter.nodeKinds.includes(node.nodeKind)) return false;
  if (filter.providers.length && !filter.providers.includes(node.provider)) return false;
  if (filter.minConfidence !== null && node.confidence < filter.minConfidence) return false;
  if (filter.references === 'internal' && !node.filePath) return false;
  if (filter.references === 'external' && node.filePath) return false;
  return true;
}

/** Produces the complete module-level scene. Renderers must not independently filter nodes or edges. */
export function buildModuleScene(nodes: readonly GraphMapNode[], edges: readonly GraphMapEdge[], filter: CanvasFilter = defaultCanvasFilter): CanvasScene {
  const visible = nodes.filter(node => matchesCanvasFilter(node, filter));
  const visibleNodeIds = new Set(visible.map(node => node.stableId));
  const moduleByNodeId = new Map(visible.map(node => [node.stableId, moduleKeyFor(node)]));
  const modulesByKey = new Map<string, GraphMapNode[]>();
  visible.forEach(node => { const key = moduleKeyFor(node); const group = modulesByKey.get(key) ?? []; group.push(node); modulesByKey.set(key, group); });
  const edgeCounts = new Map<string, number>();
  for (const edge of edges) {
    if (!visibleNodeIds.has(edge.sourceStableId) || !visibleNodeIds.has(edge.targetStableId)) continue;
    const source = moduleByNodeId.get(edge.sourceStableId)!;
    const target = moduleByNodeId.get(edge.targetStableId)!;
    if (source === target) continue;
    const key = `${source}\u0000${target}`;
    edgeCounts.set(key, (edgeCounts.get(key) ?? 0) + 1);
  }
  const dependenciesByModule = new Map<string, number>();
  const moduleEdges = [...edgeCounts.entries()].map(([key, dependencyCount]) => {
    const [sourceModuleKey, targetModuleKey] = key.split('\u0000');
    dependenciesByModule.set(sourceModuleKey, (dependenciesByModule.get(sourceModuleKey) ?? 0) + dependencyCount);
    dependenciesByModule.set(targetModuleKey, (dependenciesByModule.get(targetModuleKey) ?? 0) + dependencyCount);
    return { id: `${sourceModuleKey}→${targetModuleKey}`, sourceModuleKey, targetModuleKey, dependencyCount };
  }).sort((a, b) => b.dependencyCount - a.dependencyCount || a.id.localeCompare(b.id));
  const modules = [...modulesByKey.entries()].map(([key, items]) => ({ key, label: moduleLabel(key), nodeIds: items.map(node => node.stableId).sort(), nodeCount: items.length, crossModuleDependencyCount: dependenciesByModule.get(key) ?? 0 })).sort((a, b) => b.crossModuleDependencyCount - a.crossModuleDependencyCount || a.label.localeCompare(b.label));
  return { modules, edges: moduleEdges, visibleNodeIds, filteredOutNodeCount: nodes.length - visible.length, isEmpty: visible.length === 0 };
}
