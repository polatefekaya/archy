import { useEffect, useRef, useState, type Dispatch, type MutableRefObject, type SetStateAction } from 'react';
import { getCapabilities, getGraphMap, getStatus, type Capability, type EventStreamItem, type GraphMap, type GraphNode, type WorkspaceStatus } from './api';
import { AdvisoryWorkbench } from './components/AdvisoryWorkbench';
import { GraphSearch } from './components/GraphSearch';
import { HealthDashboard } from './components/HealthDashboard';
import { InspectorPanel } from './components/InspectorPanel';
import { Panel } from './components/Panel';
import { QueryPanel } from './components/QueryPanel';
import { SessionTimeline } from './components/SessionTimeline';
import { SummaryDebtPanel } from './components/SummaryDebtPanel';
import { HelpHint, Tooltip } from './components/Tooltip';
import { type SelectedGraphItem } from './graph';
import { CanvasV2 } from './CanvasV2';

type LoadState = 'loading' | 'ready' | 'degraded' | 'unavailable';

export function ArchyApp() {
  const [status, setStatus] = useState<WorkspaceStatus | null>(null);
  const [graph, setGraph] = useState<GraphMap | null>(null);
  const [state, setState] = useState<LoadState>('loading');
  const [notice, setNotice] = useState<string | null>(null);
  const [selected, setSelected] = useState<SelectedGraphItem>(null);
  const [events, setEvents] = useState<EventStreamItem[]>([]);
  const [sessionId, setSessionId] = useState('');
  const [capabilities, setCapabilities] = useState<Capability[]>([]);
  const [focusStableId, setFocusStableId] = useState<string | null>(null);
  const lastSequence = useRef(0);

  useEffect(() => {
    const controller = new AbortController();
    void (async () => {
      const [statusResult, graphResult, capabilityResult] = await Promise.all([getStatus(controller.signal), getGraphMap(controller.signal), getCapabilities(controller.signal)]);
      if (controller.signal.aborted) return;
      if (statusResult.error || graphResult.error) { setNotice(statusResult.error ?? graphResult.error ?? 'The local host could not be reached.'); setState('unavailable'); return; }
      setStatus(statusResult.value!); setGraph(graphResult.value!); setCapabilities(capabilityResult.value?.capabilities ?? []);
      const warnings = [capabilityResult.error, ...(capabilityResult.value?.capabilities.filter(item => !item.ready).map(item => item.warning) ?? [])].filter(Boolean);
      setNotice(warnings.join(' ') || null); setState(warnings.length ? 'degraded' : 'ready');
    })();
    return () => controller.abort();
  }, []);

  useSessionStream(sessionId, lastSequence, setEvents, setNotice);
  useEffect(() => {
    if (!graph) return;
    const selectedId = new URLSearchParams(window.location.search).get('selected');
    if (!selectedId) return;
    const node = graph.nodes.find(item => item.stableId === selectedId);
    const edge = graph.edges.find(item => item.edgeId === selectedId);
    if (node) { setSelected({ kind: 'node', node }); setFocusStableId(node.stableId); }
    else if (edge) setSelected({ kind: 'edge', edge });
  }, [graph]);
  const chooseNode = (node: GraphNode) => {
    const mapNode = graph?.nodes.find(item => item.stableId === node.stableId);
    if (!mapNode) { setNotice('This search result is outside the current bounded map projection.'); return; }
    setSelected({ kind: 'node', node: mapNode }); setFocusStableId(mapNode.stableId);
  };

  if (state === 'loading') return <StartupState title="Opening your architecture map" message="Preparing the complete repository map…" />;
  if (state === 'unavailable' || !graph) return <StartupState title="Archy is unavailable" message={notice ?? 'The local host could not be reached.'} alert />;

  return <main className="app-shell">
    <header className="mb-8 border-b border-slate-200 pb-8 lg:flex lg:items-end lg:justify-between lg:gap-10">
      <div><p className="text-sm font-medium text-indigo-700">{status?.name} workspace</p><h1 className="mt-2 text-3xl font-semibold tracking-tight text-slate-950 md:text-4xl">Architecture, made legible.</h1><p className="mt-3 max-w-2xl text-sm leading-6 text-slate-600">Navigate the repository graph, trace dependencies, and inspect the evidence behind each architectural decision.</p></div>
      <div className="mt-6 flex flex-wrap gap-3 lg:mt-0"><Metric label="Revision" value={String(graph.revision)} hint="The immutable graph snapshot currently shown on the canvas." /><Metric label="Repository nodes" value={graph.totalNodeCount.toLocaleString()} hint="All indexed files, symbols, and other architecture records." /><Metric label="Dependencies" value={graph.totalEdgeCount.toLocaleString()} hint="The relationships discovered between indexed records." /></div>
    </header>
    <div className="mb-7"><GraphSearch onChoose={chooseNode} /></div>
    {state === 'degraded' && <div className="mb-6 border border-amber-300 bg-amber-50 px-4 py-3 text-sm text-amber-900" role="status"><span className="font-semibold">Some optional capabilities are unavailable.</span> {notice}</div>}
    {notice && state === 'ready' && <div className="mb-6 border border-indigo-200 bg-indigo-50 px-4 py-3 text-sm text-indigo-900" role="status">{notice}</div>}
    <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_380px]">
      <Panel title="Architecture map" description="A stable module-first map of the indexed repository. Use search to focus a known item." className="min-w-0" action={<span className="rounded-full border border-emerald-200 bg-emerald-50 px-2.5 py-1 text-xs font-medium text-emerald-800">Canvas V2</span>}><CanvasV2 nodes={graph.nodes} edges={graph.edges} isTruncated={graph.isTruncated} selected={selected} focusStableId={focusStableId} onSelect={setSelected} /></Panel>
      <aside className="space-y-6"><InspectorPanel selected={selected} /><WorkspaceAtAGlance capabilities={capabilities} graph={graph} /></aside>
    </div>
    <details className="surface mt-8 rounded-lg"><summary className="cursor-pointer px-5 py-4 text-sm font-semibold text-slate-800">Workspace tools <span className="ml-2 text-xs font-normal text-slate-500">Queries, health snapshots, and decision evidence</span></summary><div className="grid gap-6 border-t border-slate-200 p-5 xl:grid-cols-3"><QueryPanel /><SummaryDebtPanel /><HealthDashboard /><div className="xl:col-span-2"><AdvisoryWorkbench /></div><SessionTimeline sessionId={sessionId} events={events} onSessionIdChange={setSessionId} /></div></details>
  </main>;
}

function useSessionStream(sessionId: string, lastSequence: MutableRefObject<number>, setEvents: Dispatch<SetStateAction<EventStreamItem[]>>, setNotice: Dispatch<SetStateAction<string | null>>) {
  useEffect(() => {
    if (!sessionId) { setEvents([]); lastSequence.current = 0; return; }
    let closed = false; let retryTimer: number | undefined;
    const connect = () => {
      const protocol = location.protocol === 'https:' ? 'wss' : 'ws';
      const socket = new WebSocket(`${protocol}://${location.host}/api/v1/events?sessionId=${encodeURIComponent(sessionId)}&after=${lastSequence.current}`);
      socket.onmessage = message => { try { const event = JSON.parse(message.data) as EventStreamItem; lastSequence.current = Math.max(lastSequence.current, event.sequence); setEvents(current => current.some(item => item.sequence === event.sequence) ? current : [...current, event].sort((left, right) => left.sequence - right.sequence)); } catch { setNotice('A malformed live event was ignored; reconnecting data remains durable.'); } };
      socket.onerror = () => setNotice('Live events temporarily disconnected. Archy will reconnect using the last durable sequence.');
      socket.onclose = () => { if (!closed) retryTimer = window.setTimeout(connect, 1_000); };
    };
    connect(); return () => { closed = true; if (retryTimer) window.clearTimeout(retryTimer); };
  }, [sessionId, lastSequence, setEvents, setNotice]);
}

function WorkspaceAtAGlance({ capabilities, graph }: { capabilities: Capability[]; graph: GraphMap }) {
  return <Panel title="Map status" description="A quick check of what is present in this map and which optional integrations are available."><div className="space-y-3 text-sm"><p className="border-l-2 border-indigo-500 bg-slate-50 p-3 leading-6 text-slate-600">{graph.isTruncated ? 'This repository exceeds the map safety cap; only a bounded projection is shown.' : 'The canvas contains the complete active graph revision.'} Selecting a node highlights its direct paths.</p><StatusRow label="Rendered" value={`${graph.nodes.length.toLocaleString()} nodes · ${graph.edges.length.toLocaleString()} edges`} /><StatusRow label="Live events" value={capabilities.find(item => item.name === 'liveEvents')?.ready ? 'Ready' : 'Unavailable'} />{capabilities.filter(item => !item.ready).map(item => <StatusRow key={item.name} label={item.name} value="Optional / unavailable" detail={item.warning ?? undefined} />)}</div></Panel>;
}

function Metric({ label, value, hint }: { label: string; value: string; hint: string }) { return <Tooltip content={hint}><div className="cursor-help border-l border-slate-200 pl-3 first:border-l-0"><p className="text-[11px] font-medium uppercase tracking-wide text-slate-500">{label}</p><p className="mt-0.5 text-sm font-semibold text-slate-900">{value}</p></div></Tooltip>; }
function StatusRow({ label, value, detail }: { label: string; value: string; detail?: string }) { return <div className="border border-slate-200 bg-slate-50 p-3"><p className="text-xs font-medium uppercase tracking-wide text-slate-500">{label}</p><p className="mt-1 font-medium text-slate-800">{value}</p>{detail && <p className="mt-1 text-xs text-slate-500">{detail}</p>}</div>; }
function StartupState({ title, message, alert = false }: { title: string; message: string; alert?: boolean }) { return <main className="mx-auto flex min-h-screen max-w-lg items-center px-6"><div className="w-full border border-slate-200 bg-white p-7"><h1 className="text-xl font-semibold text-slate-900">{title}</h1><p className="mt-2 text-sm leading-6 text-slate-600" role={alert ? 'alert' : undefined}>{message}</p></div></main>; }
