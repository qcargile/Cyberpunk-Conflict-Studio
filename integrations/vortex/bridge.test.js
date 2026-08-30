const assert = require("node:assert/strict");
const crypto = require("node:crypto");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const test = require("node:test");
const bridge = require("./bridge");

test("context identity ignores capture time but includes provider and winner state", () => {
  const input = {
    profileId: "profile",
    profileName: "Standard",
    gameRoot: "C:\\Game",
    stagingRoot: "C:\\Staging",
    deploymentFresh: true,
    providers: [{ id: "alpha", name: "Alpha", rootPath: "C:\\Staging\\Alpha", order: 0 }],
    deployedWinners: { "r6\\scripts\\shared.reds": "alpha" },
    archiveOrder: ["Alpha.archive"],
    archiveOrderSha256: null,
  };

  const first = bridge.createContext({ ...input, capturedAtUtc: "2026-08-29T18:00:00.000Z" });
  const second = bridge.createContext({ ...input, capturedAtUtc: "2026-08-29T19:00:00.000Z" });

  assert.equal(first.contextId, second.contextId);
  assert.equal(first.schemaVersion, 1);
});

test("order request validates inventory and atomically replaces the manager file", () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "conflict-studio-vortex-js-"));
  try {
    const gameRoot = path.join(root, "game");
    const archiveRoot = path.join(gameRoot, "archive", "pc", "mod");
    fs.mkdirSync(archiveRoot, { recursive: true });
    fs.writeFileSync(path.join(archiveRoot, "Alpha.archive"), "alpha");
    fs.writeFileSync(path.join(archiveRoot, "Beta.archive"), "beta");
    const orderPath = path.join(archiveRoot, "modlist.txt");
    fs.writeFileSync(orderPath, "Alpha.archive\nhelper.archive.xl\nBeta.archive\n");
    const context = bridge.createContext({
      capturedAtUtc: "2026-08-29T18:00:00.000Z",
      profileId: "profile",
      profileName: "Standard",
      gameRoot,
      stagingRoot: path.join(root, "staging"),
      deploymentFresh: true,
      providers: [],
      deployedWinners: {},
      archiveOrder: ["Alpha.archive", "Beta.archive"],
      archiveOrderSha256: sha(fs.readFileSync(orderPath)),
    });
    const requestedAt = new Date();
    const request = {
      schemaVersion: 1,
      requestId: "e".repeat(32),
      contextId: context.contextId,
      profileId: "profile",
      requestedAtUtc: requestedAt.toISOString(),
      expiresAtUtc: new Date(requestedAt.getTime() + 15000).toISOString(),
      expectedOrderSha256: context.archiveOrderSha256,
      inventory: [fingerprint(archiveRoot, "Alpha.archive"), fingerprint(archiveRoot, "Beta.archive")],
      proposedOrder: ["Beta.archive", "Alpha.archive"],
    };

    const response = bridge.applyOrderRequest(request, context);

    assert.equal(response.applied, true);
    assert.equal(fs.readFileSync(orderPath, "utf8"), "Beta.archive\r\nhelper.archive.xl\r\nAlpha.archive\r\n");
    assert.equal(response.writtenSha256, sha(fs.readFileSync(orderPath)));
    assert.equal(fs.existsSync(response.backupPath), true);
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});

test("order request expires instead of applying after a later Vortex restart", () => {
  const context = bridge.createContext({
    capturedAtUtc: "2026-08-29T18:00:00.000Z",
    profileId: "profile",
    profileName: "Standard",
    gameRoot: "C:\\Game",
    stagingRoot: "C:\\Staging",
    deploymentFresh: true,
    providers: [],
    deployedWinners: {},
    archiveOrder: [],
    archiveOrderSha256: null,
  });
  const request = {
    schemaVersion: 1,
    requestId: "e".repeat(32),
    contextId: context.contextId,
    profileId: "profile",
    requestedAtUtc: "2026-08-29T18:00:00.000Z",
    expiresAtUtc: "2026-08-29T18:00:15.000Z",
    expectedOrderSha256: null,
    inventory: [],
    proposedOrder: [],
  };

  assert.throws(() => bridge.applyOrderRequest(request, context, new Date("2026-08-29T18:02:00.000Z")), /expired/i);
});

test("verification failure restores the exact previous order", () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "conflict-studio-vortex-rollback-"));
  const originalRead = fs.readFileSync;
  try {
    const gameRoot = path.join(root, "game");
    const archiveRoot = path.join(gameRoot, "archive", "pc", "mod");
    fs.mkdirSync(archiveRoot, { recursive: true });
    fs.writeFileSync(path.join(archiveRoot, "Alpha.archive"), "alpha");
    fs.writeFileSync(path.join(archiveRoot, "Beta.archive"), "beta");
    const orderPath = path.join(archiveRoot, "modlist.txt");
    const original = Buffer.from("Alpha.archive\nhelper.archive.xl\nstale.archive\nBeta.archive\n");
    fs.writeFileSync(orderPath, original);
    const context = bridge.createContext({ capturedAtUtc: new Date().toISOString(), profileId: "profile", profileName: "Standard", gameRoot, stagingRoot: path.join(root, "staging"), deploymentFresh: true, providers: [], deployedWinners: {}, archiveOrder: ["Alpha.archive", "Beta.archive"], archiveOrderSha256: sha(original) });
    const request = { schemaVersion: 1, requestId: "f".repeat(32), contextId: context.contextId, profileId: "profile", requestedAtUtc: new Date().toISOString(), expiresAtUtc: new Date(Date.now() + 15000).toISOString(), expectedOrderSha256: context.archiveOrderSha256, inventory: [fingerprint(archiveRoot, "Alpha.archive"), fingerprint(archiveRoot, "Beta.archive")], proposedOrder: ["Beta.archive", "Alpha.archive"] };
    let injected = false;
    fs.readFileSync = function read(target, ...args) {
      if (target === orderPath && !injected) {
        const value = originalRead.call(fs, target, ...args);
        if (value.toString("utf8").startsWith("Beta.archive")) {
          injected = true;
          return Buffer.from("fault");
        }
        return value;
      }
      return originalRead.call(fs, target, ...args);
    };

    assert.throws(() => bridge.applyOrderRequest(request, context), /verify/i);
    fs.readFileSync = originalRead;

    assert.deepEqual(fs.readFileSync(orderPath), original);
  } finally {
    fs.readFileSync = originalRead;
    fs.rmSync(root, { recursive: true, force: true });
  }
});

test("response publication failure restores the exact previous order", async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "conflict-studio-vortex-publish-"));
  try {
    const gameRoot = path.join(root, "game");
    const archiveRoot = path.join(gameRoot, "archive", "pc", "mod");
    fs.mkdirSync(archiveRoot, { recursive: true });
    fs.writeFileSync(path.join(archiveRoot, "Alpha.archive"), "alpha");
    fs.writeFileSync(path.join(archiveRoot, "Beta.archive"), "beta");
    const orderPath = path.join(archiveRoot, "modlist.txt");
    const original = Buffer.from("Alpha.archive\nBeta.archive\n");
    fs.writeFileSync(orderPath, original);
    const context = bridge.createContext({ capturedAtUtc: new Date().toISOString(), profileId: "profile", profileName: "Standard", gameRoot, stagingRoot: path.join(root, "staging"), deploymentFresh: true, providers: [], deployedWinners: {}, archiveOrder: ["Alpha.archive", "Beta.archive"], archiveOrderSha256: sha(original) });
    const request = { schemaVersion: 1, requestId: "d".repeat(32), contextId: context.contextId, profileId: "profile", requestedAtUtc: new Date().toISOString(), expiresAtUtc: new Date(Date.now() + 15000).toISOString(), expectedOrderSha256: context.archiveOrderSha256, inventory: [fingerprint(archiveRoot, "Alpha.archive"), fingerprint(archiveRoot, "Beta.archive")], proposedOrder: ["Beta.archive", "Alpha.archive"] };
    const response = bridge.applyOrderRequest(request, context);
    const contextPath = path.join(root, "context.json");
    const refresh = async () => {
      const bytes = fs.readFileSync(orderPath);
      const value = bridge.createContext({ capturedAtUtc: new Date().toISOString(), profileId: "profile", profileName: "Standard", gameRoot, stagingRoot: path.join(root, "staging"), deploymentFresh: true, providers: [], deployedWinners: {}, archiveOrder: bytes.toString("utf8").split(/\r?\n/).filter(Boolean), archiveOrderSha256: sha(bytes) });
      fs.writeFileSync(contextPath, JSON.stringify(value));
      return value;
    };
    await assert.rejects(() => bridge.completeOrder(response, context, refresh, () => { throw new Error("response unavailable"); }), /response unavailable/);

    assert.deepEqual(fs.readFileSync(orderPath), original);
    const restoredContext = JSON.parse(fs.readFileSync(contextPath, "utf8"));
    assert.deepEqual(restoredContext.archiveOrder, ["Alpha.archive", "Beta.archive"]);
    assert.equal(restoredContext.archiveOrderSha256, sha(original));
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});

function fingerprint(root, name) {
  const file = fs.readFileSync(path.join(root, name));
  return { name, size: file.length, sha256: sha(file) };
}

function sha(value) {
  return crypto.createHash("sha256").update(value).digest("hex");
}
