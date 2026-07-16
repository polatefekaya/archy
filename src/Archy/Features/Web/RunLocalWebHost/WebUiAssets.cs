namespace Archy.Features.Web.RunLocalWebHost;

internal static class WebUiAssets
{
    public const string IndexHtml = """
        <!doctype html>
        <html lang="en">
        <head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Archy</title></head>
        <body><main><h1>Archy</h1><p>Local architectural-memory host is running.</p><p id="status">Loading status…</p></main>
        <script>fetch('/api/v1/status').then(r=>r.json()).then(x=>document.getElementById('status').textContent=`${x.name} ${x.version} (${x.bindAddress})`).catch(()=>document.getElementById('status').textContent='Status unavailable.');</script>
        </body></html>
        """;
}
