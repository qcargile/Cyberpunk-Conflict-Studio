const assert = require("node:assert/strict");
const fs = require("node:fs");
const Module = require("node:module");
const os = require("node:os");
const path = require("node:path");
const test = require("node:test");

test("extension exports the active profile staging provider and deployed winner", async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "conflict-studio-vortex-extension-"));
  const originalLocalAppData = process.env.LOCALAPPDATA;
  const originalLoad = Module._load;
  const originalSetInterval = global.setInterval;
  try {
    const intervals = [];
    global.setInterval = (callback, milliseconds) => {
      intervals.push({ callback, milliseconds });
      return { unref: () => undefined };
    };
    const gameRoot = path.join(root, "game");
    const stagingRoot = path.join(root, "staging");
    const providerRoot = path.join(stagingRoot, "Alpha-install");
    const betaRoot = path.join(stagingRoot, "Beta-install");
    fs.mkdirSync(gameRoot, { recursive: true });
    fs.mkdirSync(providerRoot, { recursive: true });
    fs.mkdirSync(betaRoot, { recursive: true });
    const archiveRoot = path.join(gameRoot, "archive", "pc", "mod");
    fs.mkdirSync(archiveRoot, { recursive: true });
    fs.writeFileSync(path.join(archiveRoot, "Alpha.archive"), "alpha");
    fs.writeFileSync(path.join(archiveRoot, "Beta.archive"), "beta");
    fs.writeFileSync(path.join(archiveRoot, "modlist.txt"), "Alpha.archive\nBeta.archive\n");
    process.env.LOCALAPPDATA = root;
    const state = {
      settings: { gameMode: { discovered: { cyberpunk2077: { path: gameRoot } } } },
      persistent: {
        profiles: { profile: { id: "profile", name: "Standard", gameId: "cyberpunk2077", modState: { alpha: { enabled: true }, beta: { enabled: true } } } },
        mods: { cyberpunk2077: { alpha: { id: "alpha", installationPath: "Alpha-install", attributes: { name: "Alpha" } }, beta: { id: "beta", installationPath: "Beta-install", attributes: { name: "Beta" } } } },
        deployment: { needToDeploy: { cyberpunk2077: false } },
      },
    };
    let sortCalls = 0;
    let manifestCalls = 0;
    let activeSorts = 0;
    let maximumConcurrentSorts = 0;
    const vortexApi = {
      actions: {
        addDiscoveredTool: (gameId, toolId, result, manual) => ({ type: "ADD_DISCOVERED_TOOL", payload: { gameId, toolId, result, manual } }),
      },
      selectors: {
        activeProfile: () => state.persistent.profiles.profile,
        installPathForGame: () => stagingRoot,
        modPathsForGame: () => ({ "": gameRoot }),
      },
      util: {
        sortMods: async (gameId, mods) => {
          sortCalls++;
          activeSorts++;
          maximumConcurrentSorts = Math.max(maximumConcurrentSorts, activeSorts);
          await new Promise((resolve) => setImmediate(resolve));
          activeSorts--;
          return mods;
        },
        renderModName: (mod) => mod.attributes.name,
        getManifest: async () => { manifestCalls++; return { files: [{ relPath: "r6\\scripts\\shared.reds", source: "Beta-install", time: 1 }] }; },
        getVortexPath: () => path.join(root, "vortex-user"),
      },
    };
    Module._load = function load(request, parent, isMain) {
      if (request === "vortex-api") return vortexApi;
      return originalLoad.call(this, request, parent, isMain);
    };
    delete require.cache[require.resolve("./index")];
    const init = require("./index");
    let once;
    const dispatched = [];
    const callbacks = {};
    const eventCallbacks = {};
    const context = {
      once: (callback) => { once = callback; },
      api: {
        getState: () => state,
        store: { dispatch: (action) => dispatched.push(action) },
        onAsync: (name, callback) => { callbacks[name] = callback; },
        showErrorNotification: (title, error) => { throw error; },
      },
    };

    assert.equal(init(context), true);
    context.api.events = { on: (name, callback) => { eventCallbacks[name] = callback; } };
    once();
    assert.equal(typeof eventCallbacks["profile-did-change"], "function");
    const contextPath = path.join(root, "Cyberpunk Conflict Studio", "vortex", "context.json");
    await waitFor(contextPath);
    const exported = JSON.parse(fs.readFileSync(contextPath, "utf8"));
    const toolAction = dispatched.find((action) => action.type === "ADD_DISCOVERED_TOOL");

    assert.ok(toolAction);
    assert.equal(toolAction.payload.gameId, "cyberpunk2077");
    assert.equal(toolAction.payload.toolId, "conflict-studio");
    assert.equal(toolAction.payload.manual, false);
    assert.equal(toolAction.payload.result.name, "Conflict Studio");
    assert.equal(toolAction.payload.result.path, path.join(__dirname, "Conflict Studio", "ConflictStudio.exe"));
    assert.equal(toolAction.payload.result.workingDirectory, path.join(__dirname, "Conflict Studio"));
    assert.deepEqual(toolAction.payload.result.parameters, ["--manager", "vortex"]);
    assert.equal(toolAction.payload.result.hidden, false);
    assert.equal(fs.existsSync(path.join(root, "vortex-user", "cyberpunk2077", "icons", "conflict-studio.png")), true);
    assert.equal(exported.profileName, "Standard");
    assert.equal(exported.deploymentFresh, true);
    assert.equal(exported.providers[0].id, "beta");
    assert.equal(exported.providers[0].rootPath, betaRoot);
    assert.equal(exported.providers[1].id, "alpha");
    assert.equal(exported.deployedWinners["r6\\scripts\\shared.reds"], "beta");
    const initialSortCalls = sortCalls;
    const initialManifestCalls = manifestCalls;
    state.persistent.deployment.needToDeploy.cyberpunk2077 = true;
    await intervals.find((value) => value.milliseconds === 5000).callback();
    assert.equal(JSON.parse(fs.readFileSync(contextPath, "utf8")).deploymentFresh, false);
    assert.equal(sortCalls, initialSortCalls);
    assert.equal(manifestCalls, initialManifestCalls);
    state.persistent.deployment.needToDeploy.cyberpunk2077 = false;
    const request = {
      schemaVersion: 1,
      requestId: "e".repeat(32),
      contextId: exported.contextId,
      profileId: "profile",
      requestedAtUtc: new Date().toISOString(),
      expiresAtUtc: new Date(Date.now() + 15000).toISOString(),
      expectedOrderSha256: exported.archiveOrderSha256,
      inventory: [fingerprint(archiveRoot, "Alpha.archive"), fingerprint(archiveRoot, "Beta.archive")],
      proposedOrder: ["Beta.archive", "Alpha.archive"],
    };
    const requestPath = path.join(root, "Cyberpunk Conflict Studio", "vortex", "order-request.json");
    const responsePath = path.join(root, "Cyberpunk Conflict Studio", "vortex", "order-response.json");
    fs.writeFileSync(requestPath, JSON.stringify(request));
    await intervals.find((value) => value.milliseconds === 500).callback();
    await waitFor(responsePath);
    const response = JSON.parse(fs.readFileSync(responsePath, "utf8"));

    assert.equal(response.applied, true);
    assert.equal(fs.readFileSync(path.join(archiveRoot, "modlist.txt"), "utf8"), "Beta.archive\r\nAlpha.archive\r\n");
    assert.equal(dispatched.some((action) => action.type === "SET_FB_LOAD_ORDER"), false);
    state.persistent.profiles.profile = { ...state.persistent.profiles.profile, id: "other-profile", name: "Other" };
    await callbacks["did-deploy"]("profile", { "r6\\scripts\\shared.reds": { relPath: "r6\\scripts\\shared.reds", source: "Beta-install" } });
    assert.equal(JSON.parse(fs.readFileSync(contextPath, "utf8")).profileName, "Standard");
    state.persistent.profiles.profile = { ...state.persistent.profiles.profile, id: "profile", name: "Changed" };
    await Promise.all([
      intervals.find((value) => value.milliseconds === 5000).callback(),
      callbacks["did-deploy"]("profile", { "r6\\scripts\\shared.reds": { relPath: "r6\\scripts\\shared.reds", source: "Beta-install" } }),
    ]);
    assert.equal(maximumConcurrentSorts, 1);
    assert.equal(JSON.parse(fs.readFileSync(contextPath, "utf8")).profileName, "Changed");
  } finally {
    Module._load = originalLoad;
    global.setInterval = originalSetInterval;
    process.env.LOCALAPPDATA = originalLocalAppData;
    fs.rmSync(root, { recursive: true, force: true });
  }
});

test("failed Vortex manifest export marks the context deployment unresolved", async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "conflict-studio-vortex-manifest-"));
  const originalLocalAppData = process.env.LOCALAPPDATA;
  const originalLoad = Module._load;
  try {
    const gameRoot = path.join(root, "game");
    const stagingRoot = path.join(root, "staging");
    fs.mkdirSync(gameRoot, { recursive: true });
    fs.mkdirSync(stagingRoot, { recursive: true });
    process.env.LOCALAPPDATA = root;
    const profile = { id: "profile", name: "Standard", gameId: "cyberpunk2077", modState: {} };
    const state = { settings: { gameMode: { discovered: { cyberpunk2077: { path: gameRoot } } } }, persistent: { profiles: { profile }, mods: { cyberpunk2077: {} }, deployment: { needToDeploy: { cyberpunk2077: false } } } };
    Module._load = function load(request, parent, isMain) {
      if (request === "vortex-api") return { actions: { addDiscoveredTool: () => ({ type: "ADD_DISCOVERED_TOOL" }) }, selectors: { activeProfile: () => profile, installPathForGame: () => stagingRoot, modPathsForGame: () => ({ "": gameRoot }) }, util: { sortMods: async () => [], renderModName: () => "", getManifest: async () => { throw new Error("manifest unavailable"); }, getVortexPath: () => path.join(root, "vortex-user") } };
      return originalLoad.call(this, request, parent, isMain);
    };
    delete require.cache[require.resolve("./index")];
    const init = require("./index");
    let once;
    init({ once: (callback) => { once = callback; }, api: { getState: () => state, store: { dispatch: () => undefined }, events: { on: () => undefined }, onAsync: () => undefined, showErrorNotification: (title, error) => { throw error; } } });

    once();
    const contextPath = path.join(root, "Cyberpunk Conflict Studio", "vortex", "context.json");
    await waitFor(contextPath);

    assert.equal(JSON.parse(fs.readFileSync(contextPath, "utf8")).deploymentFresh, false);
  } finally {
    Module._load = originalLoad;
    process.env.LOCALAPPDATA = originalLocalAppData;
    fs.rmSync(root, { recursive: true, force: true });
  }
});

function fingerprint(root, name) {
  const file = fs.readFileSync(path.join(root, name));
  return { name, size: file.length, sha256: require("node:crypto").createHash("sha256").update(file).digest("hex") };
}

async function waitFor(file) {
  for (let attempt = 0; attempt < 40; attempt++) {
    if (fs.existsSync(file)) return;
    await new Promise((resolve) => setTimeout(resolve, 25));
  }
  throw new Error("The Vortex bridge did not export context.json.");
}
