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
  };
  return {
    schemaVersion: 1,
    contextId: sha(Buffer.from(JSON.stringify(identity))),
    capturedAtUtc: input.capturedAtUtc,
    ...identity,
  };
}

function applyOrderRequest(request, context, now = new Date()) {
  requireRequest(request, context, now);
  const archiveRoot = path.join(context.gameRoot, "archive", "pc", "mod");
  const orderPath = path.join(archiveRoot, "modlist.txt");
  const inventory = archiveInventory(archiveRoot);
  if (!sameInventory(inventory, request.inventory)) throw new Error("The deployed archive inventory changed after Conflict Studio previewed it.");
  const expectedNames = new Set(inventory.map((entry) => entry.name.toLowerCase()));
  const proposedNames = new Set(request.proposedOrder.map((entry) => entry.toLowerCase()));
  if (expectedNames.size !== request.proposedOrder.length || proposedNames.size !== request.proposedOrder.length || [...expectedNames].some((name) => !proposedNames.has(name))) throw new Error("The proposed archive order does not contain every deployed archive exactly once.");
  const current = fs.existsSync(orderPath) ? fs.readFileSync(orderPath) : null;
  const currentSha = current === null ? null : sha(current);
  if (currentSha !== request.expectedOrderSha256) throw new Error("The deployed archive order changed after Conflict Studio previewed it.");
  fs.mkdirSync(archiveRoot, { recursive: true });
  const backupPath = current === null ? null : `${orderPath}.${request.requestId}.bak`;
  if (backupPath !== null) fs.writeFileSync(backupPath, current);
  const output = mergeOrder(current, request.proposedOrder);
  const temporary = `${orderPath}.${request.requestId}.tmp`;
  try {
    const handle = fs.openSync(temporary, "wx");
    try {
      fs.writeFileSync(handle, output);
      fs.fsyncSync(handle);
    } finally {
      fs.closeSync(handle);
    }
    fs.renameSync(temporary, orderPath);
    const verified = fs.readFileSync(orderPath);
    if (!verified.equals(output)) throw new Error("The Vortex archive order did not verify after writing.");
    return {
      schemaVersion: 1,
      requestId: request.requestId,
      applied: true,
      message: "Vortex applied and verified the archive order.",
      backupPath,
      writtenSha256: sha(verified),
      completedAtUtc: new Date().toISOString(),
    };
  } catch (error) {
    if (current === null)
    {
      if (fs.existsSync(orderPath)) fs.unlinkSync(orderPath);
    }
    else
    {
      fs.writeFileSync(orderPath, current);
    }
    throw error;
  } finally {
    if (fs.existsSync(temporary)) fs.unlinkSync(temporary);
  }
}

function rollbackOrder(response, context) {
  if (!response || response.applied !== true) return;
  const orderPath = path.join(context.gameRoot, "archive", "pc", "mod", "modlist.txt");
  if (response.backupPath === null)
  {
    if (fs.existsSync(orderPath)) fs.unlinkSync(orderPath);
  }
  else
  {
    fs.copyFileSync(response.backupPath, orderPath);
  }
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
  if (!Number.isFinite(requestedAt.getTime()) || !Number.isFinite(expiresAt.getTime()) || now.getTime() > expiresAt.getTime() || expiresAt.getTime() - requestedAt.getTime() > 15000 || age < -5000) throw new Error("The archive order request expired. Preview the order again.");
}

function archiveInventory(archiveRoot) {
  if (!fs.existsSync(archiveRoot)) return [];
  return fs.readdirSync(archiveRoot, { withFileTypes: true })
    .filter((entry) => entry.isFile() && entry.name.toLowerCase().endsWith(".archive"))
    .map((entry) => {
      const file = fs.readFileSync(path.join(archiveRoot, entry.name));
      return { name: entry.name, size: file.length, sha256: sha(file) };
    })
    .sort((left, right) => left.name.localeCompare(right.name));
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

module.exports = { applyOrderRequest, archiveInventory, completeOrder, createContext, rollbackOrder, sha };
