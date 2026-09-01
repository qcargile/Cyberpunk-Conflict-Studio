const crypto = require("node:crypto");
const fs = require("node:fs");
const path = require("node:path");

function createContext(input) {
  const identity = {
    profileId: input.profileId,
    profileName: input.profileName,
    gameRoot: path.resolve(input.gameRoot),
    stagingRoot: path.resolve(input.stagingRoot),
    deploymentFresh: input.deploymentFresh,
    providers: [...input.providers].sort((left, right) => left.order - right.order),
    deployedWinners: sortObject(input.deployedWinners),
    archiveOrder: [...input.archiveOrder],
    archiveOrderSha256: input.archiveOrderSha256,
    deploymentInventoryComplete: input.deploymentInventoryComplete === true,
    deploymentFileCount: input.deploymentFileCount || 0,
    relevantDeploymentFileCount: input.relevantDeploymentFileCount || 0,
    unmappedRelevantFileCount: input.unmappedRelevantFileCount || 0,
    targetRelocatedFileCount: input.targetRelocatedFileCount || 0,
  };
  return {
    schemaVersion: 1,
    contextId: sha(Buffer.from(JSON.stringify(identity))),
    capturedAtUtc: input.capturedAtUtc,
    heartbeatAtUtc: input.heartbeatAtUtc || input.capturedAtUtc,
    ...identity,
    bridgeRefreshMilliseconds: input.bridgeRefreshMilliseconds || 0,
  };
}

function applyOrderRequest(request, context, now = new Date(), guards = {}) {
  requireRequest(request, context, now);
  const gameRunning = guards.gameRunning || (() => false);
  const currentTime = guards.now || (() => new Date());
  if (gameRunning()) throw new Error("Archive order cannot be written while Cyberpunk2077 is running.");
  const archiveRoot = path.join(context.gameRoot, "archive", "pc", "mod");
  const orderPath = path.join(archiveRoot, "modlist.txt");
  const inventory = archiveInventory(archiveRoot);
  if (!sameInventory(inventory, request.inventory)) throw new Error("The deployed archive inventory changed after Conflict Studio previewed it.");
  if (request.restorePrevious !== true) {
    const expectedNames = new Set(inventory.map((entry) => entry.name.toLowerCase()));
    const proposedNames = new Set(request.proposedOrder.map((entry) => entry.toLowerCase()));
    if (expectedNames.size !== request.proposedOrder.length || proposedNames.size !== request.proposedOrder.length || [...expectedNames].some((name) => !proposedNames.has(name))) throw new Error("The proposed archive order does not contain every deployed archive exactly once.");
  }
  const current = fs.existsSync(orderPath) ? fs.readFileSync(orderPath) : null;
  const currentSha = current === null ? null : sha(current);
  if (currentSha !== request.expectedOrderSha256) throw new Error("The deployed archive order changed after Conflict Studio previewed it.");
  fs.mkdirSync(archiveRoot, { recursive: true });
  const output = request.restorePrevious === true ? restoreBytes(request.restoreBackupPath, orderPath) : mergeOrder(current, request.proposedOrder);
  const backupPath = current === null ? null : `${orderPath}.${request.requestId}.bak`;
  if (backupPath !== null) {
    fs.writeFileSync(backupPath, current);
    const verifiedBackup = fs.readFileSync(backupPath);
    if (!verifiedBackup.equals(current)) {
      fs.unlinkSync(backupPath);
      throw new Error("The Vortex archive order backup did not verify after writing.");
    }
  }
  const temporary = `${orderPath}.${request.requestId}.tmp`;
  let replaced = false;
  try {
    if (output === null) {
      requireWriteAuthorized(request, gameRunning, currentTime);
      if (fs.existsSync(orderPath)) fs.unlinkSync(orderPath);
      replaced = true;
    } else {
      const handle = fs.openSync(temporary, "wx");
      try {
        fs.writeFileSync(handle, output);
        fs.fsyncSync(handle);
      } finally {
        fs.closeSync(handle);
      }
      requireWriteAuthorized(request, gameRunning, currentTime);
      fs.renameSync(temporary, orderPath);
      replaced = true;
    }
    const verified = fs.existsSync(orderPath) ? fs.readFileSync(orderPath) : null;
    if (output === null ? verified !== null : verified === null || !verified.equals(output)) throw new Error("The Vortex archive order did not verify after writing.");
    return {
      schemaVersion: 1,
      requestId: request.requestId,
      applied: true,
      message: request.restorePrevious === true ? "Vortex restored and verified the previous archive order." : "Vortex applied and verified the archive order.",
      backupPath,
      writtenSha256: verified === null ? null : sha(verified),
      completedAtUtc: new Date().toISOString(),
    };
  } catch (error) {
    if (!replaced) {
      if (backupPath !== null && fs.existsSync(backupPath)) fs.unlinkSync(backupPath);
      throw error;
    }
    const afterFailure = fs.existsSync(orderPath) ? fs.readFileSync(orderPath) : null;
    if (output === null ? afterFailure !== null : afterFailure === null || !afterFailure.equals(output)) throw new Error(`The Vortex archive order changed during verification; automatic rollback was not attempted: ${error instanceof Error ? error.message : String(error)}`);
    if (current === null)
    {
      if (fs.existsSync(orderPath)) fs.unlinkSync(orderPath);
    }
    else
    {
      fs.writeFileSync(orderPath, current);
    }
    const restored = fs.existsSync(orderPath) ? fs.readFileSync(orderPath) : null;
    if (current === null ? restored !== null : restored === null || !restored.equals(current)) throw new Error(`The Vortex archive order rollback did not verify: ${error instanceof Error ? error.message : String(error)}`);
    throw error;
  } finally {
    if (fs.existsSync(temporary)) fs.unlinkSync(temporary);
  }
}

function restoreBytes(backupPath, orderPath) {
  if (backupPath === null || backupPath === undefined) return null;
  const resolvedBackup = path.resolve(backupPath);
  const prefix = `${path.resolve(orderPath)}.`;
  const token = resolvedBackup.slice(prefix.length, -4);
  if (!resolvedBackup.toLowerCase().startsWith(prefix.toLowerCase()) || !resolvedBackup.toLowerCase().endsWith(".bak") || !/^[0-9a-f]{32}$/.test(token)) throw new Error("The previous Vortex archive order backup is invalid.");
  if (!fs.existsSync(resolvedBackup)) throw new Error("The previous Vortex archive order backup is missing.");
  return fs.readFileSync(resolvedBackup);
}

function rollbackOrder(response, context) {
  if (!response || response.applied !== true) return;
  const orderPath = path.join(context.gameRoot, "archive", "pc", "mod", "modlist.txt");
  const current = fs.existsSync(orderPath) ? fs.readFileSync(orderPath) : null;
  const currentSha = current === null ? null : sha(current);
  if (currentSha !== response.writtenSha256) throw new Error("The Vortex archive order changed before rollback; the concurrent edit was preserved.");
  const restoredBytes = restoreBytes(response.backupPath, orderPath);
  if (response.backupPath === null)
  {
    if (fs.existsSync(orderPath)) fs.unlinkSync(orderPath);
  }
  else
  {
    fs.copyFileSync(response.backupPath, orderPath);
  }
  const restored = fs.existsSync(orderPath) ? fs.readFileSync(orderPath) : null;
  if (restoredBytes === null ? restored !== null : restored === null || !restored.equals(restoredBytes)) throw new Error("The Vortex archive order rollback did not verify.");
}

async function completeOrder(response, context, refresh, publish) {
  try {
    const updated = await refresh();
    response.contextId = updated && updated.contextId;
    publish(response);
    return response;
  } catch (error) {
    rollbackOrder(response, context);
    try { await refresh(); }
    catch {}
    throw error;
  }
}

function requireRequest(request, context, now) {
  if (request.schemaVersion !== 1 || !/^[0-9a-f]{32}$/.test(request.requestId) || request.contextId !== context.contextId || request.profileId !== context.profileId) throw new Error("The archive order request does not belong to the active Vortex profile context.");
  if (!context.deploymentFresh) throw new Error("Deploy the active Vortex profile before applying an archive order.");
  if (!Array.isArray(request.inventory) || !Array.isArray(request.proposedOrder)) throw new Error("The archive order request is incomplete.");
  const requestedAt = new Date(request.requestedAtUtc);
  const expiresAt = new Date(request.expiresAtUtc);
  const age = now.getTime() - requestedAt.getTime();
  if (!Number.isFinite(requestedAt.getTime()) || !Number.isFinite(expiresAt.getTime()) || expiresAt.getTime() - requestedAt.getTime() > 60000 || age < -5000) throw new Error("The archive order request expired. Preview the order again.");
  requireRequestTime(request, now);
}

function requireRequestTime(request, now) {
  if (now.getTime() > new Date(request.expiresAtUtc).getTime()) throw new Error("The archive order request expired. Preview the order again.");
}

function requireWriteAuthorized(request, gameRunning, currentTime) {
  if (gameRunning()) throw new Error("Archive order cannot be written while Cyberpunk2077 is running.");
  requireRequestTime(request, currentTime());
}

function archiveInventory(archiveRoot) {
  return archiveNames(archiveRoot)
    .map((name) => ({ name, ...fingerprintFile(path.join(archiveRoot, name)) }));
}

function archiveNames(archiveRoot) {
  if (!fs.existsSync(archiveRoot)) return [];
  return fs.readdirSync(archiveRoot, { withFileTypes: true })
    .filter((entry) => entry.isFile() && entry.name.toLowerCase().endsWith(".archive"))
    .map((entry) => entry.name)
    .sort((left, right) => left.localeCompare(right));
}

async function selectDeploymentWinners(manifests, modPaths, gameRoot, mods, yieldEvery = 2000, yieldNow = () => new Promise((resolve) => setImmediate(resolve))) {
  const providerIds = new Map();
  for (const mod of Object.values(mods)) {
    if (mod.id) providerIds.set(String(mod.id).toLowerCase(), mod.id);
    if (mod.installationPath) providerIds.set(String(mod.installationPath).toLowerCase(), mod.id);
  }
  const winners = {};
  let deploymentFileCount = 0;
  let relevantDeploymentFileCount = 0;
  let unmappedRelevantFileCount = 0;
  let targetRelocatedFileCount = 0;
  for (const [typeId, files] of Object.entries(manifests || {})) {
    const typeTarget = modPaths[typeId] || gameRoot;
    for (const file of files || []) {
      deploymentFileCount++;
      if (deploymentFileCount % yieldEvery === 0) await yieldNow();
      const target = file.target ? path.join(typeTarget, file.target) : typeTarget;
      const prefix = path.relative(gameRoot, target);
      const relative = path.join(prefix, file.relPath).replaceAll("/", "\\").replace(/^\\+/, "");
      if (!isRelevantDeploymentPath(relative)) continue;
      relevantDeploymentFileCount++;
      if (file.target) {
        unmappedRelevantFileCount++;
        targetRelocatedFileCount++;
        continue;
      }
      const providerId = providerIds.get(String(file.source || "").toLowerCase());
      if (!providerId) {
        unmappedRelevantFileCount++;
        continue;
      }
      winners[relative] = providerId;
    }
  }
  return { winners, deploymentFileCount, relevantDeploymentFileCount, unmappedRelevantFileCount, targetRelocatedFileCount };
}

function isRelevantDeploymentPath(relativePath) {
  const normalized = relativePath.replaceAll("/", "\\").replace(/^\\+/, "").toLowerCase();
  return normalized.endsWith(".xl")
    || normalized.startsWith("archive\\pc\\mod\\")
    || normalized.startsWith("bin\\x64\\plugins\\")
    || normalized.startsWith("engine\\config\\")
    || normalized.startsWith("mods\\")
    || normalized.startsWith("r6\\input\\")
    || normalized.startsWith("r6\\scripts\\")
    || normalized.startsWith("r6\\tweaks\\")
    || normalized.startsWith("red4ext\\plugins\\")
    || normalized === "r6\\cache\\modded\\mo_redmod_load_order.txt";
}

function fingerprintFile(filePath, fileSystem = fs) {
  const handle = fileSystem.openSync(filePath, "r");
  try {
    const size = fileSystem.fstatSync(handle).size;
    const hash = crypto.createHash("sha256");
    const buffer = Buffer.allocUnsafe(1024 * 1024);
    for (;;) {
      const bytesRead = fileSystem.readSync(handle, buffer, 0, buffer.length, null);
      if (bytesRead === 0) break;
      hash.update(buffer.subarray(0, bytesRead));
    }
    return { size, sha256: hash.digest("hex") };
  } finally {
    fileSystem.closeSync(handle);
  }
}

function shaFile(filePath, fileSystem = fs) {
  return fingerprintFile(filePath, fileSystem).sha256;
}

function sameInventory(left, right) {
  if (left.length !== right.length) return false;
  const values = new Map(right.map((entry) => [entry.name.toLowerCase(), entry]));
  return left.every((entry) => {
    const other = values.get(entry.name.toLowerCase());
    return other !== undefined && other.size === entry.size && other.sha256 === entry.sha256;
  });
}

function mergeOrder(existing, proposedOrder) {
  if (existing === null) return Buffer.from(`${proposedOrder.join("\r\n")}\r\n`, "utf8");
  const archives = [...proposedOrder];
  let index = 0;
  const lines = [];
  for (const entry of existing.toString("utf8").split(/\r?\n/).map((value) => value.trim()).filter(Boolean)) {
    if (entry.toLowerCase().endsWith(".archive")) {
      if (index < archives.length) lines.push(archives[index++]);
    } else {
      lines.push(entry);
    }
  }
  while (index < archives.length) lines.push(archives[index++]);
  return Buffer.from(`${lines.join("\r\n")}\r\n`, "utf8");
}

function sortObject(value) {
  return Object.fromEntries(Object.entries(value).sort(([left], [right]) => left.localeCompare(right)));
}

function sha(value) {
  return crypto.createHash("sha256").update(value).digest("hex");
}

module.exports = { applyOrderRequest, archiveInventory, archiveNames, completeOrder, createContext, isRelevantDeploymentPath, rollbackOrder, selectDeploymentWinners, sha, shaFile };
