import { useState, type FormEvent } from 'react';
import { getClusterMembers, getDuplicateObservations, type ClusterMember, type DuplicateObservation } from '../api';
import { Panel } from './Panel';

export function AdvisoryWorkbench() {
  const [findingId, setFindingId] = useState('');
  const [observations, setObservations] = useState<DuplicateObservation[]>([]);
  const [message, setMessage] = useState<string | null>(null);
  const [clusterRevisionId, setClusterRevisionId] = useState('');
  const [clusterId, setClusterId] = useState('');
  const [members, setMembers] = useState<ClusterMember[]>([]);
  const [clusterMessage, setClusterMessage] = useState<string | null>(null);

  async function inspect(event: FormEvent) {
    event.preventDefault(); setMessage(null);
    const result = await getDuplicateObservations(findingId);
    setObservations(result.value?.items ?? []); setMessage(result.error ?? null);
  }

  async function inspectCluster(event: FormEvent) {
    event.preventDefault(); setClusterMessage(null);
    const result = await getClusterMembers(clusterRevisionId, clusterId);
    setMembers(result.value?.items ?? []); setClusterMessage(result.error ?? null);
  }

  return <Panel title="Advisory workbenches" description="Review advisory evidence before recording an explicit decision; advisories are never enforcement rules." action={<span className="text-xs text-slate-500">Evidence review only</span>}>
    <div className="grid gap-5 lg:grid-cols-2">
      <section><h3 className="text-sm font-medium text-slate-100">Duplicate review</h3><p className="mt-1 text-xs leading-5 text-slate-400">Inspect historical evidence before accepting, ignoring, or modifying a duplicate advisory through the recorded-decision MCP tool.</p><form onSubmit={inspect} className="mt-3 flex gap-2"><label className="sr-only" htmlFor="duplicate-finding">Duplicate finding ID</label><input id="duplicate-finding" required value={findingId} onChange={event => setFindingId(event.target.value)} placeholder="duplicate finding ID" className="min-w-0 flex-1 rounded-md border border-slate-600 bg-slate-950/50 px-3 py-2 text-sm" /><button className="rounded-md border border-sky-400/60 px-3 py-2 text-sm text-sky-200 hover:bg-sky-400/10">Inspect</button></form>{message && <p className="mt-2 text-xs text-amber-200">{message}</p>}{observations.length > 0 && <ol className="mt-3 max-h-40 space-y-2 overflow-auto">{observations.map(item => <li key={item.observationId} className="rounded-md bg-slate-950/40 p-2 text-xs"><p className="font-medium text-slate-200">Confidence {item.confidence.toFixed(2)} · revision {item.graphRevision}</p><pre className="mt-1 whitespace-pre-wrap break-words text-slate-400">{item.rationale}</pre></li>)}</ol>}</section>
      <section><h3 className="text-sm font-medium text-slate-100">Placement review</h3><p className="mt-1 text-xs leading-5 text-slate-400">Inspect the persisted cluster membership behind split/append advice. Archy deliberately separates uncertain placement signals from deterministic enforcement.</p><form onSubmit={inspectCluster} className="mt-3 grid gap-2 sm:grid-cols-2"><label className="sr-only" htmlFor="cluster-revision">Cluster revision ID</label><input id="cluster-revision" required value={clusterRevisionId} onChange={event => setClusterRevisionId(event.target.value)} placeholder="cluster revision ID" className="rounded-md border border-slate-600 bg-slate-950/50 px-3 py-2 text-sm" /><label className="sr-only" htmlFor="cluster-id">Cluster ID</label><input id="cluster-id" required value={clusterId} onChange={event => setClusterId(event.target.value)} placeholder="cluster ID" className="rounded-md border border-slate-600 bg-slate-950/50 px-3 py-2 text-sm" /><button className="w-fit rounded-md border border-sky-400/60 px-3 py-2 text-sm text-sky-200 hover:bg-sky-400/10">Inspect cluster</button></form>{clusterMessage && <p className="mt-2 text-xs text-amber-200">{clusterMessage}</p>}{members.length > 0 && <ol className="mt-3 max-h-40 space-y-2 overflow-auto">{members.map(member => <li key={`${member.targetKind}:${member.targetStableId}`} className="rounded-md bg-slate-950/40 p-2 text-xs"><span className="text-slate-200">{member.targetStableId}</span><span className="ml-2 text-slate-400">{member.targetKind} · membership {member.membershipWeight.toFixed(2)}</span></li>)}</ol>}<ul className="mt-3 space-y-2 text-xs text-slate-300"><li className="rounded-md bg-slate-950/40 p-2">Record a resolution with MCP <code className="text-sky-300">record_decision</code>; its session event is replayable.</li><li className="rounded-md bg-slate-950/40 p-2">Use a decision note to document why an advisory was accepted, ignored, or changed.</li></ul></section>
    </div>
  </Panel>;
}
