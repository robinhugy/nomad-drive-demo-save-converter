using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NomadSave;

// Compiled separately with TEST_HARNESS. Never embedded in the release EXE.
internal static class NativeHarness
{
    static int? Players(Dictionary<string, object> request)
    {
        object value = Json.Get(request, "player_slots"); if (value == null) return null;
        if (!(value is int) || (int)value < 1 || (int)value > 8) throw new ConversionError("Player slots must be an integer from 1 to 8."); return (int)value;
    }
    static byte[] Bytes(Dictionary<string, object> request, string key) { return Convert.FromBase64String(Json.S(request, key)); }
    static int Main(string[] args)
    {
        try
        {
            var r = Json.Read(File.ReadAllText(args[0], Encoding.UTF8)); var mapping = Json.Get(r, "mapping") == null ? Mapping.Load() : Mapping.FromJson(Json.Write(r["mapping"]));
            SaveOperations.TestGameRunning = Convert.ToBoolean(Json.Get(r, "game_running", false));
            Conversion conversion = null; object result;
            switch (Json.S(r, "mode"))
            {
                case "plan": result = SelectedOperations.Execute(Json.Dict(r["plan"]), Convert.ToBoolean(Json.Get(r, "apply", false)), mapping); break;
                case "transform": conversion = ConverterCore.Transform(Bytes(r, "data"), mapping, Convert.ToBoolean(Json.Get(r, "replace_missing", true)), Players(r)); result = null; break;
                case "repair": conversion = ConverterCore.Repair(Bytes(r, "data"), mapping, Players(r)); result = null; break;
                case "clean": conversion = LootCleanup.Clean(Bytes(r, "data"), Bytes(r, "legacy"), mapping); result = null; break;
                case "convert_slot": result = SaveOperations.ConvertSlot(Json.S(r, "source"), Json.S(r, "output_root"), Json.S(r, "backup_root", SaveOperations.DefaultBackups), mapping, Json.S(r, "game"), Convert.ToBoolean(Json.Get(r, "replace_missing", true)), Json.Get(r, "display_name") as string, Players(r), Convert.ToBoolean(Json.Get(r, "repair", false)), Convert.ToBoolean(Json.Get(r, "cleanup_loot", false)), Json.Get(r, "legacy_source") as string); break;
                case "batch": result = SaveOperations.Batch(Json.S(r, "output_root"), Json.S(r, "backup_root", SaveOperations.DefaultBackups), mapping, Json.S(r, "game"), Convert.ToBoolean(Json.Get(r, "replace_missing", true)), Players(r), Convert.ToBoolean(Json.Get(r, "cleanup_loot", false))); break;
                case "decode": var decoded = SaveFormat.Decode(Bytes(r, "data")); result = Json.D("version", decoded.Version); break;
                default: throw new ConversionError("Unknown harness operation.");
            }
            if (conversion != null) result = Json.D("data", Convert.ToBase64String(conversion.Output), "report", conversion.Report);
            File.WriteAllText(args[1], Json.Write(result), SaveOperations.Utf8); return 0;
        }
        catch (Exception ex) { File.WriteAllText(args[1], Json.Write(Json.D("error", ex.Message)), SaveOperations.Utf8); return 1; }
    }
}
