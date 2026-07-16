# Canvas V2 Production Backlog

Canvas V2 should be an architecture investigation workspace rather than a static graph viewer. The work below is ordered to deliver useful exploration workflows without requiring a full rewrite at the start.

## Epic: Canvas V2 foundations

### CV2-01 — Define canvas interaction model

Document Explore, Trace, and Impact modes, including click, keyboard, focus, reset, and selection behavior.

**Acceptance criteria**

- Each mode has a defined purpose, entry action, and empty state.
- Keyboard shortcuts are documented and discoverable.
- Selection, hover, pinned selection, and cleared-state behavior are specified.
- Product and design review approve the interaction contract.

### CV2-02 — Create reusable canvas UI primitives

Build reusable components for toolbar controls, icon buttons, segmented controls, filter chips, canvas panels, tooltips, and empty states.

**Acceptance criteria**

- Components use the project’s neutral visual system and Inter.
- Every icon-only control has a Radix tooltip and accessible label.
- Controls support keyboard focus and disabled/loading states.
- No shadows or gradients are used.

### CV2-03 — Introduce Canvas V2 state model

Create a typed client state model for canvas mode, viewport, selected item, pinned items, active filters, expanded clusters, and visible overlays.

**Acceptance criteria**

- State can be serialized into the URL.
- Refreshing restores the current investigation view.
- Existing graph-selection behavior remains backward-compatible.
- Unit tests cover state transitions.

## Epic: Navigation and comprehension

### CV2-04 — Implement module-first overview

Render repository modules or features as clusters at the initial zoom level rather than rendering all nodes immediately.

**Acceptance criteria**

- Each cluster shows module name, node count, and dependency volume.
- Cross-module edges are aggregated.
- Clicking a cluster focuses it and reveals its immediate neighborhood.
- Initial render remains responsive for the current largest supported graph.

### CV2-05 — Implement progressive disclosure

Reveal files, types, and symbols based on zoom level and explicit expansion.

**Acceptance criteria**

- Overview shows modules only.
- Mid-level view shows files and types inside focused modules.
- Detailed view shows symbols only when useful.
- Users can collapse an expanded module.
- Unrelated detail remains visually muted.

### CV2-06 — Add focus and isolation controls

Allow users to focus a selection, show one- or two-hop neighborhoods, isolate a connected component, and reset the canvas.

**Acceptance criteria**

- Toolbar provides Focus, 1 hop, 2 hops, Isolate, and Reset.
- Each action updates URL state.
- Reset restores the default overview.
- The inspector clearly identifies when the graph is filtered.

### CV2-07 — Improve graph search into a command palette

Upgrade search from a result list into a command-oriented navigation entry point.

**Acceptance criteria**

- Search finds modules, files, types, and symbols.
- Results show type, path, and relevant context.
- Keyboard navigation works with Enter, Escape, and Arrow keys.
- Commands include Focus, Trace path, Show dependents, and Reset view.
- Selecting a result focuses the item and opens its inspector.

## Epic: Architecture investigation workflows

### CV2-08 — Build Explore mode

Make Explore the default interaction mode for browsing ownership and dependencies.

**Acceptance criteria**

- Hover previews node metadata and relationship counts.
- Selection highlights direct inbound and outbound dependencies.
- Inspector shows source, evidence, decisions, and dependency summary.
- Users can pin up to two nodes for comparison.

### CV2-09 — Build Trace mode

Allow users to select a source and target, then investigate dependency paths between them.

**Acceptance criteria**

- Users can set source and target from the canvas, search, or inspector.
- Canvas highlights candidate paths and dims unrelated nodes.
- Inspector lists path length and intermediary nodes.
- An empty result explains that no path was found.
- URL supports sharing a trace view.

### CV2-10 — Build Impact mode

Allow users to understand downstream dependency risk for a node or module.

**Acceptance criteria**

- Users select a target and traversal depth.
- Canvas groups affected items by module.
- Inspector presents direct dependents, total affected nodes, and truncation status.
- High- and low-confidence relationships are distinguishable without relying only on color.
- Results can be narrowed by node type or module.

## Epic: Filtering and architecture signals

### CV2-11 — Implement filter drawer

Provide a compact, non-modal filter interface.

**Acceptance criteria**

- Filter by node kind, provider, confidence, module, and external/internal references.
- Active filters are visible as removable chips.
- Filter state persists in the URL.
- Result count updates immediately.
- Clear all resets filters without resetting mode or viewport.

### CV2-12 — Add semantic overlays

Expose architecture signals as opt-in layers rather than permanent visual noise.

Initial overlays:

- Cycles
- Boundary violations
- High fan-in / fan-out
- Changed areas
- Stale summaries
- Duplicate candidates

**Acceptance criteria**

- Each overlay has a tooltip explaining what it means and how it is calculated.
- Multiple overlays can be enabled safely.
- Overlay state is URL-shareable.
- Overlay data fails gracefully when an optional capability is unavailable.

### CV2-13 — Add “why is this highlighted?” explanations

Every non-default visual state needs an explicit explanation.

**Acceptance criteria**

- Inspector explains active highlight reasons.
- Filtered and hidden counts are visible.
- Low-confidence edges are described in text.
- Warning overlays link to their source evidence or advisory.

## Epic: UX quality and accessibility

### CV2-14 — Add minimap, breadcrumbs, and orientation cues

Give users persistent context when navigating a large graph.

**Acceptance criteria**

- Minimap displays viewport and supports click-to-navigate.
- Breadcrumb shows repository, module, and focused scope.
- Current mode, active filters, and focused item are always visible.
- All cues remain legible at standard zoom and high-DPI displays.

### CV2-15 — Full keyboard and screen-reader support

Ensure Canvas V2 is useful without a mouse or visual-only signals.

**Acceptance criteria**

- Keyboard controls cover pan, zoom, select, focus, mode changes, and reset.
- Canvas has an accessible summary of visible state.
- Selected items have an equivalent semantic inspector representation.
- Tooltips are reachable by keyboard.
- Accessibility tests cover keyboard flows and non-color indicators.

### CV2-16 — Performance and rendering hardening

Make Canvas V2 reliable on large repositories.

**Acceptance criteria**

- Define supported node and edge thresholds and render budgets.
- Module aggregation is used beyond the threshold.
- Pan and zoom interactions remain smooth under supported load.
- Rendering work is scheduled to avoid blocking primary interactions.
- Add performance fixtures and regression tests.

## Release tasks

### CV2-17 — Instrument usage and failure events

Track whether Canvas V2 helps people reach useful outcomes.

Track:

- Search-to-selection success
- Focus, Trace, and Impact usage
- Filter usage
- Time from opening canvas to first meaningful selection
- Empty Trace and Impact results
- Rendering and API failures

### CV2-18 — Feature flag and staged rollout

Ship V2 safely alongside the current canvas.

**Acceptance criteria**

- Canvas V2 is behind a feature flag.
- Users can switch back during rollout.
- Existing deep links continue to work.
- Rollout includes internal testing, telemetry review, and a documented rollback path.

## Suggested delivery order

1. CV2-01 through CV2-04
2. CV2-06, CV2-07, and CV2-08
3. CV2-09 and CV2-10
4. CV2-11 through CV2-15
5. CV2-16 through CV2-18
