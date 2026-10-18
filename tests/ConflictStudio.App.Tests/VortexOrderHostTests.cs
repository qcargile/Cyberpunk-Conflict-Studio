using System.Diagnostics;
using System.IO;

namespace ConflictStudio.App.Tests;

[TestClass]
public sealed class VortexOrderHostTests
{
    [TestMethod]
    public async Task ExtensionUsesLivePermissionBeforeWritingAndBeforeAcknowledging()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-order-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string harness = Path.Combine(root, "host.cjs");
        File.WriteAllText(harness, """
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const source = process.argv[2];
const bridge = require(path.join(path.dirname(source), "bridge.js"));
const root = __dirname;
const archiveRoot = path.join(root, "archive", "pc", "mod");
const bridgeRoot = path.join(root, "Cyberpunk Conflict Studio", "vortex");
const orderPath = path.join(archiveRoot, "modlist.txt");
const leasePath = path.join(bridgeRoot, "order-lease.json");
const original = "Alpha.archive\r\nBeta.archive\r\n";
const reordered = "Beta.archive\r\nAlpha.archive\r\n";
fs.mkdirSync(archiveRoot, { recursive: true });
fs.writeFileSync(path.join(archiveRoot, "Alpha.archive"), "alpha");
fs.writeFileSync(path.join(archiveRoot, "Beta.archive"), "beta");
fs.writeFileSync(orderPath, original);
let listener;
let revoke = false;
const errors = [];
const profile = { id: "profile", name: "Standard", gameId: "cyberpunk2077", modState: {} };
const state = { settings: { gameMode: { discovered: { cyberpunk2077: { path: root } } } }, persistent: { mods: { cyberpunk2077: {} }, deployment: { needToDeploy: { cyberpunk2077: false } } } };
const vortex = {
  actions: { addDiscoveredTool: () => null, setToolVisible: () => null },
  selectors: { activeProfile: () => profile, installPathForGame: () => root, modPathsForGame: () => ({ archive: root }) },
  util: {
    getVortexPath: () => root, sortMods: async () => [],
    getManifest: async () => {
      if (revoke && fs.readFileSync(orderPath, "utf8") === original) fs.unlinkSync(leasePath);
      return { files: [] };
    }
  }
};
const sandbox = {
  __dirname: root, module: { exports: {} }, process: { env: { LOCALAPPDATA: root } },
  require: name => name === "vortex-api" ? vortex
    : name === "./bridge" ? bridge
    : name === "node:fs" ? { ...fs, watch: (_, callback) => { listener = callback; return { on() {} }; } }
    : name === "node:child_process" ? { execFileSync: () => "" } : require(name)
};
vm.runInNewContext(fs.readFileSync(source, "utf8"), sandbox);
sandbox.module.exports({ once: callback => callback(), api: { getState: () => state, store: { dispatch() {} }, events: { on() {} }, onAsync() {}, showErrorNotification: (...args) => errors.push(args) } });
function write(name, value) { fs.writeFileSync(path.join(bridgeRoot, name), JSON.stringify(value)); }
function read(name) { try { return JSON.parse(fs.readFileSync(path.join(bridgeRoot, name), "utf8")); } catch { return null; } }
async function response(name, requestId) {
  const until = Date.now() + 5000;
  while (Date.now() < until) {
    const value = read(name);
    if (value && value.requestId === requestId) return value;
    await new Promise(resolve => setTimeout(resolve, 10));
  }
  throw new Error(`No ${name}: ${JSON.stringify(errors)}`);
}
(async () => {
  const contextId = "a".repeat(32);
  write("context-request.json", { schemaVersion: 1, requestId: contextId, requestedAtUtc: new Date().toISOString(), expiresAtUtc: new Date(Date.now() + 60000).toISOString() });
  listener();
  assert.equal((await response("context-response.json", contextId)).refreshed, true);
  for (const [requestId, proposedOrder, cancelAfterCommit] of [["b".repeat(32), ["Beta.archive", "Alpha.archive"], false], ["c".repeat(32), ["Alpha.archive", "Beta.archive"], true]]) {
    const context = read("context.json");
    const request = { schemaVersion: 2, requestId, contextId: context.contextId, profileId: profile.id, requestedAtUtc: new Date().toISOString(), expiresAtUtc: new Date(Date.now() + 60000).toISOString(), expectedOrderSha256: bridge.sha(fs.readFileSync(orderPath)), inventory: bridge.archiveInventory(archiveRoot), proposedOrder };
    write("order-lease.json", { schemaVersion: 2, requestId, expiresAtUtc: request.expiresAtUtc });
    write("order-request.json", request);
    revoke = cancelAfterCommit;
    listener();
    const result = await response("order-response.json", requestId);
    assert.equal(result.schemaVersion, 2);
    assert.equal(result.applied, !cancelAfterCommit);
    if (cancelAfterCommit) assert.match(result.message, /no longer waiting/);
    assert.equal(fs.readFileSync(orderPath, "utf8"), reordered);
    assert.equal(read("order-progress.json").requestId, requestId);
    assert.equal(read("order-request.json"), null);
  }
  assert.deepEqual(errors, []);
})().catch(error => { console.error(error.stack || error); process.exitCode = 1; });
""");
        try
        {
            DirectoryInfo? directory = new(Environment.CurrentDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "integrations", "vortex", "index.js"))) directory = directory.Parent;
            Assert.IsNotNull(directory);
            ProcessStartInfo start = new("node") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            start.ArgumentList.Add(harness);
            start.ArgumentList.Add(Path.Combine(directory.FullName, "integrations", "vortex", "index.js"));
            using Process process = Process.Start(start)!;
            Task<string> error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(15000))
            {
                process.Kill(true);
                Assert.Fail("The Vortex host test did not finish.");
            }
            Assert.AreEqual(0, process.ExitCode, await error);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
