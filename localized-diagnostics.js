import { t } from './i18n.js';
// Public error codes stay intact. These messages describe existing diagnostics,
// never infer legality or successful execution from a translated string.
const messages = {
  CRYSTAL_INITIAL_DAMAGE: 'ui.damageMustBeAtLeast0AndBelowThe',
  FIT_INVENTORY_DESTROYED_CRYSTAL: 'ui.destroyedCrystalsCannotBeKeptInInventory',
  FIT_INVENTORY_CRYSTAL_MOUNT: 'ui.theCrystalIsIncompatibleWithTheCurrentModuleS',
  FIT_INVENTORY_CRYSTAL: 'ui.theLegacyCrystalStackLacksItemIdentitiesAndDamage',
};
export function diagnosticText(diagnostic) {
  if (!diagnostic) return '';
  const raw = typeof diagnostic === 'string' ? diagnostic : diagnostic.message || JSON.stringify(diagnostic);
  return diagnostic.code && messages[diagnostic.code]
    ? t(messages[diagnostic.code]) + ' (' + diagnostic.code + ')'
    : raw;
}
export function requestError(data, status) {
  const raw =
    typeof data.error === 'string'
      ? data.error
      : data.error?.message || data.diagnostic?.message || JSON.stringify(data);
  const code = data.error?.code || data.diagnostic?.code;
  const error = new Error(diagnosticText({ code, message: raw }));
  error.code = code;
  error.status = status;
  error.diagnostic = data;
  error.rawMessage = raw;
  return error;
}
