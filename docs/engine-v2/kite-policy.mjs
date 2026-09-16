// Design-only example for the proposed Battle Lab API; not a V1 plugin.
// The host supplies immutable observations and commits Memory + intents atomically.
export function tick({ observation, memory }) {
  const rangeMeters = memory?.rangeMeters ?? 18000;
  const intents = [];
  const hostiles = observation.contacts.filter(c => c.relation === 'hostile' && c.positionMeters);
  const squaredDistance = (a, b) => (a.x - b.x) ** 2 + (a.y - b.y) ** 2 + (a.z - b.z) ** 2;
  for (const ship of observation.ownedShips) {
    const candidates = [...hostiles].sort((a, b) => {
      const delta = squaredDistance(ship.positionMeters, a.positionMeters)
        - squaredDistance(ship.positionMeters, b.positionMeters);
      return delta || (a.entityId < b.entityId ? -1 : a.entityId > b.entityId ? 1 : 0);
    });
    const target = candidates[0];
    if (!target) continue;
    const base = { entityId: ship.entityId, decisionTick: observation.tick,
      observedWorldRevision: observation.worldRevision };
    const commandId = suffix => `${observation.tick}:${ship.entityId}:${suffix}`;
    intents.push({ ...base, kind: 'KeepRange', commandId: commandId('range'),
      targetId: target.entityId, rangeMeters });
    if (!ship.lockedTargetIds.includes(target.entityId)) {
      if (!ship.pendingLockTargetIds.includes(target.entityId)) {
        intents.push({ ...base, kind: 'LockTarget', commandId: commandId('lock'), targetId: target.entityId });
      }
      continue;
    }
    for (const module of ship.modules) {
      if (!module.capabilities.includes('weapon') || module.state !== 'idle') continue;
      intents.push({ ...base, kind: 'ActivateModule', commandId: commandId(module.moduleId),
        moduleId: module.moduleId, targetId: target.entityId, repeat: true });
    }
  }
  return { memory: { rangeMeters }, intents };
}
