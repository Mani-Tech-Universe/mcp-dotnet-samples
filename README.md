# MCP C# SDK 2.x — what changed, with runnable code

Companion repo for the video **"The MCP C# SDK just went 2.0 — here's everything that broke"**.

The C# SDK for the Model Context Protocol went **2.0.0 on 28 July 2026** and **2.2.0 on
13 August 2026**. Almost every tutorial online still targets 1.x. This repo is a minimal
server and client on **2.2.0**, plus [`FINDINGS.md`](FINDINGS.md) — every claim in the video
verified against a live server rather than paraphrased from release notes.

## The one-sentence version

**MCP moved from a session handshake to per-request metadata.**

The SDK says so itself, in `ModelContextProtocol.Protocol.McpProtocolVersions`:

| | |
|---|---|
| `InitializeHandshakeProtocolVersions` | negotiate once via `initialize`, keep a session |
| `PerRequestMetadataProtocolVersions` | every request carries its own protocol version, client info and capabilities in `_meta` |

Stateless-by-default, the new required headers, and the namespaced `_meta` keys are all
consequences of that one decision.

## The demo

Same server. Same tool. Same compiled code. Only the negotiated protocol version differs:

```
protocol 2025-06-18  ->  "structuredContent": {"result": 34}
protocol 2026-07-28  ->  "structuredContent": 34
```

## Run it

```bash
dotnet run --project src/DemoServer --urls http://localhost:5223
```

Then, in another terminal:

```bash
dotnet run --project src/DemoClient -- http://localhost:5223/
```

```
connected. server: demo-server 2.0.0
negotiated protocol: 2026-07-28
  content : 34
  structured: 34
```

The client negotiates the new protocol and handles every header and `_meta` key for you.
To see what it is doing on your behalf, call the server with curl instead:

```bash
# legacy wire shape — no version negotiated
curl -s -X POST http://localhost:5223/ \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"temperature_for","arguments":{"city":"Chennai"}}}'
# -> "structuredContent":{"result":34}

# natural shape — 2026-07-28, and everything it now demands
curl -s -X POST http://localhost:5223/ \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -H "MCP-Protocol-Version: 2026-07-28" \
  -H "Mcp-Method: tools/call" \
  -H "Mcp-Name: temperature_for" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{
        "name":"temperature_for","arguments":{"city":"Chennai"},
        "_meta":{
          "io.modelcontextprotocol/protocolVersion":"2026-07-28",
          "io.modelcontextprotocol/clientCapabilities":{},
          "io.modelcontextprotocol/clientInfo":{"name":"curl-demo","version":"1.0.0"}
        }}}'
# -> "structuredContent":34
```

## Three things the release notes do not tell you

1. **Structured content is opt-in.** By default a 2.2.0 server emits no `structuredContent`
   and no `outputSchema` at all. You need `[McpServerTool(UseStructuredContent = true)]`.
2. **The `_meta` keys are namespaced** — `io.modelcontextprotocol/protocolVersion`, not
   `protocolVersion`. A plain key is silently ignored.
3. **`Mcp-Method` and `Mcp-Name` headers are required** on Streamable HTTP POSTs under the
   new protocol. Omitting them returns `-32020`.

Full detail, including the four errors you hit on the way there, is in
[`FINDINGS.md`](FINDINGS.md).

## Requirements

- .NET 10 SDK (built on 10.0.301)
- `ModelContextProtocol.AspNetCore` 2.2.0 / `ModelContextProtocol` 2.2.0

## Licence

MIT — see [LICENSE](LICENSE).
