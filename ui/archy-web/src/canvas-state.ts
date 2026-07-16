export type CanvasMode = 'explore' | 'trace' | 'impact';
export type CanvasScope = 'all' | 'focus' | 'hop-1' | 'hop-2' | 'component';

export type CanvasViewState = {
  mode: CanvasMode;
  scope: CanvasScope;
  nodeKind: string;
  provider: string;
  module: string;
  confidence: 'all' | 'high' | 'low';
  referenceScope: 'all' | 'internal' | 'external';
  showEdges: boolean;
  showLabels: boolean;
  impactDepth: 1 | 2 | 3;
};

export const defaultCanvasViewState: CanvasViewState = {
  mode: 'explore', scope: 'all', nodeKind: 'all', provider: 'all', module: 'all', confidence: 'all', referenceScope: 'all', showEdges: true, showLabels: true, impactDepth: 2,
};

const modes = new Set<CanvasMode>(['explore', 'trace', 'impact']);
const scopes = new Set<CanvasScope>(['all', 'focus', 'hop-1', 'hop-2', 'component']);

export function readCanvasViewState(search: string): CanvasViewState {
  const query = new URLSearchParams(search);
  const mode = query.get('canvasMode');
  const scope = query.get('canvasScope');
  const confidence = query.get('confidence');
  const referenceScope = query.get('references');
  const depth = Number(query.get('impactDepth'));
  return {
    ...defaultCanvasViewState,
    mode: modes.has(mode as CanvasMode) ? mode as CanvasMode : defaultCanvasViewState.mode,
    scope: scopes.has(scope as CanvasScope) ? scope as CanvasScope : defaultCanvasViewState.scope,
    nodeKind: query.get('nodeKind')?.slice(0, 80) || defaultCanvasViewState.nodeKind,
    provider: query.get('provider')?.slice(0, 80) || defaultCanvasViewState.provider,
    module: query.get('module')?.slice(0, 160) || defaultCanvasViewState.module,
    confidence: confidence === 'high' || confidence === 'low' ? confidence : 'all',
    referenceScope: referenceScope === 'internal' || referenceScope === 'external' ? referenceScope : 'all',
    showEdges: query.get('edges') !== '0',
    showLabels: query.get('labels') !== '0',
    impactDepth: depth === 1 || depth === 3 ? depth : 2,
  };
}

export function writeCanvasViewState(currentSearch: string, state: CanvasViewState) {
  const query = new URLSearchParams(currentSearch);
  query.set('canvasMode', state.mode); query.set('canvasScope', state.scope);
  query.set('impactDepth', String(state.impactDepth));
  state.nodeKind === 'all' ? query.delete('nodeKind') : query.set('nodeKind', state.nodeKind);
  state.provider === 'all' ? query.delete('provider') : query.set('provider', state.provider);
  state.module === 'all' ? query.delete('module') : query.set('module', state.module);
  state.confidence === 'all' ? query.delete('confidence') : query.set('confidence', state.confidence);
  state.referenceScope === 'all' ? query.delete('references') : query.set('references', state.referenceScope);
  state.showEdges ? query.delete('edges') : query.set('edges', '0');
  state.showLabels ? query.delete('labels') : query.set('labels', '0');
  return query.toString();
}

export function readCanvasTraceSelection(search: string) {
  const query = new URLSearchParams(search);
  const read = (key: string) => query.get(key)?.trim().slice(0, 320) || null;
  return { start: read('traceStart'), end: read('traceEnd') };
}

export function writeCanvasTraceSelection(currentSearch: string, start: string | null, end: string | null) {
  const query = new URLSearchParams(currentSearch);
  start ? query.set('traceStart', start) : query.delete('traceStart');
  end ? query.set('traceEnd', end) : query.delete('traceEnd');
  return query.toString();
}
