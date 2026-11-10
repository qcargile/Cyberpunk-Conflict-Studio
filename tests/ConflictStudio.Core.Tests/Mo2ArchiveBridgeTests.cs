using ConflictStudio.Mo2Bridge;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class Mo2ArchiveBridgeTests
{
    private static readonly string[] ProposedOrder = ["Alpha.archive", "Beta.archive"];
    private static readonly string[] Selection = ["Alpha.archive"];
    [TestMethod]
    public async Task InvalidRequestsReturnErrorsAndLeaveTheProcessAvailable()
    {
        await using BridgeFixture bridge = new();
        await bridge.SendAsync("{malformed}").ConfigureAwait(false);
        using JsonDocument malformed = await bridge.NextAsync().ConfigureAwait(false);
        Assert.AreEqual("error", malformed.RootElement.GetProperty("type").GetString());
        await bridge.SendAsync("{\"id\":\"unknown\",\"operation\":\"unknown\"}").ConfigureAwait(false);
        using JsonDocument unknown = await bridge.NextAsync().ConfigureAwait(false);
        Assert.AreEqual("unknown", unknown.RootElement.GetProperty("id").GetString());
        Assert.AreEqual("error", unknown.RootElement.GetProperty("type").GetString());
        await bridge.SendAsync("{\"id\":\"cancel\",\"operation\":\"cancel\"}").ConfigureAwait(false);
        using JsonDocument cancelled = await bridge.NextAsync().ConfigureAwait(false);
        Assert.IsTrue(cancelled.RootElement.GetProperty("data").GetProperty("cancelled").GetBoolean());
    }

    [TestMethod]
    public async Task JsonProtocolScansSelectsPreviewsSavesAndUndoesTheRealProfileFile()
    {
        using ArchiveWorkspaceFixture fixture = new();
        await using BridgeFixture bridge = new(fixture.Workspace());
        await bridge.SendAsync(JsonSerializer.Serialize(new { id = "scan", operation = "scan", instanceRoot = fixture.Root, profileModlistPath = fixture.Profile })).ConfigureAwait(false);
        using JsonDocument scanned = await bridge.ResultAsync("scan").ConfigureAwait(false);
        string token = scanned.RootElement.GetProperty("data").GetProperty("token").GetString()!;
        Assert.AreEqual(2, scanned.RootElement.GetProperty("data").GetProperty("archiveRows").GetArrayLength());
        await bridge.SendAsync(JsonSerializer.Serialize(new { id = "select", operation = "select", token, selectedArchives = Selection, order = ProposedOrder })).ConfigureAwait(false);
        using JsonDocument selected = await bridge.ResultAsync("select").ConfigureAwait(false);
        Assert.AreEqual("Alpha.archive", selected.RootElement.GetProperty("data").GetProperty("sharedResources")[0].GetProperty("winnerArchive").GetString());
        Assert.AreEqual("42", selected.RootElement.GetProperty("data").GetProperty("sharedResources")[0].GetProperty("resourceHash").GetString());
        await bridge.SendAsync(JsonSerializer.Serialize(new { id = "preview", operation = "preview", token, order = ProposedOrder })).ConfigureAwait(false);
        using JsonDocument preview = await bridge.ResultAsync("preview").ConfigureAwait(false);
        Assert.IsTrue(preview.RootElement.GetProperty("data").GetProperty("canApply").GetBoolean());
        Assert.AreEqual("None", preview.RootElement.GetProperty("data").GetProperty("unknownImpact").GetString());
        await bridge.SendAsync(JsonSerializer.Serialize(new { id = "apply", operation = "apply", token, order = ProposedOrder })).ConfigureAwait(false);
        using JsonDocument saved = await bridge.ResultAsync("apply").ConfigureAwait(false);
        string written = saved.RootElement.GetProperty("data").GetProperty("writeTarget").GetString()!;
        Assert.IsTrue(File.Exists(written));
        token = saved.RootElement.GetProperty("data").GetProperty("token").GetString()!;
        await bridge.SendAsync(JsonSerializer.Serialize(new { id = "undo", operation = "undo", token })).ConfigureAwait(false);
        using JsonDocument undone = await bridge.ResultAsync("undo").ConfigureAwait(false);
        Assert.IsFalse(undone.RootElement.GetProperty("data").GetProperty("canUndo").GetBoolean());
        Assert.IsFalse(File.Exists(written));
    }

    [TestMethod]
    public async Task CancellationTerminatesTheOriginalRequestBeforeTheConnectionIsReused()
    {
        using ArchiveWorkspaceFixture fixture = new();
        await using BridgeFixture bridge = new(fixture.Workspace());
        string scan = JsonSerializer.Serialize(new { id = "old-scan", operation = "scan", instanceRoot = fixture.Root, profileModlistPath = fixture.Profile });
        await bridge.SendAsync(scan + "\n{\"id\":\"cancelled\",\"operation\":\"cancel\"}").ConfigureAwait(false);
        bool oldTerminated = false;
        bool cancelAcknowledged = false;
        while (!oldTerminated || !cancelAcknowledged)
        {
            using JsonDocument message = await bridge.NextAsync().ConfigureAwait(false);
            JsonElement response = message.RootElement;
            if (response.GetProperty("id").GetString() == "cancelled")
            {
                Assert.IsTrue(response.GetProperty("data").GetProperty("cancelled").GetBoolean());
                cancelAcknowledged = true;
            }
            else if (response.GetProperty("type").GetString() != "progress")
            {
                Assert.IsFalse(oldTerminated);
                Assert.AreEqual("old-scan", response.GetProperty("id").GetString());
                Assert.AreEqual("error", response.GetProperty("type").GetString());
                Assert.IsTrue(response.GetProperty("cancelled").GetBoolean());
                oldTerminated = true;
            }
        }
        await bridge.SendAsync(JsonSerializer.Serialize(new { id = "new-scan", operation = "scan", instanceRoot = fixture.Root, profileModlistPath = fixture.OtherProfile })).ConfigureAwait(false);
        using JsonDocument current = await bridge.ResultAsync("new-scan", "old-scan").ConfigureAwait(false);
        Assert.AreEqual(fixture.OtherProfile, current.RootElement.GetProperty("data").GetProperty("profilePath").GetString());
    }

    [TestMethod]
    public async Task AQueuedReplacementScanTerminatesTheSupersededRequestExactlyOnce()
    {
        using ArchiveWorkspaceFixture fixture = new();
        await using BridgeFixture bridge = new(fixture.Workspace());
        string oldScan = JsonSerializer.Serialize(new { id = "old-scan", operation = "scan", instanceRoot = fixture.Root, profileModlistPath = fixture.Profile });
        string newScan = JsonSerializer.Serialize(new { id = "new-scan", operation = "scan", instanceRoot = fixture.Root, profileModlistPath = fixture.OtherProfile });
        await bridge.SendAsync(oldScan + "\n" + newScan).ConfigureAwait(false);
        using JsonDocument cancelled = await bridge.TerminalAsync("old-scan").ConfigureAwait(false);
        Assert.AreEqual("error", cancelled.RootElement.GetProperty("type").GetString());
        Assert.IsTrue(cancelled.RootElement.GetProperty("cancelled").GetBoolean());
        using JsonDocument replacement = await bridge.ResultAsync("new-scan", "old-scan").ConfigureAwait(false);
        Assert.AreEqual(fixture.OtherProfile, replacement.RootElement.GetProperty("data").GetProperty("profilePath").GetString());
    }

    [TestMethod]
    public async Task EndOfInputCancelsAndDrainsAnActiveScan()
    {
        using ArchiveWorkspaceFixture fixture = new();
        BridgeFixture bridge = new(fixture.Workspace());
        try
        {
            await bridge.SendAsync(JsonSerializer.Serialize(new { id = "scan", operation = "scan", instanceRoot = fixture.Root, profileModlistPath = fixture.Profile })).ConfigureAwait(false);
        }
        finally { await bridge.DisposeAsync().ConfigureAwait(false); }
        using JsonDocument terminal = await bridge.TerminalAsync("scan").ConfigureAwait(false);
        Assert.AreEqual("error", terminal.RootElement.GetProperty("type").GetString());
        Assert.IsTrue(terminal.RootElement.GetProperty("cancelled").GetBoolean());
        Assert.IsFalse(Directory.Exists(Path.Combine(Path.GetDirectoryName(fixture.Profile)!, "archive-order")));
    }

    private sealed class BridgeFixture : IAsyncDisposable
    {
        private readonly Pipe _pipe = new();
        private readonly StreamWriter _writer;
        private readonly StreamReader _reader;
        private readonly OutputLines _output = new();
        private readonly Task _run;

        public BridgeFixture(ConflictStudio.Core.Mo2ArchiveWorkspace? workspace = null)
        {
            _writer = new StreamWriter(_pipe.Writer.AsStream(), new UTF8Encoding(false)) { AutoFlush = true };
            _reader = new StreamReader(_pipe.Reader.AsStream());
            BridgeServer server = workspace is null ? new BridgeServer(_reader, _output) : new BridgeServer(_reader, _output, workspace);
            _run = server.RunAsync();
        }

        public Task SendAsync(string request) => _writer.WriteLineAsync(request);

        public async Task<JsonDocument> NextAsync() => JsonDocument.Parse(await _output.Lines.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false));

        public async Task<JsonDocument> ResultAsync(string id, string? forbiddenId = null)
        {
            JsonDocument result = await TerminalAsync(id, forbiddenId).ConfigureAwait(false);
            Assert.IsTrue(result.RootElement.GetProperty("ok").GetBoolean(), result.RootElement.ToString());
            return result;
        }

        public async Task<JsonDocument> TerminalAsync(string id, string? forbiddenId = null)
        {
            while (true)
            {
                JsonDocument document = await NextAsync().ConfigureAwait(false);
                Assert.AreNotEqual(forbiddenId ?? string.Empty, document.RootElement.GetProperty("id").GetString());
                if (document.RootElement.GetProperty("id").GetString() == id && document.RootElement.GetProperty("type").GetString() != "progress")
                {
                    return document;
                }
                document.Dispose();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _writer.DisposeAsync().ConfigureAwait(false);
            await _run.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            _reader.Dispose();
            _output.Dispose();
        }
    }

    private sealed class OutputLines : TextWriter
    {
        public Channel<string> Lines { get; } = Channel.CreateUnbounded<string>();
        public override Encoding Encoding => Encoding.UTF8;
        public override void WriteLine(string? value)
        {
            if (value is not null) Lines.Writer.TryWrite(value);
        }
    }
}

