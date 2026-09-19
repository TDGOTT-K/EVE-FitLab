// Synthetic policy. Each team sees an omniscient teaching observation.
function tick({ observation, memory }) {
  const intents = [];
  for (const ship of observation.ownedShips) {
    if (ship.destroyed) continue;
    const target = observation.contacts.find(c => !c.destroyed);
    const common = { entityId: ship.id, observedRevision: observation.revision };
    if (target && ship.locks[target.id] === undefined) {
      intents.push({ ...common, kind: 'LockTarget', targetId: target.id,
        commandId: `${observation.timeUs}:${ship.id}:lock` });
    }
    for (const ability of ship.abilities) {
      if (ability.active || ability.ammo < ability.ammoPerActivation) continue;
      if (ability.mechanism !== 'pulse' && (!target || !ship.completedLocks.includes(target.id))) continue;
      intents.push({ ...common, kind: 'ActivateAbility', abilityId: ability.id,
        targetId: ability.mechanism === 'pulse' ? null : target.id,
        commandId: `${observation.timeUs}:${ship.id}:${ability.id}` });
    }
  }
  return { memory: { decisions: (memory.decisions || 0) + 1 }, intents };
}
