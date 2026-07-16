import { useCallback, useEffect, useMemo, useRef, useState, type KeyboardEvent, type PointerEvent, type WheelEvent } from 'react';
import type { GraphMapEdge, GraphMapNode } from './api';
import { buildModuleDetailScene, connectedFileDetail, directoryFileDetail, type ModuleDetailScene } from './canvas-detail-scene';
import { layoutModuleDetail, type ModuleDetailLayout } from './canvas-detail-layout';
import { layoutModules, moduleCenter, type ModuleBounds, type ModuleLayout, type ScenePoint } from './canvas-layout';
import { buildModuleScene, defaultCanvasFilter, matchesCanvasFilter, type CanvasFilter, type CanvasScene } from './canvas-scene';
import { findDirectedTrace, findDownstreamImpact } from './canvas-investigation';
import { Tooltip } from './components/Tooltip';
import type { SelectedGraphItem } from './graph';

type Props = {
  nodes: GraphMapNode[];
  edges: GraphMapEdge[];
  isTruncated: boolean;
  selected: SelectedGraphItem;
  focusStableId: string | null;
  onSelect: (item: SelectedGraphItem) => void;
};
type Viewport = { x: number; y: number; scale: number };
type CanvasLevel = 'overview' | 'module';
type Mode = 'explore' | 'trace' | 'impact';
type FileScope = 'connections' | 'all';
type Drag = { pointerId: number; start: ScenePoint; viewport: Viewport; moved: boolean };
type WorldSize = { width: number; height: number };

const MIN_SCALE = .35;
const MAX_SCALE = 2.4;
const BUTTON_ZOOM = 1.14;

export function CanvasV2({ nodes, edges, isTruncated, selected, focusStableId, onSelect }: Props) {
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const hostRef = useRef<HTMLDivElement | null>(null);
  const dragRef = useRef<Drag | null>(null);
  const [size, setSize] = useState({ width: 1, height: 1 });
  const [viewport, setViewport] = useState<Viewport>({ x: 0, y: 0, scale: 1 });
  const [level, setLevel] = useState<CanvasLevel>('overview');
  const [focusedModuleKey, setFocusedModuleKey] = useState<string | null>(null);
  const [filter, setFilter] = useState<CanvasFilter>(defaultCanvasFilter);
  const [mode, setMode] = useState<Mode>('explore');
  const [fileScope, setFileScope] = useState<FileScope>('connections');
  const [connectionDirectoryKey, setConnectionDirectoryKey] = useState<string | null>(null);
  const [selectedFileId, setSelectedFileId] = useState<string | null>(null);
  const [traceStart, setTraceStart] = useState<string | null>(null);
  const [traceEnd, setTraceEnd] = useState<string | null>(null);

  // Filters are highlights, not topology mutations. Removing filtered endpoints made
  // real directory relationships disappear and produced misleading empty scenes.
  const scene = useMemo(() => buildModuleScene(nodes, edges, defaultCanvasFilter), [nodes, edges]);
  const matchingNodeIds = useMemo(() => new Set(nodes.filter(node => matchesCanvasFilter(node, filter)).map(node => node.stableId)), [filter, nodes]);
  const filterActive = filter.nodeKinds.length > 0 || filter.providers.length > 0 || filter.minConfidence !== null || filter.references !== 'all';
  const overviewLayout = useMemo(() => layoutModules(scene.modules), [scene.modules]);
  const nodeById = useMemo(() => new Map(nodes.map(node => [node.stableId, node])), [nodes]);
  const moduleByNodeId = useMemo(() => new Map(scene.modules.flatMap(module => module.nodeIds.map(id => [id, module.key] as const))), [scene.modules]);
  const detail = useMemo(() => focusedModuleKey ? buildModuleDetailScene(focusedModuleKey, scene, nodes, edges) : null, [edges, focusedModuleKey, nodes, scene]);
  const selectedNodeId = selected?.kind === 'node' ? selected.node.stableId : null;
  const selectedModuleKey = selectedNodeId ? moduleByNodeId.get(selectedNodeId) ?? null : focusedModuleKey;
  const activeConnectionDirectoryKey = fileScope === 'connections' ? connectionDirectoryKey ?? defaultConnectionKey(detail) : null;
  const displayedDetail = useMemo(() => {
    if (!detail) return null;
    if (fileScope === 'connections') return activeConnectionDirectoryKey ? directoryFileDetail(detail, activeConnectionDirectoryKey, selectedFileId) : connectedFileDetail(detail, selectedFileId);
    // The all-files view is a local inventory. Cross-directory file pairs remain
    // available through the explicit Connection boundary, avoiding hundreds of
    // long lines when a module contains many files.
    return { ...detail, remoteFiles: [], crossDirectoryConnections: [] };
  }, [activeConnectionDirectoryKey, detail, fileScope, selectedFileId]);
  const detailLayout = useMemo(() => displayedDetail ? layoutModuleDetail(displayedDetail) : null, [displayedDetail]);
  const trace = useMemo(() => traceStart && traceEnd ? findDirectedTrace(traceStart, traceEnd, edges) : null, [edges, traceEnd, traceStart]);
  const impact = useMemo(() => mode === 'impact' && selectedNodeId ? findDownstreamImpact(selectedNodeId, 2, nodes, edges) : null, [edges, mode, nodes, selectedNodeId]);
  const worldSize: WorldSize = level === 'module' && detailLayout ? detailLayout : overviewLayout;

  const fit = useCallback(() => setViewport(fitViewport(worldSize, size, level)), [level, size, worldSize]);
  const chooseRepresentative = useCallback((moduleKey: string) => {
    const module = scene.modules.find(item => item.key === moduleKey);
    if (!module) return null;
    return module.nodeIds.map(id => nodeById.get(id)).find(Boolean) ?? null;
  }, [nodeById, scene.modules]);
  const selectModule = useCallback((moduleKey: string) => {
    setFocusedModuleKey(moduleKey);
    const node = chooseRepresentative(moduleKey);
    if (node) onSelect({ kind: 'node', node });
  }, [chooseRepresentative, onSelect]);
  const openModule = useCallback((moduleKey: string) => {
    selectModule(moduleKey);
    setSelectedFileId(null);
    setFileScope('connections');
    setConnectionDirectoryKey(null);
    setLevel('module');
  }, [selectModule]);
  const showOverview = useCallback(() => setLevel('overview'), []);

  useEffect(() => {
    const host = hostRef.current;
    if (!host) return;
    const observer = new ResizeObserver(([entry]) => { const width = Math.max(1, Math.floor(entry.contentRect.width)); const height = Math.max(1, Math.floor(entry.contentRect.height)); setSize(current => current.width === width && current.height === height ? current : { width, height }); });
    observer.observe(host);
    return () => observer.disconnect();
  }, []);
  useEffect(() => {
    if (size.width <= 1 || size.height <= 1 || scene.isEmpty) return;
    setViewport(fitViewport(worldSize, size, level));
  }, [detail?.moduleKey, level, overviewLayout, scene.isEmpty, size, worldSize]);
  useEffect(() => {
    if (!focusStableId) return;
    const moduleKey = moduleByNodeId.get(focusStableId);
    if (moduleKey) openModule(moduleKey);
  }, [focusStableId, moduleByNodeId, openModule]);
  useEffect(() => {
    if (level === 'module' && !detail) setLevel('overview');
  }, [detail, level]);
  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const frame = requestAnimationFrame(() => drawCanvas(canvas, size, viewport, level, scene, overviewLayout, displayedDetail, detailLayout, selectedModuleKey, selectedFileId, activeConnectionDirectoryKey, trace, impact, matchingNodeIds, filterActive));
    return () => cancelAnimationFrame(frame);
  }, [activeConnectionDirectoryKey, detailLayout, displayedDetail, filterActive, impact, level, matchingNodeIds, overviewLayout, scene, selectedFileId, selectedModuleKey, size, trace, viewport]);

  const selectNode = (node: GraphMapNode, fileId: string) => {
    setSelectedFileId(fileId);
    onSelect({ kind: 'node', node });
    if (mode !== 'trace') return;
    if (!traceStart || traceEnd) { setTraceStart(node.stableId); setTraceEnd(null); }
    else if (traceStart !== node.stableId) setTraceEnd(node.stableId);
  };
  const selectAt = (screen: ScenePoint, open = false) => {
    const world = toWorld(screen, viewport);
    if (level === 'module' && displayedDetail && detailLayout) {
      for (const file of displayedDetail.files) {
        const bounds = detailLayout.fileBoundsById.get(file.id);
        if (!bounds || !contains(world, bounds)) continue;
        const node = nodeById.get(file.representativeNodeId);
        if (node) selectNode(node, file.id);
        return;
      }
      for (const file of displayedDetail.remoteFiles) {
        const bounds = detailLayout.remoteFileBoundsById.get(file.id);
        if (!bounds || !contains(world, bounds)) continue;
        const node = nodeById.get(file.representativeNodeId);
        if (node) selectNode(node, file.id);
        return;
      }
      return;
    }
    for (const [moduleKey, bounds] of overviewLayout.boundsByKey) {
      if (!contains(world, bounds)) continue;
      if (open) openModule(moduleKey); else selectModule(moduleKey);
      return;
    }
    onSelect(null);
  };
  const pointerDown = (event: PointerEvent<HTMLCanvasElement>) => {
    event.currentTarget.setPointerCapture(event.pointerId);
    dragRef.current = { pointerId: event.pointerId, start: pointerPoint(event), viewport, moved: false };
  };
  const pointerMove = (event: PointerEvent<HTMLCanvasElement>) => {
    const drag = dragRef.current;
    if (!drag || drag.pointerId !== event.pointerId) return;
    const point = pointerPoint(event);
    const dx = point.x - drag.start.x;
    const dy = point.y - drag.start.y;
    if (Math.abs(dx) + Math.abs(dy) > 4) drag.moved = true;
    if (drag.moved) setViewport({ ...drag.viewport, x: drag.viewport.x + dx, y: drag.viewport.y + dy });
  };
  const pointerUp = (event: PointerEvent<HTMLCanvasElement>) => {
    const drag = dragRef.current;
    if (!drag || drag.pointerId !== event.pointerId) return;
    dragRef.current = null;
    if (!drag.moved) selectAt(pointerPoint(event));
  };
  const wheel = (event: WheelEvent<HTMLCanvasElement>) => {
    event.preventDefault();
    if (event.ctrlKey || event.metaKey) {
      const pointer = pointerPoint(event);
      const normalizedDelta = clamp(event.deltaY, -80, 80);
      zoomAround(pointer, Math.exp(-normalizedDelta * .003));
      return;
    }
    setViewport(current => ({ ...current, x: current.x - event.deltaX, y: current.y - event.deltaY }));
  };
  const zoomAround = (point: ScenePoint, factor: number) => setViewport(current => {
    const scale = clamp(current.scale * factor, MIN_SCALE, MAX_SCALE);
    const ratio = scale / current.scale;
    return { scale, x: point.x - (point.x - current.x) * ratio, y: point.y - (point.y - current.y) * ratio };
  });
  const zoomCentered = (factor: number) => zoomAround({ x: size.width / 2, y: size.height / 2 }, factor);
  const keyDown = (event: KeyboardEvent<HTMLCanvasElement>) => {
    if (event.key === '+' || event.key === '=') { event.preventDefault(); zoomCentered(BUTTON_ZOOM); }
    else if (event.key === '-') { event.preventDefault(); zoomCentered(1 / BUTTON_ZOOM); }
    else if (event.key.toLowerCase() === 'f') { event.preventDefault(); fit(); }
    else if (event.key === 'Escape' && level === 'module') { event.preventDefault(); showOverview(); }
    else if (event.key === 'Enter' && level === 'overview' && focusedModuleKey) { event.preventDefault(); openModule(focusedModuleKey); }
    else if (event.key.startsWith('Arrow')) {
      event.preventDefault();
      const pan = 52;
      setViewport(current => ({ ...current, x: current.x + (event.key === 'ArrowLeft' ? pan : event.key === 'ArrowRight' ? -pan : 0), y: current.y + (event.key === 'ArrowUp' ? pan : event.key === 'ArrowDown' ? -pan : 0) }));
    }
  };
  const reset = () => {
    setFilter(defaultCanvasFilter);
    setFocusedModuleKey(null);
    setLevel('overview');
    setMode('explore');
    setFileScope('connections');
    setConnectionDirectoryKey(null);
    setSelectedFileId(null);
    setTraceStart(null);
    setTraceEnd(null);
    onSelect(null);
  };
  const kinds = [...new Set(nodes.map(node => node.nodeKind))].sort();
  const providers = [...new Set(nodes.map(node => node.provider))].sort();

  return (
    <div className="space-y-3" data-testid="canvas-v2">
      <div className="border border-slate-200 bg-slate-50 p-3">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <p className="text-sm font-semibold text-slate-900">{level === 'module' && detail ? detail.moduleLabel : 'Architecture map'}</p>
            <p className="mt-0.5 text-xs text-slate-500">{level === 'module' && detail && displayedDetail ? fileScope === 'connections' ? `${displayedDetail.files.length} of ${detail.files.length} files${activeConnectionDirectoryKey ? ` connected to ${detail.connectedDirectories.find(directory => directory.key === activeConnectionDirectoryKey)?.label ?? activeConnectionDirectoryKey}` : ''} · ${displayedDetail.remoteFiles.length} neighbour files · ${displayedDetail.crossDirectoryConnections.length} direct file links` : `${displayedDetail.files.length} of ${detail.files.length} local files · choose Connections to inspect cross-directory file links` : `${scene.modules.length} directories · ${scene.edges.length} cross-directory relationships`}{filterActive ? ` · ${matchingNodeIds.size} symbols highlighted` : ''}</p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            {level === 'module' && <button type="button" onClick={showOverview} className="map-button">← Overview</button>}
            {level === 'overview' && focusedModuleKey && <button type="button" onClick={() => openModule(focusedModuleKey)} className="map-button">Open directory</button>}
            {level === 'module' && <div className="flex overflow-hidden rounded border border-slate-300 bg-white"><ScopeButton active={fileScope} value="connections" onClick={() => setFileScope('connections')}>Connections</ScopeButton><ScopeButton active={fileScope} value="all" onClick={() => setFileScope('all')}>All files</ScopeButton></div>}
            {level === 'module' && activeConnectionDirectoryKey && <button type="button" onClick={() => openModule(activeConnectionDirectoryKey)} className="map-button">Open connected directory</button>}
            <div className="flex overflow-hidden rounded border border-slate-300 bg-white"><ModeButton active={mode} value="explore" onClick={() => setMode('explore')} /><ModeButton active={mode} value="trace" onClick={() => { setMode('trace'); setTraceStart(null); setTraceEnd(null); }} /><ModeButton active={mode} value="impact" onClick={() => setMode('impact')} /></div>
          </div>
        </div>
        <div className="mt-3 flex flex-wrap items-center gap-2">
          <FilterSelect label="Directory" value={focusedModuleKey ?? ''} onChange={value => value && (level === 'module' ? openModule(value) : selectModule(value))}><option value="">Select</option>{scene.modules.map(module => <option key={module.key} value={module.key}>{module.label}</option>)}</FilterSelect>
          {level === 'module' && fileScope === 'connections' && detail?.connectedDirectories.length ? <FilterSelect label="Connection" value={activeConnectionDirectoryKey ?? ''} onChange={value => { setConnectionDirectoryKey(value || null); setSelectedFileId(null); setFileScope('connections'); }}>{detail.connectedDirectories.map(directory => <option key={directory.key} value={directory.key}>{directory.label} · {directory.fileConnectionCount}</option>)}</FilterSelect> : null}
          <FilterSelect label="Highlight type" value={filter.nodeKinds[0] ?? ''} onChange={value => setFilter(current => ({ ...current, nodeKinds: value ? [value] : [] }))}><option value="">All</option>{kinds.map(kind => <option key={kind}>{kind}</option>)}</FilterSelect>
          <FilterSelect label="Provider" value={filter.providers[0] ?? ''} onChange={value => setFilter(current => ({ ...current, providers: value ? [value] : [] }))}><option value="">All</option>{providers.map(provider => <option key={provider}>{provider}</option>)}</FilterSelect>
          <FilterSelect label="Confidence" value={filter.minConfidence ?? ''} onChange={value => setFilter(current => ({ ...current, minConfidence: value ? Number(value) : null }))}><option value="">All</option><option value="0.7">≥ 0.70</option><option value="0.9">≥ 0.90</option></FilterSelect>
          <FilterSelect label="References" value={filter.references} onChange={value => setFilter(current => ({ ...current, references: value as CanvasFilter['references'] }))}><option value="all">All</option><option value="internal">Internal</option><option value="external">External</option></FilterSelect>
          <Tooltip content="Clear selection, filters, investigation state, and return to the directory overview."><button type="button" onClick={reset} className="map-button">Reset</button></Tooltip>
        </div>
      </div>
      {isTruncated && <p className="border-l-2 border-amber-500 bg-amber-50 px-3 py-2 text-xs text-amber-900" role="status">This map is a bounded projection. Results cover rendered data only.</p>}
      {(mode === 'trace' || mode === 'impact') && <InvestigationSummary mode={mode} traceStart={traceStart} traceEnd={traceEnd} trace={trace} impact={impact} />}
      {scene.isEmpty ? <EmptyState onClear={() => setFilter(defaultCanvasFilter)} /> : (
        <div ref={hostRef} className="relative h-[min(72vh,720px)] min-h-[480px] overflow-hidden border border-slate-300 bg-slate-50">
          <canvas ref={canvasRef} tabIndex={0} role="application" aria-label={level === 'module' ? `Files and connected directories for ${detail?.moduleLabel ?? 'selected directory'}` : 'Interactive architecture directory map'} aria-describedby="canvas-v2-instructions" onPointerDown={pointerDown} onPointerMove={pointerMove} onPointerUp={pointerUp} onPointerCancel={pointerUp} onDoubleClick={event => selectAt(pointerPoint(event), true)} onWheel={wheel} onKeyDown={keyDown} className="block size-full cursor-grab touch-none outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-indigo-600 active:cursor-grabbing" />
          <div className="absolute right-3 top-3 flex items-center overflow-hidden rounded border border-slate-300 bg-white" aria-label="Canvas zoom controls">
            <Tooltip content="Zoom out around the center of the canvas."><button type="button" onClick={() => zoomCentered(1 / BUTTON_ZOOM)} className="h-8 w-9 border-r border-slate-200 text-base text-slate-700" aria-label="Zoom out">−</button></Tooltip>
            <span className="min-w-14 px-2 text-center text-xs tabular-nums text-slate-600" aria-live="polite">{Math.round(viewport.scale * 100)}%</span>
            <Tooltip content="Zoom in around the center of the canvas."><button type="button" onClick={() => zoomCentered(BUTTON_ZOOM)} className="h-8 w-9 border-l border-slate-200 text-base text-slate-700" aria-label="Zoom in">+</button></Tooltip>
            <Tooltip content="Fit the current directory or file map inside the canvas."><button type="button" onClick={fit} className="h-8 border-l border-slate-200 px-3 text-xs font-medium text-slate-700">Fit</button></Tooltip>
          </div>
          <div className="pointer-events-none absolute bottom-3 left-3 border border-slate-200 bg-white/95 px-2.5 py-1.5 text-[11px] text-slate-600">{level === 'overview' ? 'Click a directory to isolate its connections · double-click to open' : 'Scroll to pan · pinch or Ctrl-scroll to zoom · click either side of a file-to-file link'}</div>
          <p id="canvas-v2-instructions" className="sr-only">In the overview, click a directory to highlight its cross-directory relationships and press Enter or double-click to open it. In a directory, files are grouped from symbols and connected directories remain visible. Click a file to highlight its connections. Drag to pan, use plus or minus to zoom, F to fit, and Escape to return to overview.</p>
        </div>
      )}
    </div>
  );
}

function drawCanvas(canvas: HTMLCanvasElement, size: { width: number; height: number }, viewport: Viewport, level: CanvasLevel, scene: CanvasScene, overview: ModuleLayout, detail: ModuleDetailScene | null, detailLayout: ModuleDetailLayout | null, selectedModuleKey: string | null, selectedFileId: string | null, connectionDirectoryKey: string | null, trace: ReturnType<typeof findDirectedTrace> | null, impact: ReturnType<typeof findDownstreamImpact> | null, matchingNodeIds: ReadonlySet<string>, filterActive: boolean) {
  const ratio = window.devicePixelRatio || 1;
  if (canvas.width !== Math.floor(size.width * ratio) || canvas.height !== Math.floor(size.height * ratio)) { canvas.width = Math.floor(size.width * ratio); canvas.height = Math.floor(size.height * ratio); }
  const ctx = canvas.getContext('2d');
  if (!ctx) return;
  ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
  ctx.fillStyle = '#f8fafc';
  ctx.fillRect(0, 0, size.width, size.height);
  ctx.save();
  ctx.translate(viewport.x, viewport.y);
  ctx.scale(viewport.scale, viewport.scale);
  if (level === 'module' && detail && detailLayout) drawModuleDetail(ctx, detail, detailLayout, selectedFileId, connectionDirectoryKey, trace, impact, matchingNodeIds, filterActive);
  else drawOverview(ctx, scene, overview, selectedModuleKey, matchingNodeIds, filterActive);
  ctx.restore();
}

function drawOverview(ctx: CanvasRenderingContext2D, scene: CanvasScene, layout: ModuleLayout, selectedKey: string | null, matchingNodeIds: ReadonlySet<string>, filterActive: boolean) {
  const connectedKeys = new Set<string>();
  if (selectedKey) for (const edge of scene.edges) if (edge.sourceModuleKey === selectedKey || edge.targetModuleKey === selectedKey) { connectedKeys.add(edge.sourceModuleKey); connectedKeys.add(edge.targetModuleKey); }
  for (const edge of scene.edges) {
    const from = layout.boundsByKey.get(edge.sourceModuleKey); const to = layout.boundsByKey.get(edge.targetModuleKey);
    if (!from || !to) continue;
    const highlighted = !!selectedKey && (edge.sourceModuleKey === selectedKey || edge.targetModuleKey === selectedKey);
    drawArrow(ctx, moduleCenter(from), moduleCenter(to), highlighted ? '#4f46e5' : '#94a3b8', highlighted ? 2.5 : 1, highlighted ? .95 : selectedKey ? .05 : .14);
  }
  for (const module of scene.modules) {
    const bounds = layout.boundsByKey.get(module.key)!;
    const active = module.key === selectedKey;
    const mutedBySelection = !!selectedKey && !active && !connectedKeys.has(module.key);
    const mutedByFilter = filterActive && !module.nodeIds.some(id => matchingNodeIds.has(id));
    ctx.globalAlpha = mutedBySelection ? .2 : mutedByFilter ? .28 : 1;
    card(ctx, bounds, active ? '#eef2ff' : '#ffffff', active ? '#4f46e5' : connectedKeys.has(module.key) ? '#94a3b8' : '#cbd5e1', active ? 2 : 1, 8);
    ctx.fillStyle = '#0f172a'; ctx.font = '600 15px Inter, sans-serif'; ctx.fillText(module.label, bounds.x + 18, bounds.y + 31);
    ctx.fillStyle = '#475569'; ctx.font = '12px Inter, sans-serif'; ctx.fillText(`${module.nodeCount.toLocaleString()} symbols`, bounds.x + 18, bounds.y + 58);
    ctx.fillText(`${module.crossModuleDependencyCount.toLocaleString()} external connections`, bounds.x + 18, bounds.y + 81);
  }
  ctx.globalAlpha = 1;
}

function drawModuleDetail(ctx: CanvasRenderingContext2D, detail: ModuleDetailScene, layout: ModuleDetailLayout, selectedFileId: string | null, connectionDirectoryKey: string | null, trace: ReturnType<typeof findDirectedTrace> | null, impact: ReturnType<typeof findDownstreamImpact> | null, matchingNodeIds: ReadonlySet<string>, filterActive: boolean) {
  card(ctx, layout.focusBounds, '#ffffff', '#94a3b8', 1, 10);
  ctx.fillStyle = '#0f172a'; ctx.font = '600 18px Inter, sans-serif'; ctx.fillText(detail.moduleLabel, layout.focusBounds.x + 28, layout.focusBounds.y + 35);
  ctx.fillStyle = '#64748b'; ctx.font = '12px Inter, sans-serif'; ctx.fillText(`${detail.files.length} local files · ${detail.remoteFiles.length} neighbour files · ${detail.crossDirectoryConnections.length} direct links`, layout.focusBounds.x + 28, layout.focusBounds.y + 59);

  for (const connection of detail.internalConnections) {
    const source = layout.fileBoundsById.get(connection.sourceFileId); const target = layout.fileBoundsById.get(connection.targetFileId);
    if (!source || !target) continue;
    const highlighted = !!selectedFileId && (connection.sourceFileId === selectedFileId || connection.targetFileId === selectedFileId);
    drawArrow(ctx, moduleCenter(source), moduleCenter(target), highlighted ? '#4f46e5' : '#94a3b8', highlighted ? 2.2 : 1, highlighted ? .9 : selectedFileId ? .06 : .16);
  }
  for (const connection of detail.crossDirectoryConnections) {
    const local = layout.fileBoundsById.get(connection.localFileId); const remote = layout.remoteFileBoundsById.get(connection.remoteFileId);
    if (!local || !remote) continue;
    const highlighted = connection.localFileId === selectedFileId || connection.remoteFileId === selectedFileId;
    const from = connection.direction === 'outgoing' ? moduleCenter(local) : moduleCenter(remote);
    const to = connection.direction === 'outgoing' ? moduleCenter(remote) : moduleCenter(local);
    drawArrow(ctx, from, to, highlighted ? '#4f46e5' : '#475569', highlighted ? 2.8 : 1.35, highlighted ? 1 : selectedFileId ? .08 : .52);
  }
  for (const file of detail.remoteFiles) {
    const bounds = layout.remoteFileBoundsById.get(file.id)!;
    const active = file.id === selectedFileId;
    const matchesFilter = !filterActive || file.nodeIds.some(id => matchingNodeIds.has(id));
    ctx.globalAlpha = matchesFilter ? 1 : .22;
    card(ctx, bounds, active ? '#eef2ff' : '#f8fafc', active ? '#4f46e5' : '#94a3b8', active ? 2 : 1, 7);
    ctx.fillStyle = '#172033'; ctx.font = '600 12px Inter, sans-serif'; ctx.fillText(ellipsize(file.label, 29), bounds.x + 12, bounds.y + 22);
    ctx.fillStyle = '#475569'; ctx.font = '10px Inter, sans-serif'; ctx.fillText(`${file.moduleLabel} · ${file.connectionCount} direct ${file.connectionCount === 1 ? 'link' : 'links'}`, bounds.x + 12, bounds.y + 42);
    if (file.path) ctx.fillText(ellipsize(parentPath(file.path), 34), bounds.x + 12, bounds.y + 58);
  }
  ctx.globalAlpha = 1;
  for (const file of detail.files) {
    const bounds = layout.fileBoundsById.get(file.id)!;
    const active = file.id === selectedFileId;
    const signaled = trace?.kind === 'path' && file.nodeIds.some(id => trace.nodeIds.includes(id)) || impact && file.nodeIds.some(id => impact.nodeIds.has(id));
    const matchesFilter = !filterActive || file.nodeIds.some(id => matchingNodeIds.has(id));
    ctx.globalAlpha = matchesFilter ? 1 : .22;
    card(ctx, bounds, active ? '#eef2ff' : signaled ? '#f5f3ff' : '#ffffff', active ? '#4f46e5' : signaled ? '#7c3aed' : '#cbd5e1', active ? 2 : 1, 7);
    ctx.fillStyle = '#172033'; ctx.font = '600 12px Inter, sans-serif'; ctx.fillText(ellipsize(file.label, 29), bounds.x + 12, bounds.y + 23);
    ctx.fillStyle = '#64748b'; ctx.font = '10px Inter, sans-serif'; ctx.fillText(`${file.symbolCount} symbols · ${file.internalConnectionCount} internal · ${file.externalConnectionCount} external`, bounds.x + 12, bounds.y + 44);
    if (file.path) ctx.fillText(ellipsize(parentPath(file.path), 34), bounds.x + 12, bounds.y + 58);
  }
  ctx.globalAlpha = 1;
}

function drawArrow(ctx: CanvasRenderingContext2D, from: ScenePoint, to: ScenePoint, color: string, width: number, alpha: number) {
  ctx.save(); ctx.globalAlpha = alpha; ctx.strokeStyle = color; ctx.fillStyle = color; ctx.lineWidth = width;
  const angle = Math.atan2(to.y - from.y, to.x - from.x); const end = { x: to.x - Math.cos(angle) * 14, y: to.y - Math.sin(angle) * 14 };
  ctx.beginPath(); ctx.moveTo(from.x, from.y); ctx.lineTo(end.x, end.y); ctx.stroke();
  ctx.beginPath(); ctx.moveTo(end.x, end.y); ctx.lineTo(end.x - Math.cos(angle - .45) * 7, end.y - Math.sin(angle - .45) * 7); ctx.lineTo(end.x - Math.cos(angle + .45) * 7, end.y - Math.sin(angle + .45) * 7); ctx.closePath(); ctx.fill(); ctx.restore();
}
function card(ctx: CanvasRenderingContext2D, bounds: ModuleBounds, fill: string, stroke: string, width: number, radius: number) { ctx.beginPath(); ctx.roundRect(bounds.x, bounds.y, bounds.width, bounds.height, radius); ctx.fillStyle = fill; ctx.fill(); ctx.strokeStyle = stroke; ctx.lineWidth = width; ctx.stroke(); }
function fitViewport(world: WorldSize, size: { width: number; height: number }, level: CanvasLevel): Viewport {
  const padding = 48;
  const widthScale = clamp((size.width - padding * 2) / world.width, MIN_SCALE, 1);
  if (level === 'module') return { scale: widthScale, x: (size.width - world.width * widthScale) / 2, y: padding };
  const scale = clamp(Math.min(widthScale, (size.height - padding * 2) / world.height), MIN_SCALE, 1);
  return { scale, x: (size.width - world.width * scale) / 2, y: (size.height - world.height * scale) / 2 };
}
function pointerPoint(event: { nativeEvent: { offsetX: number; offsetY: number } }) { return { x: event.nativeEvent.offsetX, y: event.nativeEvent.offsetY }; }
function toWorld(point: ScenePoint, viewport: Viewport) { return { x: (point.x - viewport.x) / viewport.scale, y: (point.y - viewport.y) / viewport.scale }; }
function contains(point: ScenePoint, bounds: ModuleBounds) { return point.x >= bounds.x && point.x <= bounds.x + bounds.width && point.y >= bounds.y && point.y <= bounds.y + bounds.height; }
function clamp(value: number, min: number, max: number) { return Math.max(min, Math.min(max, value)); }
function ellipsize(value: string, length: number) { return value.length > length ? `${value.slice(0, length - 1)}…` : value; }
function parentPath(path: string) { const parts = path.split('/'); return parts.slice(Math.max(0, parts.length - 3), -1).join('/'); }
function defaultConnectionKey(detail: ModuleDetailScene | null) { return detail?.connectedDirectories.find(directory => directory.key !== 'References/External')?.key ?? detail?.connectedDirectories[0]?.key ?? null; }
function ModeButton({ active, value, onClick }: { active: Mode; value: Mode; onClick: () => void }) { return <button type="button" onClick={onClick} aria-pressed={active === value} className={`px-2.5 py-1.5 text-xs font-medium capitalize ${active === value ? 'bg-indigo-700 text-white' : 'text-slate-600 hover:bg-slate-50'}`}>{value}</button>; }
function ScopeButton({ active, value, onClick, children }: { active: FileScope; value: FileScope; onClick: () => void; children: React.ReactNode }) { return <button type="button" onClick={onClick} aria-pressed={active === value} className={`px-2.5 py-1.5 text-xs font-medium ${active === value ? 'bg-slate-800 text-white' : 'text-slate-600 hover:bg-slate-50'}`}>{children}</button>; }
function FilterSelect({ label, value, onChange, children }: { label: string; value: string | number; onChange: (value: string) => void; children: React.ReactNode }) { return <label className="text-xs text-slate-600">{label}<select value={value} onChange={event => onChange(event.target.value)} className="ml-2 max-w-44 rounded border border-slate-300 bg-white px-2 py-1">{children}</select></label>; }
function EmptyState({ onClear }: { onClear: () => void }) { return <div className="flex min-h-96 items-center justify-center border border-dashed border-slate-300 bg-slate-50 p-8 text-center"><div><p className="font-medium text-slate-800">No directories match this filter.</p><button type="button" onClick={onClear} className="mt-3 text-sm text-indigo-700">Clear filters</button></div></div>; }
function InvestigationSummary({ mode, traceStart, traceEnd, trace, impact }: { mode: Mode; traceStart: string | null; traceEnd: string | null; trace: ReturnType<typeof findDirectedTrace> | null; impact: ReturnType<typeof findDownstreamImpact> | null }) { if (mode === 'trace') return <p className="border-l-2 border-indigo-600 bg-indigo-50 px-3 py-2 text-xs text-slate-700">{!traceStart ? 'Trace: open a directory and select a source file.' : !traceEnd ? 'Trace: select a destination file.' : trace?.kind === 'path' ? `Trace found: ${trace.nodeIds.length - 1} directed steps.` : 'No directed path exists between the selected files.'}</p>; return <p className="border-l-2 border-indigo-600 bg-indigo-50 px-3 py-2 text-xs text-slate-700">{impact ? `Impact: ${impact.nodeIds.size} affected symbols across ${impact.moduleCounts.size} directories.` : 'Impact: open a directory and select a file.'}</p>; }
