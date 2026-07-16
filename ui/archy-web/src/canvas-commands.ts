export type CanvasCommand = 'explore' | 'trace' | 'impact' | 'fit' | 'reset';
export type CanvasCommandEvent = CanvasCommand | { command: 'trace-from'; stableId: string };

const eventName = 'archy:canvas-command';

export function dispatchCanvasCommand(command: CanvasCommandEvent) {
  window.dispatchEvent(new CustomEvent<CanvasCommandEvent>(eventName, { detail: command }));
}

export function subscribeCanvasCommands(listener: (command: CanvasCommandEvent) => void) {
  const handle = (event: Event) => listener((event as CustomEvent<CanvasCommandEvent>).detail);
  window.addEventListener(eventName, handle);
  return () => window.removeEventListener(eventName, handle);
}
