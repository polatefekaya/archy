import { afterEach, describe, expect, it, vi } from 'vitest';
import { getGraph, getGraphExplorer, getGraphMap, runQuery } from './api';

const originalFetch = globalThis.fetch;

afterEach(() => { globalThis.fetch = originalFetch; });

describe('Archy API client', () => {
  it('uses a bounded revision-aware graph request', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ items: [], revision: 7 }), { status: 200 }));
    globalThis.fetch = fetch;

    const result = await getGraph('nodes', 7);

    expect(result.value?.revision).toBe(7);
    expect(fetch).toHaveBeenCalledWith('/api/v1/graph/nodes?limit=200&revision=7', expect.objectContaining({ headers: { Accept: 'application/json' } }));
  });

  it('encodes deterministic query input and declared revision', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ edges: [], revision: 4, direction: 'dependents', isTruncated: false }), { status: 200 }));
    globalThis.fetch = fetch;

    await runQuery('what breaks if I delete type:Orders?', 4);

    expect(fetch).toHaveBeenCalledWith('/api/v1/query?text=what+breaks+if+I+delete+type%3AOrders%3F&revision=4', expect.any(Object));
  });

  it('loads a connected graph explorer around an optional selected node', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ nodes: [], edges: [], revision: 7 }), { status: 200 }));
    globalThis.fetch = fetch;

    await getGraphExplorer('csharp:type:src/Features/Orders.cs:Orders');

    expect(fetch).toHaveBeenCalledWith('/api/v1/graph/explorer?maxNodes=72&focus=csharp%3Atype%3Asrc%2FFeatures%2FOrders.cs%3AOrders', expect.any(Object));
  });

  it('loads the lean full-map projection instead of paged graph facts', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ nodes: [], edges: [], revision: 7 }), { status: 200 }));
    globalThis.fetch = fetch;

    await getGraphMap();

    expect(fetch).toHaveBeenCalledWith('/api/v1/graph/map', expect.any(Object));
  });

  it('returns the server problem message instead of pretending an unsupported query succeeded', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ message: 'Use a documented query form.' }), { status: 400 }));

    const result = await runQuery('tell me everything');

    expect(result.value).toBeUndefined();
    expect(result.error).toBe('Use a documented query form.');
  });
});
