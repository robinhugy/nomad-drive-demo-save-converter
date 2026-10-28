using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace NomadSave
{
    internal sealed class SlotInfo
    {
        internal Dictionary<string, object> Row, Report;
        internal Manifest Manifest;
        internal string Path { get { return Json.S(Row, "path"); } }
        internal string Slot { get { return Json.S(Row, "slot"); } }
        internal string Name { get { return Json.S(Row, "name"); } }
        internal double Modified { get { return Convert.ToDouble(Row["modified"]); } }
    }
    internal static class SaveOperations
    {
        internal static readonly string DefaultSaves = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "Indie Devil", "Nomad Drive Demo", "EvilSave");
        internal static readonly string DefaultBackups = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NomadDriveSaveConverter", "Backups");
        internal static readonly UTF8Encoding Utf8 = new UTF8Encoding(false), Utf8Bom = new UTF8Encoding(true);
        internal static string DiscoverGame()
        {
            var roots = new List<string>();
            foreach (var spec in new[] {
                Tuple.Create(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
                Tuple.Create(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
                Tuple.Create(Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath") })
            {
                try { using (var key = spec.Item1.OpenSubKey(spec.Item2)) { var value = key == null ? null : key.GetValue(spec.Item3) as string; if (!string.IsNullOrWhiteSpace(value)) roots.Add(value); } }
                catch (System.Security.SecurityException) { }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }
            foreach (string variable in new[] { "ProgramFiles(x86)", "ProgramFiles" }) { string value = Environment.GetEnvironmentVariable(variable); if (!string.IsNullOrWhiteSpace(value)) roots.Add(Path.Combine(value, "Steam")); }
            var libraries = new List<string>(roots);
            foreach (string root in roots)
            {
                string config = Path.Combine(root, "steamapps", "libraryfolders.vdf"); if (!File.Exists(config)) continue;
                try { foreach (Match match in Regex.Matches(File.ReadAllText(config, Encoding.UTF8), "\"path\"\\s*\"((?:\\\\.|[^\"\\\\])*)\"")) libraries.Add(match.Groups[1].Value.Replace(@"\\", @"\")); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            foreach (string library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string candidate = Path.Combine(library, "steamapps", "common", "Nomad Drive Demo");
                if (File.Exists(Catalog(candidate))) return candidate;
            }
            return null;
        }
        static string Catalog(string game) { return Path.Combine(game, "Nomad Drive Demo_Data", "StreamingAssets", "aa", "catalog.bin"); }
        internal static void VerifyBuild(string game, Mapping mapping)
        {
            if (string.IsNullOrWhiteSpace(game)) game = DiscoverGame();
            if (game == null) throw new ConversionError("Game folder not found. Choose the Nomad Drive Demo installation folder.");
            string catalog = Catalog(game); if (!File.Exists(catalog)) throw new ConversionError("Invalid game folder: catalog.bin was not found.");
            if (SaveFormat.Sha(File.ReadAllBytes(catalog)) != mapping.CatalogSha256) throw new ConversionError("This game build is not supported. The converter needs an updated compatibility mapping.");
        }
        internal static bool GameRunning()
        {
#if TEST_HARNESS
            if (TestGameRunning.HasValue) return TestGameRunning.Value;
#endif
            // Read process names only; no game handle, injection or process memory access.
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    string name;
                    try { name = process.ProcessName.Replace(" ", "").ToLowerInvariant(); }
                    catch (InvalidOperationException) { continue; }
                    catch (System.ComponentModel.Win32Exception) { throw new ConversionError("Could not check whether the game is running."); }
                    if (name == "nomaddrivedemo" || name == "nomaddrive") return true;
                }
            }
            return false;
        }
#if TEST_HARNESS
        internal static bool? TestGameRunning;
#endif
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder name, uint length, uint flags);
        internal static string CanonicalPath(string path)
        {
            string existing = Path.GetFullPath(path); var suffix = new Stack<string>();
            while (!Directory.Exists(existing) && !File.Exists(existing))
            {
                string parent = Path.GetDirectoryName(existing); if (string.IsNullOrEmpty(parent)) throw new ConversionError("The destination drive does not exist.");
                suffix.Push(Path.GetFileName(existing)); existing = parent;
            }
            using (var handle = CreateFile(existing, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero))
            {
                if (handle.IsInvalid) throw new ConversionError("Could not resolve the save or backup path.");
                var name = new StringBuilder(32768); uint length = GetFinalPathNameByHandle(handle, name, (uint)name.Capacity, 0);
                if (length == 0 || length >= name.Capacity) throw new ConversionError("Could not resolve the save or backup path.");
                existing = name.ToString();
                if (existing.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) existing = @"\\" + existing.Substring(8);
                else if (existing.StartsWith(@"\\?\", StringComparison.Ordinal)) existing = existing.Substring(4);
            }
            while (suffix.Count > 0) existing = Path.Combine(existing, suffix.Pop()); return Path.GetFullPath(existing);
        }
        internal static byte[] ReadSave(string file)
        { if (new FileInfo(file).Length > SaveFormat.MaxData) throw new ConversionError("Save file is too large."); return File.ReadAllBytes(file); }
        internal static string SourceFile(string source)
        {
            string path = CanonicalPath(source); if (File.Exists(path)) return path;
            if (!Directory.Exists(path)) throw new ConversionError("Save not found.");
            var files = new[] { Path.Combine(path, "save.dat"), Path.Combine(path, "autosave.dat") }.Where(File.Exists).ToArray();
            if (files.Length == 0) throw new ConversionError("The folder contains no save.dat or autosave.dat."); return files.OrderByDescending(File.GetLastWriteTimeUtc).First();
        }
        internal static Dictionary<string, object> ReadMeta(string slot)
        {
            string path = Path.Combine(slot, "meta.json"); if (!File.Exists(path)) return Json.D();
            try { var d = Json.Read(File.ReadAllText(path, Encoding.UTF8)); if (d == null) throw new FormatException(); return d; }
            catch (Exception ex) { if (ex is IOException || ex is ArgumentException || ex is FormatException || ex is InvalidOperationException) throw new ConversionError("Invalid meta.json."); throw; }
        }
        internal static Dictionary<string, object> TryReadJson(string path)
        {
            try { return Json.Read(File.ReadAllText(path, Encoding.UTF8)); }
            catch (Exception ex) { if (ex is IOException || ex is ArgumentException || ex is FormatException || ex is InvalidOperationException || ex is UnauthorizedAccessException) return null; throw; }
        }
        internal static Dictionary<string, object> Inspect(string source)
        {
            string file = SourceFile(source), slot = Path.GetDirectoryName(file); var data = ReadSave(file); var m = SaveFormat.Parse(SaveFormat.Decode(data).Entries["game.manifest"]); var meta = ReadMeta(slot);
            return Json.D("path", file, "slot", slot, "name", Json.S(meta, "displayName", Path.GetFileName(slot)), "format", m.Format, "objects", m.Objects.Count, "vehicles", m.Objects.Count(o => o.State.ContainsKey("vehicle")), "modified", (File.GetLastWriteTimeUtc(file) - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds, "sha256", SaveFormat.Sha(data));
        }
        internal static string ConvertedName(string name)
        {
            string value = Regex.Replace(name, @"\s*\((?:converted|\u00e1talak\u00edtott)\)\s*$", "", RegexOptions.IgnoreCase).Trim(); return (value.Length == 0 ? "Legacy save" : value) + " (converted)";
        }
        static bool Inside(string child, string parent)
        {
            string p = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), c = Path.GetFullPath(child).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return c.Equals(p, StringComparison.OrdinalIgnoreCase) || c.StartsWith(p + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        static void RejectReparseAncestors(string path)
        {
            // Do not let junctions or symbolic links bypass the original-slot boundary.
            for (var current = new DirectoryInfo(Path.GetFullPath(path)); current != null; current = current.Parent)
                if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0) throw new ConversionError("For write safety, select a save and backup folder without symbolic links or directory junctions.");
        }
        static void WriteNew(string path, byte[] data)
        { using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { output.Write(data, 0, data.Length); output.Flush(true); } }
        internal static Dictionary<string, object> ConvertSlot(string source, string outputRoot, string backupRoot, Mapping mapping, string game, bool replace, string displayName, int? playerSlots, bool repair, bool cleanup, string legacySource = null)
        {
            VerifyBuild(game, mapping); if (GameRunning()) throw new ConversionError("Close the game before converting saves.");
            string src = SourceFile(source), sourceSlot = Path.GetDirectoryName(src); byte[] data = ReadSave(src); var meta = ReadMeta(sourceSlot);
            var conversion = repair ? ConverterCore.Repair(data, mapping, playerSlots) : ConverterCore.Transform(data, mapping, replace, playerSlots);
            if (cleanup) conversion = LootCleanup.Apply(src, conversion, mapping, backupRoot, legacySource);
            string root = CanonicalPath(outputRoot), archive = CanonicalPath(backupRoot);
            if (!Directory.Exists(root)) throw new ConversionError("The destination save folder does not exist.");
            RejectReparseAncestors(root); RejectReparseAncestors(archive); RejectReparseAncestors(sourceSlot);
            if (Inside(root, sourceSlot)) throw new ConversionError("The new slot cannot be created inside the original slot.");
            if (Inside(archive, sourceSlot)) throw new ConversionError("The backup cannot be created inside the original slot.");
            string slotId = "slot_" + Guid.NewGuid().ToString("N"), destination = Path.Combine(root, slotId);
            if (Directory.Exists(destination) || File.Exists(destination)) throw new ConversionError("The destination already exists. Overwriting is not allowed.");
            string now = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", System.Globalization.CultureInfo.InvariantCulture);
            string name = (string.IsNullOrEmpty(displayName) ? ConvertedName(Json.S(meta, "displayName", "Legacy save")) : displayName).Trim();
            if (name.Length == 0 || name.Length > 120) throw new ConversionError("The save name must contain 1 to 120 characters.");
            Directory.CreateDirectory(archive); string backup = Path.Combine(archive, Path.GetFileName(sourceSlot) + "_" + SaveFormat.Sha(data).Substring(0, 16)); Directory.CreateDirectory(backup); RejectReparseAncestors(backup);
            string archived = Path.Combine(backup, Path.GetFileName(src));
            if (File.Exists(archived)) { if (!File.ReadAllBytes(archived).SequenceEqual(data)) throw new ConversionError("A different backup already exists at this path."); }
            else WriteNew(archived, data);
            foreach (string aux in new[] { "meta.json", "thumbnail.jpg", "conversion-report.json", "CONVERSION_REPORT.txt" })
            { string input = Path.Combine(sourceSlot, aux), target = Path.Combine(backup, aux); if (File.Exists(input) && !File.Exists(target)) WriteNew(target, File.ReadAllBytes(input)); }
            var report = conversion.Report; report["displayName"] = name; report["source"] = src; report["destination"] = destination; report["backup"] = backup; report["convertedAt"] = now;
            string lineage = Json.S(report, "sourceSha256"); var previous = repair ? TryReadJson(Path.Combine(sourceSlot, "conversion-report.json")) : null;
            if (previous != null)
            {
                string candidate = Json.S(previous, "originalSourceSha256"); if (candidate.Length == 0) candidate = Json.S(previous, "sourceSha256");
                if (Regex.IsMatch(candidate, "\\A[0-9a-fA-F]{64}\\z")) lineage = candidate.ToLowerInvariant();
                if (previous.ContainsKey("lootCleanup") && !report.ContainsKey("lootCleanup")) report["lootCleanup"] = previous["lootCleanup"];
            }
            report["originalSourceSha256"] = lineage; var newMeta = new Dictionary<string, object>(meta);
            foreach (var field in Json.D("slotId", slotId, "displayName", name, "saveVersion", 1, "createdAt", Json.Get(meta, "createdAt", now), "lastSavedAt", now, "playtimeSeconds", Json.Get(meta, "playtimeSeconds", 0),
                "generation", 1, "contentFile", "save.dat", "contentHash", SaveFormat.Sha(conversion.Output), "contentSize", conversion.Output.Length, "contentFormatVersion", 16, "buildVersion", "converter-" + BuildInfo.Version + " / Steam-" + mapping.Build, "deviceId", "", "cloudAccount", "")) newMeta[field.Key] = field.Value;
            string staging = Path.Combine(root, ".converter_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(staging);
            try
            {
                WriteNew(Path.Combine(staging, "save.dat"), conversion.Output); WriteNew(Path.Combine(staging, "meta.json"), Utf8.GetBytes(Json.Write(newMeta)));
                if (File.Exists(Path.Combine(sourceSlot, "thumbnail.jpg"))) File.Copy(Path.Combine(sourceSlot, "thumbnail.jpg"), Path.Combine(staging, "thumbnail.jpg"), false);
                WriteNew(Path.Combine(staging, "conversion-report.json"), Utf8.GetBytes(Json.Write(report)));
                File.WriteAllText(Path.Combine(staging, "CONVERSION_REPORT.txt"), ConverterCore.ReportText(report), Utf8Bom);
                if (SaveFormat.Sha(ReadSave(Path.Combine(staging, "save.dat"))) != Json.S(newMeta, "contentHash")) throw new ConversionError("The written file did not pass verification.");
                if (SaveFormat.Sha(ReadSave(src)) != Json.S(report, "sourceSha256")) throw new ConversionError("The source save changed during conversion. No new slot was installed.");
                Directory.Move(staging, destination);
            }
            finally
            {
                // This private flat staging directory never contains source files or subdirectories.
                if (Directory.Exists(staging) && Path.GetDirectoryName(Path.GetFullPath(staging)).Equals(root, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(staging).StartsWith(".converter_", StringComparison.Ordinal))
                {
                    RejectReparseAncestors(staging); foreach (string file in Directory.GetFiles(staging)) File.Delete(file); Directory.Delete(staging);
                }
            }
            return report;
        }
        static string Lineage(Dictionary<string, object> report)
        { string value = Json.S(report, "originalSourceSha256"); return (value.Length > 0 ? value : Json.S(report, "sourceSha256")).ToLowerInvariant(); }
        internal static Dictionary<string, object> Batch(string outputRoot, string backups, Mapping mapping, string game, bool replace, int? players, bool cleanup)
        {
            VerifyBuild(game, mapping); string root = Path.GetFullPath(outputRoot);
            if (!Directory.Exists(root)) throw new ConversionError("The save folder does not exist. Use Browse to choose it.");
            var infos = new List<SlotInfo>(); var errors = new List<object>();
            foreach (string folder in Directory.GetDirectories(root, "slot_*").OrderBy(x => x, StringComparer.Ordinal))
            {
                if (!File.Exists(Path.Combine(folder, "save.dat")) && !File.Exists(Path.Combine(folder, "autosave.dat"))) continue;
                try
                {
                    var row = Inspect(folder); infos.Add(new SlotInfo { Row = row, Manifest = SaveFormat.Parse(SaveFormat.Decode(ReadSave(Json.S(row, "path"))).Entries["game.manifest"]), Report = TryReadJson(Path.Combine(folder, "conversion-report.json")) });
                }
                catch (Exception ex) { errors.Add(Json.D("name", Path.GetFileName(folder), "error", ex.Message)); }
            }
            var descendants = infos.Where(i => i.Manifest.Format == 16 && i.Report != null).GroupBy(i => Lineage(i.Report)).ToDictionary(g => g.Key, g => g.ToList());
            var results = new List<object>(); var tasks = new List<Tuple<SlotInfo, bool>>(); var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var scheduled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Action<SlotInfo> addExisting = info =>
            {
                if (!scheduled.Add(info.Slot)) return;
                int desired = ConverterCore.PlayerSlots(info.Manifest, players), actual = (int)(info.Manifest.Economy >> 56); bool cleaned = Json.Get(info.Report, "lootCleanup") is Dictionary<string, object>;
                if (actual != desired || info.Name != ConvertedName(info.Name) || (cleanup && !cleaned)) tasks.Add(Tuple.Create(info, true));
                else results.Add(Json.D("name", info.Name, "status", "already-converted", "destination", info.Slot, "playerSlots", actual));
            };
            foreach (var info in infos.Where(i => i.Manifest.Format == 5))
            {
                List<SlotInfo> matches;
                if (descendants.TryGetValue(Json.S(info.Row, "sha256"), out matches))
                {
                    foreach (var match in matches) handled.Add(match.Slot); var latest = matches.OrderByDescending(i => i.Modified).First();
                    try { addExisting(latest); } catch (ConversionError ex) { errors.Add(Json.D("name", latest.Name, "error", ex.Message)); }
                }
                else tasks.Add(Tuple.Create(info, false));
            }
            foreach (var info in infos.Where(i => i.Manifest.Format == 16))
            {
                if (handled.Contains(info.Slot)) continue;
                if (info.Report != null)
                {
                    List<SlotInfo> group; if (!descendants.TryGetValue(Lineage(info.Report), out group)) group = new List<SlotInfo> { info };
                    foreach (var member in group) handled.Add(member.Slot); var latest = group.OrderByDescending(i => i.Modified).First();
                    try { addExisting(latest); } catch (ConversionError ex) { errors.Add(Json.D("name", latest.Name, "error", ex.Message)); }
                }
                else results.Add(Json.D("name", info.Name, "status", "current-format", "destination", info.Slot));
            }
            if (tasks.Count > 0 && GameRunning()) throw new ConversionError("Close the game, then click Back up and convert all.");
            foreach (var task in tasks)
            {
                var info = task.Item1; bool repair = task.Item2;
                try
                {
                    bool needsCleanup = cleanup && !(Json.Get(info.Report, "lootCleanup") is Dictionary<string, object>);
                    var r = ConvertSlot(info.Path, root, backups, mapping, game, replace, ConvertedName(info.Name), players, repair, needsCleanup); var detail = Json.Get(r, "lootCleanup") as Dictionary<string, object>;
                    results.Add(Json.D("name", r["displayName"], "status", needsCleanup ? "cleaned" : repair ? "repaired" : "converted", "destination", r["destination"], "backup", r["backup"], "objects", r["outputObjects"], "playerSlots", r["playerSlots"], "lootRemoved", Json.Get(detail, "removedCount", 0), "replaced", Json.Items(r["replacements"]).Count(), "omitted", Json.Items(r["omitted"]).Count()));
                }
                catch (Exception ex) { errors.Add(Json.D("name", info.Name, "error", ex.Message)); }
            }
            var rows = results.Select(Json.Dict).ToList(); int converted = rows.Count(r => Json.S(r, "status") == "converted"), repaired = rows.Count(r => Json.S(r, "status") == "repaired"), cleanedCount = rows.Count(r => Json.S(r, "status") == "cleaned"), unchanged = rows.Count - converted - repaired - cleanedCount;
            var text = new List<string> { "Converted: " + converted + " | Multiplayer repaired: " + repaired + " | Loot cleaned: " + cleanedCount + " | Unchanged: " + unchanged + " | Errors: " + errors.Count, "" };
            foreach (var row in rows)
            {
                text.Add(Json.S(row, "name") + " - " + row["status"]); text.Add("  " + row["destination"]);
                if (row.ContainsKey("backup")) text.Add("  Backup: " + row["backup"]); if (Json.S(row, "status") == "cleaned") text.Add("  Imported objects removed: " + row["lootRemoved"]);
            }
            foreach (var error in errors.Select(Json.Dict)) text.Add(Json.S(error, "name") + " - ERROR: " + error["error"]);
            text.AddRange(new[] { "", "Load the newest (converted) entries in the game.", "Original slots are unchanged. Individual conversion reports are saved beside each new world." });
            var result = Json.D("converted", converted, "repaired", repaired, "cleaned", cleanedCount, "unchanged", unchanged, "errors", errors, "worlds", results, "reportText", string.Join("\n", text), "locations", Json.D("game", string.IsNullOrWhiteSpace(game) ? DiscoverGame() ?? "" : game, "saves", root, "backups", backups));
            if (converted + repaired + cleanedCount > 0) { Directory.CreateDirectory(backups); File.WriteAllText(Path.Combine(backups, "batch-" + DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".json"), Json.Write(result), Utf8); }
            return result;
        }
    }
}
