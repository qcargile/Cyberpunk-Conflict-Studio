const fs = require("node:fs");
const path = require("node:path");
const { execFileSync } = require("node:child_process");
const vortexApi = require("vortex-api");
const { actions, selectors, util } = vortexApi;
const bridge = require("./bridge");

const gameId = "cyberpunk2077";

function init(context) {
  const bridgeRoot = path.join(process.env.LOCALAPPDATA, "Cyberpunk Conflict Studio", "vortex");
  const contextPath = path.join(bridgeRoot, "context.json");
  const heartbeatPath = path.join(bridgeRoot, "heartbeat.json");
  const contextRequestPath = path.join(bridgeRoot, "context-request.json");
  const contextResponsePath = path.join(bridgeRoot, "context-response.json");
  const orderRequestPath = path.join(bridgeRoot, "order-request.json");
  const orderResponsePath = path.join(bridgeRoot, "order-response.json");
  const applicationRoot = path.join(__dirname, "Conflict Studio");
  const applicationPath = path.join(applicationRoot, "ConflictStudio.exe");
  const bridgeLog = typeof vortexApi.log === "function" ? vortexApi.log : () => undefined;
  let queue = Promise.resolve();
  let requestDrain;
  let requestsPending = false;
  let lastContextRequestId;
  let lastOrderRequestId;
  let stateRevision = 0;

  function enqueue(operation) {
    const next = queue.then(operation, operation);
    queue = next.catch(() => undefined);
    return next;
  }

  function registerTool() {
    const discovered = context.api.getState().settings.gameMode.discovered[gameId];
    if (!discovered || !discovered.path) return false;
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
    context.api.store.dispatch(actions.setToolVisible(gameId, "conflict-studio", true));
    bridgeLog("info", "Conflict Studio tool registered", { path: applicationPath });
    return true;
  }

  async function refresh(reason) {
    const started = Date.now();
    const capturedRevision = stateRevision;
    const state = context.api.getState();
    const profile = selectors.activeProfile(state);
    if (!profile || profile.gameId !== gameId) throw new Error("Activate a Cyberpunk 2077 Vortex profile first.");
    const discovery = state.settings.gameMode.discovered[gameId];
    const gameRoot = discovery && discovery.path;
    const stagingRoot = selectors.installPathForGame(state, gameId);
    if (!gameRoot || !stagingRoot) throw new Error("Vortex has not discovered the Cyberpunk game or staging folder.");
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
    const providers = rawProviders.map((provider) => nameCounts.get(provider.name.toLowerCase()) > 1 ? { ...provider, name: `${provider.name} [${provider.id}]` } : provider);
    const deploymentResult = await winnerMap(context.api, state, gameRoot, mods);
    const archiveRoot = path.join(gameRoot, "archive", "pc", "mod");
    const orderPath = path.join(archiveRoot, "modlist.txt");
    const archiveOrder = fs.existsSync(orderPath) ? fs.readFileSync(orderPath, "utf8").split(/\r?\n/).map((entry) => entry.trim()).filter(Boolean) : bridge.archiveNames(archiveRoot);
    const archiveOrderSha256 = fs.existsSync(orderPath) ? bridge.sha(fs.readFileSync(orderPath)) : null;
    const stateFresh = !Boolean(state.persistent.deployment.needToDeploy[gameId]);
    const deploymentFresh = stateFresh && deploymentResult.complete;
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
      deploymentInventoryComplete: deploymentResult.inventoryComplete,
      deploymentFileCount: deploymentResult.deploymentFileCount,
      relevantDeploymentFileCount: deploymentResult.relevantDeploymentFileCount,
      unmappedRelevantFileCount: deploymentResult.unmappedRelevantFileCount,
      targetRelocatedFileCount: deploymentResult.targetRelocatedFileCount,
      bridgeRefreshMilliseconds: Date.now() - started,
    });
    requireStableRevision(capturedRevision);
    writeJson(contextPath, current);
    requireStableRevision(capturedRevision);
    writeJson(heartbeatPath, { schemaVersion: 1, contextId: current.contextId, profileId: current.profileId, heartbeatAtUtc: new Date().toISOString() });
    if (stateRevision !== capturedRevision) {
      invalidateHeartbeat();
      throw new Error("The active Vortex profile changed during export. Run the scan again.");
    }
    bridgeLog("info", "Conflict Studio bridge refresh", { reason, durationMs: Date.now() - started, providers: providers.length, deploymentFiles: deploymentResult.deploymentFileCount, relevantFiles: deploymentResult.relevantDeploymentFileCount, winners: Object.keys(deploymentResult.winners).length, unmappedRelevantFiles: deploymentResult.unmappedRelevantFileCount, targetRelocatedFiles: deploymentResult.targetRelocatedFileCount, complete: deploymentResult.complete });
    return current;
  }

  async function processContextRequest() {
    const request = readJson(contextRequestPath);
    if (!request || request.requestId === lastContextRequestId) return;
    lastContextRequestId = request.requestId;
    let response;
    try {
      validateContextRequest(request);
      const current = await refresh("context request");
      response = {
        schemaVersion: 1,
        requestId: request.requestId,
        refreshed: true,
        message: "Vortex profile refreshed.",
        contextId: current.contextId,
        completedAtUtc: new Date().toISOString(),
      };
    } catch (error) {
      response = {
        schemaVersion: 1,
        requestId: request.requestId,
        refreshed: false,
        message: error instanceof Error ? error.message : String(error),
        contextId: null,
        completedAtUtc: new Date().toISOString(),
      };
    }
    writeJson(contextResponsePath, response);
    deleteMatchingRequest(contextRequestPath, request.requestId);
  }

  async function processOrderRequest() {
    const request = readJson(orderRequestPath);
    if (!request || request.requestId === lastOrderRequestId) return;
    lastOrderRequestId = request.requestId;
    let response;
    try {
      const current = await refresh("order request");
      const expectedRevision = stateRevision;
      response = await bridge.applyOrderRequest(request, current, new Date(), { gameRunning: cyberpunkRunning, currentContext: () => readJson(contextPath), stateRevision: () => stateRevision, expectedRevision });
      response = await bridge.completeOrder(response, current, () => refresh("order completion"), (value) => writeJson(orderResponsePath, value));
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
    if (!response.applied) writeJson(orderResponsePath, response);
    deleteMatchingRequest(orderRequestPath, request.requestId);
  }

  function scheduleRequests() {
    requestsPending = true;
    if (requestDrain) return requestDrain;
    requestDrain = enqueue(async () => {
      while (requestsPending) {
        requestsPending = false;
        await processContextRequest();
        await processOrderRequest();
      }
    }).finally(() => {
      requestDrain = undefined;
      if (requestsPending) scheduleRequests().catch(reportRequestFailure);
    });
    return requestDrain;
  }

  function watchRequests() {
    fs.mkdirSync(bridgeRoot, { recursive: true });
    const watcher = fs.watch(bridgeRoot, () => scheduleRequests().catch(reportRequestFailure));
    watcher.on("error", (error) => context.api.showErrorNotification("Conflict Studio bridge request listener failed", error, { allowReport: false }));
    bridgeLog("info", "Conflict Studio bridge request listener ready", { path: bridgeRoot });
    if (fs.existsSync(contextRequestPath) || fs.existsSync(orderRequestPath)) scheduleRequests().catch(reportRequestFailure);
  }

  function reportRequestFailure(error) {
    context.api.showErrorNotification("Conflict Studio bridge request failed", error, { allowReport: false });
  }

  function invalidateState() {
    stateRevision++;
    try { invalidateHeartbeat(); }
    catch (error) { bridgeLog("warn", "Conflict Studio bridge invalidation could not be saved", { message: error instanceof Error ? error.message : String(error) }); }
  }

  function invalidateHeartbeat() {
    if (!fs.existsSync(contextPath) && !fs.existsSync(heartbeatPath)) return;
    const active = selectors.activeProfile(context.api.getState());
    writeJson(heartbeatPath, { schemaVersion: 1, contextId: "0".repeat(64), profileId: active && active.id ? active.id : "inactive", heartbeatAtUtc: new Date().toISOString() });
  }

  function recordPurge(profileId) {
    const current = readJson(contextPath);
    if (!current || current.profileId !== profileId) return;
    invalidateState();
    const capturedAtUtc = new Date().toISOString();
    const purged = bridge.createContext({
      ...current,
      capturedAtUtc,
      heartbeatAtUtc: capturedAtUtc,
      deploymentFresh: false,
      deployedWinners: {},
      deploymentInventoryComplete: true,
      deploymentFileCount: 0,
      relevantDeploymentFileCount: 0,
      unmappedRelevantFileCount: 0,
      targetRelocatedFileCount: 0,
      bridgeRefreshMilliseconds: 0,
    });
    try {
      writeJson(contextPath, purged);
      writeJson(heartbeatPath, { schemaVersion: 1, contextId: purged.contextId, profileId: purged.profileId, heartbeatAtUtc: capturedAtUtc });
    } catch (error) {
      bridgeLog("warn", "Conflict Studio purge state could not be saved", { message: error instanceof Error ? error.message : String(error) });
    }
  }

  function requireStableRevision(expected) {
    if (stateRevision !== expected) throw new Error("The active Vortex profile changed during export. Run the scan again.");
  }

  context.once(() => {
    registerTool();
    watchRequests();
    context.api.events.on("profile-did-change", () => {
      invalidateState();
      registerTool();
    });
    context.api.events.on("gamemode-activated", (activeGameId) => { if (activeGameId === gameId) registerTool(); });
    context.api.onAsync("discover-tools", (discoveryGameId) => {
      if (discoveryGameId === gameId) registerTool();
      return Promise.resolve();
    });
    context.api.onAsync("did-deploy", (profileId) => {
      const active = selectors.activeProfile(context.api.getState());
      if (active && active.id === profileId) invalidateState();
      return Promise.resolve();
    });
    context.api.onAsync("did-purge", (profileId) => {
      recordPurge(profileId);
      return Promise.resolve();
    });
  });
  context.api.onStateChange?.(["settings", "gameMode", "discovered", gameId], () => registerTool());
  return true;
}

async function winnerMap(api, state, gameRoot, mods) {
  const modPaths = selectors.modPathsForGame(state, gameId) || {};
  const manifests = {};
  let complete = true;
  await Promise.all(Object.keys(modPaths).map(async (typeId) => {
    try { manifests[typeId] = (await util.getManifest(api, typeId, gameId)).files; }
    catch { manifests[typeId] = []; complete = false; }
  }));
  const selection = await bridge.selectDeploymentWinners(manifests, modPaths, gameRoot, mods);
  return { ...selection, inventoryComplete: complete, complete: complete && selection.unmappedRelevantFileCount === 0 };
}

function validateContextRequest(request) {
  if (request.schemaVersion !== 1 || !/^[0-9a-f]{32}$/.test(request.requestId || "")) throw new Error("The Conflict Studio context request is invalid.");
  const requestedAt = new Date(request.requestedAtUtc);
  const expiresAt = new Date(request.expiresAtUtc);
  const now = new Date();
  if (!Number.isFinite(requestedAt.getTime()) || !Number.isFinite(expiresAt.getTime()) || expiresAt <= requestedAt || expiresAt - requestedAt > 5 * 60 * 1000 || requestedAt - now > 30000) throw new Error("The Conflict Studio context request is invalid.");
  if (now > expiresAt) throw new Error("The Conflict Studio context request expired.");
}

function readJson(target) {
  if (!fs.existsSync(target)) return null;
  try { return JSON.parse(fs.readFileSync(target, "utf8")); }
  catch { return null; }
}

function deleteMatchingRequest(target, requestId) {
  const pending = readJson(target);
  if (pending && pending.requestId === requestId) {
    try { fs.unlinkSync(target); }
    catch {}
  }
}

function writeJson(target, value) {
  fs.mkdirSync(path.dirname(target), { recursive: true });
  const temporary = `${target}.${process.pid}.tmp`;
  fs.writeFileSync(temporary, JSON.stringify(value, null, 2));
  fs.renameSync(temporary, target);
}

function cyberpunkRunning() {
  try {
    const output = execFileSync("tasklist.exe", ["/FI", "IMAGENAME eq Cyberpunk2077.exe", "/FO", "CSV", "/NH"], { encoding: "utf8", windowsHide: true });
    return /Cyberpunk2077\.exe/i.test(output);
  } catch {
    throw new Error("Conflict Studio could not verify whether Cyberpunk2077 is running.");
  }
}

module.exports = init;
