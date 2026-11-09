// État client seulement : aucune fenêtre ni API Windows.
import test from 'node:test';
import assert from 'node:assert/strict';
globalThis.window = {};
const { rememberOperation, isTerminal, duration } = await import('../../web/js/operations.js');

test('Le profil suivant peut revenir en préparation sans laisser une réponse initiale écraser son état', () => {
  const base = { id: 'operation-client-1', title: 'Fictive', startedAt: '2026-10-26T12:00:00Z', demonstration: true };
  rememberOperation({ ...base, state: 'running', currentAction: 'Premier profil' });
  const preparing = rememberOperation({ ...base, state: 'preparing', currentAction: 'Deuxième profil' });
  assert.equal(preparing.currentAction, 'Deuxième profil');
  assert.deepEqual(rememberOperation({ ...base, state: 'pending' }), preparing);
  const done = rememberOperation({ ...base, state: 'partial', currentAction: 'Omission signalée' });
  assert.equal(isTerminal(done), true);
  assert.deepEqual(rememberOperation({ ...base, state: 'running' }), done);
  assert.equal(duration(null), 'non estimée');
});
