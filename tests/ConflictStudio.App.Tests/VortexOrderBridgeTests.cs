using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ConflictStudio.Core;

namespace ConflictStudio.App.Tests;

[TestClass]
public sealed class VortexOrderBridgeTests
{
    [TestMethod]
    public void SchemaTwoExchangeRenewsItsLeaseThroughTwoSlowActualArchivePasses()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-vortex-exchange-" + Guid.NewGuid().ToString("N"));
        string archive = Path.Combine(root, "archive", "pc", "mod");
        string harness = Path.Combine(root, "exchange.cjs");
        Process? host = null;
        try
        {
            Directory.CreateDirectory(archive);
            string alpha = Path.Combine(archive, "Alpha.archive");
            string beta = Path.Combine(archive, "Beta.archive");
            File.WriteAllBytes(alpha, new byte[8 * 1024 * 1024]);
            File.WriteAllBytes(beta, Enumerable.Repeat((byte)1, 8 * 1024 * 1024).ToArray());
            string orderPath = Path.Combine(archive, "modlist.txt");
            byte[] original = "Alpha.archive\r\nBeta.archive\r\n"u8.ToArray();
            File.WriteAllBytes(orderPath, original);
            File.WriteAllText(harness, """
const fs = require("node:fs");
const path = require("node:path");
const bridge = require(process.argv[2]);
const root = process.argv[3];
const requestPath = path.join(root, "order-request.json");
const leasePath = path.join(root, "order-lease.json");
const progressPath = path.join(root, "order-progress.json");
const responsePath = path.join(root, "order-response.json");
const read = target => { try { return JSON.parse(fs.readFileSync(target, "utf8")); } catch { return null; } };
const write = (target, value) => { const temporary = `${target}.${process.pid}.tmp`; fs.writeFileSync(temporary, JSON.stringify(value)); fs.renameSync(temporary, target); };
const wait = () => new Promise(resolve => setTimeout(resolve, 10));
(async () => {
  let request;
  while ((request = read(requestPath)) === null) await wait();
  const context = { contextId: request.contextId, profileId: request.profileId, deploymentFresh: true, gameRoot: root };
  const session = bridge.createOrderSession(request, () => read(requestPath), () => read(leasePath), value => write(progressPath, value), () => new Date());
  session.progress(true);
  const response = await bridge.applyOrderRequest(request, context, new Date(), { yieldEveryBytes: 1024 * 1024, yieldNow: () => new Promise(resolve => setTimeout(resolve, 250)), progress: session.progress, authorize: session.authorize, now: () => new Date() });
  await bridge.completeOrder(response, context, async () => context, value => { session.authorize(); write(responsePath, value); });
})().catch(error => { console.error(error.stack || error); process.exitCode = 1; });
""");
            ArchiveFingerprint[] inventory =
            [
                new("Alpha.archive", new FileInfo(alpha).Length, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(alpha)))),
                new("Beta.archive", new FileInfo(beta).Length, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(beta))))
            ];
            DateTimeOffset now = DateTimeOffset.UtcNow;
            VortexOrderRequest request = new(2, Guid.NewGuid().ToString("N"), Convert.ToHexStringLower(SHA256.HashData(Guid.NewGuid().ToByteArray())), "profile", now, now.AddSeconds(60), Convert.ToHexStringLower(SHA256.HashData(original)), inventory, ["Beta.archive", "Alpha.archive"]);
            string bridge = Path.Combine(RepositoryRoot(), "integrations", "vortex", "bridge.js");
            host = StartNode(harness, bridge, root, out StringBuilder error);
            Stopwatch elapsed = Stopwatch.StartNew();
            VortexOrderResponse response = new VortexOrderBridgeStore(root).Exchange(request, TimeSpan.FromSeconds(2));
            elapsed.Stop();

            Assert.IsTrue(response.Applied);
            Assert.IsGreaterThan(TimeSpan.FromSeconds(3), elapsed.Elapsed);
            CollectionAssert.AreEqual("Beta.archive\r\nAlpha.archive\r\n"u8.ToArray(), File.ReadAllBytes(orderPath));
            Assert.IsTrue(host.WaitForExit(10000), error.ToString());
            host.WaitForExit();
            Assert.AreEqual(0, host.ExitCode, error.ToString());
        }
        finally
        {
            if (host is not null)
            {
                if (!host.HasExited) host.Kill(true);
                host.Dispose();
            }
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void BridgeKeepsAnActiveSchemaTwoClientAuthorizedThroughSlowVerification()
    {
        string script = WriteHarness("slow", """
const original = Buffer.from("Alpha.archive\r\nBeta.archive\r\n");
const setup = makeProfile(original);
let time = 0;
let lease = { schemaVersion: 2, requestId: id("a"), expiresAtUtc: iso(59000) };
const request = orderRequest(setup, id("a"), 2, ["Beta.archive", "Alpha.archive"], iso(60000));
const session = bridge.createOrderSession(request, () => request, () => lease, value => { lease = { schemaVersion: 2, requestId: value.requestId, expiresAtUtc: iso(time + 59000) }; }, () => new Date(time));
const options = { yieldEveryBytes: 1024 * 1024, yieldNow: async () => { time += 31000; }, progress: session.progress };
const applied = await bridge.applyOrderRequest(request, setup.context, new Date(time), { ...options, now: () => new Date(time), authorize: session.authorize });
assert.equal(applied.applied, true);
assert.ok(time > 60000);
assert.equal(readOrder(setup), "Beta.archive\r\nAlpha.archive\r\n");
const undoRequest = orderRequest(setup, id("b"), 2, [], iso(time + 60000), { restorePrevious: true, restoreBackupPath: applied.backupPath, expectedOrderSha256: applied.writtenSha256 });
undoRequest.requestedAtUtc = iso(time);
lease = { schemaVersion: 2, requestId: undoRequest.requestId, expiresAtUtc: iso(time + 59000) };
const undoSession = bridge.createOrderSession(undoRequest, () => undoRequest, () => lease, value => { lease = { schemaVersion: 2, requestId: value.requestId, expiresAtUtc: iso(time + 59000) }; }, () => new Date(time));
const restored = await bridge.applyOrderRequest(undoRequest, setup.context, new Date(time), { ...options, now: () => new Date(time), progress: undoSession.progress, authorize: undoSession.authorize });
assert.equal(restored.applied, true);
assert.equal(readOrder(setup), original.toString("utf8"));
""");

        AssertNode(script);
    }

    [TestMethod]
    public void BridgeRejectsExpiredOrLostAuthorizationWithoutWriting()
    {
        string script = WriteHarness("authorization", """
const expected = { legacy: "expired", missing: "no longer waiting", expired: "no longer waiting", wrong: "no longer waiting", removed: "no longer waiting", replaced: "no longer waiting", revision: "profile changed", context: "does not match", inventory: "changed while it was being fingerprinted", game: "running", order: "order changed" };
for (const kind of Object.keys(expected)) {
  const original = Buffer.from("Alpha.archive\r\nBeta.archive\r\n");
  const setup = makeProfile(original);
  let time = 0;
  const request = orderRequest(setup, id("a"), kind === "legacy" ? 1 : 2, ["Beta.archive", "Alpha.archive"], iso(60000));
  let pending = request;
  let lease = { schemaVersion: 2, requestId: request.requestId, expiresAtUtc: iso(59000) };
  let revision = 0;
  let gameRunning = false;
  let current = setup.context;
  let yields = 0;
  const session = request.schemaVersion === 2 ? bridge.createOrderSession(request, () => pending, () => lease, value => { if (!["missing", "expired", "wrong"].includes(kind)) lease = { schemaVersion: 2, requestId: value.requestId, expiresAtUtc: iso(time + 59000) }; }, () => new Date(time)) : null;
  const options = {
    yieldEveryBytes: 1024 * 1024,
    progress: session && session.progress,
    yieldNow: async () => {
      yields++;
      time += 31000;
      if (kind === "missing") lease = null;
      if (kind === "expired") lease = { schemaVersion: 2, requestId: request.requestId, expiresAtUtc: iso(time - 1) };
      if (kind === "wrong") lease = { schemaVersion: 2, requestId: id("f"), expiresAtUtc: iso(time + 59000) };
      if (kind === "removed") pending = null;
      if (kind === "replaced") pending = { ...request, requestId: id("e") };
      if (kind === "revision") revision++;
      if (kind === "context") current = { ...setup.context, contextId: id("c") + id("d") };
      if (kind === "inventory") fs.appendFileSync(setup.alpha, "changed");
      if (kind === "game") gameRunning = true;
    }
  };
  if (kind === "order") request.expectedOrderSha256 = "0".repeat(64);
  await assert.rejects(() => bridge.applyOrderRequest(request, setup.context, new Date(time), {
    ...options,
    now: () => new Date(time),
    authorize: session && session.authorize,
    gameRunning: () => gameRunning,
    currentContext: () => current,
    stateRevision: () => revision,
    expectedRevision: 0
  }), new RegExp(expected[kind], "i"));
  assert.equal(readOrder(setup), original.toString("utf8"), kind);
}
{
  const original = Buffer.from("Alpha.archive\r\nBeta.archive\r\n");
  const setup = makeProfile(original);
  let time = 0;
  let pending;
  const request = orderRequest(setup, id("a"), 2, ["Beta.archive", "Alpha.archive"], iso(60000));
  pending = request;
  let lease = { schemaVersion: 2, requestId: request.requestId, expiresAtUtc: iso(59000) };
  const session = bridge.createOrderSession(request, () => pending, () => lease, value => { lease = { schemaVersion: 2, requestId: value.requestId, expiresAtUtc: iso(time + 59000) }; }, () => new Date(time));
  let gameChecks = 0;
  await assert.rejects(() => bridge.applyOrderRequest(request, setup.context, new Date(time), {
    yieldEveryBytes: 1024 * 1024,
    yieldNow: async () => { time += 31000; },
    progress: session.progress,
    authorize: session.authorize,
    now: () => new Date(time),
    gameRunning: () => { gameChecks++; if (gameChecks === 2) pending = null; return false; }
  }), /no longer waiting/i);
  assert.ok(gameChecks >= 2);
  assert.equal(readOrder(setup), original.toString("utf8"));
}
""");

        AssertNode(script);
    }

    [TestMethod]
    public void BridgeProgressIsThrottledAndACompletionPermissionLossRollsBack()
    {
        string script = WriteHarness("progress", """
let time = 0;
const request = { schemaVersion: 2, requestId: id("a"), expiresAtUtc: iso(60000) };
const messages = [];
const session = bridge.createOrderSession(request, () => request, () => ({ schemaVersion: 2, requestId: request.requestId, expiresAtUtc: iso(time + 59000) }), value => messages.push(value), () => new Date(time));
session.progress();
session.progress();
time = 999;
session.progress();
time = 1000;
session.progress();
session.progress(true);
session.progress(true);
assert.equal(messages.length, 4);
assert.deepEqual(messages.map(value => value.sequence), [1, 2, 3, 4]);
const original = Buffer.from("Alpha.archive\r\nBeta.archive\r\n");
const setup = makeProfile(original);
const apply = orderRequest(setup, id("b"), 2, ["Beta.archive", "Alpha.archive"], iso(60000));
let pending = apply;
let lease = { schemaVersion: 2, requestId: apply.requestId, expiresAtUtc: iso(59000) };
const completionSession = bridge.createOrderSession(apply, () => pending, () => lease, value => { lease = { schemaVersion: 2, requestId: value.requestId, expiresAtUtc: iso(59000) }; }, () => new Date(0));
const response = await bridge.applyOrderRequest(apply, setup.context, new Date(0), { now: () => new Date(0), authorize: completionSession.authorize, progress: completionSession.progress });
await assert.rejects(() => bridge.completeOrder(response, setup.context, async () => { pending = null; return setup.context; }, () => completionSession.authorize()), /no longer waiting/i);
assert.equal(readOrder(setup), original.toString("utf8"));
""");

        AssertNode(script);
    }

    private static string WriteHarness(string name, string scenario)
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-vortex-bridge-" + name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string harness = Path.Combine(root, "bridge.cjs");
        File.WriteAllText(harness, """
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const bridge = require(process.argv[2]);
const id = char => char.repeat(32);
const iso = value => new Date(value).toISOString();
function makeProfile(order) {
  const root = fs.mkdtempSync(path.join(__dirname, "profile-"));
  const archive = path.join(root, "archive", "pc", "mod");
  fs.mkdirSync(archive, { recursive: true });
  const alpha = path.join(archive, "Alpha.archive");
  const beta = path.join(archive, "Beta.archive");
  fs.writeFileSync(alpha, Buffer.alloc(3 * 1024 * 1024, 1));
  fs.writeFileSync(beta, Buffer.alloc(3 * 1024 * 1024, 2));
  const orderPath = path.join(archive, "modlist.txt");
  fs.writeFileSync(orderPath, order);
  const context = bridge.createContext({ capturedAtUtc: iso(0), profileId: "profile", profileName: "Standard", gameRoot: root, stagingRoot: root, deploymentFresh: true, providers: [], deployedWinners: {}, archiveOrder: ["Alpha.archive", "Beta.archive"], archiveOrderSha256: bridge.sha(order), deploymentInventoryComplete: true });
  return { root, archive, alpha, beta, orderPath, context };
}
function orderRequest(setup, requestId, schemaVersion, proposedOrder, expiresAtUtc, overrides = {}) {
  const current = fs.readFileSync(setup.orderPath);
  return { schemaVersion, requestId, contextId: setup.context.contextId, profileId: "profile", requestedAtUtc: iso(0), expiresAtUtc, expectedOrderSha256: bridge.sha(current), inventory: bridge.archiveInventory(setup.archive), proposedOrder, ...overrides };
}
function readOrder(setup) { return fs.readFileSync(setup.orderPath, "utf8"); }
(async () => {
""" + scenario + """
})().catch(error => { console.error(error.stack || error); process.exitCode = 1; });
""");
        return harness;
    }

    private static void AssertNode(string harness)
    {
        string bridge = Path.Combine(RepositoryRoot(), "integrations", "vortex", "bridge.js");
        try
        {
            using Process process = StartNode(harness, bridge, null, out StringBuilder error);
            if (!process.WaitForExit(15000))
            {
                process.Kill(true);
                Assert.Fail(error.ToString());
            }
            process.WaitForExit();
            Assert.AreEqual(0, process.ExitCode, error.ToString());
        }
        finally
        {
            string root = Path.GetDirectoryName(harness)!;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(Environment.CurrentDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "integrations", "vortex", "bridge.js"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Conflict Studio repository root was not found.");
    }

    private static Process StartNode(string harness, string bridge, string? root, out StringBuilder error)
    {
        string arguments = root is null ? $"\"{harness}\" \"{bridge}\"" : $"\"{harness}\" \"{bridge}\" \"{root}\"";
        Process process = Process.Start(new ProcessStartInfo("node", arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true })!;
        StringBuilder captured = new();
        error = captured;
        process.ErrorDataReceived += (_, value) => { if (value.Data is not null) captured.AppendLine(value.Data); };
        process.BeginErrorReadLine();
        return process;
    }
}
