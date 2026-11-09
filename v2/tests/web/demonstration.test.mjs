// Prévisualisation navigateur seulement. L'intégration du vrai SimulationWorld et Windows est contrôlée séparément.
import test from 'node:test';
import assert from 'node:assert/strict';
import { mockCall } from '../../web/js/mock.js';

const codeIs = (code) => (error) => error?.code === code;

test('Catalogue, choix communs et réinitialisation restent dans leur génération fictive', async () => {
  const before = await mockCall('demonstration.catalog');
  const profiles = (await mockCall('sauvegarder.profiles')).profiles;
  assert.equal(before.destinations.length, 4);
  assert.equal(before.stores.length, 60);
  assert.equal(before.profileStates.length, 6);
  const sophie = profiles.find((profile) => profile.account === 'DEMO\\sophie').sid;
  const hugo = profiles.find((profile) => profile.account === 'DEMO\\hugo').sid;
  await mockCall('sauvegarder.saveSelection', { sid: hugo, groupIds: [] });
  const common = await mockCall('sauvegarder.setCommonSelection', { sid: sophie, groupIds: ['known:office'] });
  assert.ok(common.appData.length > 0 && common.registryKeys.length > 0);
  assert.deepEqual((await mockCall('sauvegarder.software', { sid: sophie })).groups.filter((group) => group.selected).map((group) => group.id), ['known:office']);
  assert.deepEqual((await mockCall('sauvegarder.software', { sid: hugo })).groups.filter((group) => group.selected), []);
  await mockCall('session.demoReset', { confirmed: true });
  const after = await mockCall('demonstration.catalog');
  assert.notEqual(after.generation, before.generation);
  assert.equal(after.stores.length, 60);
  assert.equal((await mockCall('sauvegarder.readCommonSelection')).hasSelection, false);
});

test('Toutes les routes natives restent refusées même avec un mode forgé', async () => {
  const routes = ['sauvegarder.confirmPlan', 'sauvegarder.confirmRestorePlan', 'analyser.confirmRepair', 'analyser.rollbackRepair',
    'recuperer.confirmPlan', 'updates.install', 'updates.rollback', 'session.choosePath'];
  for (const route of routes) await assert.rejects(mockCall(route, { confirmed: true, demonstration: false, mode: 'real', path: 'C:\\Reel' }), codeIs('simulation.readOnly'));
  await assert.rejects(mockCall('sauvegarder.inspectStore', { path: 'C:\\VraieSauvegarde' }), codeIs('demonstration.boundary'));
  await assert.rejects(mockCall('demonstration.backup', { planId: 'inconnu', confirmed: true, acknowledgeCapacity: true, mode: 'real' }), codeIs('arguments.invalid'));
});

test('Configuration fictive : révision, export fermé et confirmation avant défauts', async () => {
  const catalog = await mockCall('demonstration.catalog');
  const initial = await mockCall('configuration.read');
  await assert.rejects(mockCall('configuration.save', { settings: { ...initial.settings, secret: 'interdit' }, expectedRevision: initial.revision }), codeIs('configuration.format'));
  const saved = await mockCall('configuration.save', { settings: { ...initial.settings, theme: 'contrast' }, expectedRevision: initial.revision });
  await assert.rejects(mockCall('configuration.save', { settings: initial.settings, expectedRevision: initial.revision }));
  assert.equal(saved.state.settings.theme, 'contrast');
  const preview = await mockCall('configuration.previewDefaults');
  await assert.rejects(mockCall('configuration.applyDefaults', { previewId: preview.id, confirmed: false }), codeIs('execution.confirmation'));
  const applied = await mockCall('configuration.applyDefaults', { previewId: preview.id, confirmed: true });
  assert.equal(applied.demonstration, true);
  await assert.rejects(mockCall('configuration.applyDefaults', { previewId: preview.id, confirmed: true }));
  await mockCall('configuration.export', { destinationPath: catalog.assets.exportConfigurationPath, confirmed: true });
  const exported = await mockCall('configuration.previewImport', { sourcePath: catalog.assets.exportConfigurationPath });
  assert.ok(exported.changes.every((change) => !/sid|path|secret|history/i.test(change.key)));
  await assert.rejects(mockCall('configuration.export', { destinationPath: 'C:\\Reel\\config.json', confirmed: true }), codeIs('demonstration.boundary'));
});

test('Annuler attend la fin fictive, puis la mise à jour garde son accord séparé', async () => {
  const catalog = await mockCall('demonstration.catalog');
  const safe = (await mockCall('sauvegarder.profiles')).profiles.filter((profile) => profile.migrationSupported && profile.loaded === false && profile.session.state === 'none');
  const plan = await mockCall('sauvegarder.plan', { sids: safe.map((profile) => profile.sid), destination: catalog.destinations[0].path, migApp: true });
  assert.equal(plan.migApp, true);
  await assert.rejects(mockCall('demonstration.backup', { planId: plan.id, confirmed: false, acknowledgeCapacity: true }), codeIs('execution.confirmation'));
  const job = await mockCall('demonstration.backup', { planId: plan.id, confirmed: true, acknowledgeCapacity: true });
  assert.equal(job.state, 'pending');
  await assert.rejects(mockCall('session.demoReset', { confirmed: true }), codeIs('session.busy'));
  const stopped = await mockCall('operations.cancel', { id: job.id, confirmed: true });
  assert.equal(stopped.state, 'cancelled');
  assert.equal((await mockCall('demonstration.catalog')).stores.length, catalog.stores.length);
  const previousVersion = (await mockCall('updates.read')).currentVersion;
  const offer = await mockCall('updates.inspect', { kind: 'folder', path: catalog.assets.updateFolderPath });
  assert.notEqual(offer.version, previousVersion);
  const prepared = await mockCall('updates.prepare', { offerId: offer.id, confirmed: true });
  await assert.rejects(mockCall('demonstration.updateInstall', { transactionId: prepared.transactionId, confirmed: false }), codeIs('execution.confirmation'));
  await mockCall('demonstration.updateInstall', { transactionId: prepared.transactionId, confirmed: true });
  assert.equal((await mockCall('updates.read')).currentVersion, offer.version);
  await mockCall('demonstration.updateRollback', { transactionId: prepared.transactionId, confirmed: true });
  assert.equal((await mockCall('updates.read')).currentVersion, previousVersion);
});

test('Source de mise à jour fictive : lecture, choix mémorisé, dossier hors catalogue refusé, remise à zéro', async () => {
  const catalog = await mockCall('demonstration.catalog');
  assert.deepEqual(await mockCall('updates.source', {}), { demonstration: true, kind: 'folder', folder: null });
  const folder = catalog.destinations[0].path;
  assert.deepEqual(await mockCall('updates.saveSource', { kind: 'folder', folder }), { demonstration: true, kind: 'folder', folder });
  assert.equal((await mockCall('updates.source', {})).kind, 'folder');
  await assert.rejects(mockCall('updates.saveSource', { kind: 'folder', folder: 'C:\\Partage\\Reel' }), codeIs('demonstration.boundary'));
  await assert.rejects(mockCall('updates.saveSource', { kind: 'ftp' }), codeIs('demonstration.boundary'));
  await assert.rejects(mockCall('updates.saveSource', { kind: 'folder', folder: '' }), codeIs('demonstration.boundary'), 'dossier vide refusé comme en natif');
  await assert.rejects(mockCall('updates.saveSource', { kind: 'manualZip', folder }), codeIs('demonstration.boundary'), 'dossier interdit hors source dossier');
  await assert.rejects(mockCall('updates.saveSource', { kind: 'gitHubReleases' }), codeIs('demonstration.boundary'), 'GitHub n’est plus une source');
  await assert.rejects(mockCall('updates.inspect', { kind: 'gitHubReleases' }), codeIs('demonstration.boundary'), 'aucune recherche GitHub');
  assert.equal((await mockCall('updates.source', {})).folder, folder, 'un refus ne modifie pas le choix précédent');
  await mockCall('session.demoReset', { confirmed: true });
  assert.deepEqual(await mockCall('updates.source', {}), { demonstration: true, kind: 'folder', folder: null });
});
