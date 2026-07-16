import { useEffect, useMemo, useState } from 'react';
import type { EventStreamItem } from '../api';
import { Panel } from './Panel';

type SessionTimelineProps = { sessionId: string; events: EventStreamItem[]; onSessionIdChange: (value: string) => void };

export function SessionTimeline({ sessionId, events, onSessionIdChange }: SessionTimelineProps) {
  const [playing, setPlaying] = useState(false);
  const [frame, setFrame] = useState(0);
  const ordered = useMemo(() => [...events].sort((left, right) => left.sequence - right.sequence), [events]);
  useEffect(() => { setFrame(ordered.length); }, [ordered.length, sessionId]);
  useEffect(() => {
    if (!playing || frame >= ordered.length) return;
    const timer = window.setTimeout(() => setFrame(value => Math.min(value + 1, ordered.length)), 650);
    return () => window.clearTimeout(timer);
  }, [playing, frame, ordered.length]);
  const visible = ordered.slice(0, frame);

  return <Panel title="Session replay" description="Replay durable session events in sequence and follow new events as they arrive." action={<span className="text-xs text-slate-500">{visible.length}/{ordered.length} frames</span>}>
    <label className="block text-xs font-medium text-slate-300" htmlFor="session-id">Codex session ID</label>
    <input id="session-id" value={sessionId} onChange={event => onSessionIdChange(event.target.value.trim())} placeholder="session-…" className="mt-2 w-full rounded-md border border-slate-600 bg-slate-950/50 px-3 py-2 text-sm outline-none placeholder:text-slate-500 focus:border-sky-400" />
    {sessionId && <div className="mt-4 space-y-3"><div className="flex items-center gap-2"><button type="button" onClick={() => setPlaying(value => !value)} className="rounded-md bg-sky-400 px-2.5 py-1.5 text-xs font-semibold text-slate-950 hover:bg-sky-300">{playing ? 'Pause' : 'Play'}</button><button type="button" onClick={() => { setPlaying(false); setFrame(0); }} className="rounded-md border border-slate-600 px-2.5 py-1.5 text-xs text-slate-200 hover:border-slate-400">Restart</button></div><input aria-label="Replay position" className="w-full accent-sky-400" type="range" min="0" max={ordered.length} value={frame} onChange={event => { setPlaying(false); setFrame(Number(event.target.value)); }} /></div>}
    {sessionId ? <ol className="mt-4 max-h-64 space-y-2 overflow-auto" aria-live="polite">{visible.slice().reverse().map(event => <li key={event.eventId} className="rounded-md border border-slate-700 bg-slate-950/40 px-3 py-2 text-xs"><div className="flex justify-between gap-2"><span className="font-semibold text-sky-300">#{event.sequence} {event.kind.replaceAll('_', ' ')}</span><time className="text-slate-500">{new Date(event.occurredAtUtc).toLocaleTimeString()}</time></div>{event.graphRevision && <p className="mt-1 text-slate-400">Graph revision {event.graphRevision}</p>}</li>)}</ol> : <p className="mt-4 text-sm text-slate-400">Enter a session ID to replay durable events and follow new events live.</p>}
  </Panel>;
}
