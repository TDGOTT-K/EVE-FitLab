const cfg = {"phoenixId": "phoenix", "typhoonId": "typhoon", "bhaalgornId": "bhaalgorn", "keresId": "keres", "scytheId": "scythe", "interceptorId": "crow", "phoenixTargets": ["typhoon", "bhaalgorn"]};

function tick({ observation: o, memory }) {
  const m = memory || {};
  const intents = [];
  const live = id => o.ownedShips.find(s => s.id === id && !s.destroyed);
  const contact = id => o.contacts.find(c => c.id === id && !c.destroyed);
  const ability = (s, id) => s && s.abilities.find(a => a.id === id);
  const locked = (s, id) => s && s.completedLocks && s.completedLocks.includes(id);
  const distance = (s, c) => Math.hypot(s.position.x - c.position.x, s.position.y - c.position.y, s.position.z - c.position.z);
  const send = (kind, entityId, targetId = null, abilityId = null, extra = {}) => intents.push({
    kind, entityId, targetId, abilityId, ...extra,
    observedRevision: o.revision,
    commandId: `${entityId}:${kind}:${abilityId || targetId || ''}:${o.timeUs}`
  });
  const lock = (s, targetId) => {
    const t = contact(targetId);
    if (!s || !t || locked(s, targetId) || s.locks[targetId] !== undefined) return;
    if (Number.isFinite(s.capabilities && s.capabilities.lockRange) && distance(s, t) > s.capabilities.lockRange) return;
    send('LockTarget', s.id, targetId);
  };
  const activate = (s, id, targetId = null) => {
    const a = ability(s, id);
    if (!a || a.active || (a.retry && a.retry.reason === 'TARGET_NOT_LOCKED')) return;
    if (!targetId || locked(s, targetId)) send('ActivateAbility', s.id, targetId, id);
  };
  const stopCapFailed = s => {
    for (const a of (s && s.abilities) || []) {
      if (a.active && a.retry && a.retry.reason === 'INSUFFICIENT_CAPACITOR') send('DeactivateAbility', s.id, null, a.id);
    }
  };

  const phoenix = live(cfg.phoenixId);
  if (phoenix) {
    stopCapFailed(phoenix);
    for (const id of cfg.phoenixTargets) lock(phoenix, id);
    const target = locked(phoenix, cfg.typhoonId) ? cfg.typhoonId : cfg.bhaalgornId;
    if (target) {
      for (const a of phoenix.abilities || []) {
        if (/^(xl|torp|cruise|launcher)/i.test(a.id)) activate(phoenix, a.id, target);
      }
      for (const id of ['capital-shield', 'hardener0', 'hardener1', 'web', 'grappler', 'painter']) activate(phoenix, id, target);
      if (ability(phoenix, 'siege')) activate(phoenix, 'siege');
      const injector = ability(phoenix, 'cap-injector');
      if (injector && phoenix.capacitor < (phoenix.capabilities.capacitorCapacity || 1) * 0.35) activate(phoenix, 'cap-injector');
      if (injector && injector.retry && injector.retry.reason === 'INSUFFICIENT_CAPACITOR') send('DeactivateAbility', phoenix.id, null, 'cap-injector');
    }
  }

  const typhoon = live(cfg.typhoonId);
  if (typhoon) {
    lock(typhoon, cfg.phoenixId);
    if (locked(typhoon, cfg.phoenixId)) for (const a of typhoon.abilities || []) if (a.id.startsWith('cruise')) activate(typhoon, a.id, cfg.phoenixId);
  }
  const bhaalgorn = live(cfg.bhaalgornId);
  if (bhaalgorn) {
    lock(bhaalgorn, cfg.phoenixId);
    if (locked(bhaalgorn, cfg.phoenixId)) for (const a of bhaalgorn.abilities || []) {
      if (a.id.startsWith('neut') || a.id === 'web') activate(bhaalgorn, a.id, cfg.phoenixId);
    }
  }
  const keres = live(cfg.keresId);
  if (keres) {
    lock(keres, cfg.phoenixId);
    if (locked(keres, cfg.phoenixId)) for (const a of keres.abilities || []) if (a.id.startsWith('damp')) activate(keres, a.id, cfg.phoenixId);
  }
  const scythe = live(cfg.scytheId);
  if (scythe) {
    lock(scythe, cfg.typhoonId);
    if (locked(scythe, cfg.typhoonId)) for (const a of scythe.abilities || []) if (a.id.startsWith('remote')) activate(scythe, a.id, cfg.typhoonId);
  }
  const interceptor = live(cfg.interceptorId);
  if (interceptor && contact(cfg.phoenixId)) {
    if (!m.interceptorNavigated) {
      send('Navigate', interceptor.id, null, null, { navigation: { mode: 'orbit', targetId: cfg.phoenixId, distanceMeters: 5000, speedFraction: 1, orbitAxis: { x: 0, y: 0, z: 1 } } });
      m.interceptorNavigated = true;
    }
    lock(interceptor, cfg.phoenixId);
    if (locked(interceptor, cfg.phoenixId)) {
      for (const a of interceptor.abilities || []) if (a.id === 'mwd') activate(interceptor, a.id);
    }
  }
  m.lastTimeUs = o.timeUs;
  return { memory: m, intents };
}
