import type { GraphMapEdge, GraphMapNode } from './api';
import { moduleKeyFor } from './canvas-scene';

export type TraceResult = { kind: 'path'; nodeIds: readonly string[]; edgeIds: readonly string[] } | { kind: 'no_path' };
export type ImpactResult = { nodeIds: ReadonlySet<string>; edgeIds: ReadonlySet<string>; depth: number; isTruncated: boolean; moduleCounts: ReadonlyMap<string, number> };

export function findDirectedTrace(start: string, end: string, edges: readonly GraphMapEdge[]): TraceResult {
  if (start === end) return { kind: 'path', nodeIds: [start], edgeIds: [] };
  const outgoing = new Map<string, GraphMapEdge[]>(); edges.forEach(edge => { const group = outgoing.get(edge.sourceStableId) ?? []; group.push(edge); outgoing.set(edge.sourceStableId, group); });
  const queue = [start]; const previous = new Map<string, GraphMapEdge>(); const visited = new Set([start]);
  while (queue.length) { const current = queue.shift()!; for (const edge of outgoing.get(current) ?? []) if (!visited.has(edge.targetStableId)) { visited.add(edge.targetStableId); previous.set(edge.targetStableId, edge); if (edge.targetStableId === end) { const nodes = [end]; const edgeIds: string[] = []; for (let cursor = end; cursor !== start;) { const link = previous.get(cursor)!; edgeIds.unshift(link.edgeId); cursor = link.sourceStableId; nodes.unshift(cursor); } return { kind: 'path', nodeIds: nodes, edgeIds }; } queue.push(edge.targetStableId); } }
  return { kind: 'no_path' };
}

export function findDownstreamImpact(start: string, depth: number, nodes: readonly GraphMapNode[], edges: readonly GraphMapEdge[], maxNodes = 1_000): ImpactResult {
  const incoming = new Map<string, GraphMapEdge[]>(); edges.forEach(edge => { const group = incoming.get(edge.targetStableId) ?? []; group.push(edge); incoming.set(edge.targetStableId, group); });
  const nodeIds = new Set([start]); const edgeIds = new Set<string>(); const queue: Array<{ id: string; depth: number }> = [{ id: start, depth: 0 }]; let isTruncated = false;
  while (queue.length) { const current = queue.shift()!; if (current.depth >= depth) continue; for (const edge of incoming.get(current.id) ?? []) { edgeIds.add(edge.edgeId); if (!nodeIds.has(edge.sourceStableId)) { if (nodeIds.size >= maxNodes) { isTruncated = true; continue; } nodeIds.add(edge.sourceStableId); queue.push({ id: edge.sourceStableId, depth: current.depth + 1 }); } } }
  const nodeById = new Map(nodes.map(node => [node.stableId, node])); const moduleCounts = new Map<string, number>(); nodeIds.forEach(id => { const node = nodeById.get(id); if (!node) return; const key = moduleKeyFor(node); moduleCounts.set(key, (moduleCounts.get(key) ?? 0) + 1); });
  return { nodeIds, edgeIds, depth, isTruncated, moduleCounts };
}
