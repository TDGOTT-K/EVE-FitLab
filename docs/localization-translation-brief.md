# COORDINATION UPDATE — root 04:39

Japanese 1401-1879 is now COMPLETE: root commissioned a read-only Sol translation pass and merged all 479 validated values into locales/ja.json. Do not overwrite Japanese from stale copies. /root/japanese was the original idle Luna, not a new active agent.

French 501-1200 is COMPLETE in output/localization-20260920/fr-pure-500-translations.json; keys are fr-pure-500-keys.json. French 1201-1879 is currently being translated by another read-only Sol pass into fr-pure-1200-translations.json. Root will merge these independent scratch outputs. sol_ja_fr should FINISH ONLY French 1-500 and then STOP edits / report completion. Do not duplicate French beyond 500. Root owns final merge and will validate counts and named parameters. English/German/Russian are complete.

# Translation handoff

Scope: UI only. source.json contains Simplified Chinese baseline. Legacy Chinese keys are temporary adapter aliases; five semantic keys have named parameters. Never change keys. Preserve {name}, {count}, escapes, units, protocol identifiers and necessary markup. Do NOT replace words to generate translations; translate complete messages naturally. For fragments, use concise UI phrases without inventing missing content. Short buttons where possible.

Terminology: EVE fitting/fit; module; high/mid/low slot; rig; cargo hold; drone bay; fighter; implant; booster; Abyssal mutation; mutaplasmid. Item names and descriptions use SDE separately and must not be invented in UI dictionaries. TW is community OpenCC + glossary conversion, not official. Preserve EVE, SDE, ESI, EFT, DPS, EDPS, CPU, ISK, IDs, skill roman numerals and units. Users' fit, plan, character names and notes are parameters, not translation targets.

Context: abyssal-workbench = random roll / edit value, acceptance ranges, experiment results (no promise of guaranteed roll count). loadout-manager = implant/booster plans and side effects. Native reports are engine results; unavailable/partial remain unavailable/partial. Import/export refers to fits, text/images and plan sharing. Don't soften errors into success.

Read source resources in manageable batches. No Chinese residue in de/fr/ru/en except intentional proper names documented in report. Japanese must be natural Japanese, not substituted Chinese. Existing 901 en/ja/TW baseline may be reused; the remainder is NOT reliable. Validate keys, nonempty values and placeholders yourself. Do not edit business logic or source.json. Do not commit. Use output/localization-20260920 for temporary files.
