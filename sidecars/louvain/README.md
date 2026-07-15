# Archy Louvain sidecar

`cluster_graph` accepts a weighted undirected graph over the shared NDJSON sidecar protocol. Nodes and edges are sorted before graph construction; Louvain uses `randomWalk: false` and a fixed RNG. Community IDs are reassigned by each cluster's lexicographically first member, so input order cannot affect output.
