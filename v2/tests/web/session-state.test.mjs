import test from 'node:test';
import assert from 'node:assert/strict';
import { acceptSession, sessionEpoch, sessionInfo, invalidateSession, isCurrent } from '../../web/js/session-state.js';

test('Une génération absente est refusée et les réponses d’un ancien contexte sont invalidées', () => {
  assert.throws(() => acceptSession({ demonstration: true }), { code: 'session.invalid' });
  acceptSession({ generation: 'demo-a', demonstration: true, capabilities: { backup: true } });
  const first = sessionEpoch();
  assert.equal(isCurrent(first), true);
  assert.equal(Object.isFrozen(sessionInfo().capabilities), true);
  acceptSession({ generation: 'native-b', demonstration: false, capabilities: { backup: false } });
  assert.equal(isCurrent(first), false);
  const second = sessionEpoch();
  invalidateSession();
  assert.equal(isCurrent(second), false);
});
