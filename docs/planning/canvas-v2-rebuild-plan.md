# Canvas V2 Rebuild Plan

## Purpose

Replace the current graph-canvas implementation with a stable, readable architecture investigation workspace. This is a replacement effort, not a visual cleanup of the existing renderer.

## Non-negotiable outcomes

- The first view is readable at repository scale.
- Filtering, selection, layout, minimap, and rendered edges always agree.
- Explore, Trace, and Impact have explicit state transitions and failure states.
- No visual effect substitutes for information: no gradients, glows, or color-only meaning.
- Every visible architecture signal has a deterministic source and explanation.
- Large maps remain responsive through aggregation and bounded rendering.

## Architecture principles

### 1. Pure scene model

All graph decisions happen before rendering. Create pure, independently tested functions for:

- Module grouping and module metrics
- Filter predicates and active-filter summary
- Progressive disclosure level
- Visible nodes and edges
- Explore neighborhood, Trace path, and Impact traversal
- Selection, pinning, and scene annotations
- Render limits and truncation notices

The canvas renderer receives a complete `CanvasScene` and does not decide what should be visible.

### 2. One authoritative UI state

Use one typed Canvas V2 state model for mode, selection, filters, viewport, disclosure level, pinned items, and URL serialization. Avoid event buses, duplicated local state, or independent controls that can disagree.

### 3. Module-first navigation

The default scene renders modules only. A module card includes:

- Module name
- Indexed node count
- Cross-module dependency volume
- Verified architecture signal count, when available

Files, types, and symbols are revealed only through a focused module, explicit expansion, or a sufficiently detailed zoom level.

### 4. Deterministic visibility

Filters apply before layout. The same visible-node set drives:

- Module cards
- Graph edges
- Canvas hit targets
- Minimap
- Counts and empty states
- Inspector context

No hidden element may remain interactive or be shown in the minimap.

### 5. Intentional investigation workflows

| Mode | Input | Output |
| --- | --- | --- |
| Explore | Select a module or item | Direct relationships and evidence |
| Trace | Source and destination | Directed path or explicit no-path state |
| Impact | Selected item and depth | Downstream dependents grouped by module |

Modes must handle unavailable, truncated, and empty data without inventing a result.

## Delivery phases

### Phase 0 — Audit and acceptance fixtures

- Capture representative small, medium, and large graph fixtures.
- Document expected first view, filter behavior, path behavior, and empty states.
- Define rendering budgets for nodes, edges, and input latency.
- Freeze the existing canvas as a rollback reference.

**Exit criteria:** acceptance fixtures and observable behavior are agreed before renderer work begins.

### Phase 1 — Scene and state foundation

- Define `CanvasViewState`, `CanvasScene`, `ModuleScene`, and `SceneAnnotation` types.
- Implement pure grouping, filtering, traversal, and layout functions.
- Add unit tests for every state transition and filter combination.
- Implement safe URL parsing and serialization.

**Exit criteria:** a fixture can generate the complete scene without React, browser APIs, or canvas APIs.

### Phase 2 — Minimal readable renderer

- Replace the existing graph drawing code with a module-only renderer.
- Implement stable viewport, pan, zoom, fit, and minimap controls.
- Render readable module cards and aggregated cross-module dependencies.
- Add empty and loading states.

**Exit criteria:** the default view is readable and interactive for the large fixture without detail-node clutter.

### Phase 3 — Progressive disclosure and selection

- Focus and expand modules.
- Reveal file/type/symbol detail only in a focused scope.
- Make selection, hover, pinning, and inspector state consistent.
- Implement keyboard navigation and equivalent semantic inspector content.

**Exit criteria:** every rendered item can be selected, inspected, focused, and cleared predictably.

### Phase 4 — Investigation modes and filters

- Implement Explore, Trace, and Impact from the pure scene model.
- Implement module, provider, type, confidence, and reference filters.
- Show active filter chips, counts, and no-result states.
- Add command palette and search integration only through the shared state model.

**Exit criteria:** all workflows return correct scenes for fixtures, including no-path and truncated cases.

### Phase 5 — Signals, hardening, and rollout

- Add only deterministic or server-backed architecture signals.
- Add clear “why highlighted” explanations.
- Add performance regression tests and accessibility interaction tests.
- Introduce a feature flag and rollback path.
- Complete visual QA on supported viewport sizes and graph fixtures.

**Exit criteria:** Canvas V2 meets rendering budgets, passes keyboard and screen-reader tests, and has a validated rollback path.

## UX rules

- No more than one primary action emphasis at a time.
- All icon-only controls require a visible-on-focus Radix tooltip and accessible name.
- Selection uses a visible outline plus text/context, never color alone.
- Warnings appear only for verified facts or explicit bounded-data limitations.
- The default canvas remains calm: signals and detailed labels are opt-in.
- Every asynchronous operation has loading, error, retry, and empty states where applicable.

## Verification matrix

| Area | Evidence required |
| --- | --- |
| Scene correctness | Unit tests against graph fixtures |
| Filter correctness | Unit tests that compare scene, minimap, counts, and hit targets |
| Trace and Impact | Directed traversal tests, no-result tests, truncation tests |
| Interaction | Keyboard and pointer integration tests |
| Accessibility | Semantic alternative, focus order, tooltips, non-color cues |
| Performance | Fixture-based render and interaction budgets |
| Visual quality | Reviewed screenshots for small, medium, and large fixtures |
| Release safety | Feature flag, rollback verification, backward-compatible deep links |

## Explicit anti-goals

- Do not add new controls to the current renderer as a substitute for this rebuild.
- Do not calculate server-owned signals from incomplete client projections.
- Do not render all graph nodes by default.
- Do not maintain duplicated state between search, inspector, toolbar, and canvas.
