export type ApiResult<T> = { value: T; error?: never } | { value?: never; error: string };

export type WorkspaceStatus = {
  name: string;
  version: string;
  bindAddress: string;
  apiVersion: string;
};

export type GraphNode = {
  stableId: string;
  nodeKind: string;
  canonicalKey: string;
  displayName: string;
  filePath: string | null;
  startLine: number | null;
  endLine: number | null;
  provider: string;
  confidence: number;
  evidence: string;
  contentHash: string;
};

export type GraphEdge = {
  edgeId: string;
  sourceStableId: string;
  targetStableId: string;
  edgeKind: string;
  normalizedJoinKey: string | null;
  provider: string;
  confidence: number;
  evidence: string;
};

export type GraphPage<T> = {
  schema: string;
  revision: number;
  offset: number;
  limit: number;
  totalCount: number;
  items: T[];
};

export type GraphExplorer = {
  schema: string;
  revision: number;
  totalNodeCount: number;
  totalEdgeCount: number;
  centerStableId: string;
  isFocused: boolean;
  nodes: GraphNode[];
  edges: GraphEdge[];
};

export type GraphMapNode = Pick<GraphNode, 'stableId' | 'nodeKind' | 'canonicalKey' | 'displayName' | 'filePath' | 'provider' | 'confidence'> & {
  startLine?: number | null;
  endLine?: number | null;
  evidence?: string;
};
export type GraphMapEdge = Pick<GraphEdge, 'edgeId' | 'sourceStableId' | 'targetStableId' | 'edgeKind' | 'confidence'> & {
  provider?: string;
  normalizedJoinKey?: string | null;
  evidence?: string;
};
export type GraphMap = {
  schema: string;
  revision: number;
  totalNodeCount: number;
  totalEdgeCount: number;
  isTruncated: boolean;
  nodes: GraphMapNode[];
  edges: GraphMapEdge[];
};

export type GraphSearch = { schema: string; items: GraphNode[] };

export type GraphTraversal = {
  schema: string;
  revision: number;
  startStableId: string;
  direction: 'dependencies' | 'dependents';
  isTruncated: boolean;
  edges: Array<GraphEdge & { depth: number }>;
};

export type EventStreamItem = {
  eventId: string;
  sessionId: string;
  sequence: number;
  kind: string;
  graphRevision: number | null;
  occurredAtUtc: string;
  payload?: unknown;
  payloadOmitted?: boolean;
};

export type Capability = { name: string; ready: boolean; warning: string | null };

export type Decision = {
  decisionId: string;
  decisionType: string;
  resolution: string;
  note: string | null;
  actorKind: string;
  actorId: string;
  graphRevision: number | null;
  occurredAtUtc: string;
  targets: Array<{ kind: string; stableId: string }>;
};

export type DecisionPage = { schema: string; offset: number; limit: number; totalCount: number; items: Decision[] };

export type HealthComponent = {
  key: string;
  rawValue: number;
  weight: number;
  weightedContribution: number;
  detail: string;
};

export type HealthSnapshot = {
  schema: string;
  snapshotId: string;
  graphRevision: number;
  calculationVersion: string;
  score: number;
  items: HealthComponent[];
};

export type DuplicateObservation = {
  observationId: string;
  graphRevision: number;
  aggregationVersion: string;
  confidence: number;
  rationale: string;
  observedAtUtc: string;
};

export type DuplicatePage = { schema: string; findingId: string; totalCount: number; items: DuplicateObservation[] };

export type SummaryVersion = {
  summaryVersionId: string;
  version: number;
  sourceGraphRevision: number;
  staleness: 'current' | 'stale' | 'missing' | string;
  summaryText: string;
  summaryTextTruncated: boolean;
  englishDiff: string;
  englishDiffTruncated: boolean;
  provider: string;
  model: string;
  createdAtUtc: string;
};

export type SummaryPage = { schema: string; summaryId: string; totalCount: number; items: SummaryVersion[] };

export type ClusterMember = { targetKind: string; targetStableId: string; membershipWeight: number };
export type ClusterPage = { schema: string; clusterRevisionId: string; clusterId: string; clusterKey: string; graphRevision: number; totalCount: number; items: ClusterMember[] };

async function getJson<T>(path: string, signal?: AbortSignal): Promise<ApiResult<T>> {
  try {
    const response = await fetch(path, { signal, headers: { Accept: 'application/json' } });
    if (response.ok) return { value: await response.json() as T };
    const body = await response.json().catch(() => null) as { message?: string } | null;
    return { error: body?.message ?? `Request failed with HTTP ${response.status}.` };
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') return { error: 'Request cancelled.' };
    return { error: 'Archy local host is unavailable.' };
  }
}

export const getStatus = (signal?: AbortSignal) => getJson<WorkspaceStatus>('/api/v1/status', signal);
export const getCapabilities = (signal?: AbortSignal) => getJson<{ schema: string; capabilities: Capability[] }>('/api/v1/capabilities', signal);

export function getGraph<T>(kind: 'nodes' | 'edges', revision?: number, signal?: AbortSignal) {
  const query = new URLSearchParams({ limit: '200' });
  if (revision) query.set('revision', String(revision));
  return getJson<GraphPage<T>>(`/api/v1/graph/${kind}?${query}`, signal);
}

export function getGraphExplorer(focus?: string, signal?: AbortSignal) {
  const query = new URLSearchParams({ maxNodes: '72' });
  if (focus) query.set('focus', focus);
  return getJson<GraphExplorer>(`/api/v1/graph/explorer?${query}`, signal);
}

export const getGraphMap = (signal?: AbortSignal) => getJson<GraphMap>('/api/v1/graph/map', signal);

export function searchGraphNodes(text: string, signal?: AbortSignal) {
  return getJson<GraphSearch>(`/api/v1/graph/search?q=${encodeURIComponent(text)}`, signal);
}

export function getTraversal(stableId: string, direction: 'dependencies' | 'dependents', depth = 3, signal?: AbortSignal) {
  const query = new URLSearchParams({ stableId, direction, depth: String(depth) });
  return getJson<GraphTraversal>(`/api/v1/graph/traverse?${query}`, signal);
}

export function runQuery(text: string, revision?: number, signal?: AbortSignal) {
  const query = new URLSearchParams({ text });
  if (revision) query.set('revision', String(revision));
  return getJson<GraphTraversal>(`/api/v1/query?${query}`, signal);
}

export function getDecisions(targetKind: string, stableId: string, signal?: AbortSignal) {
  const query = new URLSearchParams({ targetKind, stableId, limit: '20' });
  return getJson<DecisionPage>(`/api/v1/decisions?${query}`, signal);
}

export function getHealth(snapshotId: string, signal?: AbortSignal) {
  return getJson<HealthSnapshot>(`/api/v1/health/${encodeURIComponent(snapshotId)}?limit=100`, signal);
}

export function getDuplicateObservations(findingId: string, signal?: AbortSignal) {
  return getJson<DuplicatePage>(`/api/v1/duplicates/${encodeURIComponent(findingId)}?limit=100`, signal);
}

export function getSummaryVersions(summaryId: string, signal?: AbortSignal) {
  return getJson<SummaryPage>(`/api/v1/summaries/${encodeURIComponent(summaryId)}?limit=20`, signal);
}

export function getClusterMembers(clusterRevisionId: string, clusterId: string, signal?: AbortSignal) {
  return getJson<ClusterPage>(`/api/v1/clusters/${encodeURIComponent(clusterRevisionId)}/${encodeURIComponent(clusterId)}?limit=100`, signal);
}
