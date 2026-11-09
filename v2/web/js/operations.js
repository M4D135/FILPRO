import { call, onEvent } from './bridge.js';
import { sessionEpoch, isCurrent } from './session-state.js';

const jobs = new Map();
const observers = new Set();
const terminal = new Set(['succeeded', 'partial', 'cancelled', 'failed']);
let pendingRefresh = null;
let disposeEvent = null;

export const JOB_STATES = {
  pending: { label: 'En attente', tone: 'off' }, preparing: { label: 'Préparation', tone: 'info' },
  running: { label: 'En cours', tone: 'info' }, cancelling: { label: 'Arrêt demandé', tone: 'warn' },
  succeeded: { label: 'Terminée', tone: 'ok' }, partial: { label: 'Partielle', tone: 'warn' },
  cancelled: { label: 'Annulée', tone: 'off' }, failed: { label: 'En échec', tone: 'alert' },
};
export function stateKey(job) { return String(job?.state ?? '').toLowerCase(); }
export function isTerminal(job) { return terminal.has(stateKey(job)); }
export function jobState(job) {
  if (job?.cancellationRequested && !isTerminal(job)) return JOB_STATES.cancelling;
  return JOB_STATES[stateKey(job)] ?? { label: 'État inconnu', tone: 'warn' };
}
export function operationList() {
  return [...jobs.values()].sort((a, b) => Date.parse(b.startedAt) - Date.parse(a.startedAt));
}
export function duration(seconds) {
  if (!Number.isFinite(seconds) || seconds < 0) return 'non estimée';
  const total = Math.round(seconds);
  return total < 60 ? `${total} s` : total < 3600 ? `${Math.floor(total / 60)} min ${total % 60} s` : `${Math.floor(total / 3600)} h ${Math.floor(total % 3600 / 60)} min`;
}
export function observeOperations(observer) {
  observers.add(observer);
  observer(operationList(), null, null);
  return () => observers.delete(observer);
}
export function rememberOperation(job, { silent = false } = {}) {
  if (!job || typeof job.id !== 'string' || !JOB_STATES[stateKey(job)]) return null;
  const previous = jobs.get(job.id) ?? null;
  // Le moteur peut préparer un deuxième profil après avoir exécuté le premier. Seuls une réponse initiale tardive
  // et un état postérieur au bilan final sont écartés ; l'ordre Preparing/Running n'est pas une horloge.
  if (previous && (isTerminal(previous) || stateKey(job) === 'pending' && stateKey(previous) !== 'pending')) return previous;
  jobs.set(job.id, Object.freeze({ ...job }));
  if (!silent) observers.forEach((observer) => observer(operationList(), job, previous));
  return job;
}
export async function refreshOperations() {
  if (pendingRefresh) return pendingRefresh;
  const epoch = sessionEpoch();
  const before = new Map(jobs);
  pendingRefresh = call('operations.list', {}).then((list) => {
    if (!isCurrent(epoch)) return operationList();
    if (!Array.isArray(list)) throw new Error('La liste des opérations est illisible.');
    const completions = [];
    list.forEach((job) => {
      const previous = jobs.get(job.id);
      // Un événement arrivé pendant cette lecture reste prioritaire sur son résultat capturé plus tôt.
      if (previous !== before.get(job.id)) return;
      const current = rememberOperation(job, { silent: true });
      if (previous && current && !isTerminal(previous) && isTerminal(current)) completions.push([current, previous]);
    });
    observers.forEach((observer) => observer(operationList(), null, null));
    completions.forEach(([current, previous]) => observers.forEach((observer) => observer(operationList(), current, previous)));
    return operationList();
  }).finally(() => { pendingRefresh = null; });
  return pendingRefresh;
}
export function startOperationEvents() {
  disposeEvent?.();
  const epoch = sessionEpoch();
  disposeEvent = onEvent('operations.changed', (job) => { if (isCurrent(epoch)) rememberOperation(job); });
  return () => { disposeEvent?.(); disposeEvent = null; };
}
export function followOperation(job, ctx) {
  if (!rememberOperation(job)) throw new Error('L’hôte n’a pas renvoyé d’opération suivie. Aucun succès n’est annoncé.');
  if (ctx.settings?.autoSelectCurrentTask !== false) ctx.go('suivi', { operation: job.id });
  else ctx.toast?.('Opération engagée. Son état est visible dans le suivi.', { tone: 'info' });
}
