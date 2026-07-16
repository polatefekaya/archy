import { useState, type FormEvent } from 'react';
import { getHealth, type HealthSnapshot } from '../api';
import { Panel } from './Panel';

export function HealthDashboard() {
  const [snapshotId, setSnapshotId] = useState('');
  const [snapshot, setSnapshot] = useState<HealthSnapshot | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function load(event: FormEvent) {
    event.preventDefault(); setError(null);
    const result = await getHealth(snapshotId);
    setSnapshot(result.value ?? null); setError(result.error ?? null);
  }

  return <Panel title="Health dashboard" description="Load a persisted health snapshot to see the evidence and weighting behind its score.">
    <form className="flex gap-2" onSubmit={load}><label className="sr-only" htmlFor="health-snapshot">Health snapshot ID</label><input id="health-snapshot" required value={snapshotId} onChange={event => setSnapshotId(event.target.value)} placeholder="health snapshot ID" className="min-w-0 flex-1 rounded-md border border-slate-600 bg-slate-950/50 px-3 py-2 text-sm" /><button className="rounded-md border border-sky-400/60 px-3 py-2 text-sm text-sky-200 hover:bg-sky-400/10">Load</button></form>
    {error && <p className="mt-3 text-xs text-amber-200">{error}</p>}
    {snapshot && <div className="mt-4"><div className="flex items-baseline justify-between"><p className="text-3xl font-semibold text-sky-200">{snapshot.score.toFixed(1)}</p><p className="text-xs text-slate-400">Revision {snapshot.graphRevision} · {snapshot.calculationVersion}</p></div><ul className="mt-3 space-y-2">{snapshot.items.map(component => <li key={component.key} className="rounded-md bg-slate-950/40 p-2 text-xs"><div className="flex justify-between gap-3"><span className="font-medium text-slate-200">{component.key}</span><span className="text-slate-400">{component.weightedContribution.toFixed(2)}</span></div><p className="mt-1 text-slate-400">Raw {component.rawValue.toFixed(2)} · weight {component.weight.toFixed(2)}</p></li>)}</ul></div>}
    {!snapshot && !error && <p className="mt-3 text-xs text-slate-500">Load a persisted health snapshot to trace its score back to individual contributing facts.</p>}
  </Panel>;
}
