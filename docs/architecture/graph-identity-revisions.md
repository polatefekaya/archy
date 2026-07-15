# Graph identity and revision contract

Every durable identity is scoped by `workspace_id`. Repository identity is fixed by its workspace. A file identity is its normalized repository-relative path plus first-seen incarnation; a rename is a move event and delete/re-add creates a new incarnation. A symbol identity is the language adapter's canonical symbol identity plus containing file incarnation. A virtual resource identity is provider kind plus normalized logical key.

An edge identity is source node, target node, edge kind, normalized join key, and evidence anchor. It is recomputed per revision, never updated in place. An analysis run is a random 128-bit ID; a graph revision is a monotonic workspace-local integer.

`graph_nodes` and `graph_edges` remain immutable per-revision snapshots for replay. `graph_node_identities`/`graph_edge_identities` hold stable workspace-scoped identities, while `graph_node_versions`/`graph_edge_versions` hold the fact values and inclusive validity ranges. A changed fact closes its prior version at `next_revision - 1`; an omitted active fact is closed the same way. A fact unchanged in the next complete snapshot keeps its open version. New node facts require a source content hash. Historical snapshots created before content hashes existed are migrated with an explicit `legacy:<revision>:<stable-id>` sentinel, never a fabricated source hash.

Symbols are durable identities linked to a graph node. `symbol_versions` records fully qualified identity, visibility, normalized signature, parameter/return metadata, and signature hash; `interface_fingerprint_versions` records a named normalized-members fingerprint for the same validity interval. Both are written in the graph revision transaction and therefore share its producing run and atomic visibility guarantees.

1. A revision exists only for a completed successful run.
2. Every node/edge records producing run and revision.
3. Revisions never mutate; the current graph is selected only by the active-revision pointer.
4. Facts retain provider, confidence, source range, raw evidence, normalized join key, and resolution state.
5. Failed/cancelled runs create events but no graph revision.

## Activation boundary

`workspace_graph_states.active_graph_revision` is the sole default-read pointer. A graph revision is staged inside one SQLite transaction, checked for snapshot-edge integrity, marked as the successful analysis result, then atomically activated by updating that pointer in the same transaction. Readers choose either the prior active revision or the new complete one; historical revision queries are explicit.
