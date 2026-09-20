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
