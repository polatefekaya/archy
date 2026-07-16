import type { PropsWithChildren, ReactNode } from 'react';
import { HelpHint } from './Tooltip';

type PanelProps = PropsWithChildren<{ title: string; action?: ReactNode; className?: string; description?: string }>;

export function Panel({ title, action, className = '', description, children }: PanelProps) {
  return <section className={`surface rounded-lg ${className}`}>
    <header className="flex min-h-14 items-center justify-between gap-4 border-b border-slate-200 px-5 py-3"><div className="flex items-center gap-2"><h2 className="text-sm font-semibold text-slate-900">{title}</h2>{description && <HelpHint>{description}</HelpHint>}</div>{action}</header>
    <div className="p-5">{children}</div>
  </section>;
}
