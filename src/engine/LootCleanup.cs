using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace NomadSave
{
    internal static class LootCleanup
    {
        static HashSet<string> Links(WorldObject o)
        {
            var links = new HashSet<string>(StringComparer.Ordinal); if (o.Parent.Length > 0) links.Add(o.Parent);
            foreach (string key in new[] { "placement", "vehicle", "backpack", "plant" })
            {
                byte[] blob; if (!o.State.TryGetValue(key, out blob)) continue; var r = new DataReader(blob); int version = r.B();
                switch (key)
                {
                    case "placement":
                        if (version != 1) throw new ConversionError("Unknown placement state; loot cleanup stopped.");
                        int kind = r.B();
                        if (kind == 1 || kind == 2)
                        {
                            links.Add(r.String());
                            if (kind == 1) { r.U(); r.Floats(3); r.Floats(4); r.Floats(3); r.Floats(4); }
                            else { r.Floats(3); r.Floats(3); }
                        }
                        else if (kind != 0) throw new ConversionError("Unknown placement link; loot cleanup stopped."); break;
                    case "vehicle":
                        if (version != 1) throw new ConversionError("Unknown vehicle state; loot cleanup stopped.");
                        int count = r.Count(256); for (int i = 0; i < count; i++) { r.B(); links.Add(r.String()); }
                        count = r.Count(256); for (int i = 0; i < count; i++) { r.B(); r.B(); } r.I(); break;
                    case "backpack":
                        if (version != 1 && version != 2) throw new ConversionError("Unknown backpack state; loot cleanup stopped."); r.B();
                        if (version == 2) { int n = r.B(); for (int i = 0; i < n; i++) links.Add(r.String()); } break;
                    case "plant":
                        if (version != 1) throw new ConversionError("Unknown plant state; loot cleanup stopped."); r.B(); r.B(); r.B(); r.Floats(1);
                        int children = r.Count(1000); for (int i = 0; i < children; i++) { links.Add(r.String()); r.Floats(3); r.Floats(3); } break;
                }
                r.End();
            }
            links.Remove(""); return links;
        }
        static void Reason(Dictionary<string, HashSet<string>> reasons, string guid, string reason)
        { HashSet<string> values; if (!reasons.TryGetValue(guid, out values)) reasons.Add(guid, values = new HashSet<string>(StringComparer.Ordinal)); values.Add(reason); }
        internal static Conversion Clean(byte[] data, byte[] legacy, Mapping mapping)
        {
            var save = SaveFormat.Decode(data); var m = SaveFormat.Parse(save.Entries["game.manifest"]); var old = SaveFormat.Parse(SaveFormat.Decode(legacy).Entries["game.manifest"]);
            if (m.Format != 16 || old.Format != 5 || m.Seed != old.Seed) throw new ConversionError("Loot cleanup requires this converted world and its matching format-5 original.");
            var objects = m.Objects.ToDictionary(o => o.Guid); var originals = old.Objects.ToDictionary(o => o.Guid);
            var graph = objects.Keys.ToDictionary(g => g, g => new HashSet<string>(StringComparer.Ordinal)); var reasons = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal); var nativeSeeds = new HashSet<int>();
            foreach (var o in m.Objects)
            {
                foreach (string other in Links(o)) if (objects.ContainsKey(other)) { graph[o.Guid].Add(other); graph[other].Add(o.Guid); }
                if (o.State.ContainsKey("vehicle")) Reason(reasons, o.Guid, "vehicle");
                WorldObject original;
                if (!originals.TryGetValue(o.Guid, out original)) Reason(reasons, o.Guid, "created-after-conversion");
                else if (o.Position.Zip(original.Position, (a, b) => (a - b) * (a - b)).Sum() > 1) Reason(reasons, o.Guid, "moved-since-legacy-save");
                byte[] marker;
                if (o.State.TryGetValue("_wildLootSeed", out marker))
                {
                    var r = new DataReader(marker); if (r.B() != 1) throw new ConversionError("Unknown wild-loot marker; loot cleanup stopped.");
                    nativeSeeds.Add(r.I()); r.End(); Reason(reasons, o.Guid, "current-world-loot");
                }
            }
            foreach (var p in ConverterCore.Players(m))
                foreach (string guid in new[] { p.Equipped, p.Seat, p.Backpack }.Concat(p.Hotbar)) if (objects.ContainsKey(guid)) Reason(reasons, guid, "player-equipment");
            var protectedIds = new HashSet<string>(reasons.Keys, StringComparer.Ordinal); var pending = new Stack<string>(protectedIds);
            while (pending.Count > 0)
                foreach (string other in graph[pending.Pop()]) if (protectedIds.Add(other)) { Reason(reasons, other, "linked-to-kept-object"); pending.Push(other); }
            var removed = m.Objects.Where(o => !protectedIds.Contains(o.Guid)).ToList(); var beforeSeeds = new HashSet<int>(m.Seeds); var legacySeeds = new HashSet<int>(old.Seeds);
            m.Objects = m.Objects.Where(o => protectedIds.Contains(o.Guid)).ToList(); m.Seeds = m.Seeds.Where(s => !legacySeeds.Contains(s) || nativeSeeds.Contains(s)).ToList();
            m.Seeds.AddRange(nativeSeeds.Except(m.Seeds).OrderBy(s => s));
            save.Entries["game.manifest"] = SaveFormat.Serialize(m); var output = SaveFormat.Encode(save); SaveFormat.Verify(output, save);
            var counts = Json.D(); foreach (var values in reasons.Values) foreach (string reason in values) Json.Increment(counts, reason);
            var report = Json.D("version", 1, "legacySourceSha256", SaveFormat.Sha(legacy), "removedCount", removed.Count, "keptCount", m.Objects.Count,
                "resetLegacySeeds", beforeSeeds.Except(m.Seeds).Count(), "keptSeedCount", m.Seeds.Count, "protectedReasons", counts,
                "removed", removed.Select(o => (object)Json.D("guid", o.Guid, "asset", o.Asset, "name", mapping.Name(o.Asset), "position", o.Position)).ToList());
            return new Conversion { Output = output, Report = report };
        }
        internal static string Fingerprint(string source)
        {
            Dictionary<string, object> report = SaveOperations.TryReadJson(Path.Combine(Path.GetDirectoryName(source), "conversion-report.json"));
            string value = Json.S(report, "originalSourceSha256"); if (value.Length == 0) value = Json.S(report, "sourceSha256");
            if (!Regex.IsMatch(value, "\\A[0-9a-fA-F]{64}\\z")) throw new ConversionError("No converter lineage found. Select a converted slot with conversion-report.json."); return value.ToLowerInvariant();
        }
        internal static byte[] FindOriginal(string source, string backups, string explicitSource)
        {
            string file = SaveOperations.SourceFile(source); byte[] data = SaveOperations.ReadSave(file);
            if (SaveFormat.Parse(SaveFormat.Decode(data).Entries["game.manifest"]).Format == 5) return data;
            string fingerprint = Fingerprint(file); var candidates = new List<string>();
            if (!string.IsNullOrEmpty(explicitSource)) candidates.Add(SaveOperations.SourceFile(explicitSource));
            else
            {
                string parent = Directory.GetParent(Path.GetDirectoryName(file)).FullName;
                foreach (string slot in Directory.GetDirectories(parent, "slot_*").OrderBy(s => s, StringComparer.Ordinal))
                    foreach (string name in new[] { "save.dat", "autosave.dat" }) { string path = Path.Combine(slot, name); if (File.Exists(path)) candidates.Add(path); }
                if (Directory.Exists(backups)) foreach (string name in new[] { "save.dat", "autosave.dat" })
                    foreach (string folder in Directory.GetDirectories(backups).OrderBy(s => s, StringComparer.Ordinal)) { string path = Path.Combine(folder, name); if (File.Exists(path)) candidates.Add(path); }
            }
            foreach (string candidate in candidates)
            {
                byte[] bytes = SaveOperations.ReadSave(candidate);
                if (SaveFormat.Sha(bytes) != fingerprint) continue;
                if (SaveFormat.Parse(SaveFormat.Decode(bytes).Entries["game.manifest"]).Format != 5) break; return bytes;
            }
            throw new ConversionError("The exact format-5 original was not found in sibling slots or converter backups. Restore the original backup, or use --legacy-source. No cleanup was performed.");
        }
        internal static Conversion Apply(string source, Conversion conversion, Mapping mapping, string backups, string explicitSource)
        {
            var clean = Clean(conversion.Output, FindOriginal(source, backups, explicitSource), mapping); var r = conversion.Report; r["operation"] = "cleanup-legacy-loot"; r["lootCleanup"] = clean.Report;
            var m = SaveFormat.Parse(SaveFormat.Decode(clean.Output).Entries["game.manifest"]); ConverterCore.CompleteReport(r, m, clean.Output);
            r["verification"] = "CRC and complete read-back passed. Kept object records, players and all sidecar bytes are unchanged by cleanup.";
            Json.List(r, "warnings").AddRange(new object[] {
                "Cleanup removes imported loose ground items and their linked contents, including intentional stashes. The legacy save has no reliable visited/touched flag.",
                "All vehicles, their linked parts/cargo, player equipment/backpack contents, current-only objects and imported objects moved over 1 metre are kept.",
                "Old loot reservations are reset so the updated map can generate loot at its current locations. Previously cleared locations may receive new loot.",
                "Floating objects that are protected or have already moved may remain. Cleanup does not move vehicles or restore old buildings." });
            conversion.Output = clean.Output; return conversion;
        }
    }
}
