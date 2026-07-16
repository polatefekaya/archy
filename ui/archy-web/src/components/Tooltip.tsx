import * as TooltipPrimitive from '@radix-ui/react-tooltip';
import type { PropsWithChildren, ReactNode } from 'react';

type TooltipProps = PropsWithChildren<{ content: ReactNode; side?: 'top' | 'right' | 'bottom' | 'left' }>;

/** A small, consistent explanation for controls whose result is not obvious from their label. */
export function Tooltip({ children, content, side = 'top' }: TooltipProps) {
  return <TooltipPrimitive.Provider delayDuration={260}>
    <TooltipPrimitive.Root>
      <TooltipPrimitive.Trigger asChild>{children}</TooltipPrimitive.Trigger>
      <TooltipPrimitive.Portal>
        <TooltipPrimitive.Content className="tooltip-content" side={side} sideOffset={8}>
          {content}
          <TooltipPrimitive.Arrow className="fill-slate-900" width={10} height={5} />
        </TooltipPrimitive.Content>
      </TooltipPrimitive.Portal>
    </TooltipPrimitive.Root>
  </TooltipPrimitive.Provider>;
}

export function HelpHint({ children }: PropsWithChildren) {
  return <Tooltip content={children}><span className="help-hint" tabIndex={0} aria-label="More information">i</span></Tooltip>;
}
