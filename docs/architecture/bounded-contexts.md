# Bounded-context dependency rules

Feature code lives under `Features/<context>/<use-case>`. Contexts are Workspaces, Storage, Graph, Analysis, Rules, Memory, Duplicates, Placement, Sessions, Integrations, and Visualization.

Dependencies flow toward stable contracts: adapters/CLI → use-case handler → context policy/contracts → Storage contracts. A context may consume another context's public request/result or contract, never its feature-local SQL, policy, or private implementation. `SharedKernel` contains only stable primitives and cross-cutting contracts; it is not a services or utilities bucket.

The current implementation is permitted to have Workspaces depend on Storage's database-initialization contract. Storage may depend on the Workspace state-location and lock contracts. No future Graph/Rules/Memory code may reach into `InitializeWorkspace` internals.
