import { useState, type FormEvent } from 'react';
import { getSummaryVersions, type SummaryVersion } from '../api';
import { Panel } from './Panel';

export function SummaryDebtPanel() {
  const [summaryId, setSummaryId] = useState('');
  const [versions, setVersions] = useState<SummaryVersion[]>([]);
  const [error, setError] = useState<string | null>(null);

  async function load(event: FormEvent) {
    event.preventDefault(); setError(null);
    const result = await getSummaryVersions(summaryId);
    setVersions(result.value?.items ?? []); setError(result.error ?? null);
  }

  return <Panel title="Summary freshness" description="Compare summary versions with the graph revision they were generated from.">
    <form className="flex gap-2" onSubmit={load}><label className="sr-only" htmlFor="summary-id">Summary ID</label><input id="summary-id" required value={summaryId} onChange={event => setSummaryId(event.target.value)} placeholder="summary ID" className="min-w-0 flex-1 rounded-md border border-slate-600 bg-slate-950/50 px-3 py-2 text-sm" /><button className="rounded-md border border-sky-400/60 px-3 py-2 text-sm text-sky-200 hover:bg-sky-400/10">Inspect</button></form>
    {error && <p className="mt-3 text-xs text-amber-200">{error}</p>}
    {versions.length > 0 && <ol className="mt-3 max-h-56 space-y-2 overflow-auto">{versions.map(version => <li key={version.summaryVersionId} className="rounded-md border border-slate-700 bg-slate-950/40 p-3 text-xs"><div className="flex items-center justify-between gap-3"><p className="font-medium text-slate-100">Version {version.version} · revision {version.sourceGraphRevision}</p><FreshnessBadge state={version.staleness} /></div><p className="mt-1 text-slate-400">Generated {new Date(version.createdAtUtc).toLocaleString()} · {version.provider}/{version.model}</p><p className="mt-2 whitespace-pre-wrap text-slate-300">{version.summaryText}</p>{version.staleness !== 'current' && <p className="mt-2 text-amber-200">This summary is {version.staleness}; regenerate it through the deferred summary workflow before treating it as current documentation.</p>}</li>)}</ol>}
    {!error && versions.length === 0 && <p className="mt-3 text-xs text-slate-500">Inspect a summary history to distinguish current documentation from stale dependent summaries.</p>}
  </Panel>;
}

function FreshnessBadge({ state }: { state: string }) {
  const className = state === 'current' ? 'bg-emerald-400/15 text-emerald-200' : state === 'stale' ? 'bg-amber-400/15 text-amber-100' : 'bg-slate-500/20 text-slate-200';
  return <span className={`rounded-full px-2 py-0.5 font-medium ${className}`}>{state}</span>;
}
