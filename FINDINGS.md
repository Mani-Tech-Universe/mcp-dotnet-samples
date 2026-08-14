# MCP C# SDK 2.x — verified findings

Everything here was run against a **live server on ModelContextProtocol.AspNetCore 2.2.0**,
.NET 10.0.301, on 2026-08-14. Nothing is taken from release notes alone — several of the
notes turned out to be incomplete or misleading, and those are flagged.

## Why this is the topic

| | |
|---|---|
| MCP C# SDK 2.0.0 shipped | **28 July 2026** |
| 2.2.0 shipped | **13 August 2026** (yesterday) |
| 2.0.0 downloads already | ~211,000 |
| Existing YouTube videos | all target **1.x** |
| Web search still reports "latest" as | **1.4.0** — already two majors behind |

A major version with real breaking changes, 17 days old, with every tutorial online
teaching the previous API.

---

## 1. Structured content is OPT-IN (the release notes do not say this)

The notes say non-object results now emit raw values instead of `{"result": 72}`. True —
but only once you opt in. By default a 2.2.0 server emits **no `structuredContent` and no
`outputSchema` at all**:

```json
{"result":{"content":[{"type":"text","text":"34"}]},"id":2,"jsonrpc":"2.0"}
```

You have to ask for it:

```csharp
[McpServerTool(UseStructuredContent = true), Description("...")]
public static int TemperatureFor(string city) => 34;
```

## 2. The shape depends on the NEGOTIATED PROTOCOL VERSION, not the SDK version

This is the part worth the video. Same server, same tool, same compiled code:

**Protocol `2025-06-18` (or no version negotiated) — legacy envelope**
```json
"structuredContent": {"result": 34}
```
```json
"outputSchema": {"type":"object","properties":{"result":{"type":"integer"}},"required":["result"]}
```

**Protocol `2026-07-28` (SEP-2106) — natural shape**
```json
"structuredContent": 34,
"resultType": "complete",
"_meta": {"io.modelcontextprotocol/serverInfo":{"name":"demo-server","version":"2.0.0"}}
```

The SDK decides via `McpSessionHandler.SupportsNaturalOutputSchemas(string)` and rewrites
older wire formats with `AIFunctionMcpServerTool.TransformOutputSchemaForLegacyWire`. So it
is a **negotiated difference, not a hard break** — an old client keeps working against a new
server. That is the single most useful thing to tell an upgrading audience.

## 3. What a 2026-07-28 request must carry (discovered by hitting errors)

Under the new protocol a stateless request must supply everything a session used to hold.
Each of these produced a distinct error until satisfied:

| Missing | Error |
|---|---|
| `_meta/io.modelcontextprotocol/protocolVersion` | `-32602 Requests using protocol version '2026-07-28' must include ...` |
| `Mcp-Method` header | `-32020 Missing required Mcp-Method header.` |
| `Mcp-Name` header | `-32020 Missing required Mcp-Name header.` |
| `_meta/io.modelcontextprotocol/clientCapabilities` | `-32602 ... must include ... as a JSON object.` |

The complete working call:

```bash
curl -s -X POST http://localhost:5223/ \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -H "MCP-Protocol-Version: 2026-07-28" \
  -H "Mcp-Method: tools/call" \
  -H "Mcp-Name: temperature_for" \
  -d '{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{
        "name":"temperature_for","arguments":{"city":"Chennai"},
        "_meta":{
          "io.modelcontextprotocol/protocolVersion":"2026-07-28",
          "io.modelcontextprotocol/clientCapabilities":{},
          "io.modelcontextprotocol/clientInfo":{"name":"curl-demo","version":"1.0.0"}
        }}}'
```

Note the `_meta` keys are **namespaced** — `io.modelcontextprotocol/protocolVersion`, not
`protocolVersion`. A plain key is silently ignored.

## 4. Stateless is the default, and `tools/list` needs no handshake

`HttpServerTransportOptions.Stateless` now defaults to `true`. A consequence you can show on
screen: `tools/list` answers immediately with **no `initialize` call at all**. On 1.x that
was a protocol error. Set `Stateless = false` to restore session behaviour — required if the
server needs to push unsolicited requests to the client.

## 5. Gotchas hit while building (worth 30 seconds each on screen)

- `dotnet run` obeys `Properties/launchSettings.json` and **ignores `ASPNETCORE_URLS`**. Use
  `--urls`, or the server will not be where you think it is.
- Rebuilding while the server runs fails with `MSB3027 ... file is locked by DemoServer`.
- Log to **stderr** (`LogToStandardErrorThreshold`) or console logging corrupts the JSON-RPC
  stream on stdout for stdio transports.

---

## Repo layout

```
src/DemoServer   ASP.NET Core MCP server, ModelContextProtocol.AspNetCore 2.2.0
src/DemoClient   console client, ModelContextProtocol 2.2.0
```

Run: `dotnet run --project src/DemoServer --urls http://localhost:5223`

## Sources

- https://www.nuget.org/packages/ModelContextProtocol (version + dates)
- https://github.com/modelcontextprotocol/csharp-sdk/releases (release notes)
- `ModelContextProtocol.Core.xml` shipped in the NuGet package (the SEP-2106 detail)

---

## 6. The real API diff, 1.4.0 → 2.2.0

Taken by diffing the public type lists in the shipped XML docs, not from the release notes.
282 public types in 1.4.0, 287 in 2.2.0.

**Removed — 25 types, all of them the Tasks API**, extracted to `ModelContextProtocol.Extensions.Tasks`:
`IMcpTaskStore`, `InMemoryMcpTaskStore`, `McpTask`, `McpTaskStatus`, `McpTaskMetadata`,
`ToolExecution`, and every `*McpTasksCapability`. If you used tasks, this is your migration.

**Added — the new protocol surface:**

| Type | What it is |
|---|---|
| `DiscoverRequestParams` / `DiscoverResult` | discovery-first negotiation |
| `McpHttpHeaders` | the new required headers |
| `MetaKeys` | the namespaced `_meta` keys |
| `McpProtocolVersions` | which versions handshake vs carry per-request metadata |
| `MissingRequiredClientCapabilityException` | the error you hit when `_meta` is incomplete |
| `InputRequest` / `InputResponse` / `MrtrContext` | multi-round-trip requests |
| `CacheScope` | SEP-2549 caching hints |
| `AuthorizationResult` / `ScopeSelectorDelegate` | the reworked OAuth surface |

⚠️ **A rename story I got wrong and had to correct.** I assumed 1.x used
`McpClientFactory` + `SseClientTransport` and 2.x renamed them. The diff shows `McpClient`
and `HttpClientTransport` **already exist in 1.4.0**. Client-side type names barely move.
The break is on the wire, not in the API names — do not repeat the rename claim.

## 7. The one-sentence thesis for the video

From `McpProtocolVersions`, the SDK splits every protocol revision into two buckets:

- `InitializeHandshakeProtocolVersions` — negotiate once via `initialize`, keep a session
- `PerRequestMetadataProtocolVersions` — every request carries its own protocol version,
  client info and capabilities in `_meta`

**MCP moved from a session handshake to per-request metadata.** Stateless-by-default, the
required `Mcp-Method` / `Mcp-Name` headers, the namespaced `_meta` keys and the
`MissingRequiredClientCapabilityException` are all consequences of that one decision.

## 8. Verified constants

`MetaKeys`: `ProtocolVersion`, `ClientInfo`, `ServerInfo`, `ClientCapabilities`, `LogLevel`,
`SubscriptionId` — all documented as "Introduced by the 2026-07-28 revision".

`McpHttpHeaders`: `SessionId`, `ProtocolVersion`, `LastEventId`, `Method` ("Required on all
Streamable HTTP POST"), `Name` ("Required for tools/call, resources/read, prompts/get"),
`ParamPrefix` (`Mcp-Param-{Name}`), `ToolContextKey`.

## 9. The C# client hides all of it

`DemoClient` connects, negotiates `2026-07-28` on its own and prints `structured: 34` — no
headers or `_meta` written by hand. The curl walkthrough shows what the SDK is doing for you,
which is the point: **use the SDK and this is a non-event; call MCP from anything else and
you must send all of it yourself.**

```
connected. server: demo-server 2.0.0
negotiated protocol: 2026-07-28
  content : 34
  structured: 34
```
