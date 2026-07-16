using System.Text.Json;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.Features.Graph.ReadGraphPage;
using Archy.Features.Graph.TraverseDependencies;
using Archy.Features.Graph.ExploreGraph;
using Archy.Features.Graph.RenderGraphMap;
using Archy.SharedKernel.Primitives;
using Microsoft.AspNetCore.Http;

namespace Archy.Features.Web.GraphApi;

/// <summary>Writes the public graph API schema without reflection-based serialization.</summary>
public static class GraphApiJsonWriter
{
    public static GraphApiResult Problem(int statusCode, string code, string message) => Bytes(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("schema", "archy.problem/v1");
        writer.WriteString("code", code);
        writer.WriteString("message", message);
        writer.WriteEndObject();
    }, statusCode);

    public static GraphApiResult Problem(Problem problem) => Problem(ToStatusCode(problem), problem.Code, problem.Message);

    public static GraphApiResult Metadata(GraphRevisionMetadata metadata) => Bytes(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("schema", "archy.graph-revision/v1");
        writer.WriteNumber("revision", metadata.Revision);
        writer.WriteNumber("nodeCount", metadata.NodeCount);
        writer.WriteNumber("edgeCount", metadata.EdgeCount);
        writer.WriteEndObject();
    });

    public static GraphApiResult Page(GraphRevisionPage page) => Bytes(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("schema", "archy.graph-page/v1");
        writer.WriteString("kind", page.FactKind == GraphRevisionFactKind.Nodes ? "nodes" : "edges");
        writer.WriteNumber("revision", page.Revision);
        writer.WriteNumber("offset", page.Offset);
        writer.WriteNumber("limit", page.Limit);
        writer.WriteNumber("totalCount", page.TotalCount);
        writer.WriteStartArray("items");
        if (page.FactKind == GraphRevisionFactKind.Nodes)
        {
            foreach (var node in page.Nodes) WriteNode(writer, node);
        }
        else
        {
            foreach (var edge in page.Edges) WriteEdge(writer, edge);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    });

    public static GraphApiResult Traversal(GraphTraversal traversal) => Bytes(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("schema", "archy.graph-traversal/v1");
        writer.WriteNumber("revision", traversal.Revision);
        writer.WriteString("startStableId", traversal.StartStableId);
        writer.WriteString("direction", traversal.Direction == GraphTraversalDirection.Dependencies ? "dependencies" : "dependents");
        writer.WriteBoolean("isTruncated", traversal.IsTruncated);
        writer.WriteStartArray("edges");
        foreach (var edge in traversal.Edges)
        {
            writer.WriteStartObject();
            writer.WriteNumber("depth", edge.Depth);
            writer.WriteString("edgeId", edge.EdgeId);
            writer.WriteString("sourceStableId", edge.SourceStableId);
            writer.WriteString("targetStableId", edge.TargetStableId);
            writer.WriteString("edgeKind", edge.EdgeKind);
            WriteOptionalString(writer, "normalizedJoinKey", edge.NormalizedJoinKey);
            writer.WriteString("provider", edge.Provider);
            writer.WriteNumber("confidence", edge.Confidence);
            writer.WriteString("evidence", edge.EvidenceJson);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    });

    public static GraphApiResult Explorer(GraphExplorerSnapshot snapshot) => Bytes(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("schema", "archy.graph-explorer/v1");
        writer.WriteNumber("revision", snapshot.Revision);
        writer.WriteNumber("totalNodeCount", snapshot.TotalNodeCount);
        writer.WriteNumber("totalEdgeCount", snapshot.TotalEdgeCount);
        writer.WriteString("centerStableId", snapshot.CenterStableId);
        writer.WriteBoolean("isFocused", snapshot.IsFocused);
        writer.WriteStartArray("nodes");
        foreach (var node in snapshot.Nodes) WriteNode(writer, node);
        writer.WriteEndArray();
        writer.WriteStartArray("edges");
        foreach (var edge in snapshot.Edges) WriteEdge(writer, edge);
        writer.WriteEndArray();
        writer.WriteEndObject();
    });

    public static GraphApiResult Search(IReadOnlyList<GraphNodeFact> nodes) => Bytes(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("schema", "archy.graph-search/v1");
        writer.WriteStartArray("items");
        foreach (var node in nodes) WriteNode(writer, node);
        writer.WriteEndArray();
        writer.WriteEndObject();
    });

    public static GraphApiResult Map(GraphMapSnapshot snapshot) => Bytes(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("schema", "archy.graph-map/v1");
        writer.WriteNumber("revision", snapshot.Revision);
        writer.WriteNumber("totalNodeCount", snapshot.TotalNodeCount);
        writer.WriteNumber("totalEdgeCount", snapshot.TotalEdgeCount);
        writer.WriteBoolean("isTruncated", snapshot.IsTruncated);
        writer.WriteStartArray("nodes");
        foreach (var node in snapshot.Nodes)
        {
            writer.WriteStartObject();
            writer.WriteString("stableId", node.StableId);
            writer.WriteString("nodeKind", node.NodeKind);
            writer.WriteString("canonicalKey", node.CanonicalKey);
            writer.WriteString("displayName", node.DisplayName);
            WriteOptionalString(writer, "filePath", node.FilePath);
            writer.WriteString("provider", node.Provider);
            writer.WriteNumber("confidence", node.Confidence);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteStartArray("edges");
        foreach (var edge in snapshot.Edges)
        {
            writer.WriteStartObject();
            writer.WriteString("edgeId", edge.EdgeId);
            writer.WriteString("sourceStableId", edge.SourceStableId);
            writer.WriteString("targetStableId", edge.TargetStableId);
            writer.WriteString("edgeKind", edge.EdgeKind);
            writer.WriteNumber("confidence", edge.Confidence);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    });

    private static GraphApiResult Bytes(Action<Utf8JsonWriter> write, int statusCode = StatusCodes.Status200OK)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            write(writer);
        }

        return new GraphApiResult(stream.ToArray(), statusCode);
    }

    private static void WriteNode(Utf8JsonWriter writer, GraphNodeFact node)
    {
        writer.WriteStartObject();
        writer.WriteString("stableId", node.StableId);
        writer.WriteString("nodeKind", node.NodeKind);
        writer.WriteString("canonicalKey", node.CanonicalKey);
        writer.WriteString("displayName", node.DisplayName);
        WriteOptionalString(writer, "filePath", node.FilePath);
        WriteOptionalNumber(writer, "startLine", node.StartLine);
        WriteOptionalNumber(writer, "endLine", node.EndLine);
        writer.WriteString("provider", node.Provider);
        writer.WriteNumber("confidence", node.Confidence);
        writer.WriteString("evidence", node.EvidenceJson);
        writer.WriteString("contentHash", node.ContentHash);
        writer.WriteEndObject();
    }

    private static void WriteEdge(Utf8JsonWriter writer, GraphEdgeFact edge)
    {
        writer.WriteStartObject();
        writer.WriteString("edgeId", edge.EdgeId);
        writer.WriteString("sourceStableId", edge.SourceStableId);
        writer.WriteString("targetStableId", edge.TargetStableId);
        writer.WriteString("edgeKind", edge.EdgeKind);
        WriteOptionalString(writer, "normalizedJoinKey", edge.NormalizedJoinKey);
        writer.WriteString("provider", edge.Provider);
        writer.WriteNumber("confidence", edge.Confidence);
        writer.WriteString("evidence", edge.EvidenceJson);
        writer.WriteEndObject();
    }

    private static void WriteOptionalString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null) writer.WriteNull(name); else writer.WriteString(name, value);
    }

    private static void WriteOptionalNumber(Utf8JsonWriter writer, string name, int? value)
    {
        if (value is null) writer.WriteNull(name); else writer.WriteNumber(name, value.Value);
    }

    private static int ToStatusCode(Problem problem) => problem.Code switch
    {
        "validation" => StatusCodes.Status400BadRequest,
        "not_found" => StatusCodes.Status404NotFound,
        "conflict" => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status503ServiceUnavailable,
    };
}
