import { useEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react';
import { searchGraphNodes, type GraphNode } from '../api';
import { Tooltip } from './Tooltip';
import { dispatchCanvasCommand, type CanvasCommand } from '../canvas-commands';

type GraphSearchProps = { onChoose: (node: GraphNode) => void };

/// <summary>Searches names and paths, then lets a person navigate the graph without ever handling a stable ID.</summary>
export function GraphSearch({ onChoose }: GraphSearchProps) {
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<GraphNode[]>([]);
  const [message, setMessage] = useState<string | null>(null);
  const [activeIndex, setActiveIndex] = useState(0);
  const request = useRef<AbortController | null>(null);
  const input = useRef<HTMLInputElement | null>(null);
  const commands: Array<{ command: CanvasCommand; label: string; detail: string }> = [
    { command: 'explore', label: 'Explore canvas', detail: 'Browse modules and direct relationships' },
    { command: 'trace', label: 'Trace dependency path', detail: 'Choose a source and destination node' },
    { command: 'impact', label: 'Show downstream impact', detail: 'Inspect dependents of a selected node' },
    { command: 'fit', label: 'Fit canvas view', detail: 'Fit visible graph items into the canvas' },
    { command: 'reset', label: 'Reset canvas', detail: 'Clear canvas state and return to the overview' },
  ];

  const commandMatches = useMemo(() => commands.filter(item => item.label.toLowerCase().includes(query.trim().slice(1).trim().toLowerCase())), [query]);
  const isCommandPalette = query.trim().startsWith('>');

  useEffect(() => {
    const trimmed = query.trim();
    request.current?.abort();
    setActiveIndex(0);
    if (trimmed.startsWith('>')) { setResults([]); setMessage(null); return; }
    if (trimmed.length < 2) { setResults([]); setMessage(null); return; }
    const controller = new AbortController(); request.current = controller;
    const timer = window.setTimeout(() => {
      void searchGraphNodes(trimmed, controller.signal).then(result => {
        if (controller.signal.aborted) return;
        setResults(result.value?.items ?? []);
        setMessage(result.error ?? (result.value?.items.length === 0 ? 'No matching architecture item.' : null));
      });
    }, 180);
    return () => { window.clearTimeout(timer); controller.abort(); };
  }, [query]);

  useEffect(() => {
    const focusSearch = (event: globalThis.KeyboardEvent) => {
      const target = event.target as HTMLElement | null;
      if (event.key !== '/' || event.metaKey || event.ctrlKey || event.altKey || target?.matches('input, textarea, select, [contenteditable="true"]')) return;
      event.preventDefault(); input.current?.focus();
    };
    window.addEventListener('keydown', focusSearch);
    return () => window.removeEventListener('keydown', focusSearch);
  }, []);

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    const count = isCommandPalette ? commandMatches.length : results.length;
    if (!count) return;
    if (event.key === 'ArrowDown') { event.preventDefault(); setActiveIndex(current => (current + 1) % count); }
    else if (event.key === 'ArrowUp') { event.preventDefault(); setActiveIndex(current => (current - 1 + count) % count); }
    else if (event.key === 'Enter') {
      event.preventDefault();
      if (isCommandPalette) { dispatchCanvasCommand(commandMatches[activeIndex]!.command); setQuery(''); }
      else { onChoose(results[activeIndex]!); setQuery(''); setResults([]); }
    } else if (event.key === 'Escape') { setQuery(''); setResults([]); setMessage(null); }
  };

  return <div className="relative w-full max-w-2xl" data-testid="graph-search">
    <label className="sr-only" htmlFor="graph-search">Find a type, file, module, or symbol</label>
    <div className="flex items-center border-b border-slate-300 bg-white px-1 focus-within:border-indigo-600">
      <span className="mr-3 text-lg text-slate-400" aria-hidden="true">⌕</span>
      <input ref={input} id="graph-search" value={query} onChange={event => setQuery(event.target.value)} onKeyDown={onKeyDown} aria-activedescendant={(isCommandPalette ? commandMatches.length : results.length) ? `graph-search-result-${activeIndex}` : undefined} className="min-w-0 flex-1 bg-transparent py-3 text-sm text-slate-900 outline-none placeholder:text-slate-400" placeholder="Search files, types, modules, or symbols — type > for commands" autoComplete="off" />
      {query && <Tooltip content="Clear the current search and close its results."><button type="button" onClick={() => setQuery('')} className="text-xs text-slate-500 hover:text-slate-900" aria-label="Clear graph search">Clear</button></Tooltip>}
    </div>
    {isCommandPalette && <div className="absolute z-20 mt-2 w-full overflow-hidden border border-slate-200 bg-white" role="menu" aria-label="Canvas commands">
      {commandMatches.map((item, index) => <button type="button" id={`graph-search-result-${index}`} key={item.command} onMouseMove={() => setActiveIndex(index)} onClick={() => { dispatchCanvasCommand(item.command); setQuery(''); }} className={`block w-full border-b border-slate-100 px-4 py-3 text-left last:border-0 focus:outline-none ${activeIndex === index ? 'bg-slate-50' : 'hover:bg-slate-50'}`} role="menuitem"><span className="block text-sm font-medium text-slate-900">{item.label}</span><span className="mt-1 block text-xs text-slate-500">{item.detail}</span></button>)}
    </div>}
    {(results.length > 0 || message) && !isCommandPalette && <div className="absolute z-20 mt-2 w-full overflow-hidden border border-slate-200 bg-white" role="listbox" aria-label="Architecture search results">
      {results.map((node, index) => <button type="button" id={`graph-search-result-${index}`} key={node.stableId} onMouseMove={() => setActiveIndex(index)} onClick={() => { onChoose(node); setQuery(''); setResults([]); }} className={`block w-full border-b border-slate-100 px-4 py-3 text-left last:border-0 focus:outline-none ${activeIndex === index ? 'bg-slate-50' : 'hover:bg-slate-50'}`} role="option" aria-selected={activeIndex === index}><span className="block text-sm font-medium text-slate-900">{node.displayName}</span><span className="mt-1 block truncate text-xs text-slate-500">{node.nodeKind} · {node.filePath ?? node.canonicalKey}</span></button>)}
      {message && <p className="px-4 py-3 text-sm text-slate-500">{message}</p>}
    </div>}
  </div>;
}
