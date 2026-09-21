# Weapon/faction research

A broad question such as “四族主要武器是什么，输出曲线是什么样的？” contains two different deliverables:

1. A qualitative classification, with evidence from official hull bonuses/descriptions and suitable weapon families.
2. Condition-bound numerical curves from the engine.

A useful starting classification to verify is Amarr energy turrets (pulse/beam, with drone hulls), Caldari missiles and hybrid rails, Gallente hybrid blasters/rails and drones, Minmatar projectile autocannons/artillery (with missile variants). This is orientation, not a universal hull rule or engine-generated ranking. Include exceptions relevant to the user's question; do not invent “one race = one weapon”. Query representative hulls with catalog describe and distinguish your synthesis from the returned traits.

For comparable examples, declare size, tech level, skill preset, ammunition, hull bonuses and number of weapons. A one-gun hull demonstrates that fit's behavior; it is not a fair faction-wide total-DPS ranking. Contrast short/long-range variants if the requested scope needs them. Do not force drones, fighters, missiles and turrets into one averaged curve.

`fitting outputs` returns contribution IDs and per-output states. `fitting curves` defaults to online ship weapons as potential output; supply contributionIds for another explicit selection. It returns distance, signature and angular axes, target/reference policy, source/fitHash and nullable sample values. Target inputs are distanceMeters, signatureMeters, speedMetersPerSecond and angularRadiansPerSecond. A missing target opts into the engine's declared ideal reference, which must be labeled as ideal. Linear target speed and angular velocity are independent supplied conditions; do not silently infer one from the other.

Present curves with units, assumptions and excluded contributions. Changing hull, ammo, skills or target changes the curve. `appliedCycleDps` and `appliedLoadedCycleDps` are not a measured sustained combat average. Missile damage-application multiplier is a damage fraction before resistance, not a hit probability. Unknown/unavailable results stay explicit. Further time-dependent questions may require a battle experiment, but basic weapon taxonomy does not.

Example request after creating an actual fitting:
```json
{"sessionId":"laser-example","target":{"distanceMeters":1000,"signatureMeters":40,"speedMetersPerSecond":100,"angularRadiansPerSecond":0.02},"intervals":16}
```

## Recovering an unavailable curve

`selectionDiagnostics` lists each selected contribution's native base metric state/value/reason. `nativeBaseline` retains the engine's own exclusions. `recoveryPlans` changes the selection or metric explicitly and provides executable calls in top-level `next`, preserving target, interval count and fitHash. They are alternative analyses, not the originally requested combined curve. Do not label a loaded-cycle curve as sustained output. For example fighter primary supports nominalCycleDps while finite fighter rockets require loadedCycleDps; analyze the offered subsets with their stated exclusions rather than inventing a combined sustained DPS. A recovery call still validates application/target and may remain unavailable. If fitHash changes, restart discovery rather than combining stale results.

Editing fighters returns deployedFighterPrimaryNominalDps directly from the same native receipt. Use that returned value rather than doubling an earlier partial-roster DPS. Do not infer a generic number of equivalent ships from one bare-hull nominal-DPS comparison.
