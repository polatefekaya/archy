import { useEffect, useState } from 'react';
import { getDecisions, getTraversal, type Decision, type GraphTraversal } from '../api';
import type { SelectedGraphItem } from '../graph';
import { Panel } from './Panel';
import { dispatchCanvasCommand } from '../canvas-commands';

export function InspectorPanel({ selected }: { selected: SelectedGraphItem }) {
  const [traversal, setTraversal] = useState<GraphTraversal | null>(null);
  const [decisions, setDecisions] = useState<Decision[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!selected) { setTraversal(null); setDecisions([]); setError(null); return; }
    const controller = new AbortController();
    const target = selected.kind === 'node'
      ? { kind: 'graph_node', stableId: selected.node.stableId }
      : { kind: 'graph_edge', stableId: selected.edge.edgeId };
    void Promise.all([
      selected.kind === 'node' ? getTraversal(target.stableId, 'dependents', 3, controller.signal) : Promise.resolve({ value: null } as { value: null; error?: never }),
      getDecisions(target.kind, target.stableId, controller.signal),
    ]).then(([blast, decisionPage]) => {
      if (blast.error || decisionPage.error) setError(blast.error ?? decisionPage.error ?? null);
      setTraversal(blast.value ?? null);
      setDecisions(decisionPage.value?.items ?? []);
    });
    return () => controller.abort();
  }, [selected]);

  const selectedNode = selected?.kind === 'node' ? selected.node : null;
  return <Panel title="Inspector" description="Selection details are loaded from recorded graph evidence and decisions." action={selectedNode ? <div className="flex gap-2"><button type="button" onClick={() => dispatchCanvasCommand('impact')} className="text-xs text-indigo-700 hover:text-indigo-900">Impact</button><button type="button" onClick={() => dispatchCanvasCommand({ command: 'trace-from', stableId: selectedNode.stableId })} className="text-xs text-indigo-700 hover:text-indigo-900">Trace from</button></div> : selected ? <span className="text-xs text-slate-500">Stored evidence</span> : undefined}>
    {!selected && <p className="text-sm leading-6 text-slate-500">Select a node or edge to inspect source evidence, direct context, and recorded decisions.</p>}
    {selected?.kind === 'node' && <div className="space-y-4 text-sm">
      <div><p className="font-medium text-slate-900">{selected.node.displayName}</p><p className="text-slate-500">{selected.node.nodeKind} · {selected.node.provider} · confidence {selected.node.confidence.toFixed(2)}</p></div>
      <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 text-xs"><dt className="text-slate-500">Stable ID</dt><dd className="break-all text-slate-700">{selected.node.stableId}</dd><dt className="text-slate-500">Source</dt><dd className="text-slate-700">{selected.node.filePath ?? 'No source path recorded'}{selected.node.startLine ? `:${selected.node.startLine}${selected.node.endLine ? `–${selected.node.endLine}` : ''}` : ''}</dd></dl>
      <Evidence title="Node evidence" value={selected.node.evidence ?? 'The canvas map intentionally omits duplicated evidence payloads. Architecture evidence is loaded on demand by the source and traversal APIs.'} />
      <TraversalSummary traversal={traversal} />
    </div>}
    {selected?.kind === 'edge' && <div className="space-y-4 text-sm">
      <div><p className="font-medium text-slate-900">{selected.edge.edgeKind}</p><p className="break-all text-slate-500">{selected.edge.sourceStableId} → {selected.edge.targetStableId}</p></div>
      <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 text-xs"><dt className="text-slate-500">Provider</dt><dd className="text-slate-700">{selected.edge.provider ?? 'Compact map projection'}</dd><dt className="text-slate-500">Confidence</dt><dd className="text-slate-700">{selected.edge.confidence.toFixed(2)}</dd><dt className="text-slate-500">Join key</dt><dd className="break-all text-slate-700">{selected.edge.normalizedJoinKey ?? 'Not included in the compact map'}</dd></dl>
      <Evidence title="Why this dependency exists" value={selected.edge.evidence ?? 'The map omits repeated edge evidence for rendering performance. Select a node to load its dependency context on demand.'} />
    </div>}
    {error && <p className="mt-4 border border-amber-300 bg-amber-50 p-3 text-xs text-amber-900">Some inspector context is unavailable: {error}</p>}
    {selected && <DecisionList decisions={decisions} />}
  </Panel>;
}

function TraversalSummary({ traversal }: { traversal: GraphTraversal | null }) {
  if (!traversal) return null;
  return <div className="border-l-2 border-indigo-600 bg-slate-50 p-3"><p className="text-xs font-semibold uppercase tracking-wide text-indigo-700">Blast radius</p><p className="mt-1 text-sm text-slate-700">{traversal.edges.length} dependent edge{traversal.edges.length === 1 ? '' : 's'} through depth 3.</p>{traversal.isTruncated && <p className="mt-1 text-xs text-amber-700">Results are truncated; narrow the traversal or inspect a lower-depth path.</p>}</div>;
}

function Evidence({ title, value }: { title: string; value: string }) {
  return <details className="border border-slate-200 bg-slate-50 p-3"><summary className="cursor-pointer text-xs font-medium text-indigo-700">{title}</summary><pre className="mt-2 max-h-40 overflow-auto whitespace-pre-wrap break-words text-xs text-slate-600">{value}</pre></details>;
}

function DecisionList({ decisions }: { decisions: Decision[] }) {
  return <div className="mt-5 border-t border-slate-200 pt-4"><p className="text-xs font-semibold uppercase tracking-wide text-slate-500">Recorded decisions</p>{decisions.length === 0 ? <p className="mt-2 text-xs text-slate-500">No decision has been recorded for this target.</p> : <ol className="mt-2 space-y-2">{decisions.map(decision => <li key={decision.decisionId} className="border border-slate-200 bg-slate-50 p-2 text-xs"><p className="font-medium text-slate-800">{decision.resolution} · {decision.decisionType}</p><p className="mt-1 text-slate-500">{decision.note ?? 'No note'} · {new Date(decision.occurredAtUtc).toLocaleString()}</p></li>)}</ol>}</div>;
}
