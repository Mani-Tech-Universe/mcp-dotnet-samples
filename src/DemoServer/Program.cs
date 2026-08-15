// MCP server on the C# SDK 2.2.0 (ModelContextProtocol.AspNetCore).
//
// Written against 2.x on purpose. Every tutorial currently online targets 1.x, and 2.0
// shipped on 28 July 2026 with real breaking changes — the ones this file demonstrates are
// flagged inline as [2.x] so they can be pulled straight into the video.

using System.ComponentModel;
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);

// Logs go to stderr so they can never corrupt a stdio transport's JSON-RPC stream on stdout.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "demo-server", Version = "2.0.0" };
    })
    // [2.x] Stateless is now the DEFAULT for HTTP. In 1.x the server held a session per
    // client. Stateless servers cannot push unsolicited server-to-client requests, which is
    // the single most common thing to break on upgrade. Set Stateless = false to get the old
    // behaviour back.
    .WithHttpTransport(o => o.Stateless = true)
    .WithToolsFromAssembly();

var app = builder.Build();

// One call mounts the whole protocol surface.
app.MapMcp();

app.Run();


[McpServerToolType]
public static class DemoTools
{
    // A plain string result — unchanged between 1.x and 2.x.
    [McpServerTool, Description("Echo a message back to the caller.")]
    public static string Echo(
        [Description("The message to echo.")] string message)
        => $"echo: {message}";

    // [2.x] STRUCTURED RESULTS — THE HEADLINE CHANGE, AND IT IS SUBTLER THAN THE RELEASE
    // NOTES SUGGEST.
    //
    // First: structured content is OPT-IN. Without UseStructuredContent the SDK emits only a
    // text content block and no outputSchema at all — verified against a live 2.2.0 server.
    //
    // Second: with it enabled, the shape depends on the NEGOTIATED PROTOCOL VERSION, not on
    // the SDK version. SEP-2106 (protocol revision 2026-07-28) widened outputSchema to any
    // JSON Schema 2020-12 document, so a non-object return travels in its natural shape:
    //     structuredContent: 34
    // A client negotiating anything older still receives the legacy envelope:
    //     structuredContent: { "result": 34 }
    // The server applies that automatically via TransformOutputSchemaForLegacyWire, so this
    // is a negotiated difference rather than a hard break.
    [McpServerTool(UseStructuredContent = true),
     Description("Return a temperature as a bare number, to show the SEP-2106 structured-content shape.")]
    public static int TemperatureFor(
        [Description("City name.")] string city)
        => city.ToLowerInvariant() switch
        {
            "chennai" => 34,
            "bengaluru" => 27,
            "london" => 18,
            _ => 25,
        };

    // THE CONTROL CASE. An object result is already a JSON object, so SEP-2106 has nothing to
    // unwrap: its structuredContent is identical on 2025-06-18 and on 2026-07-28. Pairing this
    // with TemperatureFor is what proves the change is specifically about NON-OBJECT results,
    // not about structured content in general.
    //
    // ⚠️ UseStructuredContent IS REQUIRED HERE TOO. This tool originally shipped without it,
    // which meant the "control case" emitted no structuredContent at all and so demonstrated
    // nothing — the opt-in is per tool, and forgetting it fails silently.
    [McpServerTool(UseStructuredContent = true),
     Description("Return a small object, whose structured shape is identical in 1.x and 2.x.")]
    public static WeatherReport WeatherFor(
        [Description("City name.")] string city)
        => new(city, TemperatureFor(city), "Clear");
}

public record WeatherReport(string City, int Celsius, string Conditions);
