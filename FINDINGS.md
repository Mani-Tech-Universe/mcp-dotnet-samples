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

### The control case: only NON-OBJECT results move

`weather_for` returns a record, and its structured payload is **identical under both
protocols** — an object is already an object, so SEP-2106 has nothing to unwrap:

```json
"structuredContent": {"city":"Chennai","celsius":34,"conditions":"Clear"}
```

Run side by side with `temperature_for`, that is the whole change in one screen:

| Tool | Returns | 2025-06-18 | 2026-07-28 |
|---|---|---|---|
| `temperature_for` | `int` | `{"result":34}` | `34` |
| `weather_for` | record | `{"city":…,"celsius":34,…}` | `{"city":…,"celsius":34,…}` |

⚠️ `weather_for` originally shipped **without** `UseStructuredContent`, so it emitted no
`structuredContent` at all and silently proved nothing. The opt-in is **per tool**. Fixed
2026-08-15 and re-verified live.

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
was a protocol error.

⚠️ **Do NOT just set `Stateless = false` to "get sessions back".** That assigns
`SessionMode.Stateful`, which then *refuses* every 2026-07-28 client with
`-32022 UnsupportedProtocolVersion`. See [§10](#10-stateless--false-is-a-trap--sessionmode-has-three-values)
for the three-value `SessionMode` and the hybrid mode you almost certainly want instead.

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

⚠️ **The rename that did not happen.** The natural assumption — and what several upgrade
posts imply — is that 1.x used `McpClientFactory` + `SseClientTransport` and 2.x renamed them
to `McpClient` + `HttpClientTransport`. The diff of the shipped XML docs says otherwise:
**`McpClient` and `HttpClientTransport` already exist in 1.4.0.** Client-side type names
barely move. The break is on the wire, not in the API names.

**Narration note:** deliver this as a myth-bust — "the rename everyone expects isn't there" —
never as a first-person correction. It is a fact about the SDK, not a confession.

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

---

## 10. `Stateless = false` IS A TRAP — `SessionMode` has THREE values

This is the most consequential thing in this document, and it is easy to get backwards.
`HttpServerTransportOptions.Stateless` is only a **convenience proxy** over the real setting,
`SessionMode`. From the shipped `ModelContextProtocol.AspNetCore.xml`:

| `HttpServerSessionMode` | Old clients (2025-11-25 and earlier) | 2026-07-28 clients |
|---|---|---|
| `Stateless` *(default)* | no session | served per request |
| `Stateful` | full session + `Mcp-Session-Id` | **refused — `-32022 UnsupportedProtocolVersion`** |
| `StatefulForInitializeClients` | full session + `Mcp-Session-Id` | served per request |

> `Stateful` — "Requests using the 2026-07-28 or later protocol revision are refused with a
> `-32022` UnsupportedProtocolVersion error so that a dual-path client downgrades to the
> initialize handshake… Use `StatefulForInitializeClients` to serve those clients natively
> instead of forcing a downgrade."

So the obvious "I need sampling back, I'll set `Stateless = false`" **starts rejecting every
modern client**. For a mixed fleet the answer is `SessionMode = StatefulForInitializeClients`,
which serves both on one endpoint and lets you migrate progressively.

Reading `Stateless` returns `true` only when `SessionMode` is `Stateless`, so
`StatefulForInitializeClients` reads as `false`. Both write the same field — last assignment
wins.

## 11. What stateless actually disables, and the replacement

> "Client sampling, elicitation, and roots capabilities are disabled because the server cannot
> make requests; **use Multi Round-Trip Requests (MRTR) instead.**"

Also gone in stateless mode: `SessionId` is null, `Mcp-Session-Id` is unused, and the GET,
DELETE and `/sse` endpoints are unavailable (405). Tools, resources and prompts are unaffected.

**MRTR** is the replacement mechanism. `InputRequest` is documented as "a server-initiated
request that the client must fulfill as part of an MRTR flow" — carrying `SamplingParams` when
the method is sampling, `ElicitationParams` when it is elicitation. Rather than pushing down a
session, the handler suspends and returns the request inside its own response; the client
resolves it and answers on the next round trip. Same capability, no server-to-client channel,
nothing that must land on the same process twice.

## 12. What replaced the handshake: `server/discover` + caching

Two proposals did the damage: **SEP-2567 removed `Mcp-Session-Id`** and **SEP-2575 removed the
`initialize` handshake**, so "requests using that revision or later can only ever be served
statelessly".

`DiscoverResult` is what took their place:

| Property | What it carries |
|---|---|
| `SupportedVersions` | protocol revisions the server accepts for per-request-metadata requests |
| `Capabilities` | what the server can do |
| `Instructions` | how to use it |
| `TimeToLive` | how long the client may cache this |
| `CacheScope` | `Public` or `Private` |

`CacheScope` is documented as "analogous to the HTTP `Cache-Control: public` and
`Cache-Control: private` directives":

- **`Public`** — no user-specific data; any client, shared gateway or caching proxy may store
  it and serve it to any user.
- **`Private`** — user-specific; only the requesting user's client may cache it, and shared
  caches must not serve it to a different user.

It is not limited to discovery — `ListToolsResult`, `ListResourcesResult`, `ListPromptsResult`,
`ListResourceTemplatesResult` and `ReadResourceResult` all implement `ICacheableResult`.

**That is the whole design in one line: state that used to live in a session on your server now
lives in a cache, with an explicit TTL and an explicit sharing rule.**

## 13. How to check any of this yourself

`probe-mcp-xml.mjs` (in the video's build repo) reads the XML documentation shipped inside the
NuGet package — the only source guaranteed to match the assembly you are referencing:

```bash
node probe-mcp-xml.mjs SessionMode CacheScope DiscoverResult
node probe-mcp-xml.mjs --diff     # public type lists, 1.4.0 vs 2.2.0
```
