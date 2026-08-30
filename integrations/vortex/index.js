const fs = require("node:fs");
const path = require("node:path");
const { actions, selectors, util } = require("vortex-api");
const bridge = require("./bridge");

const gameId = "cyberpunk2077";

function init(context) {
  const bridgeRoot = path.join(process.env.LOCALAPPDATA, "Cyberpunk Conflict Studio", "vortex");
  const contextPath = path.join(bridgeRoot, "context.json");
  const requestPath = path.join(bridgeRoot, "order-request.json");
  const responsePath = path.join(bridgeRoot, "order-response.json");
  let lastRequestId;
  let contextQueue = Promise.resolve();

  function enqueue(operation) {
    const next = contextQueue.then(operation, operation);
    contextQueue = next.catch(() => undefined);
    return next;
  }

  function registerTool() {
    const applicationRoot = path.join(__dirname, "Conflict Studio");
    const applicationPath = path.join(applicationRoot, "ConflictStudio.exe");
    const iconDirectory = path.join(util.getVortexPath("userData"), gameId, "icons");
    try {
      fs.mkdirSync(iconDirectory, { recursive: true });
      fs.copyFileSync(path.join(__dirname, "ConflictStudio.png"), path.join(iconDirectory, "conflict-studio.png"));
    } catch {}
    context.api.store.dispatch(actions.addDiscoveredTool(gameId, "conflict-studio", {
      id: "conflict-studio",
      name: "Conflict Studio",
      path: applicationPath,
      workingDirectory: applicationRoot,
      parameters: ["--manager", "vortex"],
      hidden: false,
      custom: false,
      exclusive: false,
      detach: true,
    }, false));
  }

  async function refresh(deployment, forcedFreshness) {
    const state = context.api.getState();
    const profile = selectors.activeProfile(state);
    if (!profile || profile.gameId !== gameId) return null;
    const discovery = state.settings.gameMode.discovered[gameId];
    const gameRoot = discovery && discovery.path;
    const stagingRoot = selectors.installPathForGame(state, gameId);
    if (!gameRoot || !stagingRoot) return null;
    const mods = state.persistent.mods[gameId] || {};
    const enabled = Object.values(mods).filter((mod) => profile.modState && profile.modState[mod.id] && profile.modState[mod.id].enabled === true && mod.installationPath);
    const sorted = await util.sortMods(gameId, enabled, context.api);
    const rawProviders = sorted.slice().reverse().map((mod, order) => ({
      id: mod.id,
      name: util.renderModName(mod),
      rootPath: path.join(stagingRoot, mod.installationPath),
      order,
    })).filter((provider) => fs.existsSync(provider.rootPath));
    const nameCounts = rawProviders.reduce((counts, provider) => counts.set(provider.name.toLowerCase(), (counts.get(provider.name.toLowerCase()) || 0) + 1), new Map());
    const providers = rawProviders.map((provider) => nameCounts.get(provider.name.toLowerCase()) > 1 ? { ...provider, name: `${provider.name} [${provider.id.slice(0, 8)}]` } : provider);
    const deploymentResult = await winnerMap(context.api, state, gameRoot, mods, deployment);
    const archiveRoot = path.join(gameRoot, "archive", "pc", "mod");
    const orderPath = path.join(archiveRoot, "modlist.txt");
    const archiveOrder = fs.existsSync(orderPath) ? fs.readFileSync(orderPath, "utf8").split(/\r?\n/).map((entry) => entry.trim()).filter(Boolean) : bridge.archiveInventory(archiveRoot).map((entry) => entry.name).sort();
    const archiveOrderSha256 = fs.existsSync(orderPath) ? bridge.sha(fs.readFileSync(orderPath)) : null;
    const deploymentFresh = (forcedFreshness === undefined ? !Boolean(state.persistent.deployment.needToDeploy[gameId]) : forcedFreshness) && deploymentResult.complete;
    const current = bridge.createContext({
      capturedAtUtc: new Date().toISOString(),
      profileId: profile.id,
      profileName: profile.name,
      gameRoot,
      stagingRoot,
      deploymentFresh,
      providers,
      deployedWinners: deploymentResult.winners,
      archiveOrder,
      archiveOrderSha256,
    });
    writeJson(contextPath, current);
    return current;
  }

  async function processRequest() {
    if (!fs.existsSync(requestPath)) return;
    let request;
    try { request = JSON.parse(fs.readFileSync(requestPath, "utf8")); }
    catch { return; }
    if (!request || request.requestId === lastRequestId) return;
    lastRequestId = request.requestId;
    let response;
    try {
      const current = await refresh();
      if (!current) throw new Error("Activate the Cyberpunk 2077 Vortex profile before applying an archive order.");
      response = bridge.applyOrderRequest(request, current);
      response = await bridge.completeOrder(response, current, refresh, (value) => writeJson(responsePath, value));
    } catch (error) {
      response = {
        schemaVersion: 1,
        requestId: request.requestId,
        applied: false,
        message: error instanceof Error ? error.message : String(error),
        backupPath: null,
        writtenSha256: null,
        completedAtUtc: new Date().toISOString(),
        contextId: null,
      };
    }
    if (!response.applied) writeJson(responsePath, response);
    try { fs.unlinkSync(requestPath); }
    catch {}
  }

  async function heartbeat() {
    if (!fs.existsSync(contextPath)) return refresh();
    let current;
    try { current = JSON.parse(fs.readFileSync(contextPath, "utf8")); }
    catch { return refresh(); }
    const state = context.api.getState();
    const profile = selectors.activeProfile(state);
    const discovery = state.settings.gameMode.discovered[gameId];
    const gameRoot = discovery && discovery.path;
    const stagingRoot = selectors.installPathForGame(state, gameId);
    if (!profile || profile.gameId !== gameId || !gameRoot || !stagingRoot) return null;
    const mods = state.persistent.mods[gameId] || {};
    const enabledIds = Object.values(mods).filter((mod) => profile.modState && profile.modState[mod.id] && profile.modState[mod.id].enabled === true && mod.installationPath && fs.existsSync(path.join(stagingRoot, mod.installationPath))).map((mod) => mod.id).sort();
    const currentIds = (current.providers || []).map((provider) => provider.id).sort();
    const identityMatches = current.profileId === profile.id
      && current.profileName === profile.name
      && path.resolve(current.gameRoot) === path.resolve(gameRoot)
      && path.resolve(current.stagingRoot) === path.resolve(stagingRoot)
      && JSON.stringify(currentIds) === JSON.stringify(enabledIds);
    if (!identityMatches) return refresh();
    const deploymentPending = Boolean(state.persistent.deployment.needToDeploy[gameId]);
    if (!deploymentPending && !current.deploymentFresh) return refresh();
    const touched = bridge.createContext({ ...current, capturedAtUtc: new Date().toISOString(), deploymentFresh: current.deploymentFresh && !deploymentPending });
    writeJson(contextPath, touched);
    return touched;
  }

  context.once(() => {
    registerTool();
    enqueue(() => refresh()).catch((error) => context.api.showErrorNotification("Conflict Studio bridge could not export the active profile", error, { allowReport: false }));
    const timer = setInterval(() => enqueue(() => processRequest()).catch((error) => context.api.showErrorNotification("Conflict Studio bridge request failed", error, { allowReport: false })), 500);
    const heartbeatTimer = setInterval(() => enqueue(() => heartbeat()).catch((error) => context.api.showErrorNotification("Conflict Studio bridge could not refresh the active profile", error, { allowReport: false })), 5000);
    if (timer.unref) timer.unref();
    if (heartbeatTimer.unref) heartbeatTimer.unref();
    context.api.events.on("profile-did-change", () => enqueue(() => refresh()).catch(() => undefined));
    context.api.onAsync("did-deploy", (profileId) => {
      const active = selectors.activeProfile(context.api.getState());
      return active && active.id === profileId ? enqueue(() => refresh(undefined, true)).then(() => undefined) : Promise.resolve();
    });
    context.api.onAsync("did-purge", () => enqueue(() => refresh(undefined, false)).then(() => undefined));
  });
  return true;
}

async function winnerMap(api, state, gameRoot, mods, deployment) {
  const installationToId = new Map(Object.values(mods).filter((mod) => mod.installationPath).map((mod) => [mod.installationPath, mod.id]));
  const modPaths = selectors.modPathsForGame(state, gameId) || {};
  let manifests = deployment;
  let complete = true;
  if (!manifests) {
    manifests = {};
    await Promise.all(Object.keys(modPaths).map(async (typeId) => {
      try { manifests[typeId] = (await util.getManifest(api, typeId, gameId)).files; }
      catch { manifests[typeId] = []; complete = false; }
    }));
  }
  const winners = {};
  for (const [typeId, files] of Object.entries(manifests)) {
    const target = modPaths[typeId] || gameRoot;
    const prefix = path.relative(gameRoot, target);
    for (const file of files || []) {
      const providerId = installationToId.get(file.source) || file.source;
      if (!mods[providerId]) continue;
      const relative = path.join(prefix, file.relPath).replaceAll("/", "\\").replace(/^\\+/, "");
      winners[relative] = providerId;
    }
  }
  return { winners, complete };
}

function writeJson(target, value) {
  fs.mkdirSync(path.dirname(target), { recursive: true });
  const temporary = `${target}.${process.pid}.tmp`;
  fs.writeFileSync(temporary, JSON.stringify(value, null, 2));
  fs.renameSync(temporary, target);
}

module.exports = init;
