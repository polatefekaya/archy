import { useState, type FormEvent } from 'react';
import { runQuery, type GraphTraversal } from '../api';
import { Panel } from './Panel';
import { HelpHint } from './Tooltip';

export function QueryPanel() {
  const [text, setText] = useState('');
  const [result, setResult] = useState<GraphTraversal | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [running, setRunning] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setRunning(true); setResult(null); setMessage(null);
    const query = await runQuery(text);
    setRunning(false); setResult(query.value ?? null); setMessage(query.error ?? null);
  }

  return <Panel title="Deterministic query" description="Queries run against the current graph rather than generating a best-guess answer.">
    <form className="flex gap-2" onSubmit={submit}>
      <label className="sr-only" htmlFor="architecture-query">Architecture question</label>
      <input id="architecture-query" required className="field flex-1" value={text} onChange={event => setText(event.target.value)} placeholder="what uses type:orders?" />
      <button disabled={running} className="button button-primary">{running ? 'Running…' : 'Run'}</button>
    </form>
    <p className="mt-3 text-xs leading-5 text-slate-500">Supports “what uses …?”, “what does … use?”, and “what breaks if I delete …?”. <HelpHint>Unsupported phrasing returns a clarification instead of an invented answer.</HelpHint></p>
    {result && <div className="mt-3 border-l-2 border-indigo-600 bg-slate-50 p-3 text-sm"><p className="font-medium text-slate-800">{result.edges.length} {result.direction} edge{result.edges.length === 1 ? '' : 's'} at revision {result.revision}</p>{result.isTruncated && <p className="mt-1 text-xs text-amber-700">Traversal is truncated; inspect a narrower path.</p>}</div>}
    {message && <p className="mt-3 border border-amber-300 bg-amber-50 p-3 text-sm text-amber-900" role="status">{message}</p>}
  </Panel>;
}
