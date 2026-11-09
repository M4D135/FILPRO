// Source-only checks for the browser demonstration. No Windows, WebView2 or native-engine validation.
// Run with Node 22 or later: node --test v2/tests/web/selection.test.mjs
import test from 'node:test';
import assert from 'node:assert/strict';
import { mockCall } from '../../web/js/mock.js';

const inventory = await mockCall('sauvegarder.profiles');
const sidOf = (name) => inventory.profiles.find((profile) => profile.account === `DEMO\\${name}`).sid;
const camille = sidOf('camille');
const lea = sidOf('lea');
const sophie = sidOf('sophie');
const hugo = sidOf('hugo');
const selectedGroups = (result) => result.groups.filter((group) => group.selectedCount > 0).map((group) => group.id).sort();
const save = (sid, groupIds) => mockCall('sauvegarder.saveSelection', { sid, groupIds });
const codeIs = (code) => (error) => error?.code === code;

test('Identités fictives valides et dernière sélection initiale de Camille réappliquée', async () => {
  for (const profile of inventory.profiles) {
    assert.match(profile.sid, profile.identityKind === 'entra' ? /^S-1-12-1-\d+-\d+-\d+-\d+$/ : /^S-1-5-21-\d+-\d+-\d+-\d+$/);
  }
  assert.equal(camille, 'S-1-5-21-111222333-444555666-777888999-1000');
  assert.equal(sidOf('nadia'), 'S-1-12-1-700000005-1005-2005-3005');
  assert.deepEqual(selectedGroups(await mockCall('sauvegarder.software', { sid: camille })), ['known:notepadpp']);
  const last = await mockCall('sauvegarder.lastSelection', { sid: camille });
  assert.equal(last.files[0].rel, 'Notepad++');
  assert.equal(last.registry[0].rel, 'Software\\Notepad++');
});

test('Choix distincts par compte, fichiers et registre enregistrés ensemble', async () => {
  const first = await save(lea, ['known:chrome']);
  await save(sophie, ['known:office']);
  assert.deepEqual(selectedGroups(first), ['known:chrome']);
  assert.deepEqual(selectedGroups(await mockCall('sauvegarder.software', { sid: lea })), ['known:chrome']);
  assert.deepEqual(selectedGroups(await mockCall('sauvegarder.software', { sid: sophie })), ['known:office']);
  const chrome = first.groups.find((group) => group.id === 'known:chrome');
  assert.ok(chrome.files.length > 0 && chrome.registry.length > 0);
  assert.ok([...chrome.files, ...chrome.registry].every((item) => item.selected));
  const last = await mockCall('sauvegarder.lastSelection', { sid: lea });
  assert.equal(last.sid, lea);
  assert.equal(last.files.length, chrome.files.length);
  assert.equal(last.registry.length, chrome.registry.length);
  last.files.length = 0;
  assert.equal((await mockCall('sauvegarder.lastSelection', { sid: lea })).files.length, chrome.files.length);
  assert.deepEqual(selectedGroups(await mockCall('sauvegarder.software', { sid: camille })), ['known:notepadpp']);
});

test('Plan multi-profils : tous les comptes sont présents avec leurs propres choix', async () => {
  await save(lea, ['known:chrome']);
  await save(sophie, ['known:office']);
  const plan = await mockCall('sauvegarder.plan', { sids: [lea, sophie], destination: 'D:\\Sauvegardes\\demo' });
  assert.equal(plan.demonstration, true);
  assert.equal(plan.profiles.length, 2);
  const leaPlan = plan.profiles.find((profile) => profile.sid === lea);
  const sophiePlan = plan.profiles.find((profile) => profile.sid === sophie);
  assert.deepEqual(leaPlan.registry.map((item) => item.rel), ['Software\\Google\\Chrome']);
  assert.deepEqual(sophiePlan.registry.map((item) => item.rel), ['Software\\Microsoft\\Office']);
  assert.ok(leaPlan.files.every((item) => item.rel.startsWith('Google\\Chrome\\')));
  assert.ok(sophiePlan.files.every((item) => item.rel.startsWith('Microsoft\\')));
  await assert.rejects(mockCall('sauvegarder.plan', { sids: [lea, lea], destination: 'D:\\Sauvegardes\\demo' }), codeIs('backup.selection'));
});

test('Sélection vide explicite : aucune recommandation réactivée après relecture', async () => {
  await save(hugo, []);
  assert.deepEqual(selectedGroups(await mockCall('sauvegarder.software', { sid: hugo })), []);
  const last = await mockCall('sauvegarder.lastSelection', { sid: hugo });
  assert.ok(last);
  assert.deepEqual(last.files, []);
  assert.deepEqual(last.registry, []);
  const plan = await mockCall('sauvegarder.plan', { sids: [hugo], destination: 'D:\\Sauvegardes\\vide' });
  assert.deepEqual(plan.profiles[0].files, []);
  assert.deepEqual(plan.profiles[0].registry, []);
});

test('Le mock refuse les destinations et clés que le plan natif interdit', async () => {
  const base = { sids: [lea], destination: 'D:\\Sauvegardes\\demo' };
  for (const destination of ['relatif', 'C:\\Users\\lea', 'C:\\Users\\lea\\Sauvegarde', 'D:\\Sauvegardes\\..\\autre']) {
    await assert.rejects(mockCall('sauvegarder.plan', { ...base, destination }), codeIs('backup.destination'));
  }
  for (const keyPath of ['cle.key', 'D:\\Sauvegardes\\demo\\cle.key', 'C:\\Users\\lea\\cle.key']) {
    await assert.rejects(mockCall('sauvegarder.plan', { ...base, encrypted: true, keyPath }), codeIs('backup.destination'));
  }
  await assert.rejects(mockCall('sauvegarder.plan', { ...base, keyPath: 'E:\\cles\\demo.key' }), codeIs('backup.destination'));
  const plan = await mockCall('sauvegarder.plan', { ...base, encrypted: true, keyPath: 'E:\\cles\\demo.key' });
  assert.equal(plan.keyPath, 'E:\\cles\\demo.key');
});

test('Arguments fermés : les refus ne modifient pas les choix précédents', async () => {
  await save(lea, ['known:notepadpp']);
  const invalid = [null, [], {}, { sid: lea }, { sid: lea, groupIds: null }, { sid: lea, groupIds: 'known:chrome' },
    { sid: lea, groupIds: [null] }, { sid: lea, groupIds: [''] }, { sid: lea, groupIds: ['unknown'] },
    { sid: lea, groupIds: ['known:chrome', 'known:chrome'] }, { sid: lea, groupIds: ['x'.repeat(1025)] },
    { sid: lea, groupIds: Array(501).fill('known:chrome') }, { sid: lea, groupIds: [], files: ['C:\\intrus'] }];
  for (const args of invalid) {
    await assert.rejects(mockCall('sauvegarder.saveSelection', args), codeIs('arguments.invalid'));
  }
  await assert.rejects(save('S-1-5-21-1-2-3-9999', []), codeIs('backup.selection'));
  assert.deepEqual(selectedGroups(await mockCall('sauvegarder.software', { sid: lea })), ['known:notepadpp']);
});

test('Restauration : SID valide accepté, compte exécutant fictif refusé', async () => {
  const catalog = await mockCall('demonstration.catalog');
  const base = { bundle: catalog.stores[0].bundle, storeReady: true, priorRoot: catalog.assets.safetyBackupRoot, usmtVersion: '10.0' };
  const restored = await mockCall('sauvegarder.restorePlan', {
    ...base, users: ['DEMO\\sophie'], targetSids: [sophie], conflictSid: sophie,
  });
  assert.equal(restored.plan.targets[0].sid, sophie);
  assert.equal(restored.plan.priorBackup.verified, false);
  assert.equal(restored.store.declaredByOperator, false);
  await assert.rejects(mockCall('sauvegarder.restorePlan', {
    ...base, users: ['DEMO\\sophie'], mappings: [{ source: 'DEMO\\sophie', target: 'DEMO\\camille' }], targetSids: [camille], conflictSid: camille,
  }), (error) => error.code === 'restore.target' && /compte qui exécute FILPRO/.test(error.message));
  await assert.rejects(mockCall('sauvegarder.restorePlan', {
    ...base, users: ['DEMO\\sophie'], targetSids: [sophie], conflictSid: hugo,
  }), (error) => error.code === 'restore.target' && /destinations sélectionnées/.test(error.message));
});

test('La démonstration refuse les deux confirmations d’écriture et les opérations inconnues', async () => {
  await assert.rejects(mockCall('sauvegarder.confirmPlan', { planId: 'f'.repeat(32), acknowledgeCapacity: true }), codeIs('simulation.readOnly'));
  await assert.rejects(mockCall('sauvegarder.confirmRestorePlan', {
    planId: 'f'.repeat(32), acknowledgeCapacity: true, acknowledgePriorBackup: true, acknowledgeIdentityReview: true,
  }), codeIs('simulation.readOnly'));
  await assert.rejects(mockCall('sauvegarder.execute', {}), codeIs('operation.unknown'));
});
