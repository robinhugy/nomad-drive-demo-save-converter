using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace NomadSave
{
    internal sealed class Mapping
    {
        internal Dictionary<string, object> Raw, Assets, Replacements, OldNames, PlantPhases;
        internal string Build, CatalogSha256, GameDataDirectory, Edition;
        internal ulong Economy;
        internal static Mapping Load()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("NomadDriveSaveConverter.Mapping"))
            using (var reader = new StreamReader(stream, Encoding.UTF8)) return FromJson(reader.ReadToEnd());
        }
        internal static Mapping LoadEarlyAccess()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("NomadDriveSaveConverter.EarlyAccessMapping"))
            using (var reader = new StreamReader(stream, Encoding.UTF8)) return FromJson(reader.ReadToEnd());
        }
        internal static Mapping FromJson(string text)
        {
            var d = Json.Read(text);
            return new Mapping { Raw = d, GameDataDirectory = Json.S(d, "gameDataDirectory", "Nomad Drive Demo_Data"), Edition = Json.S(d, "edition", "Demo"), Build = Json.S(d, "build"), CatalogSha256 = Json.S(d, "catalogSha256"), Economy = Convert.ToUInt64(d["economyPacked"]), Assets = Json.Dict(d["assets"]), Replacements = Json.Dict(d["replacements"]), OldNames = Json.Dict(d["oldNames"]), PlantPhases = Json.Dict(d["plantPhases"]) };
        }
        internal Dictionary<string, object> Asset(string guid) { return Json.Dict(Assets[guid]); }
        internal string Name(string guid) { return Assets.ContainsKey(guid) ? Json.S(Asset(guid), "name") : Json.S(OldNames, guid, guid); }
    }
    internal sealed class Conversion
    {
        internal byte[] Output;
        internal Dictionary<string, object> Report;
    }
    internal static class ConverterCore
    {
        internal static readonly HashSet<string> Passthrough = new HashSet<string>(new[] { "polaroid", "liquid", "picture", "sunvisor", "hood", "handbrake", "engineheat", "door", "treasure", "charge", "condition", "chest", "plate", "food" }, StringComparer.Ordinal);
        internal static IEnumerable<Player> Players(Manifest m) { return (m.Host == null ? Enumerable.Empty<Player>() : new[] { m.Host }).Concat(m.Remote); }
        internal static int PlayerSlots(Manifest m, int? requested)
        {
            string host = m.Host == null ? "" : m.Host.Puid;
            int required = 1 + m.Remote.Where(p => p.Puid.Length > 0 && p.Puid != host).Select(p => p.Puid).Distinct(StringComparer.Ordinal).Count();
            if (required > 8) throw new ConversionError("This save contains more players than the supported limit of 8.");
            int current = m.Format == 16 ? (int)(m.Economy >> 56) : 0;
            if (!requested.HasValue) return Math.Max(2, Math.Max(required, Math.Min(8, current)));
            if (requested.Value < 1 || requested.Value > 8) throw new ConversionError("Player slots must be an integer from 1 to 8.");
            if (requested.Value < required) throw new ConversionError("Choose at least " + required + " player slots to keep all saved players."); return requested.Value;
        }
        internal static ulong WithPlayers(ulong packed, int players) { return (packed & 0x00ffffffffffffffUL) | ((ulong)players << 56); }
        static double[] QMul(double[] a, double[] b)
        {
            double x = a[0], y = a[1], z = a[2], w = a[3], X = b[0], Y = b[1], Z = b[2], W = b[3];
            return new[] { w * X + x * W + y * Z - z * Y, w * Y - x * Z + y * W + z * X, w * Z + x * Y - y * X + z * W, w * W - x * X - y * Y - z * Z };
        }
        static double[] QInv(double[] q)
        { double norm = q.Sum(x => x * x); if (norm < 0.00001) throw new ConversionError("Invalid rotation."); return new[] { -q[0] / norm, -q[1] / norm, -q[2] / norm, q[3] / norm }; }
        static double[] QRot(double[] q, double[] v) { return QMul(QMul(q, new[] { v[0], v[1], v[2], 0 }), QInv(q)).Take(3).ToArray(); }
        static double[] Sub(double[] a, double[] b) { return a.Zip(b, (x, y) => x - y).ToArray(); }
        static double[] Vector(object value) { return Json.Items(value).Select(Convert.ToDouble).ToArray(); }
        static uint BigUInt(byte[] data, int pos) { return ((uint)data[pos] << 24) | ((uint)data[pos + 1] << 16) | ((uint)data[pos + 2] << 8) | data[pos + 3]; }
        static void ValidPng(byte[] data)
        {
            if (!data.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) throw new ConversionError("Invalid paint image.");
            int p = 8; bool first = true;
            while (p <= data.Length - 12)
            {
                uint size = BigUInt(data, p); if (size > data.Length - p - 12) throw new ConversionError("Truncated paint image.");
                int n = (int)size; string kind = Encoding.ASCII.GetString(data, p + 4, 4);
                if (SaveFormat.Crc(data, p + 4, n + 4) != BigUInt(data, p + n + 8)) throw new ConversionError("Invalid PNG checksum.");
                if (first && (kind != "IHDR" || n != 13)) throw new ConversionError("PNG header is missing.");
                first = false; p += n + 12;
                if (kind == "IEND") { if (n != 0 || p != data.Length) throw new ConversionError("Invalid PNG end chunk."); return; }
            }
            throw new ConversionError("Truncated PNG file.");
        }
        static byte[] VehicleState(byte[] blob, HashSet<string> existing)
        {
            var r = new DataReader(blob); int version = r.B(); if (version != 1 && version != 2) throw new ConversionError("Unknown legacy vehicle state.");
            var slots = new List<KeyValuePair<byte, string>>(); int count = r.Count(256);
            for (int i = 0; i < count; i++) slots.Add(new KeyValuePair<byte, string>(r.B(), r.String()));
            var interactables = new List<byte[]>();
            if (version >= 2) { count = r.Count(256); for (int i = 0; i < count; i++) interactables.Add(new[] { r.B(), r.B() }); }
            r.End(); var w = new DataWriter(); w.B(1); slots = slots.Where(s => existing.Contains(s.Value)).ToList(); w.I(slots.Count);
            foreach (var slot in slots) { w.B(slot.Key); w.String(slot.Value); }
            w.I(interactables.Count); foreach (var pair in interactables) w.Raw(pair); w.I(0); return w.Finish();
        }
        static byte[] PlantState(byte[] blob, HashSet<string> existing, Mapping mapping)
        {
            var r = new DataReader(blob); int version = r.B(); if (version != 1 && version != 2) throw new ConversionError("Unknown legacy plant state.");
            int plant = r.B(), state = r.B(), phase = r.B(); double elapsed = r.Floats(1)[0];
            var links = new List<Tuple<string, double[], double[]>>();
            if (version >= 2) { int count = r.Count(1000); for (int i = 0; i < count; i++) links.Add(Tuple.Create(r.String(), r.Floats(3), r.Floats(3))); }
            r.End(); var durations = Json.Items(Json.Get(mapping.PlantPhases, plant.ToString())).Select(Convert.ToDouble).ToArray();
            if (plant != 0 && durations.Length == 0) throw new ConversionError("Unknown plant type.");
            elapsed += durations.Take(Math.Min(phase, Math.Max(0, durations.Length - 1))).Sum();
            var w = new DataWriter(); w.B(1); w.B(plant); w.B(state); w.B(phase); w.Floats(new[] { Math.Max(0, elapsed) });
            links = links.Where(p => existing.Contains(p.Item1)).ToList(); w.I(links.Count);
            foreach (var link in links) { w.String(link.Item1); w.Floats(link.Item2); w.Floats(link.Item3); } return w.Finish();
        }
        static byte[] PlacementState(byte[] blob, WorldObject o, Dictionary<string, WorldObject> objects, Mapping mapping, Dictionary<string, object> counters)
        {
            var r = new DataReader(blob); if (r.B() != 1) throw new ConversionError("Unknown legacy placement state.");
            string parent = r.String(); if (parent.Length == 0) { r.End(); return new byte[] { 1, 0 }; }
            r.B(); var oldPos = r.Floats(3); var oldEuler = r.Floats(3); r.End(); WorldObject support;
            if (!objects.TryGetValue(parent, out support)) { Json.Increment(counters, "lostSupports"); return new byte[] { 1, 0 }; }
            var inv = QInv(support.Rotation); var rootPos = QRot(inv, Sub(o.Position, support.Position)); var rootRot = QMul(inv, o.Rotation);
            var planes = Json.Items(mapping.Asset(support.Asset)["planes"]).Select(Json.Dict).ToList(); var w = new DataWriter(); w.B(1);
            if (planes.Count > 0)
            {
                var plane = planes.OrderBy(p => Sub(rootPos, Vector(p["position"])).Sum(x => x * x)).First();
                var planeInv = QInv(Vector(plane["rotation"]));
                w.B(1); w.String(parent); w.U(Convert.ToUInt32(plane["planeId"]));
                w.Floats(QRot(planeInv, Sub(rootPos, Vector(plane["position"])))); w.Floats(QMul(planeInv, rootRot)); w.Floats(rootPos); w.Floats(rootRot); Json.Increment(counters, "placements");
            }
            else if (support.State.ContainsKey("vehicle")) { w.B(2); w.String(parent); w.Floats(oldPos); w.Floats(oldEuler); Json.Increment(counters, "vehicleRestFallbacks"); }
            else { w.B(0); Json.Increment(counters, "lostSupports"); } return w.Finish();
        }
        static byte[] RestorationState(byte[] blob, WorldObject o, SaveData save, Mapping mapping, Dictionary<string, object> report)
        {
            var r = new DataReader(blob); if (r.B() != 1) throw new ConversionError("Unknown legacy restoration state.");
            int count = r.Count(1024); var surfaces = Json.Items(mapping.Asset(o.Asset)["surfaces"]).Select(Json.Dict).ToArray(); var rows = new List<Tuple<uint, bool[]>>();
            if (count > 1) report["paintHierarchyNeedsVisualCheck"] = true;
            for (int index = 0; index < count; index++)
            {
                r.Floats(1); var flags = new bool[6]; for (int t = 0; t < 6; t++) flags[t] = r.Bool();
                if (index >= surfaces.Length) { Json.List(report, "unmappedPaintSurfaces").Add(Json.D("guid", o.Guid, "asset", mapping.Name(o.Asset), "oldIndex", index)); continue; }
                uint id = Convert.ToUInt32(surfaces[index]["surfaceId"]);
                for (int tex = 0; tex < 6; tex++)
                {
                    string oldKey = "restoration." + o.Guid + ".surf" + index + ".tex" + tex, newKey = "restoration." + o.Guid + ".s" + id + ".tex" + tex;
                    if (!flags[tex]) continue;
                    byte[] png; if (!save.Entries.TryGetValue(oldKey, out png)) throw new ConversionError("Missing paint image: " + oldKey);
                    ValidPng(png); byte[] conflicting;
                    if (save.Entries.TryGetValue(newKey, out conflicting) && !conflicting.SequenceEqual(png)) throw new ConversionError("Conflicting paint surface identifier.");
                    save.Entries[newKey] = png; Json.Dict(report["paintHashes"])[newKey] = SaveFormat.Sha(png);
                }
                rows.Add(Tuple.Create(id, flags)); Json.Increment(Json.Dict(report["counters"]), "paintSurfaces");
            }
            r.End(); var w = new DataWriter(); w.B(1); w.I(rows.Count); foreach (var row in rows) { w.U(row.Item1); foreach (bool flag in row.Item2) w.Bool(flag); } return w.Finish();
        }
        internal static Dictionary<string, object> BaseReport(byte[] data, Manifest m, Mapping mapping)
        {
            return Json.D("converterVersion", BuildInfo.Version, "targetBuild", mapping.Build, "sourceSha256", SaveFormat.Sha(data), "sourceFormat", m.Format, "targetFormat", 16,
                "sourceObjects", m.Objects.Count, "replacements", new List<object>(), "omitted", new List<object>(), "counters", Json.D(), "unmappedPaintSurfaces", new List<object>(), "paintHashes", Json.D(), "warnings", new List<object>(), "paintHierarchyNeedsVisualCheck", false);
        }
        internal static Conversion Transform(byte[] data, Mapping mapping, bool replace, int? playerSlots)
        {
            var save = SaveFormat.Decode(data); var m = SaveFormat.Parse(save.Entries["game.manifest"]);
            if (m.Format != 5) throw new ConversionError("This save is already in the current format. Conversion is not needed.");
            var originals = m.Objects.ToDictionary(o => o.Guid, o => Tuple.Create(o.Position.ToArray(), o.Rotation.ToArray(), o.Seed));
            var originalEntries = new Dictionary<string, byte[]>(save.Entries); var report = BaseReport(data, m, mapping); var counters = Json.Dict(report["counters"]);
            var kept = new List<WorldObject>();
            foreach (var o in m.Objects)
            {
                if (!mapping.Assets.ContainsKey(o.Asset))
                {
                    string target = replace ? Json.S(mapping.Replacements, o.Asset) : "";
                    if (target.Length > 0) { Json.List(report, "replacements").Add(Json.D("guid", o.Guid, "oldAsset", o.Asset, "old", mapping.Name(o.Asset), "newAsset", target, "new", mapping.Name(target))); o.Asset = target; }
                    else { Json.List(report, "omitted").Add(Json.D("guid", o.Guid, "asset", o.Asset, "name", mapping.Name(o.Asset))); continue; }
                }
                kept.Add(o);
            }
            m.Objects = kept; if (kept.Count == 0) throw new ConversionError("This save contains no transferable objects.");
            var objects = kept.ToDictionary(o => o.Guid); var existing = new HashSet<string>(objects.Keys, StringComparer.Ordinal);
            foreach (string key in save.Entries.Keys.ToArray())
                if (key != "game.manifest" && !key.StartsWith("restoration.", StringComparison.Ordinal) && !key.StartsWith("picture.", StringComparison.Ordinal) && !key.StartsWith("polaroid.", StringComparison.Ordinal))
                { save.Entries["legacy.converter." + key] = save.Entries[key]; save.Entries.Remove(key); Json.Increment(counters, "archivedGlobalStates"); }
            foreach (var o in kept)
            {
                if (o.Parent.Length > 0 && !existing.Contains(o.Parent)) { o.Parent = ""; o.Kind = 0; o.Sub = 0; Json.Increment(counters, "orphanLinksCleared"); }
                foreach (string key in o.State.Keys.ToArray())
                {
                    byte[] blob = o.State[key];
                    switch (key)
                    {
                        case "vehicle": o.State[key] = VehicleState(blob, existing); break;
                        case "placement": o.State[key] = PlacementState(blob, o, objects, mapping, counters); break;
                        case "restoration": o.State[key] = RestorationState(blob, o, save, mapping, report); break;
                        case "plant": o.State[key] = PlantState(blob, existing, mapping); break;
                        case "chestloot":
                            {
                                var r = new DataReader(blob); if (r.B() != 1) throw new ConversionError("Unknown legacy chest loot state."); bool opened = r.Bool(); r.End();
                                var w = new DataWriter(); w.B(1); w.Bool(opened); w.I(o.Seed); w.I((int)Math.Max(0, Math.Min(int.MaxValue, Math.Round(o.Position[2], MidpointRounding.ToEven)))); o.State[key] = w.Finish(); break;
                            }
                        case "food":
                            {
                                int expected = Convert.ToBoolean(mapping.Asset(o.Asset)["organicFood"]) ? 12 : 7;
                                if ((blob.Length != 7 && blob.Length != 12) || blob[0] != 1) throw new ConversionError("Unknown legacy food state.");
                                var value = new byte[expected]; Buffer.BlockCopy(blob, 0, value, 0, Math.Min(blob.Length, expected)); o.State[key] = value; break;
                            }
                        case "plate":
                            {
                                var r = new DataReader(blob); if (r.B() != 1) throw new ConversionError("Unknown legacy license plate state.");
                                int city = r.B(); r.Bool(); r.B(); r.String(); r.End(); if (city > 9) throw new ConversionError("Unknown license plate city."); break;
                            }
                        default:
                            if (key.StartsWith("nomadtrainer.", StringComparison.Ordinal))
                            {
                                if (key == "nomadtrainer.paint-amount") { var r = new DataReader(blob); if (r.B() != 1) throw new ConversionError("Unknown trainer paint capacity state."); r.Floats(1); r.End(); }
                                else if (key != "nomadtrainer.delete-tool") throw new ConversionError("Unknown trainer state: " + key);
                            }
                            else if (!Passthrough.Contains(key)) throw new ConversionError("Unknown object state: " + key + ". The original save is unchanged."); break;
                    }
                }
                Json.Increment(counters, "states", o.State.Count);
            }
            foreach (var p in Players(m))
            {
                if (p.Equipped.Length > 0 && !existing.Contains(p.Equipped)) p.Equipped = "";
                if (p.Seat.Length > 0 && !existing.Contains(p.Seat)) p.Seat = "";
                if (p.Survival.Length > 0 && (p.Survival.Length != 21 || p.Survival[0] != 1)) throw new ConversionError("Unknown legacy player state.");
            }
            int players = PlayerSlots(m, playerSlots); m.Format = 16; m.Economy = WithPlayers(mapping.Economy, players);
            report["operation"] = "convert"; report["playerSlots"] = players; report["multiplayerEnabled"] = players > 1;
            save.Entries["game.manifest"] = SaveFormat.Serialize(m); var output = SaveFormat.Encode(save); SaveFormat.Verify(output, save);
            foreach (var o in m.Objects)
            {
                var before = originals[o.Guid]; if (!o.Position.SequenceEqual(before.Item1) || !o.Rotation.SequenceEqual(before.Item2) || o.Seed != before.Item3) throw new ConversionError("An object pose or identifier changed during conversion.");
            }
            foreach (var entry in originalEntries.Where(e => e.Key != "game.manifest"))
            {
                string destination = save.Entries.ContainsKey(entry.Key) ? entry.Key : "legacy.converter." + entry.Key;
                if (!save.Entries.ContainsKey(destination) || !save.Entries[destination].SequenceEqual(entry.Value)) throw new ConversionError("Original sidecar data was lost.");
            }
            CompleteReport(report, m, output);
            report["verification"] = "Container CRC, complete format-16 reparse, object identities/world poses, original sidecars and converted PNG SHA-256 verified. In-game loading not verified.";
            Json.List(report, "warnings").AddRange(new object[] {
                "Inspect the converted world in game; terrain and buildings may differ after the game update.",
                "Legacy quest and shared odometer data are archived; the new systems start from their defaults.",
                "Placed items are linked to a current plane on the same container while retaining their saved world pose.",
                "The old paint age value no longer exists. Multi-surface paint mapping uses the current prefab hierarchy and needs a visual check." });
            if (Json.List(report, "unmappedPaintSurfaces").Count > 0) Json.List(report, "warnings").Add(Json.List(report, "unmappedPaintSurfaces").Count + " old paint surface(s) have no match in the current model; their images remain in the original backup and archived sidecars.");
            if (Convert.ToInt32(Json.Get(counters, "lostSupports", 0)) > 0) Json.List(report, "warnings").Add(counters["lostSupports"] + " item support(s) could not be restored; the items retain their saved world pose.");
            return new Conversion { Output = output, Report = report };
        }
        internal static void CompleteReport(Dictionary<string, object> report, Manifest m, byte[] output)
        { report["outputObjects"] = m.Objects.Count; report["vehicles"] = m.Objects.Count(o => o.State.ContainsKey("vehicle")); report["remotePlayers"] = m.Remote.Count; report["outputSha256"] = SaveFormat.Sha(output); report["outputBytes"] = output.Length; }
        internal static Conversion Repair(byte[] data, Mapping mapping, int? requested)
        {
            var save = SaveFormat.Decode(data); byte[] before = save.Entries["game.manifest"]; var m = SaveFormat.Parse(before);
            if (m.Format != 16) throw new ConversionError("Multiplayer repair requires a format-16 save.");
            int players = PlayerSlots(m, requested); if (players < 2) throw new ConversionError("Multiplayer repair requires at least 2 player slots.");
            ulong previous = m.Economy >> 56; m.Economy = WithPlayers(m.Economy, players); save.Entries["game.manifest"] = SaveFormat.Serialize(m);
            if (!before.Take(before.Length - 1).SequenceEqual(save.Entries["game.manifest"].Take(before.Length - 1)) || before.Length != save.Entries["game.manifest"].Length) throw new ConversionError("Multiplayer repair changed data outside the player limit.");
            var output = SaveFormat.Encode(save); SaveFormat.Verify(output, save); var report = BaseReport(data, m, mapping); CompleteReport(report, m, output);
            report["operation"] = "repair-multiplayer"; report["previousPlayerSlots"] = previous; report["playerSlots"] = players; report["multiplayerEnabled"] = true;
            report["verification"] = "The player-limit byte, container CRC and exact read-back were verified. All other manifest and sidecar bytes are unchanged.";
            Json.List(report, "warnings").Add("Only the saved player limit is changed. Player progress, inventory, vehicles, paint and resource densities are preserved."); return new Conversion { Output = output, Report = report };
        }
        internal static string ReportText(Dictionary<string, object> r)
        {
            var replacements = Json.Items(r["replacements"]).Select(Json.Dict).ToList(); var omitted = Json.Items(r["omitted"]).Select(Json.Dict).ToList();
            var lines = new List<string> { "Nomad Drive Save Converter " + BuildInfo.Version, "", "World: " + Json.S(r, "displayName"), "Action: " + Json.S(r, "operation"), "Format: " + r["sourceFormat"] + " -> " + r["targetFormat"] + "; target " + r["targetBuild"], "Objects: " + r["sourceObjects"] + " -> " + r["outputObjects"], "Vehicles: " + r["vehicles"], "Player slots: " + (r.ContainsKey("previousPlayerSlots") ? Json.S(r, "previousPlayerSlots") + " -> " : "") + Json.S(r, "playerSlots", "unknown"), "Replaced: " + replacements.Count + "; omitted: " + omitted.Count, "Paint images transferred: " + Json.Dict(r["paintHashes"]).Count, "New slot: " + Json.S(r, "destination"), "Backup: " + Json.S(r, "backup"), "", "REPLACEMENTS" };
            var groups = replacements.GroupBy(x => Tuple.Create(Json.S(x, "old"), Json.S(x, "new"))).OrderBy(g => g.Key.Item1, StringComparer.Ordinal).ThenBy(g => g.Key.Item2, StringComparer.Ordinal).ToList();
            if (groups.Count == 0) lines.Add("None."); foreach (var g in groups) lines.Add(g.Count() + " x " + g.Key.Item1 + " -> " + g.Key.Item2);
            lines.Add(""); lines.Add("OMITTED OBJECTS"); var omitGroups = omitted.GroupBy(x => Json.S(x, "name")).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
            if (omitGroups.Count == 0) lines.Add("None."); foreach (var g in omitGroups) lines.Add(g.Count() + " x " + g.Key);
            var cleanup = Json.Get(r, "lootCleanup") as Dictionary<string, object>;
            if (cleanup != null)
            {
                lines.AddRange(new[] { "", "LEGACY WORLD-LOOT CLEANUP", "Removed imported objects: " + cleanup["removedCount"] + "; kept: " + cleanup["keptCount"], "Old loot reservations reset: " + cleanup["resetLegacySeeds"], "Loose ground stashes can be removed. Vehicles, linked cargo and player equipment are protected." });
                var removed = Json.Items(cleanup["removed"]).Select(Json.Dict).GroupBy(o => Json.S(o, "name")).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
                if (removed.Count == 0) lines.Add("No removable imported loot found."); foreach (var g in removed) lines.Add(g.Count() + " x " + g.Key);
            }
            lines.Add(""); lines.Add("LIMITATIONS AND CHANGES"); lines.AddRange(Json.Items(r["warnings"]).Select(Convert.ToString));
            lines.AddRange(new[] { "", "VERIFICATION", Json.S(r, "verification"), "File validation does not replace an in-game check of this world.", "", "Source SHA-256: " + r["sourceSha256"], "Converted SHA-256: " + r["outputSha256"] }); return string.Join("\n", lines) + "\n";
        }
    }
}
