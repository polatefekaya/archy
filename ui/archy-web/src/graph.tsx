import { useCallback, useEffect, useMemo, useRef, useState, type KeyboardEvent, type PointerEvent, type WheelEvent } from 'react';
import type { GraphMapEdge, GraphMapNode } from './api';
import { defaultCanvasViewState, readCanvasTraceSelection, readCanvasViewState, writeCanvasTraceSelection, writeCanvasViewState, type CanvasMode, type CanvasScope, type CanvasViewState } from './canvas-state';
import { Tooltip } from './components/Tooltip';
import { subscribeCanvasCommands } from './canvas-commands';

export type SelectedGraphItem =
  | { kind: 'node'; node: GraphMapNode }
  | { kind: 'edge'; edge: GraphMapEdge }
  | null;

type GraphCanvasProps = {
  nodes: GraphMapNode[];
  edges: GraphMapEdge[];
  isTruncated: boolean;
  selected: SelectedGraphItem;
  focusStableId: string | null;
  onSelect: (item: SelectedGraphItem) => void;
};

type Point = { x: number; y: number };
type Viewport = { x: number; y: number; scale: number };
type Cluster = { key: string; x: number; y: number; width: number; height: number; nodes: string[] };
type Layout = { points: Map<string, Point>; clusters: Cluster[]; clusterByNode: Map<string, string>; width: number; height: number };

const WORLD_PADDING = 120;
const CARD_WIDTH = 168;
const CARD_HEIGHT = 46;
const MAX_RENDERED_EDGES = 12_000;

const palette: Record<string, { fill: string; stroke: string }> = {
  file: { fill: '#082f49', stroke: '#38bdf8' },
  type: { fill: '#312e81', stroke: '#a78bfa' }, class: { fill: '#312e81', stroke: '#a78bfa' }, interface: { fill: '#312e81', stroke: '#a78bfa' }, record: { fill: '#312e81', stroke: '#a78bfa' }, enum: { fill: '#312e81', stroke: '#a78bfa' },
  method: { fill: '#881337', stroke: '#fb7185' }, member: { fill: '#064e3b', stroke: '#34d399' }, namespace: { fill: '#713f12', stroke: '#fbbf24' },
};

function color(kind: string) { return palette[kind.toLowerCase()] ?? { fill: '#172554', stroke: '#94a3b8' }; }
function nodeTitle(node: GraphMapNode) { return node.displayName.split('.').at(-1)?.slice(0, 24) || node.displayName.slice(0, 24); }
function fileName(node: GraphMapNode) { return node.filePath?.split('/').at(-1) ?? node.nodeKind; }

export function GraphCanvas({ nodes, edges, isTruncated, selected, focusStableId, onSelect }: GraphCanvasProps) {
  const canvas = useRef<HTMLCanvasElement | null>(null);
  const host = useRef<HTMLDivElement | null>(null);
  const drag = useRef<{ pointerId: number; start: Point; origin: Point; moved: boolean } | null>(null);
  const [size, setSize] = useState({ width: 1, height: 1 });
  const [viewport, setViewport] = useState<Viewport>({ x: 0, y: 0, scale: 1 });
  const [view, setView] = useState<CanvasViewState>(() => readCanvasViewState(window.location.search));
  const [filtersOpen, setFiltersOpen] = useState(false);
  const [commandsOpen, setCommandsOpen] = useState(false);
  const [overlaysOpen, setOverlaysOpen] = useState(false);
  const [showCycles, setShowCycles] = useState(false);
  const [showCoupling, setShowCoupling] = useState(false);
  const [pinnedNodeIds, setPinnedNodeIds] = useState<string[]>([]);
  const [traceStart, setTraceStart] = useState<string | null>(() => readCanvasTraceSelection(window.location.search).start);
  const [traceEnd, setTraceEnd] = useState<string | null>(() => readCanvasTraceSelection(window.location.search).end);
  const relationships = useMemo(() => relatedTo(selected?.kind === 'node' ? selected.node.stableId : null, edges), [selected, edges]);
  const visible = useMemo(() => deriveVisibleGraph(nodes, edges, selected, view, traceStart, traceEnd, pinnedNodeIds), [nodes, edges, selected, view, traceStart, traceEnd, pinnedNodeIds]);
  const layout = useMemo(() => buildLayout(visible.nodes), [visible.nodes]);
  const tracePath = useMemo(() => traceStart && traceEnd ? shortestPath(traceStart, traceEnd, edges) : null, [traceStart, traceEnd, edges]);
  const cycleNodeIds = useMemo(() => findCycleNodeIds(nodes, edges), [nodes, edges]);
  const couplingNodeIds = useMemo(() => findHighCouplingNodeIds(nodes, edges), [nodes, edges]);
  const signalNodeIds = useMemo(() => new Set([...(showCycles ? cycleNodeIds : []), ...(showCoupling ? couplingNodeIds : [])]), [cycleNodeIds, couplingNodeIds, showCycles, showCoupling]);

  useEffect(() => {
    const query = writeCanvasViewState(window.location.search, view);
    window.history.replaceState(window.history.state, '', `${window.location.pathname}${query ? `?${query}` : ''}${window.location.hash}`);
  }, [view]);

  useEffect(() => {
    const query = writeCanvasTraceSelection(window.location.search, traceStart, traceEnd);
    window.history.replaceState(window.history.state, '', `${window.location.pathname}${query ? `?${query}` : ''}${window.location.hash}`);
  }, [traceStart, traceEnd]);

  useEffect(() => {
    const query = new URLSearchParams(window.location.search);
    const selectedId = selected?.kind === 'node' ? selected.node.stableId : selected?.kind === 'edge' ? selected.edge.edgeId : null;
    selectedId ? query.set('selected', selectedId) : query.delete('selected');
    const search = query.toString();
    window.history.replaceState(window.history.state, '', `${window.location.pathname}${search ? `?${search}` : ''}${window.location.hash}`);
  }, [selected]);

  const fit = useCallback(() => {
    const availableWidth = Math.max(size.width - 40, 1); const availableHeight = Math.max(size.height - 40, 1);
    const scale = Math.min(availableWidth / layout.width, availableHeight / layout.height);
    setViewport({ scale, x: (size.width - layout.width * scale) / 2, y: (size.height - layout.height * scale) / 2 });
  }, [layout, size]);

  useEffect(() => {
    const element = host.current; if (!element) return undefined;
    const observer = new ResizeObserver(([entry]) => setSize({ width: Math.max(1, Math.floor(entry.contentRect.width)), height: Math.max(1, Math.floor(entry.contentRect.height)) }));
    observer.observe(element); return () => observer.disconnect();
  }, []);

  useEffect(() => { if (size.width > 1 && size.height > 1) fit(); }, [fit, size.width, size.height]);

  useEffect(() => {
    if (!focusStableId || !layout.points.has(focusStableId) || size.width <= 1) return;
    const point = layout.points.get(focusStableId)!;
    setViewport(current => {
      const scale = Math.max(current.scale, 0.82);
      return { scale, x: size.width / 2 - point.x * scale, y: size.height / 2 - point.y * scale };
    });
  }, [focusStableId, layout, size]);

  useEffect(() => {
    const element = canvas.current; if (!element) return;
    const frame = requestAnimationFrame(() => drawMap(element, size, viewport, layout, visible.nodes, visible.edges, selected, relationships, view.showEdges, view.showLabels, signalNodeIds));
    return () => cancelAnimationFrame(frame);
  }, [layout, relationships, selected, signalNodeIds, size, view.showEdges, view.showLabels, viewport, visible]);

  const zoomAt = (screen: Point, delta: number) => setViewport(current => {
    const nextScale = clamp(current.scale * delta, 0.035, 3.5);
    const world = { x: (screen.x - current.x) / current.scale, y: (screen.y - current.y) / current.scale };
    return { scale: nextScale, x: screen.x - world.x * nextScale, y: screen.y - world.y * nextScale };
  });

  const selectAt = (screen: Point) => {
    const minimap = miniMapBounds(size);
    if (screen.x >= minimap.x && screen.x <= minimap.x + minimap.width && screen.y >= minimap.y && screen.y <= minimap.y + minimap.height) {
      const mapScale = Math.min(minimap.width / layout.width, minimap.height / layout.height);
      const world = { x: (screen.x - minimap.x) / mapScale, y: (screen.y - minimap.y) / mapScale };
      setViewport(current => ({ ...current, x: size.width / 2 - world.x * current.scale, y: size.height / 2 - world.y * current.scale }));
      return;
    }
    const world = { x: (screen.x - viewport.x) / viewport.scale, y: (screen.y - viewport.y) / viewport.scale };
    if (viewport.scale < .18) {
      const cluster = hitCluster(world, layout.clusters);
      if (cluster) { setViewport({ scale: .38, x: size.width / 2 - (cluster.x + cluster.width / 2) * .38, y: size.height / 2 - (cluster.y + cluster.height / 2) * .38 }); return; }
    }
    const node = hitNode(world, visible.nodes, layout);
    if (node) {
      if (view.mode === 'trace') {
        if (!traceStart || traceEnd) { setTraceStart(node.stableId); setTraceEnd(null); }
        else if (node.stableId !== traceStart) setTraceEnd(node.stableId);
      }
      onSelect({ kind: 'node', node }); return;
    }
    const edge = hitEdge(world, visible.edges, layout.points, 10 / viewport.scale);
    onSelect(edge ? { kind: 'edge', edge } : null);
  };

  const pointerDown = (event: PointerEvent<HTMLCanvasElement>) => {
    event.currentTarget.setPointerCapture(event.pointerId);
    drag.current = { pointerId: event.pointerId, start: { x: event.nativeEvent.offsetX, y: event.nativeEvent.offsetY }, origin: { x: viewport.x, y: viewport.y }, moved: false };
  };
  const pointerMove = (event: PointerEvent<HTMLCanvasElement>) => {
    const active = drag.current; if (!active || active.pointerId !== event.pointerId) return;
    const current = { x: event.nativeEvent.offsetX, y: event.nativeEvent.offsetY };
    const dx = current.x - active.start.x; const dy = current.y - active.start.y;
    if (Math.abs(dx) + Math.abs(dy) > 3) active.moved = true;
    if (active.moved) setViewport(value => ({ ...value, x: active.origin.x + dx, y: active.origin.y + dy }));
  };
  const pointerUp = (event: PointerEvent<HTMLCanvasElement>) => {
    const active = drag.current; if (!active || active.pointerId !== event.pointerId) return;
    drag.current = null; if (!active.moved) selectAt({ x: event.nativeEvent.offsetX, y: event.nativeEvent.offsetY });
  };
  const wheel = (event: WheelEvent<HTMLCanvasElement>) => { event.preventDefault(); zoomAt({ x: event.nativeEvent.offsetX, y: event.nativeEvent.offsetY }, event.deltaY < 0 ? 1.16 : 1 / 1.16); };
  const keyDown = (event: KeyboardEvent<HTMLCanvasElement>) => {
    const pan = 70;
    if (event.key === '+' || event.key === '=') { event.preventDefault(); zoomAt({ x: size.width / 2, y: size.height / 2 }, 1.18); }
    else if (event.key === '-') { event.preventDefault(); zoomAt({ x: size.width / 2, y: size.height / 2 }, 1 / 1.18); }
    else if (event.key.toLowerCase() === 'f') { event.preventDefault(); fit(); }
    else if (event.key.toLowerCase() === 'r') { event.preventDefault(); reset(); }
    else if (event.key.toLowerCase() === 'e') { event.preventDefault(); chooseMode('explore'); }
    else if (event.key.toLowerCase() === 't') { event.preventDefault(); chooseMode('trace'); }
    else if (event.key.toLowerCase() === 'i') { event.preventDefault(); chooseMode('impact'); }
    else if (event.key.startsWith('Arrow')) { event.preventDefault(); setViewport(current => ({ ...current, x: current.x + (event.key === 'ArrowLeft' ? pan : event.key === 'ArrowRight' ? -pan : 0), y: current.y + (event.key === 'ArrowUp' ? pan : event.key === 'ArrowDown' ? -pan : 0) })); }
  };

  const chooseMode = (mode: CanvasMode) => { setView(current => ({ ...current, mode })); if (mode !== 'trace') { setTraceStart(null); setTraceEnd(null); } };
  const chooseScope = (scope: CanvasScope) => setView(current => ({ ...current, scope }));
  const reset = () => { setView(defaultCanvasViewState); setTraceStart(null); setTraceEnd(null); setPinnedNodeIds([]); onSelect(null); fit(); };
  const focusNode = (stableId: string) => { const point = layout.points.get(stableId); if (!point) return; setViewport(current => { const scale = Math.max(current.scale, .82); return { scale, x: size.width / 2 - point.x * scale, y: size.height / 2 - point.y * scale }; }); };
  const kinds = [...new Set(nodes.map(node => node.nodeKind))].sort();
  const providers = [...new Set(nodes.map(node => node.provider))].sort();
  const modules = [...new Set(nodes.map(moduleKey))].sort();
  const activeFilters = [view.nodeKind !== 'all' && `Type: ${view.nodeKind}`, view.provider !== 'all' && `Provider: ${view.provider}`, view.module !== 'all' && `Module: ${view.module}`, view.confidence !== 'all' && `Confidence: ${view.confidence}`, view.referenceScope !== 'all' && `References: ${view.referenceScope}`].filter(Boolean) as string[];
  const selectedNode = selected?.kind === 'node' ? selected.node : null;
  const pinnedNodes = pinnedNodeIds.flatMap(id => nodes.filter(node => node.stableId === id));
  const pinSelected = () => { if (!selectedNode) return; setPinnedNodeIds(current => current.includes(selectedNode.stableId) ? current.filter(id => id !== selectedNode.stableId) : [...current.slice(-1), selectedNode.stableId]); };

  useEffect(() => subscribeCanvasCommands(command => {
    if (typeof command === 'object') { chooseMode('trace'); setTraceStart(command.stableId); setTraceEnd(null); }
    else if (command === 'explore') chooseMode('explore');
    else if (command === 'trace') chooseMode('trace');
    else if (command === 'impact') chooseMode('impact');
    else if (command === 'fit') fit();
    else reset();
  }), [fit]);

  const breadcrumb = selected?.kind === 'node' ? moduleKey(selected.node).replace('Features/', '') : 'Repository overview';
  return <div className="space-y-3" data-testid="graph-map">
    <div className="border border-slate-200 bg-slate-50 p-3"><div className="flex flex-wrap items-center justify-between gap-3"><div className="flex items-center gap-2 text-xs text-slate-600"><span className="inline-flex size-2 rounded-full bg-emerald-600" />{visible.nodes.length.toLocaleString()} visible nodes · {visible.edges.length.toLocaleString()} visible dependencies</div><div className="flex flex-wrap items-center gap-2"><ModeButton mode="explore" active={view.mode} onClick={chooseMode} description="Browse nodes and inspect their direct relationships." /><ModeButton mode="trace" active={view.mode} onClick={chooseMode} description="Choose a source and target node to reveal a dependency path." /><ModeButton mode="impact" active={view.mode} onClick={chooseMode} description="Show downstream dependents for the selected node." /><Tooltip content="Keep the selected node visible while comparing another selection; up to two nodes can be pinned."><button type="button" onClick={pinSelected} disabled={!selectedNode} className="map-button">{selectedNode && pinnedNodeIds.includes(selectedNode.stableId) ? 'Unpin' : 'Pin selection'}</button></Tooltip><Tooltip content="Open filters for node kind and visibility options."><button type="button" onClick={() => setFiltersOpen(value => !value)} className="map-button" aria-expanded={filtersOpen}>Filters</button></Tooltip><Tooltip content="See semantic overlay availability for this server."><button type="button" onClick={() => setOverlaysOpen(value => !value)} className="map-button" aria-expanded={overlaysOpen}>Signals</button></Tooltip><Tooltip content="Open a list of all canvas actions and their shortcuts."><button type="button" onClick={() => setCommandsOpen(value => !value)} className="map-button" aria-expanded={commandsOpen}>Commands</button></Tooltip><Tooltip content="Restore the default overview and clear selection."><button type="button" onClick={reset} className="map-button">Reset</button></Tooltip></div></div>
      {filtersOpen && <div className="mt-3 flex flex-wrap items-end gap-3 border-t border-slate-200 pt-3"><FilterSelect label="Node type" value={view.nodeKind} onChange={nodeKind => setView(current => ({ ...current, nodeKind }))} options={kinds} allLabel="All types" /><FilterSelect label="Provider" value={view.provider} onChange={provider => setView(current => ({ ...current, provider }))} options={providers} allLabel="All providers" /><FilterSelect label="Module" value={view.module} onChange={module => setView(current => ({ ...current, module }))} options={modules} allLabel="All modules" /><label className="text-xs font-medium text-slate-600">Confidence<select value={view.confidence} onChange={event => setView(current => ({ ...current, confidence: event.target.value as CanvasViewState['confidence'] }))} className="ml-2 rounded border border-slate-300 bg-white px-2 py-1 text-sm text-slate-700"><option value="all">All</option><option value="high">≥ 0.70</option><option value="low">&lt; 0.70</option></select></label><label className="text-xs font-medium text-slate-600">References<select value={view.referenceScope} onChange={event => setView(current => ({ ...current, referenceScope: event.target.value as CanvasViewState['referenceScope'] }))} className="ml-2 rounded border border-slate-300 bg-white px-2 py-1 text-sm text-slate-700"><option value="all">All</option><option value="internal">Internal</option><option value="external">External</option></select></label><label className="flex items-center gap-1.5 text-xs text-slate-600"><input type="checkbox" checked={view.showEdges} onChange={event => setView(current => ({ ...current, showEdges: event.target.checked }))} className="accent-indigo-700" />Connections</label><label className="flex items-center gap-1.5 text-xs text-slate-600"><input type="checkbox" checked={view.showLabels} onChange={event => setView(current => ({ ...current, showLabels: event.target.checked }))} className="accent-indigo-700" />Labels</label>{view.mode === 'impact' && <label className="text-xs text-slate-600">Depth<select value={view.impactDepth} onChange={event => setView(current => ({ ...current, impactDepth: Number(event.target.value) as 1 | 2 | 3 }))} className="ml-2 rounded border border-slate-300 bg-white px-2 py-1 text-sm text-slate-700"><option value="1">1 hop</option><option value="2">2 hops</option><option value="3">3 hops</option></select></label>}</div>}
      {activeFilters.length > 0 && <div className="mt-3 flex flex-wrap items-center gap-2 text-xs text-slate-600"><span>Active filters</span>{activeFilters.map(filter => <span key={filter} className="border border-indigo-200 bg-indigo-50 px-2 py-1 text-indigo-800">{filter}</span>)}<button type="button" onClick={() => setView(current => ({ ...current, nodeKind: 'all', provider: 'all', module: 'all', confidence: 'all', referenceScope: 'all' }))} className="text-indigo-700 hover:text-indigo-900">Clear filters</button></div>}
      {pinnedNodes.length > 0 && <div className="mt-3 flex flex-wrap items-center gap-2 text-xs text-slate-600"><span>Pinned for comparison</span>{pinnedNodes.map(node => <button key={node.stableId} type="button" onClick={() => { onSelect({ kind: 'node', node }); focusNode(node.stableId); }} className="border border-slate-300 bg-white px-2 py-1 text-slate-700 hover:bg-slate-50">{node.displayName}<span className="ml-2 text-slate-400">Focus</span></button>)}</div>}
      {commandsOpen && <div className="mt-3 flex flex-wrap gap-2 border-t border-slate-200 pt-3" role="menu" aria-label="Canvas commands"><CommandButton label="Explore" shortcut="E" onClick={() => chooseMode('explore')} /><CommandButton label="Trace" shortcut="T" onClick={() => chooseMode('trace')} /><CommandButton label="Impact" shortcut="I" onClick={() => chooseMode('impact')} /><CommandButton label="Fit view" shortcut="F" onClick={fit} /><CommandButton label="Reset canvas" shortcut="R" onClick={reset} /></div>}
      {overlaysOpen && <div className="mt-3 border-t border-slate-200 pt-3"><p className="text-xs font-medium text-slate-700">Architecture signals</p><p className="mt-1 text-xs leading-5 text-slate-500">Signals are opt-in. Cycle and coupling overlays are calculated deterministically from the rendered map; persisted signals remain unavailable until the server exposes authoritative evidence.</p><div className="mt-2 flex flex-wrap gap-3"><label className="flex items-center gap-1.5 text-xs text-slate-700"><input type="checkbox" checked={showCycles} onChange={event => setShowCycles(event.target.checked)} className="accent-rose-700" />Cycles ({cycleNodeIds.size})</label><label className="flex items-center gap-1.5 text-xs text-slate-700"><input type="checkbox" checked={showCoupling} onChange={event => setShowCoupling(event.target.checked)} className="accent-rose-700" />High coupling ({couplingNodeIds.size})</label></div><ul className="mt-2 flex flex-wrap gap-2 text-xs text-slate-500">{['Boundary violations', 'Changed areas', 'Stale summaries', 'Duplicate candidates'].map(name => <li key={name} className="border border-slate-200 bg-white px-2 py-1">{name} · unavailable</li>)}</ul></div>}
      <div className="mt-3 flex flex-wrap items-center gap-2"><span className="text-xs font-medium text-slate-500">View</span>{(['all', 'focus', 'hop-1', 'hop-2', 'component'] as CanvasScope[]).map(scope => <Tooltip key={scope} content={scope === 'all' ? 'Show the full map.' : scope === 'focus' ? 'Show only the selected node.' : scope === 'component' ? 'Show the connected component containing the selected node.' : `Show the selected node and its ${scope.replace('-', ' ')} neighborhood.`}><button type="button" onClick={() => chooseScope(scope)} disabled={scope !== 'all' && !selected} className={`map-button ${view.scope === scope ? 'border-indigo-700 bg-indigo-50 text-indigo-800' : ''}`}>{scope.replace('-', ' ')}</button></Tooltip>)}</div>
      {view.mode === 'trace' && <p className={`mt-3 border-l-2 bg-white px-3 py-2 text-xs ${traceStart && traceEnd && !tracePath ? 'border-amber-500 text-amber-800' : 'border-indigo-600 text-slate-600'}`}>{traceStart && traceEnd ? tracePath ? `Tracing ${tracePath.size - 1} dependency step${tracePath.size === 2 ? '' : 's'} between the selected nodes.` : 'No directed dependency path exists between these nodes. Choose another destination or reset Trace.' : traceStart ? 'Now select a destination node.' : 'Select a source node, then a destination node.'}</p>}
      {view.mode === 'impact' && <p className="mt-3 border-l-2 border-indigo-600 bg-white px-3 py-2 text-xs text-slate-600">{selected?.kind === 'node' ? `Showing downstream dependents through ${view.impactDepth} hop${view.impactDepth === 1 ? '' : 's'}.` : 'Select a node to show its downstream dependents.'}</p>}
      {isTruncated && (view.mode === 'trace' || view.mode === 'impact') && <p className="mt-3 border-l-2 border-amber-500 bg-amber-50 px-3 py-2 text-xs text-amber-900" role="status">This map is a bounded projection. Trace and Impact results only cover the rendered graph; use a narrower server-side traversal before making a change decision.</p>}
      {visible.isEdgeCapped && <p className="mt-3 border-l-2 border-amber-500 bg-amber-50 px-3 py-2 text-xs text-amber-900" role="status">The current view exceeds the safe rendering limit. Showing the first 12,000 visible dependencies; narrow the view, filter, or focus a selection for complete detail.</p>}
    </div>
    <div className="flex flex-wrap items-center justify-between gap-2"><div className="flex flex-wrap items-center gap-2"><Tooltip content="Zoom out from the center of the current view."><button type="button" onClick={() => zoomAt({ x: size.width / 2, y: size.height / 2 }, 1 / 1.18)} className="map-button" aria-label="Zoom out">−</button></Tooltip><Tooltip content="Fit all visible graph items into the canvas."><button type="button" onClick={fit} className="map-button">Fit view</button></Tooltip><Tooltip content="Zoom in toward the center of the current view."><button type="button" onClick={() => zoomAt({ x: size.width / 2, y: size.height / 2 }, 1.18)} className="map-button" aria-label="Zoom in">+</button></Tooltip></div><p className="text-xs text-slate-500" aria-live="polite">{breadcrumb} · {view.mode} · {view.scope}</p></div>
    <div ref={host} className="relative h-[min(72vh,760px)] min-h-[540px] overflow-hidden border border-slate-300 bg-slate-50"><canvas ref={canvas} tabIndex={0} onPointerDown={pointerDown} onPointerMove={pointerMove} onPointerUp={pointerUp} onPointerCancel={pointerUp} onWheel={wheel} onKeyDown={keyDown} className="block size-full cursor-grab touch-none outline-none active:cursor-grabbing" role="application" aria-label={`Interactive canvas architecture map with ${visible.nodes.length} visible nodes and ${visible.edges.length} visible dependencies`} aria-describedby="map-instructions" /><p id="map-instructions" className="pointer-events-none absolute bottom-3 left-3 border border-slate-300 bg-white px-2.5 py-1.5 text-[11px] text-slate-600">Drag to travel · scroll to zoom · click a node to inspect · click the minimap to navigate · keyboard: arrows, +/−, F, R, E, T, I</p></div>
    <p className="text-xs leading-5 text-slate-500">At overview scale, each card is a module and each line is an aggregated dependency. Zoom in to reveal symbols and filenames; selecting a node highlights its direct paths.</p>
  </div>;
}

function buildLayout(nodes: GraphMapNode[]): Layout {
  const groups = new Map<string, GraphMapNode[]>();
  nodes.forEach(node => { const key = moduleKey(node); const group = groups.get(key) ?? []; group.push(node); groups.set(key, group); });
  const points = new Map<string, Point>(); const clusterByNode = new Map<string, string>(); const clusters: Cluster[] = [];
  const maximumWidth = 11_800; let cursorX = WORLD_PADDING; let cursorY = WORLD_PADDING; let rowHeight = 0;
  [...groups.entries()].sort(([left], [right]) => left.localeCompare(right)).forEach(([key, group]) => {
    const ordered = group.sort((left, right) => left.displayName.localeCompare(right.displayName)); const columns = Math.max(6, Math.ceil(Math.sqrt(ordered.length * 2.2))); const width = Math.max(1_120, columns * 190 + 100); const height = Math.max(260, Math.ceil(ordered.length / columns) * 66 + 126);
    if (cursorX > WORLD_PADDING && cursorX + width > maximumWidth) { cursorX = WORLD_PADDING; cursorY += rowHeight + 150; rowHeight = 0; }
    ordered.forEach((node, nodeIndex) => { points.set(node.stableId, { x: cursorX + 54 + (nodeIndex % columns) * 190, y: cursorY + 94 + Math.floor(nodeIndex / columns) * 66 }); clusterByNode.set(node.stableId, key); });
    clusters.push({ key, x: cursorX, y: cursorY, width, height, nodes: ordered.map(node => node.stableId) }); cursorX += width + 150; rowHeight = Math.max(rowHeight, height);
  });
  return { points, clusters, clusterByNode, width: Math.max(maximumWidth, cursorX) + WORLD_PADDING, height: cursorY + rowHeight + WORLD_PADDING };
}

function moduleKey(node: GraphMapNode) {
  if (!node.filePath) {
    if (node.canonicalKey.startsWith('csharp:using-reference:') || node.nodeKind === 'unresolved_reference') return 'References/External';
    if (node.nodeKind === 'configuration_key') return 'Configuration/Keys';
    return `Generated/${node.provider}/${node.nodeKind}`;
  }
  const parts = node.filePath.split('/').filter(Boolean);
  const feature = parts.indexOf('Features');
  if (feature >= 0) return parts.slice(feature, feature + 2).join('/');
  return parts.slice(0, 2).join('/') || 'Workspace';
}

function clusterLabel(key: string) { return key.replace('Features/', '').split('/')[0]; }

function drawMap(canvas: HTMLCanvasElement, size: { width: number; height: number }, viewport: Viewport, layout: Layout, nodes: GraphMapNode[], edges: GraphMapEdge[], selected: SelectedGraphItem, relationships: Set<string>, showEdges: boolean, showLabels: boolean, signalNodeIds: Set<string>) {
  const ratio = window.devicePixelRatio || 1; if (canvas.width !== size.width * ratio || canvas.height !== size.height * ratio) { canvas.width = size.width * ratio; canvas.height = size.height * ratio; }
  const context = canvas.getContext('2d'); if (!context) return;
  context.setTransform(ratio, 0, 0, ratio, 0, 0); context.clearRect(0, 0, size.width, size.height);
  context.fillStyle = '#f8fafc'; context.fillRect(0, 0, size.width, size.height);
  context.save(); context.translate(viewport.x, viewport.y); context.scale(viewport.scale, viewport.scale);
  const overview = viewport.scale < .18;
  drawClusters(context, layout, edges, viewport.scale, overview);
  if (showEdges) { if (overview) drawAggregateEdges(context, edges, layout, viewport.scale); else drawEdges(context, edges, layout.points, selected, relationships, viewport.scale); }
  if (!overview) drawNodes(context, nodes, layout.points, selected, relationships, viewport.scale, showLabels, signalNodeIds);
  context.restore(); drawMiniMap(context, size, layout, viewport, nodes, edges);
}

function drawClusters(context: CanvasRenderingContext2D, layout: Layout, edges: GraphMapEdge[], scale: number, overview: boolean) {
  if (scale < 0.045) return;
  const dependenciesByCluster = new Map<string, number>();
  edges.forEach(edge => { const source = layout.clusterByNode.get(edge.sourceStableId); const target = layout.clusterByNode.get(edge.targetStableId); if (source && target && source !== target) { dependenciesByCluster.set(source, (dependenciesByCluster.get(source) ?? 0) + 1); dependenciesByCluster.set(target, (dependenciesByCluster.get(target) ?? 0) + 1); } });
  context.save();
  layout.clusters.forEach(cluster => { context.fillStyle = overview ? 'rgba(255,255,255,.98)' : 'rgba(241,245,249,.65)'; context.strokeStyle = overview ? 'rgba(148,163,184,.95)' : 'rgba(148,163,184,.46)'; context.lineWidth = (overview ? 2 : 1) / scale; roundedRect(context, cluster.x, cluster.y, cluster.width, cluster.height, 12 / scale); context.fill(); context.stroke(); if (overview || scale >= 0.16) { context.fillStyle = overview ? '#1e293b' : '#475569'; context.font = `${16 / scale}px ui-sans-serif`; context.fillText(clusterLabel(cluster.key), cluster.x + 18 / scale, cluster.y + 28 / scale); if (overview) { context.fillStyle = '#64748b'; context.font = `${12 / scale}px ui-sans-serif`; context.fillText(`${cluster.nodes.length} nodes · ${(dependenciesByCluster.get(cluster.key) ?? 0).toLocaleString()} cross-module dependencies`, cluster.x + 18 / scale, cluster.y + 48 / scale); } } });
  context.restore();
}

function drawAggregateEdges(context: CanvasRenderingContext2D, edges: GraphMapEdge[], layout: Layout, scale: number) {
  const clusters = new Map(layout.clusters.map(cluster => [cluster.key, cluster])); const aggregate = new Map<string, number>();
  edges.forEach(edge => { const source = layout.clusterByNode.get(edge.sourceStableId); const target = layout.clusterByNode.get(edge.targetStableId); if (!source || !target || source === target) return; const key = source < target ? `${source}|${target}` : `${target}|${source}`; aggregate.set(key, (aggregate.get(key) ?? 0) + 1); });
  context.save(); [...aggregate.entries()].filter(([, count]) => count >= 3).sort(([, left], [, right]) => right - left).slice(0, 64).forEach(([key, count]) => { const [sourceKey, targetKey] = key.split('|'); const source = clusters.get(sourceKey); const target = clusters.get(targetKey); if (!source || !target) return; const from = { x: source.x + source.width / 2, y: source.y + source.height / 2 }; const to = { x: target.x + target.width / 2, y: target.y + target.height / 2 }; const bend = Math.min(280, Math.abs(from.x - to.x) * .18); context.beginPath(); context.moveTo(from.x, from.y); context.quadraticCurveTo((from.x + to.x) / 2, (from.y + to.y) / 2 - bend, to.x, to.y); context.strokeStyle = 'rgba(56, 189, 248, .26)'; context.lineWidth = Math.min(7, .8 + Math.sqrt(count)) / scale; context.stroke(); }); context.restore();
}

function drawEdges(context: CanvasRenderingContext2D, edges: GraphMapEdge[], points: Map<string, Point>, selected: SelectedGraphItem, relationships: Set<string>, scale: number) {
  const selectedId = selected?.kind === 'node' ? selected.node.stableId : null;
  context.save(); context.lineCap = 'round';
  edges.forEach(edge => { const from = points.get(edge.sourceStableId); const to = points.get(edge.targetStableId); if (!from || !to) return; const active = !!selectedId && (edge.sourceStableId === selectedId || edge.targetStableId === selectedId); const edgeSelected = selected?.kind === 'edge' && selected.edge.edgeId === edge.edgeId; context.beginPath(); context.moveTo(from.x, from.y); context.lineTo(to.x, to.y); context.strokeStyle = edgeSelected ? '#d97706' : active ? '#4f46e5' : relationships.has(edge.edgeId) ? 'rgba(79, 70, 229, .68)' : 'rgba(100, 116, 139, .28)'; context.lineWidth = (edgeSelected ? 4 : active ? 4.5 : 0.8) / scale; context.setLineDash(edge.confidence < .7 && !active ? [5 / scale, 6 / scale] : []); context.stroke(); });
  context.setLineDash([]); context.restore();
}

function drawNodes(context: CanvasRenderingContext2D, nodes: GraphMapNode[], points: Map<string, Point>, selected: SelectedGraphItem, relationships: Set<string>, scale: number, showLabels: boolean, signalNodeIds: Set<string>) {
  const selectedId = selected?.kind === 'node' ? selected.node.stableId : null;
  context.save(); nodes.forEach(node => { const point = points.get(node.stableId); if (!point) return; const style = color(node.nodeKind); const active = node.stableId === selectedId || relationships.has(node.stableId); const signalled = signalNodeIds.has(node.stableId); const detailed = showLabels && scale >= .72; const width = detailed ? CARD_WIDTH : 12; const height = detailed ? CARD_HEIGHT : 12; context.fillStyle = active ? style.stroke : style.fill; context.strokeStyle = node.stableId === selectedId ? '#d97706' : signalled ? '#be123c' : active ? '#4f46e5' : style.stroke; context.lineWidth = (node.stableId === selectedId ? 3 : signalled || active ? 2 : 1) / scale; if (detailed) { roundedRect(context, point.x - width / 2, point.y - height / 2, width, height, 8 / scale); context.fill(); context.stroke(); context.fillStyle = '#e2e8f0'; context.font = `${12 / scale}px ui-sans-serif`; context.fillText(nodeTitle(node), point.x - width / 2 + 11 / scale, point.y - 2 / scale); context.fillStyle = '#94a3b8'; context.font = `${9 / scale}px ui-sans-serif`; context.fillText(fileName(node).slice(0, 24), point.x - width / 2 + 11 / scale, point.y + 13 / scale); } else { context.beginPath(); context.arc(point.x, point.y, (active || signalled ? 8 : 5) / scale, 0, Math.PI * 2); context.fill(); context.stroke(); } }); context.restore();
}

function drawMiniMap(context: CanvasRenderingContext2D, size: { width: number; height: number }, layout: Layout, viewport: Viewport, nodes: GraphMapNode[], edges: GraphMapEdge[]) {
  const { width, height, x, y } = miniMapBounds(size); const scale = Math.min(width / layout.width, height / layout.height); context.save(); context.fillStyle = 'rgba(255,255,255,.95)'; context.fillRect(x, y, width, height); context.strokeStyle = 'rgba(100, 116, 139, .72)'; context.strokeRect(x, y, width, height); context.translate(x, y); context.scale(scale, scale); context.strokeStyle = 'rgba(71, 85, 105, .32)'; context.lineWidth = 1 / scale; edges.forEach(edge => { const a = layout.points.get(edge.sourceStableId); const b = layout.points.get(edge.targetStableId); if (!a || !b) return; context.beginPath(); context.moveTo(a.x, a.y); context.lineTo(b.x, b.y); context.stroke(); }); context.fillStyle = 'rgba(79,70,229,.8)'; nodes.forEach(node => { const point = layout.points.get(node.stableId); if (point) context.fillRect(point.x - 5 / scale, point.y - 5 / scale, 10 / scale, 10 / scale); }); context.restore(); const visible = { x: (-viewport.x / viewport.scale) * scale + x, y: (-viewport.y / viewport.scale) * scale + y, width: (size.width / viewport.scale) * scale, height: (size.height / viewport.scale) * scale }; context.strokeStyle = '#d97706'; context.lineWidth = 1; context.strokeRect(visible.x, visible.y, visible.width, visible.height);
}

function miniMapBounds(size: { width: number; height: number }) { const width = 154; const height = 106; return { width, height, x: size.width - width - 14, y: size.height - height - 14 }; }

function relatedTo(selectedId: string | null, edges: GraphMapEdge[]) { const values = new Set<string>(); if (!selectedId) return values; edges.forEach(edge => { if (edge.sourceStableId === selectedId || edge.targetStableId === selectedId) { values.add(edge.edgeId); values.add(edge.sourceStableId); values.add(edge.targetStableId); } }); return values; }
function ModeButton({ mode, active, onClick, description }: { mode: CanvasMode; active: CanvasMode; onClick: (mode: CanvasMode) => void; description: string }) {
  return <Tooltip content={description}><button type="button" onClick={() => onClick(mode)} className={`map-button capitalize ${active === mode ? 'border-indigo-700 bg-indigo-50 text-indigo-800' : ''}`} aria-pressed={active === mode}>{mode}</button></Tooltip>;
}
function CommandButton({ label, shortcut, onClick }: { label: string; shortcut: string; onClick: () => void }) {
  return <button type="button" onClick={onClick} className="map-button" role="menuitem">{label}<kbd className="ml-2 border border-slate-300 bg-slate-50 px-1 text-[10px] text-slate-500">{shortcut}</kbd></button>;
}
function FilterSelect({ label, value, onChange, options, allLabel }: { label: string; value: string; onChange: (value: string) => void; options: string[]; allLabel: string }) {
  return <label className="text-xs font-medium text-slate-600">{label}<select value={value} onChange={event => onChange(event.target.value)} className="ml-2 max-w-44 rounded border border-slate-300 bg-white px-2 py-1 text-sm text-slate-700"><option value="all">{allLabel}</option>{options.map(option => <option key={option} value={option}>{option}</option>)}</select></label>;
}

export function deriveVisibleGraph(nodes: GraphMapNode[], edges: GraphMapEdge[], selected: SelectedGraphItem, view: CanvasViewState, traceStart: string | null, traceEnd: string | null, pinnedNodeIds: string[] = []) {
  const selectedId = selected?.kind === 'node' ? selected.node.stableId : null;
  const allowed = new Set(nodes.filter(node =>
    (view.nodeKind === 'all' || node.nodeKind === view.nodeKind)
    && (view.provider === 'all' || node.provider === view.provider)
    && (view.module === 'all' || moduleKey(node) === view.module)
    && (view.confidence === 'all' || (view.confidence === 'high' ? node.confidence >= .7 : node.confidence < .7))
    && (view.referenceScope === 'all' || (view.referenceScope === 'internal' ? !!node.filePath : !node.filePath))
  ).map(node => node.stableId));
  if (view.mode === 'trace' && traceStart && traceEnd) {
    const path = shortestPath(traceStart, traceEnd, edges);
    if (path) path.forEach(id => allowed.add(id));
  }
  const root = view.mode === 'impact' ? selectedId : selectedId;
  if (root && view.scope !== 'all') {
    const scoped = connectedIds(root, edges, view.scope === 'focus' ? 0 : view.scope === 'hop-1' ? 1 : view.scope === 'hop-2' ? 2 : Number.POSITIVE_INFINITY, view.scope === 'component');
    for (const id of [...allowed]) if (!scoped.has(id)) allowed.delete(id);
  }
  if (view.mode === 'impact' && root) {
    const impacted = connectedIds(root, edges, view.impactDepth, false, 'dependents');
    impacted.add(root);
    for (const id of [...allowed]) if (!impacted.has(id)) allowed.delete(id);
  }
  pinnedNodeIds.forEach(id => { if (nodes.some(node => node.stableId === id)) allowed.add(id); });
  const visibleNodes = nodes.filter(node => allowed.has(node.stableId));
  const visibleEdges = edges.filter(edge => allowed.has(edge.sourceStableId) && allowed.has(edge.targetStableId));
  return { nodes: visibleNodes, edges: visibleEdges.slice(0, MAX_RENDERED_EDGES), isEdgeCapped: visibleEdges.length > MAX_RENDERED_EDGES };
}

function connectedIds(root: string, edges: GraphMapEdge[], depth: number, undirected: boolean, direction: 'dependencies' | 'dependents' = 'dependents') {
  const found = new Set([root]); const queue: Array<{ id: string; depth: number }> = [{ id: root, depth: 0 }];
  while (queue.length) {
    const current = queue.shift()!; if (current.depth >= depth) continue;
    for (const edge of edges) {
      const next = undirected ? (edge.sourceStableId === current.id ? edge.targetStableId : edge.targetStableId === current.id ? edge.sourceStableId : null) : direction === 'dependents' && edge.targetStableId === current.id ? edge.sourceStableId : direction === 'dependencies' && edge.sourceStableId === current.id ? edge.targetStableId : null;
      if (next && !found.has(next)) { found.add(next); queue.push({ id: next, depth: current.depth + 1 }); }
    }
  }
  return found;
}

export function shortestPath(start: string, end: string, edges: GraphMapEdge[]) {
  const queue = [start]; const previous = new Map<string, string>(); const seen = new Set([start]);
  while (queue.length) {
    const current = queue.shift()!; if (current === end) break;
    for (const edge of edges) if (edge.sourceStableId === current && !seen.has(edge.targetStableId)) { seen.add(edge.targetStableId); previous.set(edge.targetStableId, current); queue.push(edge.targetStableId); }
  }
  if (!seen.has(end)) return null;
  const path = new Set<string>(); for (let current: string | undefined = end; current; current = previous.get(current)) path.add(current);
  return path;
}

export function findCycleNodeIds(nodes: GraphMapNode[], edges: GraphMapEdge[]) {
  const outgoing = new Map<string, string[]>(); nodes.forEach(node => outgoing.set(node.stableId, []));
  edges.forEach(edge => outgoing.get(edge.sourceStableId)?.push(edge.targetStableId));
  const visited = new Set<string>(); const visiting = new Set<string>(); const cycles = new Set<string>(); const trail: string[] = [];
  const visit = (id: string) => {
    if (visiting.has(id)) { const start = trail.lastIndexOf(id); trail.slice(Math.max(0, start)).forEach(item => cycles.add(item)); return; }
    if (visited.has(id)) return;
    visited.add(id); visiting.add(id); trail.push(id);
    outgoing.get(id)?.forEach(visit);
    trail.pop(); visiting.delete(id);
  };
  nodes.forEach(node => visit(node.stableId)); return cycles;
}

export function findHighCouplingNodeIds(nodes: GraphMapNode[], edges: GraphMapEdge[]) {
  if (nodes.length === 0) return new Set<string>();
  const degree = new Map(nodes.map(node => [node.stableId, 0]));
  edges.forEach(edge => { degree.set(edge.sourceStableId, (degree.get(edge.sourceStableId) ?? 0) + 1); degree.set(edge.targetStableId, (degree.get(edge.targetStableId) ?? 0) + 1); });
  const values = [...degree.values()].sort((left, right) => right - left); const threshold = values[Math.min(values.length - 1, Math.max(0, Math.ceil(values.length * .1) - 1))] ?? 0;
  return new Set([...degree].filter(([, value]) => value > 0 && value >= threshold).map(([id]) => id));
}
function hitCluster(point: Point, clusters: Cluster[]) { return clusters.find(cluster => point.x >= cluster.x && point.x <= cluster.x + cluster.width && point.y >= cluster.y && point.y <= cluster.y + cluster.height); }
function hitNode(point: Point, nodes: GraphMapNode[], layout: Layout) { return nodes.find(node => { const location = layout.points.get(node.stableId); return location && Math.abs(point.x - location.x) <= CARD_WIDTH / 2 && Math.abs(point.y - location.y) <= CARD_HEIGHT / 2; }); }
function hitEdge(point: Point, edges: GraphMapEdge[], points: Map<string, Point>, tolerance: number) { return edges.find(edge => { const a = points.get(edge.sourceStableId); const b = points.get(edge.targetStableId); return a && b && distanceToSegment(point, a, b) <= tolerance; }); }
function distanceToSegment(point: Point, a: Point, b: Point) { const dx = b.x - a.x; const dy = b.y - a.y; const length = dx * dx + dy * dy; const t = length === 0 ? 0 : clamp(((point.x - a.x) * dx + (point.y - a.y) * dy) / length, 0, 1); return Math.hypot(point.x - (a.x + dx * t), point.y - (a.y + dy * t)); }
function roundedRect(context: CanvasRenderingContext2D, x: number, y: number, width: number, height: number, radius: number) { context.beginPath(); context.roundRect(x, y, width, height, radius); }
function clamp(value: number, minimum: number, maximum: number) { return Math.max(minimum, Math.min(maximum, value)); }
