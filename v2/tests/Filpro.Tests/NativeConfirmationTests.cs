using System.Text.Json;
using Filpro.Bridge;
using Filpro.Core;
using Filpro.Simulation;

namespace Filpro.Tests;

/// <summary>
/// Confirmation Windows des opérations privilégiées, par le pont réel d'un poste : la fenêtre est réservée avant la prise du
/// plan, montre l'objet détenu par l'hôte et seul un accord lance l'effet, une seule fois et sur ce même objet. Un refus,
/// un délai dépassé, une fenêtre absente, occupée ou en échec, une annulation et un objet inconnu ne lancent rien ; la
/// démonstration n'ouvre jamais de fenêtre.
/// </summary>
internal sealed class NativeConfirmationTests
{
    private const string ReceiptPath = @"C:\ProgramData\FILPRO\Operations\0123456789abcdef0123456789abcdef\repair-receipt.json";
    private const string Bundle = @"\\nas\sauvegardes\poste-01";
    private const string ZipPath = @"D:\Mises a jour\FILPRO-2.0.9-win-x64-PROD.zip";
    private const string TransactionId = "fedcba9876543210fedcba9876543210";

    private static string NewId() => Guid.NewGuid().ToString("N");

    private static T Result<T>(BridgeResponse response)
    {
        Check.Equal("result", response.Kind, response.Error?.Code + " " + response.Error?.Message);
        return (T)response.Data!;
    }

    /// <summary>Objet préparé avant la confirmation : arguments de la page, objet lié et empreinte attendue dans la fiche.</summary>
    private sealed record Arranged(object Args, object? Bound, string Digest);

    /// <summary>Une opération privilégiée : préparation, nombre d'effets lancés et contrôle de l'objet remis au moteur.</summary>
    private sealed record Case(string Operation, bool ConsumesPlan, Func<Harness, Task<Arranged>> Arrange, Func<Harness, int> Effects,
        Action<Harness, Arranged> Started);

    private static Task<Arranged> Planned<T>(Harness harness, string id, T plan, object args) where T : class
    {
        harness.Plans.Add(id, plan);
        return Task.FromResult(new Arranged(args, plan, ConfirmedPlanStore.Fingerprint(plan)));
    }

    private static void SamePlan(Harness harness, Arranged arranged) =>
        Check.True(ReferenceEquals(arranged.Bound, harness.Executor.Started.Single()), "le plan montré est l'instance remise au moteur");

    private static IReadOnlyList<Case> Cases() =>
    [
        new("sauvegarder.confirmPlan", true, harness =>
        {
            var id = NewId();
            return Planned(harness, id, ConfirmationSamples.Backup(id), new { planId = id, acknowledgeCapacity = true });
        }, harness => harness.Executor.Started.Count, SamePlan),
        new("sauvegarder.confirmRestorePlan", true, harness =>
        {
            var id = NewId();
            return Planned(harness, id, ConfirmationSamples.Restore(id),
                new { planId = id, acknowledgeCapacity = true, acknowledgePriorBackup = true, acknowledgeIdentityReview = true });
        }, harness => harness.Executor.Started.Count, SamePlan),
        new("analyser.confirmRepair", true, harness =>
        {
            var id = NewId();
            return Planned(harness, id, ConfirmationSamples.Repair(id), new { planId = id, confirmed = true, acknowledgeCapacity = true });
        }, harness => harness.Executor.Started.Count, SamePlan),
        new("analyser.rollbackRepair", false, harness => Task.FromResult(new Arranged(new { receiptPath = ReceiptPath, confirmed = true },
            harness.Executor.Review, ConfirmedPlanStore.Fingerprint(harness.Executor.Review))),
            harness => harness.Executor.Started.Count, (harness, arranged) =>
            {
                var request = (RepairRollbackRequest)harness.Executor.Started.Single();
                Check.Equal(ReceiptPath, request.ReceiptPath, "le reçu demandé est celui qui a été relu");
                Check.Equal(((RepairRollbackReview)arranged.Bound!).ReceiptSha256, request.ExpectedReceiptSha256, "le démarrage exige l'empreinte montrée");
            }),
        new("recuperer.catalog", false, harness => Task.FromResult(new Arranged(new { bundle = Bundle, confirmed = true, spaceAcknowledged = true },
            harness.Executor.Inspection, ConfirmedPlanStore.Fingerprint(harness.Executor.Inspection))),
            harness => harness.Executor.Started.Count, (harness, arranged) =>
            {
                var request = (StoreCatalogRequest)harness.Executor.Started.Single();
                Check.Equal(Bundle, request.Bundle, "le magasin demandé est celui qui a été inspecté");
                Check.Equal(((ExecutionStoreInspection)arranged.Bound!).ManifestSha256, request.ExpectedManifestSha256, "le démarrage exige le manifeste montré");
            }),
        new("recuperer.confirmPlan", true, harness =>
        {
            var id = NewId();
            return Planned(harness, id, ConfirmationSamples.Recovery(id), new { planId = id, confirmed = true, acknowledgeCapacity = true });
        }, harness => harness.Executor.Started.Count, SamePlan),
        new("configuration.applyDefaults", false, async harness =>
        {
            var preview = Result<ConfigurationPreview>(await harness.Call("configuration.previewDefaults", new { }));
            return new Arranged(new { previewId = preview.Id, confirmed = true }, preview, ConfirmedPlanStore.Fingerprint(preview));
        }, harness => harness.Configuration.Applied.Count, (harness, arranged) =>
            Check.Equal(((ConfigurationPreview)arranged.Bound!).Id, harness.Configuration.Applied.Single(), "l'aperçu montré est celui qui est écrit")),
        new("updates.prepare", false, async harness =>
        {
            var offer = Result<UpdateOffer>(await harness.Call("updates.inspect", new { kind = "manualZip", path = ZipPath, sha256 = ConfirmationSamples.Manifest }));
            return new Arranged(new { offerId = offer.Id, confirmed = true }, offer, ConfirmedPlanStore.Fingerprint(offer));
        }, harness => harness.Updates.Prepared.Count, (harness, arranged) =>
        {
            Check.Equal(((UpdateOffer)arranged.Bound!).Id, harness.Updates.Prepared.Single(), "l'offre montrée est celle qui est préparée");
            Check.True(ConfirmationRecords.For(harness.Context).Provenance(TransactionId) is { HashTyped: true, Kind: UpdateSourceKind.ManualZip },
                "la provenance de la préparation est conservée pour l'installation");
        }),
        new("updates.install", false, harness =>
        {
            harness.Updates.Status = ConfirmationSamples.Prepared(TransactionId);
            return Task.FromResult(new Arranged(new { transactionId = TransactionId, confirmed = true }, null, ConfirmedPlanStore.Fingerprint(harness.Updates.Status)));
        }, harness => harness.Updates.Installed.Count, (harness, _) =>
        {
            Check.Equal(TransactionId, harness.Updates.Installed.Single(), "la transaction montrée est celle qui est remise");
            Check.Equal(1, harness.Session.Exits, "fermeture demandée après la remise");
        }),
        new("updates.rollback", false, harness =>
        {
            harness.Updates.Status = ConfirmationSamples.Committed(TransactionId);
            return Task.FromResult(new Arranged(new { transactionId = TransactionId, confirmed = true }, null, ConfirmedPlanStore.Fingerprint(harness.Updates.Status)));
        }, harness => harness.Updates.RolledBack.Count, (harness, _) =>
        {
            Check.Equal(TransactionId, harness.Updates.RolledBack.Single(), "la transaction montrée est celle qui revient en arrière");
            Check.Equal(1, harness.Session.Exits, "fermeture demandée après la remise");
        }),
    ];

    [Test]
    public void LesDixOperationsDeLaListeFermeeSontCouvertes()
    {
        Check.Equal(string.Join(",", PrivilegePolicy.Operations.Select(operation => operation.Name).Order(StringComparer.Ordinal)),
            string.Join(",", Cases().Select(item => item.Operation).Order(StringComparer.Ordinal)), "chaque opération privilégiée est éprouvée");
    }

    [Test]
    public async Task UnAccordLanceUneSeuleFoisLObjetMontre()
    {
        foreach (var item in Cases())
        {
            var harness = new Harness();
            var arranged = await item.Arrange(harness);
            var response = await harness.Call(item.Operation, arranged.Args);
            Check.Equal("result", response.Kind, item.Operation + " : accord dans la fenêtre (" + response.Error?.Code + ")");
            Check.Equal(1, item.Effects(harness), item.Operation + " : un seul démarrage");
            item.Started(harness, arranged);
            var sheet = harness.Confirmer!.Sheets.Single();
            Check.Equal(item.Operation, sheet.Operation, item.Operation + " : fiche de cette opération");
            Check.Equal(arranged.Digest, sheet.Digest, item.Operation + " : la fiche est liée à l'objet détenu par l'hôte");
            Check.Equal(1, harness.Journal.Lines.Count(line => line.Contains(item.Operation + " : acceptée", StringComparison.Ordinal)),
                item.Operation + " : une ligne technique pour la décision");
        }
    }

    [Test]
    public async Task UnRefusNeLanceRienEtLePlanPrisEstPerdu()
    {
        foreach (var item in Cases())
        {
            var harness = new Harness();
            var arranged = await item.Arrange(harness);
            harness.Confirmer!.Outcome = ConfirmationOutcome.Refused;
            var refused = await harness.Call(item.Operation, arranged.Args);
            Check.Equal(FilproErrors.ConfirmationRefused, refused.Error?.Code, item.Operation + " : refus dans la fenêtre");
            Check.Equal(FilproErrors.ConfirmationRefusedMessage, refused.Error?.Message, item.Operation + " : message français du refus");
            Check.Equal(0, item.Effects(harness), item.Operation + " : rien n'est lancé");
            Check.Equal(arranged.Digest, harness.Confirmer.Sheets.Single().Digest, item.Operation + " : la fiche refusée montrait l'objet détenu");
            harness.Clock.Now += NativeConfirmation.Cooldown;
            harness.Confirmer.Outcome = ConfirmationOutcome.Approved;
            var replay = await harness.Call(item.Operation, arranged.Args);
            if (item.ConsumesPlan)
            {
                Check.Equal("plan.expired", replay.Error?.Code, item.Operation + " : le plan pris avant la fenêtre ne se rejoue pas");
                Check.Equal(1, harness.Confirmer.Sheets.Count, item.Operation + " : aucune nouvelle fenêtre pour un plan consommé");
                Check.Equal(0, item.Effects(harness), item.Operation + " : toujours rien de lancé");
            }
            else
            {
                Check.Equal("result", replay.Kind, item.Operation + " : le refus n'a rien consommé (" + replay.Error?.Code + ")");
                Check.Equal(1, item.Effects(harness), item.Operation + " : lancé une seule fois après un nouvel accord");
            }
        }
    }

    [Test]
    public async Task UnDelaiDepasseNeLanceRien()
    {
        var harness = new Harness();
        harness.Confirmer!.Outcome = ConfirmationOutcome.TimedOut;
        var id = NewId();
        harness.Plans.Add(id, ConfirmationSamples.Backup(id));
        var response = await harness.Call("sauvegarder.confirmPlan", new { planId = id, acknowledgeCapacity = true });
        Check.Equal(FilproErrors.ConfirmationTimeout, response.Error?.Code, "aucune réponse dans la fenêtre");
        Check.Equal(FilproErrors.ConfirmationTimeoutMessage, response.Error?.Message, "message français du délai");
        Check.Equal(0, harness.Executor.Started.Count, "rien n'est lancé");
        Check.Equal(1, harness.Journal.Lines.Count(line => line.Contains("sauvegarder.confirmPlan : sans réponse", StringComparison.Ordinal)), "décision tracée");
    }

    [Test]
    public async Task SansFenetreDeConfirmationRienNEstConsomme()
    {
        var harness = new Harness(confirmer: false);
        var backup = NewId();
        var backupPlan = ConfirmationSamples.Backup(backup);
        harness.Plans.Add(backup, backupPlan);
        var restore = NewId();
        var restorePlan = ConfirmationSamples.Restore(restore);
        harness.Plans.Add(restore, restorePlan);
        Check.Equal(FilproErrors.ConfirmationUnavailable, (await harness.Call("sauvegarder.confirmPlan", new { planId = backup, acknowledgeCapacity = true })).Error?.Code,
            "poste sans fenêtre de confirmation");
        Check.Equal(FilproErrors.ConfirmationUnavailable, (await harness.Call("sauvegarder.confirmRestorePlan",
            new { planId = restore, acknowledgeCapacity = true, acknowledgePriorBackup = true, acknowledgeIdentityReview = true })).Error?.Code, "idem pour la restauration");
        Check.Equal(FilproErrors.ConfirmationUnavailable, (await harness.Call("updates.prepare", new { offerId = NewId(), confirmed = true })).Error?.Code,
            "la réservation précède la recherche de l'offre");
        Check.True(ReferenceEquals(backupPlan, harness.Plans.Take<BackupPlanReview>(backup)), "le plan de sauvegarde n'a pas été pris");
        Check.True(ReferenceEquals(restorePlan, harness.Plans.Take<RestorePlanReview>(restore)), "le plan de restauration n'a pas été pris");
        Check.Equal(0, harness.Executor.Started.Count, "rien n'est lancé");
        var info = Result<SessionInfo>(await harness.Call("session.info", new { }));
        Check.False(info.Capabilities!["nativeConfirmation"], "la page n'annonce pas de fenêtre absente");
        Check.True(Result<SessionInfo>(await new Harness().Call("session.info", new { })).Capabilities!["nativeConfirmation"], "poste avec fenêtre de confirmation");
    }

    [Test]
    public async Task UneSecondeDemandePendantLaFenetreEstOccupeeSansRienConsommer()
    {
        var harness = new Harness();
        var decision = new TaskCompletionSource<ConfirmationOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Confirmer!.Pending = _ => decision.Task;
        var backup = NewId();
        var backupPlan = ConfirmationSamples.Backup(backup);
        harness.Plans.Add(backup, backupPlan);
        var recovery = NewId();
        var recoveryPlan = ConfirmationSamples.Recovery(recovery);
        harness.Plans.Add(recovery, recoveryPlan);
        var first = harness.Invoke("sauvegarder.confirmPlan", new { planId = backup, acknowledgeCapacity = true }, CancellationToken.None);
        Check.False(first.IsCompleted, "la première fenêtre attend l'opérateur");
        var busy = await Check.ThrowsAsync<FilproOperationException>(() => harness.Invoke("recuperer.confirmPlan",
            new { planId = recovery, confirmed = true, acknowledgeCapacity = true }, CancellationToken.None), "une seule fenêtre à la fois");
        Check.Equal(FilproErrors.ConfirmationBusy, busy.Code, "confirmation.busy");
        Check.Equal(FilproErrors.ConfirmationBusy, Check.Throws<FilproOperationException>(() => NativeConfirmation.Begin(harness.Context), "réservation tenue").Code,
            "une seconde réservation est refusée");
        decision.SetResult(ConfirmationOutcome.Approved);
        await first;
        Check.True(ReferenceEquals(backupPlan, harness.Executor.Started.Single()), "la première opération est lancée après accord");
        Check.True(ReferenceEquals(recoveryPlan, harness.Plans.Take<RecoveryPlanReview>(recovery)), "le plan refusé pour occupation est intact");
        using var next = NativeConfirmation.Begin(harness.Context);
    }

    [Test]
    public async Task UnRefusOuUnDelaiDepasseImposeUnDelaiMesureParLHorloge()
    {
        foreach (var outcome in new[] { ConfirmationOutcome.Refused, ConfirmationOutcome.TimedOut })
        {
            var harness = new Harness();
            harness.Confirmer!.Outcome = outcome;
            var id = NewId();
            harness.Plans.Add(id, ConfirmationSamples.Backup(id));
            await harness.Call("sauvegarder.confirmPlan", new { planId = id, acknowledgeCapacity = true });
            harness.Clock.Now += NativeConfirmation.Cooldown - TimeSpan.FromTicks(1);
            Check.Equal(FilproErrors.ConfirmationBusy, Check.Throws<FilproOperationException>(() => NativeConfirmation.Begin(harness.Context), outcome + " : délai en cours").Code,
                outcome + " : nouvelle demande refusée pendant le délai");
            var next = NewId();
            var nextPlan = ConfirmationSamples.Backup(next);
            harness.Plans.Add(next, nextPlan);
            Check.Equal(FilproErrors.ConfirmationBusy, (await harness.Call("sauvegarder.confirmPlan", new { planId = next, acknowledgeCapacity = true })).Error?.Code,
                outcome + " : le pont refuse pendant le délai");
            harness.Clock.Now += TimeSpan.FromTicks(1);
            harness.Confirmer.Outcome = ConfirmationOutcome.Approved;
            Check.Equal("result", (await harness.Call("sauvegarder.confirmPlan", new { planId = next, acknowledgeCapacity = true })).Kind,
                outcome + " : le plan suivant, intact, est accepté après le délai");
            Check.True(ReferenceEquals(nextPlan, harness.Executor.Started.Single()), outcome + " : seul le plan accepté est lancé");
        }
        var approved = new Harness();
        var plan = NewId();
        approved.Plans.Add(plan, ConfirmationSamples.Backup(plan));
        await approved.Call("sauvegarder.confirmPlan", new { planId = plan, acknowledgeCapacity = true });
        using var free = NativeConfirmation.Begin(approved.Context);
    }

    [Test]
    public async Task UneAnnulationFermeLaFenetreEtRendLesBaux()
    {
        var harness = new Harness();
        harness.Confirmer!.Pending = async token =>
        {
            await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
            return ConfirmationOutcome.Approved;
        };
        var id = NewId();
        harness.Plans.Add(id, ConfirmationSamples.Backup(id));
        using (var cancellation = new CancellationTokenSource())
        {
            var pending = harness.Invoke("sauvegarder.confirmPlan", new { planId = id, acknowledgeCapacity = true }, cancellation.Token);
            Check.False(pending.IsCompleted, "la fenêtre attend l'opérateur");
            Check.Equal("update.busy", Check.Throws<FilproOperationException>(() => harness.Context.Operations.EnterUpdateHandoff(() => true),
                "bail de démarrage tenu pendant la fenêtre").Code, "aucune remise de mise à jour pendant la fenêtre");
            await cancellation.CancelAsync();
            await Check.ThrowsAsync<OperationCanceledException>(() => pending, "l'annulation ferme la fenêtre");
        }
        Check.Equal(0, harness.Executor.Started.Count, "rien n'est lancé");
        using (harness.Context.Operations.EnterUpdateHandoff(() => true))
        {
            // Le bail de démarrage est rendu : la remise de mise à jour redevient possible.
        }
        harness.Updates.Status = ConfirmationSamples.Prepared(TransactionId);
        using (var cancellation = new CancellationTokenSource())
        {
            var pending = harness.Invoke("updates.install", new { transactionId = TransactionId, confirmed = true }, cancellation.Token);
            Check.False(pending.IsCompleted, "la fenêtre d'installation attend l'opérateur");
            Check.Equal("update.busy", Check.Throws<FilproOperationException>(() => harness.Context.Operations.EnterNativeStart(),
                "bail de remise tenu pendant la fenêtre").Code, "aucun démarrage natif pendant la fenêtre d'installation");
            await cancellation.CancelAsync();
            await Check.ThrowsAsync<OperationCanceledException>(() => pending, "l'annulation ferme la fenêtre d'installation");
        }
        using (harness.Context.Operations.EnterUpdateHandoff(() => true))
        {
            // Le bail de remise est rendu lui aussi.
        }
        Check.Equal(0, harness.Updates.Installed.Count, "aucune installation");
        Check.Equal(0, harness.Session.Exits, "aucune fermeture demandée");
        using var free = NativeConfirmation.Begin(harness.Context);
    }

    [Test]
    public async Task OffreApercuTransactionOuObjetRefusesAvantLaFenetre()
    {
        var harness = new Harness();
        Check.Equal("update.offer", (await harness.Call("updates.prepare", new { offerId = NewId(), confirmed = true })).Error?.Code, "offre jamais renvoyée par cet hôte");
        Check.Equal("configuration.preview", (await harness.Call("configuration.applyDefaults", new { previewId = NewId(), confirmed = true })).Error?.Code,
            "aperçu jamais renvoyé par cet hôte");
        harness.Updates.Status = ConfirmationSamples.Prepared(TransactionId);
        Check.Equal("update.state", (await harness.Call("updates.install", new { transactionId = NewId(), confirmed = true })).Error?.Code, "autre transaction");
        harness.Updates.Status = ConfirmationSamples.Prepared(TransactionId) with { CanInstall = false };
        Check.Equal("update.state", (await harness.Call("updates.install", new { transactionId = TransactionId, confirmed = true })).Error?.Code, "transaction non installable");
        harness.Updates.Status = ConfirmationSamples.Committed(TransactionId) with { CanRollback = false };
        Check.Equal("update.rollback", (await harness.Call("updates.rollback", new { transactionId = TransactionId, confirmed = true })).Error?.Code, "aucun retour arrière");
        harness.Executor.Inspection = ConfirmationSamples.Inspection(Bundle, ready: false);
        Check.Equal("store.state", (await harness.Call("recuperer.catalog", new { bundle = Bundle, confirmed = true, spaceAcknowledged = true })).Error?.Code,
            "magasin inexploitable");
        harness.Executor.ReviewFailure = new FilproOperationException("repair.receipt", "Reçu refusé.");
        Check.Equal("repair.receipt", (await harness.Call("analyser.rollbackRepair", new { receiptPath = ReceiptPath, confirmed = true })).Error?.Code, "reçu refusé");
        Check.Equal(0, harness.Confirmer!.Sheets.Count, "aucune fenêtre ouverte");
        Check.Equal(0, harness.Updates.Prepared.Count + harness.Updates.Installed.Count + harness.Updates.RolledBack.Count
            + harness.Configuration.Applied.Count + harness.Executor.Started.Count, "aucun effet");
        using var free = NativeConfirmation.Begin(harness.Context);
    }

    [Test]
    public async Task SansAccordDeLaPageLeServiceRefuseSansFenetre()
    {
        var harness = new Harness();
        harness.Updates.Status = ConfirmationSamples.Prepared(TransactionId);
        Check.Equal("update.confirm", (await harness.Call("updates.prepare", new { offerId = NewId(), confirmed = false })).Error?.Code, "préparation non accordée");
        Check.Equal("configuration.confirm", (await harness.Call("configuration.applyDefaults", new { previewId = NewId(), confirmed = false })).Error?.Code,
            "écriture du pack non accordée");
        Check.Equal("update.confirm", (await harness.Call("updates.install", new { transactionId = TransactionId, confirmed = false })).Error?.Code, "installation non accordée");
        Check.Equal(0, harness.Confirmer!.Sheets.Count, "aucune fenêtre");
        Check.Equal("arguments.invalid", (await harness.Call("updates.install", new { transactionId = TransactionId, confirmed = true, sheet = "page" })).Error?.Code,
            "un champ de plus reste refusé");
    }

    [Test]
    public async Task UneFicheOuUneFenetreEnEchecNeLanceRien()
    {
        var harness = new Harness();
        var started = 0;
        Task<int> Start()
        {
            started++;
            return Task.FromResult(started);
        }
        using (var turn = NativeConfirmation.Begin(harness.Context))
        {
            var error = await Check.ThrowsAsync<FilproOperationException>(() => turn.ConfirmThenAsync<int>(() => throw new InvalidOperationException("fiche"),
                Start, CancellationToken.None), "fiche en échec");
            Check.Equal(FilproErrors.ConfirmationUnavailable, error.Code, "une fiche en échec vaut indisponibilité");
        }
        using (var turn = NativeConfirmation.Begin(harness.Context))
        {
            var error = await Check.ThrowsAsync<FilproOperationException>(() => turn.ConfirmThenAsync<int>(
                () => throw new FilproOperationException("store.state", "Le magasin n'est pas exploitable."), Start, CancellationToken.None), "refus attendu de la fiche");
            Check.Equal("store.state", error.Code, "un refus attendu garde son code");
        }
        Check.Equal(0, started, "aucun démarrage");
        Check.Equal(0, harness.Confirmer!.Sheets.Count, "aucune fenêtre");
        var broken = NewId();
        harness.Plans.Add(broken, ConfirmationSamples.Backup(broken) with { Profiles = null! });
        Check.Equal(FilproErrors.ConfirmationUnavailable, (await harness.Call("sauvegarder.confirmPlan", new { planId = broken, acknowledgeCapacity = true })).Error?.Code,
            "fiche impossible par le pont");
        harness.Confirmer.Pending = _ => throw new InvalidOperationException("fenêtre");
        var other = NewId();
        harness.Plans.Add(other, ConfirmationSamples.Backup(other));
        Check.Equal(FilproErrors.ConfirmationUnavailable, (await harness.Call("sauvegarder.confirmPlan", new { planId = other, acknowledgeCapacity = true })).Error?.Code,
            "fenêtre en échec");
        Check.Equal(FilproErrors.ConfirmationBusy, Check.Throws<FilproOperationException>(() => NativeConfirmation.Begin(harness.Context), "délai").Code,
            "une fenêtre en échec impose le délai d'un refus");
        Check.Equal(0, harness.Executor.Started.Count, "rien n'est lancé");
        Check.Equal(3, harness.Journal.Lines.Count(line => line.StartsWith("ERREUR", StringComparison.Ordinal)), "chaque échec est tracé");
    }

    [Test]
    public async Task UnTourNeConfirmeQuUneFoisEtLibereSaReservation()
    {
        var harness = new Harness();
        var plan = ConfirmationSamples.Repair(NewId());
        using (var turn = NativeConfirmation.Begin(harness.Context))
        {
            Check.Equal(1, await turn.ConfirmThenAsync(() => ConfirmationSheets.Repair(plan), () => Task.FromResult(1), CancellationToken.None), "accord puis démarrage");
            await Check.ThrowsAsync<InvalidOperationException>(() => turn.ConfirmThenAsync(() => ConfirmationSheets.Repair(plan), () => Task.FromResult(2), CancellationToken.None),
                "un seul accord par tour");
        }
        Check.Equal(1, harness.Confirmer!.Sheets.Count, "une seule fenêtre");
        NativeConfirmation.Begin(harness.Context).Dispose();
        using var again = NativeConfirmation.Begin(harness.Context);
        Check.Equal(FilproErrors.ConfirmationUnavailable, Check.Throws<FilproOperationException>(() => NativeConfirmation.Begin(new Harness(confirmer: false).Context),
            "poste sans fenêtre").Code, "aucune confirmation sans fenêtre de l'hôte");
    }

    [Test]
    public async Task UneLigneTechniqueParDecisionSansCheminNiCompte()
    {
        var harness = new Harness(situation: new HostSituation(SessionSituation.SameAccount, ConfirmationSamples.UserSid, @"POSTE\jdupont",
            ConfirmationSamples.UserSid, @"POSTE\jdupont"));
        var id = NewId();
        var plan = ConfirmationSamples.Backup(id);
        harness.Plans.Add(id, plan);
        Result<ExecutionJobSnapshot>(await harness.Call("sauvegarder.confirmPlan", new { planId = id, acknowledgeCapacity = true }));
        var lines = harness.Journal.Lines.Where(line => line.Contains("Confirmation Windows", StringComparison.Ordinal)).ToArray();
        Check.Equal(1, lines.Length, "une ligne par décision");
        Check.Contains("sauvegarder.confirmPlan : acceptée (empreinte " + ConfirmedPlanStore.Fingerprint(plan)[..12] + ", situation SameAccount, ", lines[0],
            "opération, empreinte et situation");
        foreach (var secret in new[] { plan.Destination, plan.Profiles[0].Account, plan.Profiles[0].ProfilePath, plan.Purpose })
            Check.False(lines[0].Contains(secret, StringComparison.OrdinalIgnoreCase), "aucun chemin, compte ni texte de la page dans le journal");
        Check.True(harness.Confirmer!.Sheets.Single().Attention.Any(item => item.Text.StartsWith("Le profil du compte élevé", StringComparison.Ordinal)),
            "le pont transmet la situation de l'hôte à la fiche");
    }

    [Test]
    public void LesEnregistrementsDuPontSontBornesEtExpirent()
    {
        var clock = new ManualClock();
        var records = new ConfirmationRecords(clock);
        var offers = Enumerable.Range(0, ConfirmationRecords.Capacity + 1).Select(_ => ConfirmationSamples.Offer(NewId())).ToArray();
        foreach (var offer in offers)
        {
            // Demande d'inspection une seconde avant son enregistrement.
            records.RecordOffer(offer, new UpdateSourceRequest(UpdateSourceKind.Folder, "d:/Mises a jour/"), clock.Now - TimeSpan.FromSeconds(1));
            clock.Now += TimeSpan.FromSeconds(1);
        }
        Check.Null(records.Offer(offers[0].Id), "la plus ancienne offre cède sa place");
        var kept = records.Offer(offers[^1].Id) ?? throw new TestFailureException("l'offre la plus récente est conservée");
        Check.True(kept is { Kind: UpdateSourceKind.Folder, HashTyped: false } && ReferenceEquals(kept.Offer, offers[^1]), "offre et demande conservées");
        Check.Equal(@"D:\Mises a jour", kept.Path, "chemin normalisé comme le service le lit");
        Check.Equal(clock.Now - TimeSpan.FromSeconds(2) + ConfirmationRecords.Lifetime, kept.Deadline, "échéance comptée depuis la demande d'inspection");
        records.RecordOffer(offers[1], new UpdateSourceRequest(UpdateSourceKind.ManualZip, @"D:\FILPRO-2.0.9-win-x64-PROD.zip", ConfirmationSamples.Manifest), clock.Now);
        Check.True(records.Offer(offers[1].Id)?.HashTyped == true, "empreinte saisie dans la page");
        var preview = ConfirmationSamples.Defaults(NewId());
        records.RecordPreview(preview, clock.Now - TimeSpan.FromSeconds(3));
        Check.Equal(clock.Now - TimeSpan.FromSeconds(3) + ConfirmationRecords.Lifetime, records.Preview(preview.Id)?.Deadline, "échéance comptée depuis la demande d'aperçu");
        records.RecordProvenance(TransactionId, kept);
        clock.Now += ConfirmationRecords.Lifetime - TimeSpan.FromTicks(1);
        Check.True(ReferenceEquals(preview, records.Preview(preview.Id)?.Preview), "aperçu encore valable");
        clock.Now += TimeSpan.FromTicks(1);
        Check.Null(records.Preview(preview.Id), "aperçu expiré après 15 minutes");
        clock.Now += TimeSpan.FromDays(1);
        Check.True(ReferenceEquals(kept, records.Provenance(TransactionId)), "provenance conservée sans limite de durée");
        var latest = NewId();
        for (var index = 1; index < ConfirmationRecords.Capacity; index++) records.RecordProvenance(NewId(), kept);
        Check.True(ReferenceEquals(kept, records.Provenance(TransactionId)), "provenance conservée jusqu'à la capacité");
        records.RecordProvenance(latest, kept);
        Check.Null(records.Provenance(TransactionId), "la plus ancienne provenance cède sa place");
        Check.True(ReferenceEquals(kept, records.Provenance(latest)), "la plus récente est conservée");
        Check.Null(records.Offer(NewId()), "identifiant inconnu");
    }

    [Test]
    public async Task UnObjetQuiExpireraitFenetreOuverteEstRefuseAvantElle()
    {
        Check.Equal(Filpro.App.NativeWindowPolicy.ResponseTimeout, NativeConfirmation.ResponseTimeout, "le pont compte avec le délai de réponse de la fenêtre");
        var margin = NativeConfirmation.ResponseTimeout + TimeSpan.FromSeconds(30);
        // Durée de vie de l'objet lié et refus que le moteur ou le service lui opposerait une fois expiré. Les plans d'exemple
        // sont créés à l'heure de départ de l'horloge du harnais ; l'offre et l'aperçu y sont demandés.
        var limits = new Dictionary<string, (TimeSpan Life, string Code)>(StringComparer.Ordinal)
        {
            ["sauvegarder.confirmPlan"] = (ExecutionLimits.PlanFreshness, "execution.expired"),
            ["sauvegarder.confirmRestorePlan"] = (ExecutionLimits.PlanFreshness, "execution.expired"),
            ["analyser.confirmRepair"] = (ExecutionLimits.PlanFreshness, "execution.expired"),
            ["recuperer.confirmPlan"] = (ExecutionLimits.PlanFreshness, "execution.expired"),
            ["updates.prepare"] = (ConfirmationRecords.Lifetime, "update.offer"),
            ["configuration.applyDefaults"] = (ConfirmationRecords.Lifetime, "configuration.preview"),
        };
        var cases = Cases().Where(item => limits.ContainsKey(item.Operation)).ToArray();
        Check.Equal(limits.Count, cases.Length, "chaque objet à durée de vie est éprouvé");
        foreach (var item in cases)
        {
            var (life, code) = limits[item.Operation];
            var late = new Harness();
            var deadline = late.Clock.Now + life;
            var arranged = await item.Arrange(late);
            late.Clock.Now = deadline - margin + TimeSpan.FromTicks(1);
            var refused = await late.Call(item.Operation, arranged.Args);
            Check.Equal(code, refused.Error?.Code, item.Operation + " : moins de 2 min 30 s avant l'expiration");
            Check.Equal(0, late.Confirmer!.Sheets.Count, item.Operation + " : aucune fenêtre ouverte");
            Check.Equal(0, item.Effects(late), item.Operation + " : rien n'est lancé");
            using (NativeConfirmation.Begin(late.Context))
            {
                // Refus avant la fenêtre : la réservation est rendue sans délai.
            }

            var timely = new Harness();
            arranged = await item.Arrange(timely);
            timely.Clock.Now = deadline - margin;
            timely.Confirmer!.Pending = _ =>
            {
                timely.Clock.Now += TimeSpan.FromSeconds(119);
                return Task.FromResult(ConfirmationOutcome.Approved);
            };
            var accepted = await timely.Call(item.Operation, arranged.Args);
            Check.Equal("result", accepted.Kind, item.Operation + " : accord après 119 s de fenêtre ouverte (" + accepted.Error?.Code + ")");
            Check.Equal(1, item.Effects(timely), item.Operation + " : lancé après l'accord");
            Check.True(timely.Clock.Now < deadline, item.Operation + " : démarrage avant l'expiration mesurée par le moteur ou le service");
        }
    }

    [Test]
    public async Task LaProvenanceDUnePreparationResteMontreeSansLimiteDeDuree()
    {
        var harness = new Harness();
        var offer = Result<UpdateOffer>(await harness.Call("updates.inspect", new { kind = "manualZip", path = ZipPath, sha256 = ConfirmationSamples.Manifest }));
        Check.Equal(UpdateStage.Prepared, Result<UpdateStatus>(await harness.Call("updates.prepare", new { offerId = offer.Id, confirmed = true })).Stage, "pack préparé");
        // Une sauvegarde longue retient l'installation : elle est demandée 40 minutes plus tard, dans la même session de FILPRO.
        harness.Clock.Now += TimeSpan.FromMinutes(40);
        Result<UpdateHandoff>(await harness.Call("updates.install", new { transactionId = TransactionId, confirmed = true }));
        var sheet = harness.Confirmer!.Sheets[^1];
        Check.Equal("updates.install", sheet.Operation, "fenêtre de l'installation");
        var provenance = sheet.Facts.SingleOrDefault(fact => fact.Label == "Provenance") ?? throw new TestFailureException("provenance perdue après 40 minutes");
        Check.True(provenance.Value.StartsWith("ZIP ", StringComparison.Ordinal)
            && provenance.Value.EndsWith(" · empreinte : saisie dans l’interface", StringComparison.Ordinal), "source et origine de l'empreinte de la préparation");
        Check.False(sheet.Attention.Any(item => item.Text.StartsWith("Provenance non conservée", StringComparison.Ordinal)), "aucune alerte de provenance perdue");
        Check.Equal(TransactionId, harness.Updates.Installed.Single(), "transaction installée");
    }

    [Test]
    public async Task UnLecteurAbsentEstRefuseSansFenetre()
    {
        var harness = new Harness();
        var started = false;
        var plan = ConfirmationSamples.Backup(NewId()) with { Destination = @"Z:\Sauvegardes" };
        using (var turn = NativeConfirmation.Begin(harness.Context))
        {
            var absent = await Check.ThrowsAsync<FilproOperationException>(() => turn.ConfirmThenAsync(
                () => ConfirmationSheets.Backup(plan, null, _ => DriveType.NoRootDirectory),
                () =>
                {
                    started = true;
                    return Task.FromResult(0);
                }, CancellationToken.None), "lecteur introuvable pour FILPRO élevé");
            Check.Equal("destination.absent", absent.Code, "le refus de la fiche garde son code");
        }
        Check.False(started, "rien n'est lancé");
        Check.Equal(0, harness.Confirmer!.Sheets.Count, "aucune fenêtre");
        using var free = NativeConfirmation.Begin(harness.Context);
    }

    [Test]
    public async Task LaDemonstrationNOuvreJamaisDeFenetre()
    {
        var world = new SimulationWorld(SimulationOptions.Default, TimeProvider.System);
        var common = new SimulationCommonSelectionService(TimeProvider.System);
        var context = new EngineContext(ModeResolver.Resolve(["--simulation"], null), world, new ConfiguredPosteSource(world.Poste, world.Configuration, common),
            TimeProvider.System, SilentTechnicalJournal.Instance, world.ReadCatalog(CancellationToken.None).Generation);
        context.Register<IFilproExecutionService>(world);
        context.Register<IFilproDemonstrationSession>(world);
        context.Register<IFilproConfigurationService>(world.Configuration);
        context.Register<IFilproUpdateService>(world.Updates);
        context.Register<IFilproCommonSelectionService>(common);
        var confirmer = new ThrowingConfirmation();
        context.Register<IOperatorConfirmation>(confirmer);
        var responses = new List<BridgeResponse>();
        var dispatcher = new BridgeDispatcher(OperationModules.CreateRegistry(context, [typeof(SessionModule).Assembly]), responses.Add, AdmissionGuards.For(context));
        async Task<BridgeResponse> Call(string operation, object args)
        {
            responses.Clear();
            await dispatcher.HandleAsync(JsonSerializer.Serialize(new { id = "r1", op = operation, args }), CancellationToken.None);
            return responses.Single(response => response.Kind is "result" or "error");
        }
        var offer = Result<UpdateOffer>(await Call("updates.inspect", new { kind = "folder", path = world.ReadCatalog(CancellationToken.None).Assets.UpdateFolderPath }));
        Check.Equal(UpdateStage.Prepared, Result<UpdateStatus>(await Call("updates.prepare", new { offerId = offer.Id, confirmed = true })).Stage,
            "préparation fictive comme avant, sans fenêtre");
        var preview = Result<ConfigurationPreview>(await Call("configuration.previewDefaults", new { }));
        Check.True(Result<ConfigurationResult>(await Call("configuration.applyDefaults", new { previewId = preview.Id, confirmed = true })).Demonstration,
            "défauts fictifs écrits en mémoire, sans fenêtre");
        Check.Equal(FilproErrors.SimulationReadOnly, (await Call("sauvegarder.confirmPlan", new { planId = NewId(), acknowledgeCapacity = true })).Error?.Code,
            "les écritures restent refusées à l'admission");
        Check.False(Result<SessionInfo>(await Call("session.info", new { })).Capabilities!["nativeConfirmation"], "aucune fenêtre annoncée en démonstration");
        using (var turn = NativeConfirmation.Begin(context))
        {
            // Sans fenêtre, aucun délai de réponse à réserver : l'échéance n'est jamais opposée en démonstration.
            turn.RequireTimeLeft(DateTimeOffset.MinValue, "execution.expired", "Jamais opposé en démonstration.");
            Check.Equal(42, await turn.ConfirmThenAsync<int>(() => throw new InvalidOperationException("jamais construite"), () => Task.FromResult(42), CancellationToken.None),
                "la fiche n'est jamais construite en démonstration");
        }
        using (NativeConfirmation.Begin(context))
        using (NativeConfirmation.Begin(context))
        {
            // Aucune réservation en démonstration.
        }
        Check.Equal(0, confirmer.Calls, "aucune fenêtre en démonstration");
        Check.Null(ConfirmationRecords.For(context).Offer(offer.Id), "rien n'est enregistré pour une confirmation en démonstration");
        Check.Null(ConfirmationRecords.For(context).Preview(preview.Id), "aucun aperçu fictif enregistré");
    }

    private static OperatorPresence ArmedPresence()
    {
        var presence = new OperatorPresence(() => Environment.TickCount64, TimeSpan.Zero);
        presence.Arm();
        return presence;
    }

    private static object BackupArgs(Harness harness, out BackupPlanReview plan)
    {
        var id = NewId();
        plan = ConfirmationSamples.Backup(id);
        harness.Plans.Add(id, plan);
        return new { planId = id, acknowledgeCapacity = true };
    }

    [Test]
    public async Task EnAutreCompteLaFicheProposeLeVerrouPourLePosteSeulement()
    {
        var cross = new Harness(situation: TestSituations.CrossAccount, presence: ArmedPresence());
        Check.Equal("result", (await cross.Call("sauvegarder.confirmPlan", BackupArgs(cross, out _))).Kind, "sauvegarde acceptée");
        Check.True(cross.Confirmer!.Sheets.Single().OfferLockAfterStart, "case « Verrouiller FILPRO après le lancement » proposée");
        var unknown = new Harness(situation: TestSituations.Unknown, presence: ArmedPresence());
        await unknown.Call("sauvegarder.confirmPlan", BackupArgs(unknown, out _));
        Check.True(unknown.Confirmer!.Sheets.Single().OfferLockAfterStart, "compte indéterminé : même protection");
        var same = new Harness(presence: ArmedPresence());
        await same.Call("sauvegarder.confirmPlan", BackupArgs(same, out _));
        Check.False(same.Confirmer!.Sheets.Single().OfferLockAfterStart, "même compte : aucune case");
        Check.Equal(FilproErrors.SessionCrossAccount, (await cross.Call("updates.prepare", new { offerId = NewId(), confirmed = true })).Error?.Code,
            "mise à jour du code refusée avant toute fenêtre");
        Check.Equal(1, cross.Confirmer.Sheets.Count, "aucune fenêtre pour la mise à jour");
    }

    [Test]
    public async Task LaCaseVerrouilleDesLAccordAvantLeLancement()
    {
        var presence = ArmedPresence();
        var harness = new Harness(situation: TestSituations.CrossAccount, presence: presence);
        harness.Confirmer!.LockAfterStart = true;
        bool? lockedAtStart = null;
        harness.Executor.BeforeStart = () => lockedAtStart = presence.Latched;
        Check.Equal("result", (await harness.Call("sauvegarder.confirmPlan", BackupArgs(harness, out var started))).Kind, "lancée");
        Check.Equal<bool?>(true, lockedAtStart, "verrou posé avant le lancement : aucune autre demande ne passe entre les deux");
        Check.Equal<PresenceReason?>(PresenceReason.LockAfterStart, presence.Reason, "motif : case cochée");
        Check.Equal(FilproErrors.PresenceLocked, (await harness.Call("sauvegarder.confirmPlan", BackupArgs(harness, out _))).Error?.Code,
            "plus aucune opération lancée");
        Check.True(ReferenceEquals(started, harness.Executor.Started.Single()), "seule l'opération acceptée tourne");
        Check.Equal("result", (await harness.Call("operations.list", new { })).Kind, "le suivi reste disponible");
        var info = Result<SessionInfo>(await harness.Call("session.info", new { }));
        Check.True(info.Capabilities!["presenceLocked"], "la page l'annonce");

        var failing = ArmedPresence();
        var broken = new Harness(situation: TestSituations.CrossAccount, presence: failing);
        broken.Confirmer!.LockAfterStart = true;
        broken.Executor.StartFailure = new FilproOperationException("job.start", "Démarrage impossible.");
        Check.Equal("job.start", (await broken.Call("sauvegarder.confirmPlan", BackupArgs(broken, out _))).Error?.Code, "lancement en échec après l'accord");
        Check.Equal<PresenceReason?>(PresenceReason.LockAfterStart, failing.Reason, "le verrou demandé tient même si le lancement échoue");
        harness.Confirmer.LockAfterStart = false;
        var open = ArmedPresence();
        var unticked = new Harness(situation: TestSituations.CrossAccount, presence: open);
        await unticked.Call("sauvegarder.confirmPlan", BackupArgs(unticked, out _));
        Check.False(open.Latched, "case décochée : FILPRO reste disponible");
        var samePresence = ArmedPresence();
        var same = new Harness(presence: samePresence);
        same.Confirmer!.LockAfterStart = true;
        await same.Call("sauvegarder.confirmPlan", BackupArgs(same, out _));
        Check.False(samePresence.Latched, "même compte : la case n'était pas proposée, la décision est ignorée");
    }

    [Test]
    public async Task UnAccordDonneApresLeVerrouillageNeLanceRien()
    {
        var presence = ArmedPresence();
        var harness = new Harness(situation: TestSituations.CrossAccount, presence: presence);
        harness.Confirmer!.Pending = _ =>
        {
            // Session verrouillée pendant que la fenêtre attendait, puis clic d'accord traité ensuite.
            presence.Latch(PresenceReason.SessionLocked);
            return Task.FromResult(ConfirmationOutcome.Approved);
        };
        var response = await harness.Call("sauvegarder.confirmPlan", BackupArgs(harness, out _));
        Check.Equal(FilproErrors.PresenceLocked, response.Error?.Code, "accord donné après le verrouillage : refusé");
        Check.Equal(0, harness.Executor.Started.Count, "rien n'est lancé");
        Check.Equal(1, harness.Journal.Lines.Count(line => line.Contains("sauvegarder.confirmPlan : acceptée après le verrouillage, rien n’est lancé",
            StringComparison.Ordinal)), "décision tracée sans lancement");
        using (NativeConfirmation.Begin(new Harness().Context))
        {
            // Même compte : la réservation reste libre, rien n'est verrouillé.
        }
    }

    [Test]
    public async Task TroisRefusEnAutreCompteVerrouillent()
    {
        var presence = ArmedPresence();
        var harness = new Harness(situation: TestSituations.CrossAccount, presence: presence);
        harness.Confirmer!.Outcome = ConfirmationOutcome.Refused;
        for (var i = 0; i < OperatorPresence.RefusalLimit; i++)
        {
            Check.False(presence.Latched, "pas encore verrouillé avant le refus " + (i + 1));
            harness.Confirmer.Outcome = i == 1 ? ConfirmationOutcome.TimedOut : ConfirmationOutcome.Refused;
            var response = await harness.Call("sauvegarder.confirmPlan", BackupArgs(harness, out _));
            Check.True(response.Error?.Code is FilproErrors.ConfirmationRefused or FilproErrors.ConfirmationTimeout, "refus ou délai " + (i + 1));
            harness.Clock.Now += NativeConfirmation.Cooldown;
        }
        Check.Equal<PresenceReason?>(PresenceReason.Refusals, presence.Reason, "3 refus ou absences de réponse");
        Check.Equal(FilproErrors.PresenceLocked, (await harness.Call("sauvegarder.confirmPlan", BackupArgs(harness, out _))).Error?.Code, "verrouillé");
        Check.Equal(0, harness.Executor.Started.Count, "rien n'est lancé");
        var samePresence = ArmedPresence();
        var same = new Harness(presence: samePresence);
        same.Confirmer!.Outcome = ConfirmationOutcome.Refused;
        for (var i = 0; i < OperatorPresence.RefusalLimit + 1; i++)
        {
            await same.Call("sauvegarder.confirmPlan", BackupArgs(same, out _));
            same.Clock.Now += NativeConfirmation.Cooldown;
        }
        Check.False(samePresence.Latched, "même compte : les refus ne verrouillent pas");
    }

    [Test]
    public async Task UnDossierModifiableAjouteUnPointGrave()
    {
        var exposure = new PackExposure(@"POSTE-01\Utilisateur", @"D:\FILPRO");
        var harness = new Harness(situation: TestSituations.CrossAccount, presence: ArmedPresence());
        harness.Context.Register(exposure);
        await harness.Call("sauvegarder.confirmPlan", BackupArgs(harness, out _));
        var attention = harness.Confirmer!.Sheets.Single().Attention;
        Check.Equal(1, attention.Count(item => item.Severe && item.Text == exposure.Attention), "point grave ajouté par l'hôte");
        Check.True(exposure.Attention.Contains(@"POSTE-01\Utilisateur", StringComparison.Ordinal) && exposure.Attention.Contains(@"D:\FILPRO", StringComparison.Ordinal),
            "compte et dossier nommés");
    }

    [Test]
    public async Task VerrouilleAucuneFenetreNEstOuverte()
    {
        var presence = ArmedPresence();
        presence.Latch(PresenceReason.SessionLocked);
        var harness = new Harness(situation: TestSituations.CrossAccount, presence: presence);
        var id = NewId();
        var plan = ConfirmationSamples.Backup(id);
        harness.Plans.Add(id, plan);
        Check.Equal(FilproErrors.PresenceLocked, (await harness.Call("sauvegarder.confirmPlan", new { planId = id, acknowledgeCapacity = true })).Error?.Code,
            "refus à l'admission");
        var direct = await Check.ThrowsAsync<FilproOperationException>(() => harness.Invoke("sauvegarder.confirmPlan",
            new { planId = id, acknowledgeCapacity = true }, CancellationToken.None), "gestionnaire appelé sans admission");
        Check.Equal(FilproErrors.PresenceLocked, direct.Code, "aucune réservation de fenêtre");
        Check.Equal(0, harness.Confirmer!.Sheets.Count, "aucune fenêtre");
        Check.True(ReferenceEquals(plan, harness.Plans.Take<BackupPlanReview>(id)), "plan intact");

        var late = ArmedPresence();
        var racing = new Harness(situation: TestSituations.CrossAccount, presence: late);
        var starts = 0;
        using (var turn = NativeConfirmation.Begin(racing.Context))
        {
            late.Latch(PresenceReason.Idle);
            var refused = await Check.ThrowsAsync<FilproOperationException>(() => turn.ConfirmThenAsync(
                () => new ConfirmationSheet("sauvegarder.confirmPlan", "Lancer la sauvegarde", "Lancer la sauvegarde", "AB", [], []),
                () => Task.FromResult(++starts), CancellationToken.None), "verrouillé entre la réservation et la fenêtre");
            Check.Equal(FilproErrors.PresenceLocked, refused.Code, "presence.locked");
        }
        Check.Equal(0, racing.Confirmer!.Sheets.Count, "aucune fenêtre");
        Check.Equal(0, starts, "rien n'est lancé");
    }

    /// <summary>Pont réel d'un poste avec des fournisseurs comptés, une horloge manuelle et une fenêtre scriptée.</summary>
    private sealed class Harness
    {
        private readonly BridgeDispatcher dispatcher;
        private readonly List<BridgeResponse> responses = [];

        public Harness(bool confirmer = true, HostSituation? situation = null, IOperatorPresence? presence = null)
        {
            Context = new(ModeResolver.Resolve(["--poste"], null), null, null, Clock, Journal);
            Context.Register(situation ?? TestSituations.SameAccount);
            if (presence is not null) Context.Register(presence);
            Context.Register<IFilproExecutionService>(Executor);
            Context.Register<IFilproUpdateService>(Updates);
            Context.Register<IFilproConfigurationService>(Configuration);
            Context.Register<IApplicationSessionControl>(Session);
            if (confirmer)
            {
                Confirmer = new ScriptedConfirmation();
                Context.Register<IOperatorConfirmation>(Confirmer);
            }
            Registry = OperationModules.CreateRegistry(Context, [typeof(SessionModule).Assembly]);
            dispatcher = new(Registry, response => { lock (responses) responses.Add(response); }, AdmissionGuards.For(Context));
        }

        public ManualClock Clock { get; } = new();
        public RecordingJournal Journal { get; } = new();
        public CountingExecutor Executor { get; } = new();
        public CountingUpdates Updates { get; } = new();
        public CountingConfiguration Configuration { get; } = new();
        public SessionControl Session { get; } = new();
        public ScriptedConfirmation? Confirmer { get; }
        public EngineContext Context { get; }
        public OperationRegistry Registry { get; }

        /// <summary>Le magasin de plans du pont : un seul par composition, partagé par type.</summary>
        public ConfirmedPlanStore Plans => Context.Shared(() => new ConfirmedPlanStore(Context.Clock));

        public async Task<BridgeResponse> Call(string operation, object args)
        {
            lock (responses) responses.Clear();
            await dispatcher.HandleAsync(JsonSerializer.Serialize(new { id = "r1", op = operation, args }), CancellationToken.None);
            lock (responses) return responses.Single(response => response.Kind is "result" or "error");
        }

        /// <summary>Gestionnaire appelé directement, pour observer les exceptions et les tâches en attente.</summary>
        public Task<object?> Invoke(string operation, object args, CancellationToken cancellationToken)
        {
            Check.True(Registry.TryGet(operation, out var definition), operation + " est déclarée");
            return definition!.Handler(JsonSerializer.SerializeToElement(args), new BridgeContext(_ => { }, "r1", operation), cancellationToken);
        }
    }

    /// <summary>Horloge manuelle, au départ à l'heure de création des plans d'exemple : ils sont frais.</summary>
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = ConfirmationSamples.Created;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class RecordingJournal : ITechnicalJournal
    {
        private readonly List<string> lines = [];
        public IReadOnlyList<string> Lines { get { lock (lines) return [.. lines]; } }
        public void Info(string message) { lock (lines) lines.Add("INFO " + message); }
        public void Warning(string message) { lock (lines) lines.Add("WARN " + message); }
        public void Error(string message, Exception exception) { lock (lines) lines.Add("ERREUR " + message); }
    }

    /// <summary>
    /// Fenêtre scriptée : décision fixe, ou attente fournie par le test, et case « Verrouiller FILPRO après le lancement » ;
    /// chaque fiche montrée est conservée.
    /// </summary>
    private sealed class ScriptedConfirmation : IOperatorConfirmation
    {
        private readonly List<ConfirmationSheet> sheets = [];
        public ConfirmationOutcome Outcome { get; set; } = ConfirmationOutcome.Approved;
        public bool LockAfterStart { get; set; }
        public Func<CancellationToken, Task<ConfirmationOutcome>>? Pending { get; set; }
        public IReadOnlyList<ConfirmationSheet> Sheets { get { lock (sheets) return [.. sheets]; } }

        public async Task<ConfirmationResult> ConfirmAsync(ConfirmationSheet sheet, CancellationToken cancellationToken)
        {
            lock (sheets) sheets.Add(sheet);
            var outcome = Pending is { } pending ? await pending(cancellationToken).ConfigureAwait(false) : Outcome;
            return new ConfirmationResult(outcome, LockAfterStart);
        }
    }

    private sealed class ThrowingConfirmation : IOperatorConfirmation
    {
        private int calls;
        public int Calls => Volatile.Read(ref calls);

        public Task<ConfirmationResult> ConfirmAsync(ConfirmationSheet sheet, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            throw new InvalidOperationException("Aucune fenêtre en démonstration.");
        }
    }

    private sealed class SessionControl : IApplicationSessionControl
    {
        private int exits;
        public int Exits => Volatile.Read(ref exits);
        public Task RequestRestartAsync(AppMode mode, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RequestExitAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref exits);
            return Task.CompletedTask;
        }
        public Task AcknowledgeReadyAsync(string generation, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> PickPathAsync(ApplicationPathKind kind, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>Exécutant natif compté : chaque démarrage conserve l'objet reçu, rien n'est exécuté.</summary>
    private sealed class CountingExecutor : IFilproExecutionService
    {
        private readonly List<object> started = [];
        public IReadOnlyList<object> Started { get { lock (started) return [.. started]; } }
        public RepairRollbackReview Review { get; set; } = ConfirmationSamples.Rollback(ReceiptPath);
        public FilproOperationException? ReviewFailure { get; set; }
        public ExecutionStoreInspection Inspection { get; set; } = ConfirmationSamples.Inspection(Bundle);
        public bool Demonstration => false;
        public ExecutionCapabilities Capabilities => new(true, true, true, true, []);
        public event Action<ExecutionJobSnapshot>? Changed { add { } remove { } }
        public IReadOnlyList<ExecutionJobSnapshot> List() => [];
        public ExecutionJobSnapshot? Get(string id) => null;
        public Task<ExecutionStoreInspection> InspectStoreAsync(string bundle, CancellationToken cancellationToken) => Task.FromResult(Inspection);
        public Task<ExecutionJobSnapshot> StartCatalogAsync(StoreCatalogRequest request, ExecutionStartOptions options, CancellationToken cancellationToken) => Start(ExecutionKind.Catalog, request);
        public Task<RecoveryPlanReview> CreateRecoveryPlanAsync(RecoveryPlanRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RepairPlanReview> CreateRepairPlanAsync(RepairPlanRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExecutionJobSnapshot> StartBackupAsync(BackupPlanReview plan, ExecutionStartOptions options, CancellationToken cancellationToken) => Start(ExecutionKind.Backup, plan);
        public Task<ExecutionJobSnapshot> StartRestoreAsync(RestorePlanReview plan, ExecutionStartOptions options, CancellationToken cancellationToken) => Start(ExecutionKind.Restore, plan);
        public Task<ExecutionJobSnapshot> StartRecoveryAsync(RecoveryPlanReview plan, ExecutionStartOptions options, CancellationToken cancellationToken) => Start(ExecutionKind.Recover, plan);
        public Task<ExecutionJobSnapshot> StartRepairAsync(RepairPlanReview plan, ExecutionStartOptions options, CancellationToken cancellationToken) => Start(ExecutionKind.Repair, plan);
        public Task<RepairRollbackReview> ReviewRepairRollbackAsync(RepairRollbackRequest request, CancellationToken cancellationToken) =>
            ReviewFailure is { } failure ? Task.FromException<RepairRollbackReview>(failure) : Task.FromResult(Review);
        public Task<ExecutionJobSnapshot> StartRepairRollbackAsync(RepairRollbackRequest request, ExecutionStartOptions options, CancellationToken cancellationToken) => Start(ExecutionKind.Rollback, request);
        public Task<ExecutionJobSnapshot> CancelAsync(string id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        /// <summary>Échec du lancement par le moteur, après l'accord : rien n'est lancé.</summary>
        public FilproOperationException? StartFailure { get; set; }

        /// <summary>Appelé au début de chaque lancement, pour observer l'état au moment où le moteur reçoit l'objet.</summary>
        public Action? BeforeStart { get; set; }

        private Task<ExecutionJobSnapshot> Start(ExecutionKind kind, object received)
        {
            BeforeStart?.Invoke();
            if (StartFailure is { } failure) return Task.FromException<ExecutionJobSnapshot>(failure);
            lock (started) started.Add(received);
            return Task.FromResult(new ExecutionJobSnapshot(Guid.NewGuid().ToString("N"), kind, "Test", ExecutionState.Pending, "En attente", null, null,
                DateTimeOffset.UnixEpoch, null, false, null, null, null, null, [], null));
        }
    }

    /// <summary>Mises à jour natives comptées : l'état est fixé par le test, aucune archive ni processus.</summary>
    private sealed class CountingUpdates : IFilproUpdateService
    {
        public UpdateStatus Status { get; set; } = new(false, UpdateStage.Idle, null, "2.0.8", null, "Aucune recherche effectuée", null, false, false, null, []);
        public List<string> Prepared { get; } = [];
        public List<string> Installed { get; } = [];
        public List<string> RolledBack { get; } = [];
        public bool Demonstration => false;
        public event Action<UpdateStatus>? Changed { add { } remove { } }
        public Task<UpdateSourcePreference> ReadSourceAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<UpdateSourcePreference> SaveSourceAsync(UpdateSourceKind kind, string? folder, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<UpdateStatus> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Status);
        public Task<UpdateOffer> InspectAsync(UpdateSourceRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(ConfirmationSamples.Offer(Guid.NewGuid().ToString("N")) with { Source = request.Kind });

        public Task<UpdateStatus> PrepareAsync(string offerId, bool confirmed, CancellationToken cancellationToken)
        {
            Confirm(confirmed);
            Prepared.Add(offerId);
            Status = ConfirmationSamples.Prepared(TransactionId);
            return Task.FromResult(Status);
        }

        public Task<UpdateHandoff> InstallAsync(string transactionId, bool confirmed, CancellationToken cancellationToken)
        {
            Confirm(confirmed);
            Installed.Add(transactionId);
            return Task.FromResult(new UpdateHandoff(transactionId, false, true, "Fermer FILPRO."));
        }

        public Task<UpdateHandoff> RollbackAsync(string transactionId, bool confirmed, CancellationToken cancellationToken)
        {
            Confirm(confirmed);
            RolledBack.Add(transactionId);
            return Task.FromResult(new UpdateHandoff(transactionId, false, true, "Fermer FILPRO."));
        }

        private static void Confirm(bool confirmed)
        {
            if (!confirmed) throw new FilproOperationException("update.confirm", "Confirmation explicite requise avant préparation, installation ou retour arrière.");
        }
    }

    /// <summary>Réglages natifs comptés : l'aperçu des défauts est fabriqué, l'écriture seulement comptée.</summary>
    private sealed class CountingConfiguration : IFilproConfigurationService
    {
        public List<string> Applied { get; } = [];
        public bool Demonstration => false;
        public Task<ConfigurationState> ReadAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ConfigurationResult> SaveAsync(SharedConfiguration settings, string expectedRevision, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ConfigurationResult> ExportAsync(string destinationPath, bool confirmed, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ConfigurationPreview> PreviewImportAsync(string sourcePath, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ConfigurationResult> ApplyImportAsync(string previewId, bool confirmed, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ConfigurationPreview> PreviewDefaultsAsync(CancellationToken cancellationToken) => Task.FromResult(ConfirmationSamples.Defaults(Guid.NewGuid().ToString("N")));

        public Task<ConfigurationResult> ApplyDefaultsAsync(string previewId, bool confirmed, CancellationToken cancellationToken)
        {
            if (!confirmed) throw new FilproOperationException("configuration.confirm", "Confirmation explicite requise avant cette action.");
            Applied.Add(previewId);
            return Task.FromResult(new ConfigurationResult(false, new ConfigurationState(false, "revision-2", new SharedConfiguration(), null, []),
                @"D:\FILPRO\wad\config\deployment-defaults.json", null, "Défauts écrits."));
        }

        public Task<ConfigurationResult> DecideBootstrapAsync(string previewId, bool apply, bool confirmed, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
