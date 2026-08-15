// MCP client on the C# SDK 2.2.0. Connects to the DemoServer over Streamable HTTP, lists
// tools, and calls one — the same flow the curl walkthrough shows, but from C#.
//
// ⚠️ THE RENAME THAT DID NOT HAPPEN. The natural assumption is that 1.x used
// McpClientFactory + SseClientTransport and that 2.x renamed them. Diffing the shipped XML
// docs shows McpClient and HttpClientTransport ALREADY EXIST in 1.4.0. Client-side code
// barely changes; the break is on the WIRE, not in these type names.
//
// What actually changed (from ModelContextProtocol.Protocol.McpProtocolVersions):
//   InitializeHandshakeProtocolVersions  — old: negotiate once via `initialize`, keep a session
//   PerRequestMetadataProtocolVersions   — new: every request carries its own protocol version,
//                                          client info and capabilities in _meta
// That is the whole of 2.0 in one sentence: MCP moved from a session handshake to per-request
// metadata. Everything else in the release follows from it.

using ModelContextProtocol.Client;

var endpoint = new Uri(args.Length > 0 ? args[0] : "http://localhost:5223/");

Console.WriteLine($"connecting to {endpoint}");

var transport = new HttpClientTransport(new HttpClientTransportOptions
{
    Endpoint = endpoint,
    Name = "demo-client",
});

await using var client = await McpClient.CreateAsync(transport);

Console.WriteLine($"connected. server: {client.ServerInfo?.Name} {client.ServerInfo?.Version}");
Console.WriteLine($"negotiated protocol: {client.NegotiatedProtocolVersion ?? "(none reported)"}");
Console.WriteLine();

Console.WriteLine("--- tools/list ---");
var tools = await client.ListToolsAsync();
foreach (var t in tools)
{
    Console.WriteLine($"  {t.Name,-18} {t.Description}");
}
Console.WriteLine();

Console.WriteLine("--- tools/call temperature_for(city: Chennai) ---");
var result = await client.CallToolAsync(
    "temperature_for",
    new Dictionary<string, object?> { ["city"] = "Chennai" });

// The SDK surfaces both the text block and the structured payload. Which SHAPE the
// structured payload arrives in depends on the negotiated protocol version — that is the
// whole point of the video, and it is visible right here.
foreach (var block in result.Content)
{
    if (block is ModelContextProtocol.Protocol.TextContentBlock text)
        Console.WriteLine($"  content : {text.Text}");
}

// StructuredContent is a JsonElement? in 2.x (not a JsonNode), so it is GetRawText() here.
Console.WriteLine($"  structured: {result.StructuredContent?.GetRawText() ?? "(none)"}");
Console.WriteLine();

// THE CONTROL CASE. weather_for returns an object, so SEP-2106 has nothing to unwrap and its
// structured payload is identical on both protocol revisions. Printing the two side by side
// is what shows the change is about NON-OBJECT results specifically.
Console.WriteLine("--- tools/call weather_for(city: Chennai) ---");
var weather = await client.CallToolAsync(
    "weather_for",
    new Dictionary<string, object?> { ["city"] = "Chennai" });
Console.WriteLine($"  structured: {weather.StructuredContent?.GetRawText() ?? "(none)"}");
Console.WriteLine();

Console.WriteLine("scalar tool -> {\"result\":34} before SEP-2106, bare 34 on 2026-07-28+");
Console.WriteLine("object tool -> identical on both. Only non-object results change shape.");
