# Graph identity and revision contract

Every durable identity is scoped by `workspace_id`. Repository identity is fixed by its workspace. A file identity is its normalized repository-relative path plus first-seen incarnation; a rename is a move event and delete/re-add creates a new incarnation. A symbol identity is the language adapter's canonical symbol identity plus containing file incarnation. A virtual resource identity is provider kind plus normalized logical key.

An edge identity is source node, target node, edge kind, normalized join key, and evidence anchor. It is recomputed per revision, never updated in place. An analysis run is a random 128-bit ID; a graph revision is a monotonic workspace-local integer.

1. A revision exists only for a completed successful run.
2. Every node/edge records producing run and revision.
3. Revisions never mutate; current graph is derived from latest successful revision.
4. Facts retain provider, confidence, source range, raw evidence, normalized join key, and resolution state.
5. Failed/cancelled runs create events but no graph revision.
