using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace NomadSave
{
    internal static class ConverterEngine
    {
        static readonly HashSet<string> Modes = new HashSet<string>(new[] { "--list", "--inspect", "--preview", "--convert", "--repair-multiplayer", "--batch" }, StringComparer.Ordinal);
        internal static Dictionary<string, object> Run(string[] args)
        {
            var options = new Dictionary<string, string>(StringComparer.Ordinal); string mode = null;
            var flags = new HashSet<string>(new[] { "--omit-missing", "--clean-legacy-loot" }, StringComparer.Ordinal);
            var values = new HashSet<string>(new[] { "--source", "--output-root", "--backup-root", "--game", "--players", "--legacy-source", "--name", "--report" }, StringComparer.Ordinal);
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (Modes.Contains(arg)) { if (mode != null) throw new ConversionError("Choose exactly one operation."); mode = arg; }
                else if (flags.Contains(arg)) options[arg] = "true";
                else if (values.Contains(arg)) { if (++i >= args.Length) throw new ConversionError("Missing value for " + arg); options[arg] = args[i]; }
                else throw new ConversionError("Unknown option: " + arg);
            }
            if (mode == null) throw new ConversionError("Choose --list, --inspect, --preview, --convert, --repair-multiplayer or --batch.");
            Func<string, string, string> opt = (key, fallback) => { string value; return options.TryGetValue(key, out value) ? value : fallback; };
            string root = opt("--output-root", SaveOperations.DefaultSaves), backups = opt("--backup-root", SaveOperations.DefaultBackups), game = opt("--game", null), source = opt("--source", null);
            int? players = null; int parsed;
            if (options.ContainsKey("--players")) { if (!int.TryParse(options["--players"], out parsed) || parsed < 1 || parsed > 8) throw new ConversionError("Player slots must be an integer from 1 to 8."); players = parsed; }
            bool replace = !options.ContainsKey("--omit-missing"), cleanup = options.ContainsKey("--clean-legacy-loot");
            if (mode == "--list")
            {
                if (!Directory.Exists(root)) throw new ConversionError("The save folder does not exist."); var rows = new List<Dictionary<string, object>>();
                foreach (string folder in Directory.GetDirectories(root, "slot_*"))
                {
                    if (!File.Exists(Path.Combine(folder, "save.dat")) && !File.Exists(Path.Combine(folder, "autosave.dat"))) continue;
                    try { rows.Add(SaveOperations.Inspect(folder)); }
                    catch (Exception ex)
                    {
                        string name = Path.GetFileName(folder); try { name = Json.S(SaveOperations.ReadMeta(folder), "displayName", name); } catch { }
                        rows.Add(Json.D("slot", folder, "name", name, "error", ex.Message, "modified", 0, "format", "?", "objects", "?"));
                    }
                }
                return Json.D("slots", rows.OrderByDescending(r => Convert.ToDouble(r["modified"])).ToList(), "locations", Json.D("game", SaveOperations.DiscoverGame() ?? "", "saves", SaveOperations.DefaultSaves, "backups", SaveOperations.DefaultBackups));
            }
            if (mode == "--batch") return SaveOperations.Batch(root, backups, Mapping.Load(), game, replace, players, cleanup);
            if (string.IsNullOrWhiteSpace(source)) throw new ConversionError("Select a source save with --source.");
            if (mode == "--inspect") return SaveOperations.Inspect(source);
            var mapping = Mapping.Load(); Dictionary<string, object> result;
            if (mode == "--preview")
            {
                SaveOperations.VerifyBuild(game, mapping); string file = SaveOperations.SourceFile(source); byte[] data = SaveOperations.ReadSave(file); int format = SaveFormat.Parse(SaveFormat.Decode(data).Entries["game.manifest"]).Format;
                var conversion = format == 16 ? ConverterCore.Repair(data, mapping, players) : ConverterCore.Transform(data, mapping, replace, players);
                if (cleanup) conversion = LootCleanup.Apply(file, conversion, mapping, backups, opt("--legacy-source", null));
                result = conversion.Report; result["displayName"] = SaveOperations.ConvertedName(Json.S(SaveOperations.ReadMeta(Path.GetDirectoryName(file)), "displayName", Path.GetFileName(Path.GetDirectoryName(file))));
            }
            else result = SaveOperations.ConvertSlot(source, root, backups, mapping, game, replace, opt("--name", null), players, mode == "--repair-multiplayer", cleanup, opt("--legacy-source", null));
            result["reportText"] = ConverterCore.ReportText(result); return result;
        }
        internal static int CommandLine(string[] args)
        {
            Dictionary<string, object> result; int status = 0;
            try { result = Run(args); if (result.ContainsKey("errors") && Json.Items(result["errors"]).Any()) status = 1; }
            catch (Exception ex) { result = Json.D("error", ex.Message); status = 1; }
            string text = Json.Write(result);
            int index = Array.IndexOf(args, "--report");
            if (index >= 0 && index + 1 < args.Length)
            {
                try { File.WriteAllText(args[index + 1], text, SaveOperations.Utf8); }
                catch (Exception ex) { try { Console.Error.WriteLine("Could not write report: " + ex.Message); } catch { } return 1; }
            }
            try { Console.WriteLine(text); } catch (IOException) { } return status;
        }
    }
}
