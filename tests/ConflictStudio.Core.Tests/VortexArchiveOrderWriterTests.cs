using ConflictStudio.Core;
using System.IO;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class VortexArchiveOrderWriterTests
{
    private static readonly ArchiveFingerprint[] Inventory = [new("Alpha.archive", 5, new string('a', 64)), new("Beta.archive", 4, new string('b', 64))];
    private static readonly string[] CurrentOrder = ["Alpha.archive", "Beta.archive"];
    private static readonly string[] ProposedOrder = ["Beta.archive", "Alpha.archive"];

    [TestMethod]
    public void ApplySendsAnExactProfileBoundRequestAndReturnsBridgeVerification()
    {
        ArchiveOrderObservation observation = new("Standard", "C:\\Vortex", new string('c', 64), Inventory, CurrentOrder);
        ArchiveOrderPreview preview = ArchiveOrderPlanner.CreatePreview(observation, ProposedOrder);
        VortexManagerContext context = Context(true);
        VortexOrderRequest? captured = null;
        VortexArchiveOrderWriter writer = new(context, request =>
        {
            captured = request;
            return new VortexOrderResponse(1, request.RequestId, true, "Applied by Vortex", "modlist.txt.20260829.bak", new string('d', 64), DateTimeOffset.UtcNow);
        }, () => DateTimeOffset.UtcNow);

        ArchiveOrderApplyResult result = writer.Apply(preview, Inventory);

        Assert.IsNotNull(captured);
        Assert.AreEqual(context.ContextId, captured.ContextId);
        Assert.AreEqual(context.ProfileId, captured.ProfileId);
        CollectionAssert.AreEqual(ProposedOrder, captured.ProposedOrder);
        CollectionAssert.AreEqual(Inventory, captured.Inventory);
        Assert.IsTrue(result.Verified);
        Assert.AreEqual("modlist.txt.20260829.bak", result.BackupPath);
        Assert.AreEqual(new string('d', 64), result.WrittenSha256);
    }

    [TestMethod]
    public void ApplyRejectsPendingVortexDeploymentBeforeSendingARequest()
    {
        ArchiveOrderObservation observation = new("Standard", "C:\\Vortex", null, Inventory, CurrentOrder);
        ArchiveOrderPreview preview = ArchiveOrderPlanner.CreatePreview(observation, ProposedOrder);
        bool exchanged = false;
        VortexArchiveOrderWriter writer = new(Context(false), request =>
        {
            exchanged = true;
            return new VortexOrderResponse(1, request.RequestId, true, "unexpected", null, new string('d', 64), DateTimeOffset.UtcNow);
        }, () => DateTimeOffset.UtcNow);

        ArchiveOrderException exception = Assert.ThrowsExactly<ArchiveOrderException>(() => writer.Apply(preview, Inventory));

        StringAssert.Contains(exception.Message, "Deploy");
        Assert.IsFalse(exchanged);
    }

    [TestMethod]
    public void ApplyRejectsWhileCyberpunkIsRunningButNotBecauseVortexIsOpen()
    {
        ArchiveOrderObservation observation = new("Standard", "C:\\Vortex", null, Inventory, CurrentOrder);
        ArchiveOrderPreview preview = ArchiveOrderPlanner.CreatePreview(observation, ProposedOrder);
        VortexArchiveOrderWriter blocked = new(Context(true), request => throw new AssertFailedException("The bridge must not be called."), () => DateTimeOffset.UtcNow, () => ["Vortex", "Cyberpunk2077"]);

        Assert.ThrowsExactly<ArchiveOrderException>(() => blocked.Apply(preview, Inventory));

        VortexArchiveOrderWriter allowed = new(Context(true), request => new VortexOrderResponse(1, request.RequestId, true, "Applied", null, new string('d', 64), DateTimeOffset.UtcNow), () => DateTimeOffset.UtcNow, () => ["Vortex"]);
        Assert.IsTrue(allowed.Apply(preview, Inventory).Verified);
    }

    [TestMethod]
    public void ApplyRejectsAnOfflineBridgeBeforeWaitingForAResponse()
    {
        ArchiveOrderObservation observation = new("Standard", "C:\\Vortex", null, Inventory, CurrentOrder);
        ArchiveOrderPreview preview = ArchiveOrderPlanner.CreatePreview(observation, ProposedOrder);
        VortexManagerContext stale = Context(true) with { CapturedAtUtc = new DateTimeOffset(2026, 8, 29, 17, 0, 0, TimeSpan.Zero) };
        bool exchanged = false;
        VortexArchiveOrderWriter writer = new(stale, request =>
        {
            exchanged = true;
            throw new AssertFailedException("The bridge must not be called.");
        }, () => new DateTimeOffset(2026, 8, 29, 18, 0, 0, TimeSpan.Zero), () => ["Vortex"]);

        ArchiveOrderException exception = Assert.ThrowsExactly<ArchiveOrderException>(() => writer.Apply(preview, Inventory));

        StringAssert.Contains(exception.Message, "Open Vortex");
        Assert.IsFalse(exchanged);
    }

    [TestMethod]
    public void RequestStoreRoundTripsAndIgnoresAResponseForAnotherRequest()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-vortex-bridge-" + Guid.NewGuid().ToString("N"));
        try
        {
            VortexOrderBridgeStore store = new(root);
            VortexOrderRequest request = new(1, new string('e', 32), new string('a', 64), "profile", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddSeconds(15), null, Inventory, ProposedOrder);
            VortexOrderResponse other = new(1, new string('f', 32), true, "other", null, new string('d', 64), DateTimeOffset.UtcNow);

            store.WriteRequest(request);
            store.WriteResponse(other);

            VortexOrderRequest? restored = store.ReadRequest();
            Assert.IsNotNull(restored);
            Assert.AreEqual(request.RequestId, restored.RequestId);
            Assert.AreEqual(request.ContextId, restored.ContextId);
            CollectionAssert.AreEqual(request.Inventory, restored.Inventory);
            CollectionAssert.AreEqual(request.ProposedOrder, restored.ProposedOrder);
            Assert.IsNull(store.TryReadResponse(request.RequestId));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void RequestStoreDeletesItsPendingRequestWhenTheWaitIsCancelled()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-vortex-timeout-" + Guid.NewGuid().ToString("N"));
        try
        {
            VortexOrderBridgeStore store = new(root);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            VortexOrderRequest request = new(1, new string('e', 32), new string('a', 64), "profile", now, now.AddSeconds(15), null, Inventory, ProposedOrder);
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();

            Assert.ThrowsExactly<OperationCanceledException>(() => store.Exchange(request, TimeSpan.FromSeconds(15), cancellation.Token));

            Assert.IsNull(store.ReadRequest());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void RestorePreviousRequestsTheExactBridgeBackup()
    {
        ArchiveOrderObservation observation = new("Standard", "C:\\Vortex", new string('c', 64), Inventory, CurrentOrder);
        ArchiveOrderPreview preview = ArchiveOrderPlanner.CreatePreview(observation, ProposedOrder);
        List<VortexOrderRequest> requests = [];
        VortexArchiveOrderWriter writer = new(Context(true), request =>
        {
            requests.Add(request);
            return new VortexOrderResponse(1, request.RequestId, true, "Applied", requests.Count == 1 ? "C:\\Game\\archive\\pc\\mod\\modlist.txt.backup.bak" : null, new string(requests.Count == 1 ? 'd' : 'e', 64), DateTimeOffset.UtcNow, requests.Count == 1 ? new string('f', 64) : null);
        }, () => DateTimeOffset.UtcNow);
        ArchiveOrderApplyResult result = writer.Apply(preview, Inventory);

        writer.RestorePrevious(result, "C:\\Game\\archive\\pc\\mod\\modlist.txt");

        Assert.AreEqual(2, requests.Count);
        Assert.IsTrue(string.Equals(result.WrittenSha256, requests[1].ExpectedOrderSha256, StringComparison.Ordinal));
        Assert.AreEqual(new string('f', 64), requests[1].ContextId);
        Assert.IsTrue(requests[1].RestorePrevious);
        Assert.AreEqual(result.BackupPath, requests[1].RestoreBackupPath);
        Assert.HasCount(0, requests[1].ProposedOrder);
    }

    [TestMethod]
    public void RepairUndoRequestsTheExactBridgeBackup()
    {
        ArchiveOrderObservation observation = new("Standard", "C:\\Vortex", new string('c', 64), Inventory, CurrentOrder);
        ArchiveOrderPreview preview = ArchiveOrderPlanner.CreatePreview(observation, CurrentOrder);
        List<VortexOrderRequest> requests = [];
        VortexArchiveOrderWriter writer = new(Context(true), request =>
        {
            requests.Add(request);
            return new VortexOrderResponse(1, request.RequestId, true, "Applied", "C:\\Game\\archive\\pc\\mod\\modlist.txt.backup.bak", new string(requests.Count == 1 ? 'd' : 'e', 64), DateTimeOffset.UtcNow, requests.Count == 1 ? new string('f', 64) : null);
        }, () => DateTimeOffset.UtcNow);
        ArchiveOrderApplyResult result = writer.Apply(preview, Inventory);

        writer.RestorePrevious(result, "C:\\Game\\archive\\pc\\mod\\modlist.txt");

        Assert.IsTrue(requests[1].RestorePrevious);
        Assert.AreEqual(result.BackupPath, requests[1].RestoreBackupPath);
    }

    [TestMethod]
    public void RestorePreviousRejectsWhileCyberpunkIsRunning()
    {
        ArchiveOrderObservation observation = new("Standard", "C:\\Vortex", null, Inventory, CurrentOrder);
        ArchiveOrderPreview preview = ArchiveOrderPlanner.CreatePreview(observation, ProposedOrder);
        bool gameRunning = false;
        int requests = 0;
        VortexArchiveOrderWriter writer = new(Context(true), request =>
        {
            requests++;
            return new VortexOrderResponse(1, request.RequestId, true, "Applied", null, new string('d', 64), DateTimeOffset.UtcNow);
        }, () => DateTimeOffset.UtcNow, () => gameRunning ? ["Cyberpunk2077"] : []);
        ArchiveOrderApplyResult result = writer.Apply(preview, Inventory);
        gameRunning = true;

        Assert.ThrowsExactly<ArchiveOrderException>(() => writer.RestorePrevious(result, "C:\\Game\\archive\\pc\\mod\\modlist.txt"));
        Assert.AreEqual(1, requests);
    }

    private static VortexManagerContext Context(bool fresh)
        => new(1, new string('a', 64), DateTimeOffset.UtcNow, "profile", "Standard", "C:\\Game", "C:\\Staging", fresh, [], [], CurrentOrder, null);
}
