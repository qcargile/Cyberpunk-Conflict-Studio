using System.Text.Json;
using ConflictStudio.Core;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class VortexOrderWaitTests
{
    [TestMethod]
    public async Task AdvancingProgressRenewsPermissionUntilVerificationCompletes()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-order-progress-" + Guid.NewGuid().ToString("N"));
        VortexOrderBridgeStore store = new(root);
        VortexOrderRequest request = Request();
        Task bridge = Task.Run(async () =>
        {
            Assert.IsTrue(SpinWait.SpinUntil(() => store.ReadRequest() is not null, TimeSpan.FromSeconds(3)));
            DateTimeOffset firstExpiry = LeaseExpiry(root);
            for (int sequence = 1; sequence <= 8; sequence++)
            {
                WriteProgress(root, request.RequestId, sequence);
                await Task.Delay(200);
            }
            Assert.IsTrue(LeaseExpiry(root) > firstExpiry.AddSeconds(1));
            store.WriteResponse(new(2, request.RequestId, true, "Verified", null, new string('a', 64), DateTimeOffset.UtcNow));
        });
        try
        {
            VortexOrderResponse response = store.Exchange(request, TimeSpan.FromMilliseconds(700));
            await bridge;

            Assert.IsTrue(response.Applied);
            Assert.IsNull(store.ReadRequest());
            Assert.IsFalse(File.Exists(Path.Combine(root, "order-lease.json")));
            Assert.IsFalse(File.Exists(Path.Combine(root, "order-progress.json")));
        }
        finally
        {
            await bridge;
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("other request")]
    [DataRow("old schema")]
    [DataRow("stalled")]
    public void MissingOrUnchangedProgressCannotKeepAnExchangeAlive(string scenario)
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-order-stall-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            VortexOrderBridgeStore store = new(root);
            VortexOrderRequest request = Request();
            if (scenario != "missing") WriteProgress(root, scenario == "other request" ? new string('f', 32) : request.RequestId, 1, scenario == "old schema" ? 1 : 2);

            ArchiveOrderException exception = Assert.ThrowsExactly<ArchiveOrderException>(() => store.Exchange(request, TimeSpan.FromMilliseconds(300)));

            StringAssert.Contains(exception.Message, scenario == "stalled" ? "stopped reporting" : "did not acknowledge");
            Assert.IsNull(store.ReadRequest());
            Assert.IsFalse(File.Exists(Path.Combine(root, "order-lease.json")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task CancellationRevokesPermissionWhileTheBridgeReportsProgress()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-order-cancel-" + Guid.NewGuid().ToString("N"));
        VortexOrderBridgeStore store = new(root);
        VortexOrderRequest request = Request();
        using CancellationTokenSource cancellation = new();
        Task bridge = Task.Run(() =>
        {
            Assert.IsTrue(SpinWait.SpinUntil(() => store.ReadRequest() is not null, TimeSpan.FromSeconds(3)));
            WriteProgress(root, request.RequestId, 1);
            cancellation.Cancel();
        });
        try
        {
            Assert.ThrowsExactly<OperationCanceledException>(() => store.Exchange(request, TimeSpan.FromSeconds(5), cancellation.Token));
            await bridge;
            Assert.IsNull(store.ReadRequest());
            Assert.IsFalse(File.Exists(Path.Combine(root, "order-lease.json")));
            Assert.IsFalse(File.Exists(Path.Combine(root, "order-progress.json")));
        }
        finally
        {
            await bridge;
            Directory.Delete(root, true);
        }
    }

    private static VortexOrderRequest Request()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new(2, Guid.NewGuid().ToString("N"), new string('a', 64), "profile", now, now.AddSeconds(60), null, [], []);
    }

    private static void WriteProgress(string root, string requestId, int sequence, int schemaVersion = 2)
    {
        string target = Path.Combine(root, "order-progress.json");
        string temporary = target + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new { schemaVersion, requestId, sequence }));
        File.Move(temporary, target, true);
    }

    private static DateTimeOffset LeaseExpiry(string root)
    {
        using FileStream stream = new(Path.Combine(root, "order-lease.json"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using JsonDocument lease = JsonDocument.Parse(stream);
        return lease.RootElement.GetProperty("expiresAtUtc").GetDateTimeOffset();
    }
}
