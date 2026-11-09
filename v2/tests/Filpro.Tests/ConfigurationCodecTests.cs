using System.Text;
using Filpro.Core;

namespace Filpro.Tests;

internal static class ConfigurationCodecTests
{
    [Test] public static void ReglagesPartageablesAllerRetour() { var settings = new SharedConfiguration(Theme: "contrast", Motion: "reduced", Vss: true); Check.Equal(settings, ConfigurationCodec.Decode(ConfigurationCodec.Encode(settings)).Settings, "réglages fermés"); }
    [Test] public static void ChampAbsentConserveLaValeurExistante()
    {
        var decoded = ConfigurationCodec.Decode(Json("\"theme\":\"dark\""), new SharedConfiguration(Vss: true, Encrypted: true));
        Check.True(decoded.Settings.Vss && decoded.Settings.Encrypted, "paramètres absents préservés"); Check.Equal("dark", decoded.Settings.Theme, "champ présent remplacé");
    }
    [Test] public static void SecretEtCheminInconnusRefuses()
    { foreach (var field in new[] { "password", "token", "profileSid", "backupPath", "history", "command" }) Check.Throws<FilproOperationException>(() => ConfigurationCodec.Decode(Json("\"" + field + "\":\"fictif\"")), "champ hors liste interdit"); }
    [Test] public static void ProprietesEnDoubleRefusees()
    { Check.Throws<FilproOperationException>(() => ConfigurationCodec.Decode(Json("\"theme\":\"light\",\"theme\":\"dark\"")), "doublon imbriqué"); Check.Throws<FilproOperationException>(() => ConfigurationCodec.Decode(Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"schemaVersion\":1,\"application\":\"FILPRO\",\"kind\":\"shared-configuration\",\"settings\":{}}")), "doublon racine"); }
    [Test] public static void VersionFutureEtMauvaiseApplicationRefusees()
    { var text = Encoding.UTF8.GetString(Json("")); foreach (var changed in new[] { text.Replace("schemaVersion\":1", "schemaVersion\":2", StringComparison.Ordinal), text.Replace("FILPRO", "AUTRE", StringComparison.Ordinal) }) Check.Throws<FilproOperationException>(() => ConfigurationCodec.Decode(Encoding.UTF8.GetBytes(changed)), "identité/version"); }
    [Test] public static void TypesEtEnumInconnusRefuses()
    { foreach (var value in new[] { "\"vss\":\"true\"", "\"theme\":null", "\"motion\":\"always\"", "\"conflictPolicy\":\"overwriteEverything\"" }) Check.Throws<FilproOperationException>(() => ConfigurationCodec.Decode(Json(value)), "type/enum"); }
    [Test] public static void TailleEtProfondeurBornees()
    { Check.Throws<FilproOperationException>(() => ConfigurationCodec.Decode(new byte[ConfigurationCodec.MaximumBytes + 1]), "taille"); Check.Throws<FilproOperationException>(() => ConfigurationCodec.Decode(Json("\"theme\":[[[[[[0]]]]]]")), "profondeur"); }
    [Test] public static void ApercuIdentifieLesChampsConserves()
    { var before = new SharedConfiguration(); var changes = ConfigurationCodec.Changes(before, ConfigurationCodec.Decode(Json("\"theme\":\"dark\""), before)); Check.Equal("replace", changes.Single(c => c.Key == "theme").Disposition, "remplacement"); Check.Equal("preserve", changes.Single(c => c.Key == "vss").Disposition, "préservation"); }
    private static byte[] Json(string fields) => Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"application\":\"FILPRO\",\"kind\":\"shared-configuration\",\"settings\":{" + fields + "}}");
}
