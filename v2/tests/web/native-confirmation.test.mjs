// Confirmation Windows côté page seulement : la fenêtre native, ses faits et ses refus sont contrôlés côté C#.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { mockCall } from '../../web/js/mock.js';
import { acceptSession, sessionInfo } from '../../web/js/session-state.js';
import {
  nativeConfirmation, primaryLabel, decisionLine, confirmUnlessNative, bindConfirmationBanner, DECISION_LINE, PENDING_TEXT,
  presenceLocked, codeUpdates, bindPresenceBanner, PRESENCE_LOCKED_TEXT, CROSS_ACCOUNT_UPDATES_TEXT,
} from '../../web/js/native-confirmation.js';
import * as nativeConfirmationModule from '../../web/js/native-confirmation.js';

// Canal WebView2 simulé : l'hôte répond avec la forme de BridgeResponse (id, kind, op, data, error) en camelCase.
let receive = null;
let answer = null;
globalThis.document = { documentElement: {} };
globalThis.window = { chrome: { webview: {
  addEventListener: (name, handler) => { if (name === 'message') receive = handler; },
  postMessage: (message) => queueMicrotask(() => receive({ data: { id: message.id, kind: 'error', op: message.op, error: answer } })),
} } };
const { call, onEvent } = await import('../../web/js/bridge.js');
const { errorText } = await import('../../web/js/ui.js');
const hostEvent = (op, data) => receive({ data: { id: null, kind: 'event', op, data } });

const poste = { generation: 'poste-a', demonstration: false, capabilities: { nativeConfirmation: true } };
// Contexte de la page avant session.info, démonstration (même forgée), prévisualisation et Poste sans fenêtre Windows.
const withoutWindow = [
  undefined,
  sessionInfo(),
  { generation: 'poste-b', demonstration: false, capabilities: {} },
  { generation: 'poste-c', demonstration: false, capabilities: { nativeConfirmation: false } },
  { generation: 'poste-d', demonstration: false, capabilities: { nativeConfirmation: 'true' } },
  { generation: 'demo-a', demonstration: true, capabilities: { nativeConfirmation: true } },
  await mockCall('session.info'),
];

test('« Continuer » et la ligne de la fenêtre Windows seulement avec la capacité de l’hôte', () => {
  assert.equal(DECISION_LINE, 'La décision se prend ensuite dans la fenêtre Windows « FILPRO — Confirmation Windows ».');
  assert.equal(withoutWindow.at(-1).capabilities.nativeConfirmation, false);
  assert.equal(nativeConfirmation(acceptSession(poste)), true);
  assert.equal(primaryLabel(sessionInfo(), 'Confirmer la préparation'), 'Continuer');
  assert.equal(primaryLabel(poste, 'Définir ces valeurs par défaut'), 'Continuer');
  assert.ok(decisionLine(poste).includes(DECISION_LINE));
  for (const session of withoutWindow) {
    assert.equal(nativeConfirmation(session), false);
    assert.equal(primaryLabel(session, 'Confirmer la préparation'), 'Confirmer la préparation');
    assert.equal(decisionLine(session), '');
  }
});

test('Les confirmations qui redisent l’opération sont sautées seulement avec la capacité', async () => {
  let asked = 0;
  const ask = (reply) => async () => { asked++; return reply; };
  assert.equal(await confirmUnlessNative(poste, ask(false)), true);
  assert.equal(asked, 0);
  for (const session of withoutWindow) {
    assert.equal(await confirmUnlessNative(session, ask(false)), false);
    assert.equal(await confirmUnlessNative(session, ask(true)), true);
  }
  assert.equal(asked, 2 * withoutWindow.length);
});

test('Le bandeau non bloquant suit l’événement confirmation.changed publié par l’hôte', () => {
  assert.equal(PENDING_TEXT, 'Confirmation Windows en attente : répondez dans la fenêtre « FILPRO — Confirmation Windows ».');
  const banner = { hidden: true, innerHTML: '' };
  const stop = bindConfirmationBanner(banner, onEvent);
  hostEvent('confirmation.changed', { pending: true });
  assert.equal(banner.hidden, false);
  assert.ok(banner.innerHTML.includes(PENDING_TEXT));
  hostEvent('operations.changed', { id: 'operation-1', state: 'running' });
  assert.equal(banner.hidden, false);
  hostEvent('confirmation.changed', { pending: false });
  assert.deepEqual(banner, { hidden: true, innerHTML: '' });
  for (const data of [null, {}, { pending: 'true' }]) {
    hostEvent('confirmation.changed', { pending: true });
    hostEvent('confirmation.changed', data);
    assert.deepEqual(banner, { hidden: true, innerHTML: '' });
  }
  stop();
  hostEvent('confirmation.changed', { pending: true });
  assert.equal(banner.hidden, true);
});

test('Refus, délai, occupation et indisponibilité s’affichent avec le message français de l’hôte', async () => {
  const messages = {
    'confirmation.refused': 'Opération refusée dans la fenêtre « FILPRO — Confirmation Windows » : rien n’a été lancé.',
    'confirmation.timeout': 'Aucune réponse dans la fenêtre « FILPRO — Confirmation Windows » depuis 2 minutes : rien n’a été lancé.',
    'confirmation.busy': 'Une fenêtre Windows de FILPRO est déjà ouverte ou une confirmation vient d’être refusée : répondez-y ou réessayez dans quelques secondes.',
    'confirmation.unavailable': 'La confirmation Windows de FILPRO n’est pas disponible : aucune opération n’a été lancée.',
  };
  for (const [code, message] of Object.entries(messages)) {
    answer = { code, message };
    const failure = await call('sauvegarder.confirmPlan', { planId: 'f'.repeat(32), acknowledgeCapacity: true }).then(() => null, (err) => err);
    assert.equal(failure?.code, code);
    assert.equal(errorText(failure, 'Confirmation refusée : rien n’a été préparé ni exécuté.'), message);
  }
});

test('Six dialogues mènent à « Continuer », quatre reformulations sont sautées et la coque porte le bandeau', async () => {
  const uses = {
    'pages/sauvegarder.js': { primaryLabel: 1, decisionLine: 1 },
    'restore-panel.js': { primaryLabel: 1, decisionLine: 1 },
    'pages/recuperer.js': { primaryLabel: 2, decisionLine: 2 },
    'pages/analyser.js': { primaryLabel: 1, decisionLine: 1, confirmUnlessNative: 1 },
    'configuration-ui.js': { primaryLabel: 1, decisionLine: 1 },
    'pages/parametres.js': { confirmUnlessNative: 2 },
    'pages/suivi.js': { confirmUnlessNative: 1 },
    'app.js': { bindConfirmationBanner: 1, bindPresenceBanner: 1 },
  };
  for (const [file, calls] of Object.entries(uses)) {
    const source = await readFile(new URL(`../../web/js/${file}`, import.meta.url), 'utf8');
    for (const [name, count] of Object.entries(calls)) assert.equal(source.split(`${name}(`).length - 1, count, `${file} : ${name}`);
  }
});

test('Autre compte : verrou et mises à jour suivent les capacités de l’hôte, jamais la démonstration', async () => {
  const across = { generation: 'poste-x', demonstration: false, capabilities: { crossAccount: true, codeUpdates: false, presenceLocked: true } };
  assert.equal(presenceLocked(across), true);
  assert.equal(codeUpdates(across), false);
  const forged = { generation: 'demo-x', demonstration: true, capabilities: { presenceLocked: true, codeUpdates: true } };
  assert.equal(presenceLocked(forged), false);
  for (const session of [undefined, {}, { capabilities: {} }, { capabilities: { codeUpdates: 'false' } }, await mockCall('session.info')]) {
    assert.equal(codeUpdates(session), true);
    assert.equal(presenceLocked(session), false);
  }
  const mock = await mockCall('session.info');
  assert.deepEqual([mock.capabilities.crossAccount, mock.capabilities.codeUpdates, mock.capabilities.presenceLocked],
    [false, true, false]);
  assert.equal('packModifiable' in mock.capabilities, false, 'aucun avertissement de dossier modifiable');
  assert.ok(CROSS_ACCOUNT_UPDATES_TEXT.startsWith('Les mises à jour de FILPRO ne sont pas proposées'));
});

test('Le bandeau de verrou part de session.info, suit presence.changed et ne se lève jamais', () => {
  const banner = { hidden: true, innerHTML: '' };
  const stop = bindPresenceBanner(banner, onEvent, { demonstration: false, capabilities: { presenceLocked: false } });
  assert.deepEqual(banner, { hidden: true, innerHTML: '' });
  hostEvent('presence.changed', { locked: 'true' });
  assert.equal(banner.hidden, true);
  hostEvent('presence.changed', { locked: true });
  assert.equal(banner.hidden, false);
  assert.ok(banner.innerHTML.includes(PRESENCE_LOCKED_TEXT));
  hostEvent('presence.changed', { locked: false });
  assert.equal(banner.hidden, false, 'le verrou va dans un seul sens');
  stop();
  const initial = { hidden: true, innerHTML: '' };
  bindPresenceBanner(initial, () => () => {}, { demonstration: false, capabilities: { presenceLocked: true } });
  assert.equal(initial.hidden, false);
  const demo = { hidden: true, innerHTML: '' };
  const stopDemo = bindPresenceBanner(demo, onEvent, { demonstration: true, capabilities: { presenceLocked: true } });
  hostEvent('presence.changed', { locked: true });
  assert.deepEqual(demo, { hidden: true, innerHTML: '' }, 'jamais dans la démonstration');
  stopDemo();
});

test('Le dossier de FILPRO modifiable n’est plus signalé dans la page', async () => {
  assert.equal('bindPackBanner' in nativeConfirmationModule, false);
  assert.equal('PACK_MODIFIABLE_TEXT' in nativeConfirmationModule, false);
  const app = await readFile(new URL('../../web/js/app.js', import.meta.url), 'utf8');
  assert.equal(app.includes('data-pack-banner'), false);
  assert.equal(app.includes('pack.exposure'), false);
});

test('Les bandeaux de l’hôte sont empilés dans un conteneur FILPRO, jamais superposés', async () => {
  const app = await readFile(new URL('../../web/js/app.js', import.meta.url), 'utf8');
  const wrapper = app.slice(app.indexOf('<div class="host-banners">'), app.indexOf('</div>\n    </div>', app.indexOf('<div class="host-banners">')));
  for (const banner of ['data-confirmation-banner', 'data-presence-banner']) assert.ok(wrapper.includes(banner), banner);
  const css = await readFile(new URL('../../web/css/filpro-shell.css', import.meta.url), 'utf8');
  assert.ok(css.includes('.shell > .host-banners { grid-area: banner; z-index: 4; min-width: 0; display: flex; flex-direction: column; }'));
  assert.ok(css.includes('.shell > .host-banners:not(:has(> .sim-banner:not([hidden]))) { display: none; }'));
});

test('Paramètres : sous un autre compte, la section Mises à jour est remplacée par l’explication', async () => {
  const source = await readFile(new URL('../../web/js/pages/parametres.js', import.meta.url), 'utf8');
  assert.equal(source.split('codeUpdates(').length - 1, 2);
  assert.ok(source.includes('data-update-controls'));
  assert.ok(source.includes('data-update-unavailable'));
  assert.ok(source.includes('CROSS_ACCOUNT_UPDATES_TEXT'));
});
