// État du contexte de cette page. Les instances natives/démo sont choisies par l'hôte, jamais par un argument d'opération.
let current = Object.freeze({ generation: null, demonstration: null, capabilities: {}, mode: 'Indisponible' });
let epoch = 0;

export function sessionInfo() { return current; }
export function sessionEpoch() { return epoch; }
export function acceptSession(info) {
  if (!info || typeof info.demonstration !== 'boolean' || typeof info.generation !== 'string' || !info.generation) {
    throw Object.assign(new Error('Contexte de session incomplet : aucune opération ne peut être engagée.'), { code: 'session.invalid' });
  }
  if (current.generation !== info.generation) epoch++;
  current = Object.freeze({ ...info, capabilities: Object.freeze({ ...(info.capabilities ?? {}) }) });
  return current;
}
export function invalidateSession() { epoch++; }
export function isCurrent(epochValue) { return epochValue === epoch; }
