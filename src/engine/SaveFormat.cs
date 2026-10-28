using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace NomadSave
{
    internal sealed class ConversionError : Exception
    {
        internal ConversionError(string message) : base(message) { }
    }

    internal static class Json
    {
        internal static JavaScriptSerializer Serializer()
        { return new JavaScriptSerializer { MaxJsonLength = 128 * 1024 * 1024, RecursionLimit = 256 }; }
        internal static Dictionary<string, object> Read(string text)
        { return Serializer().Deserialize<Dictionary<string, object>>(text); }
        internal static string Write(object value) { return Serializer().Serialize(value); }
        internal static Dictionary<string, object> D(params object[] pairs)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            for (int i = 0; i < pairs.Length; i += 2) result.Add((string)pairs[i], pairs[i + 1]);
            return result;
        }
        internal static object Get(Dictionary<string, object> d, string key, object fallback = null)
        { object value; return d != null && d.TryGetValue(key, out value) ? value : fallback; }
        internal static string S(Dictionary<string, object> d, string key, string fallback = "")
        { return Convert.ToString(Get(d, key, fallback), System.Globalization.CultureInfo.InvariantCulture); }
        internal static IEnumerable<object> Items(object value)
        { return value == null ? Enumerable.Empty<object>() : ((IEnumerable)value).Cast<object>(); }
        internal static Dictionary<string, object> Dict(object value) { return (Dictionary<string, object>)value; }
        internal static List<object> List(Dictionary<string, object> d, string key) { return (List<object>)d[key]; }
        internal static void Increment(Dictionary<string, object> d, string key, int count = 1)
        { d[key] = Convert.ToInt32(Get(d, key, 0)) + count; }
    }

    internal sealed class DataReader
    {
        readonly byte[] data;
        internal int Position;
        internal DataReader(byte[] bytes) { data = bytes; }
        internal byte[] Raw(int n)
        {
            if (n < 0 || n > data.Length - Position) throw new ConversionError("Truncated or invalid data at byte " + Position + ".");
            var result = new byte[n]; Buffer.BlockCopy(data, Position, result, 0, n); Position += n; return result;
        }
        internal byte B() { return Raw(1)[0]; }
        internal int I() { return BitConverter.ToInt32(Raw(4), 0); }
        internal uint U() { return BitConverter.ToUInt32(Raw(4), 0); }
        internal ushort H() { return BitConverter.ToUInt16(Raw(2), 0); }
        internal ulong Q() { return BitConverter.ToUInt64(Raw(8), 0); }
        internal bool Bool() { int b = B(); if (b > 1) throw new ConversionError("Invalid Boolean value."); return b == 1; }
        internal int Count(int max = 100000)
        { int n = I(); if (n < 0 || n > max) throw new ConversionError("Invalid element count: " + n + "."); return n; }
        internal string String()
        {
            int n = 0;
            for (int shift = 0; shift < 35; shift += 7)
            {
                int b = B(); if (shift == 28 && b > 7) throw new ConversionError("Invalid string length.");
                n |= (b & 127) << shift;
                if (b < 128)
                {
                    if (n > 1048576) throw new ConversionError("String is too long.");
                    return Utf8(Raw(n));
                }
            }
            throw new ConversionError("Invalid string length.");
        }
        internal static string Utf8(byte[] value)
        { try { return new UTF8Encoding(false, true).GetString(value); } catch (DecoderFallbackException) { throw new ConversionError("Invalid UTF-8 string."); } }
        internal byte[] Blob() { return Raw(I()); }
        internal double[] Floats(int n)
        {
            var result = new double[n];
            for (int i = 0; i < n; i++)
            {
                float f = BitConverter.ToSingle(Raw(4), 0);
                if (float.IsNaN(f) || float.IsInfinity(f)) throw new ConversionError("Position or state contains a non-finite value.");
                result[i] = f;
            }
            return result;
        }
        internal void End() { if (Position != data.Length) throw new ConversionError("Unexpected trailing data: " + (data.Length - Position) + " bytes."); }
    }

    internal sealed class DataWriter
    {
        readonly MemoryStream stream = new MemoryStream();
        internal void Raw(byte[] value) { stream.Write(value, 0, value.Length); }
        internal void B(int value) { if (value < 0 || value > 255) throw new ConversionError("Invalid byte value."); stream.WriteByte((byte)value); }
        internal void I(int value) { Raw(BitConverter.GetBytes(value)); }
        internal void U(uint value) { Raw(BitConverter.GetBytes(value)); }
        internal void H(ushort value) { Raw(BitConverter.GetBytes(value)); }
        internal void Q(ulong value) { Raw(BitConverter.GetBytes(value)); }
        internal void Bool(bool value) { B(value ? 1 : 0); }
        internal void String(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value); int n = bytes.Length;
            while (n >= 128) { B((n & 127) | 128); n >>= 7; }
            B(n); Raw(bytes);
        }
        internal void Blob(byte[] value) { I(value.Length); Raw(value); }
        internal void Floats(IEnumerable<double> values)
        {
            foreach (double value in values)
            {
                float f = (float)value;
                if (float.IsNaN(f) || float.IsInfinity(f)) throw new ConversionError("Non-finite converted value.");
                Raw(BitConverter.GetBytes(f));
            }
        }
        internal byte[] Finish() { return stream.ToArray(); }
    }

    internal sealed class Player
    {
        internal string Puid, Name, Equipped, Seat, Backpack;
        internal double[] Position, Euler;
        internal byte[] Survival;
        internal bool Downed;
        internal List<string> Hotbar = new List<string>();
        internal byte Slots, Active;
    }
    internal sealed class WorldObject
    {
        internal string Guid, Asset, Parent;
        internal int Seed;
        internal double[] Position, Rotation;
        internal byte Kind, Sub;
        internal Dictionary<string, byte[]> State = new Dictionary<string, byte[]>(StringComparer.Ordinal);
    }
    internal sealed class Manifest
    {
        internal int Format, Seed;
        internal ulong SavedAt, Economy;
        internal double[] Spawn;
        internal double Time;
        internal Player Host;
        internal List<WorldObject> Objects = new List<WorldObject>();
        internal List<string> Refs = new List<string>();
        internal List<Player> Remote = new List<Player>();
        internal List<int> Seeds = new List<int>();
    }
    internal sealed class SaveData
    {
        internal int Version;
        internal Dictionary<string, byte[]> Entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
    }

    internal static class SaveFormat
    {
        internal const int MaxData = 512 * 1024 * 1024;
        static readonly uint[] CrcTable = MakeCrcTable();
        static uint[] MakeCrcTable()
        {
            var result = new uint[256];
            for (uint i = 0; i < 256; i++) { uint c = i; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xedb88320U ^ (c >> 1) : c >> 1; result[i] = c; }
            return result;
        }
        internal static uint Crc(byte[] bytes, int start = 0, int length = -1)
        {
            if (length < 0) length = bytes.Length - start;
            uint c = 0xffffffffU; for (int i = start; i < start + length; i++) c = CrcTable[(c ^ bytes[i]) & 255] ^ (c >> 8); return c ^ 0xffffffffU;
        }
        internal static string Sha(byte[] bytes)
        { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        static bool Starts(byte[] data, byte[] prefix) { return data.Length >= prefix.Length && data.Take(prefix.Length).SequenceEqual(prefix); }
        static byte[] Compress(byte[] data)
        {
            using (var output = new MemoryStream())
            {
                output.Write(new byte[] { 31, 139, 8, 0, 0, 0, 0, 0, 0, 255 }, 0, 10);
                using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, true)) deflate.Write(data, 0, data.Length);
                var crc = BitConverter.GetBytes(Crc(data)); output.Write(crc, 0, 4);
                var size = BitConverter.GetBytes((uint)data.Length); output.Write(size, 0, 4); return output.ToArray();
            }
        }
        static byte[] Decompress(byte[] data)
        {
            try
            {
                if (data.Length < 18 || data[0] != 31 || data[1] != 139 || data[2] != 8 || (data[3] & 224) != 0) throw new InvalidDataException();
                int p = 10, flags = data[3], limit = data.Length - 8;
                if ((flags & 4) != 0) { if (p + 2 > limit) throw new InvalidDataException(); int n = BitConverter.ToUInt16(data, p); p += 2 + n; }
                if ((flags & 8) != 0) { while (p < limit && data[p++] != 0) { } }
                if ((flags & 16) != 0) { while (p < limit && data[p++] != 0) { } }
                if ((flags & 2) != 0)
                { if (p + 2 > limit || (Crc(data, 0, p) & 65535) != BitConverter.ToUInt16(data, p)) throw new InvalidDataException(); p += 2; }
                if (p >= limit) throw new InvalidDataException();
                using (var input = new MemoryStream(data, p, limit - p, false))
                using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    var buffer = new byte[65536]; int count;
                    while ((count = deflate.Read(buffer, 0, buffer.Length)) > 0)
                    { if (output.Length + count > MaxData) throw new ConversionError("The decompressed save is too large."); output.Write(buffer, 0, count); }
                    var bytes = output.ToArray();
                    if (Crc(bytes) != BitConverter.ToUInt32(data, limit) || (uint)bytes.Length != BitConverter.ToUInt32(data, limit + 4)) throw new InvalidDataException();
                    return bytes;
                }
            }
            catch (InvalidDataException) { throw new ConversionError("The compressed save is damaged."); }
            catch (IOException) { throw new ConversionError("The compressed save is damaged."); }
        }
        internal static SaveData Decode(byte[] data)
        {
            if (data.Length > MaxData) throw new ConversionError("Save file is too large.");
            bool compressed;
            if (Starts(data, Encoding.ASCII.GetBytes("EVL2")))
            {
                var r = new DataReader(data); r.Raw(4);
                if (data.Length < 14) throw new ConversionError("Truncated EVL2 header.");
                int version = r.B(), flags = r.B(); uint length = r.U(), crc = r.U();
                if (version != 1 || flags > 1) throw new ConversionError("Unsupported encryption or save envelope.");
                var payload = r.Raw(data.Length - 14);
                if (payload.Length != length || Crc(payload) != crc) throw new ConversionError("Damaged save: invalid length or CRC.");
                data = payload; compressed = (flags & 1) != 0;
            }
            else compressed = Starts(data, new byte[] { 31, 139 });
            if (compressed) data = Decompress(data);
            var reader = new DataReader(data);
            if (!reader.Raw(4).SequenceEqual(Encoding.ASCII.GetBytes("EVIL"))) throw new ConversionError("This is not a Nomad Drive save file.");
            var result = new SaveData { Version = reader.I() };
            if (result.Version != 1) throw new ConversionError("Unsupported EVIL data version: " + result.Version + ".");
            int count = reader.Count();
            for (int i = 0; i < count; i++)
            {
                string key = DataReader.Utf8(reader.Raw(reader.H()));
                if (result.Entries.ContainsKey(key)) throw new ConversionError("Duplicate data key in save."); result.Entries.Add(key, reader.Blob());
            }
            reader.End();
            if (!result.Entries.ContainsKey("game.manifest")) throw new ConversionError("World manifest is missing."); return result;
        }
        internal static byte[] Encode(SaveData save)
        {
            var w = new DataWriter(); w.Raw(Encoding.ASCII.GetBytes("EVIL")); w.I(save.Version); w.I(save.Entries.Count);
            foreach (var entry in save.Entries)
            {
                var key = Encoding.UTF8.GetBytes(entry.Key); if (key.Length > 65535) throw new ConversionError("Data key is too long.");
                w.H((ushort)key.Length); w.Raw(key); w.Blob(entry.Value);
            }
            var payload = Compress(w.Finish()); var result = new DataWriter(); result.Raw(Encoding.ASCII.GetBytes("EVL2"));
            result.B(1); result.B(1); result.U((uint)payload.Length); result.U(Crc(payload)); result.Raw(payload); return result.Finish();
        }
        static Player ReadPlayer(DataReader r, int format)
        {
            var p = new Player { Puid = r.String(), Name = r.String(), Position = r.Floats(3), Euler = r.Floats(3), Survival = r.Blob(), Equipped = r.String(), Seat = r.String(), Downed = r.Bool(), Backpack = "" };
            if (format == 16) { int n = r.Count(255); for (int i = 0; i < n; i++) p.Hotbar.Add(r.String()); p.Slots = r.B(); p.Active = r.B(); p.Backpack = r.String(); }
            return p;
        }
        static void WritePlayer(DataWriter w, Player p, int format)
        {
            w.String(p.Puid); w.String(p.Name); w.Floats(p.Position); w.Floats(p.Euler); w.Blob(p.Survival); w.String(p.Equipped); w.String(p.Seat); w.Bool(p.Downed);
            if (format == 16) { w.I(p.Hotbar.Count); foreach (string guid in p.Hotbar) w.String(guid); w.B(p.Slots); w.B(p.Active); w.String(p.Backpack); }
        }
        internal static Manifest Parse(byte[] data)
        {
            var r = new DataReader(data); var m = new Manifest { Format = r.I() };
            if (m.Format != 5 && m.Format != 16) throw new ConversionError("Unsupported world format: " + m.Format + ". Only format 5 to 16 is supported.");
            m.SavedAt = r.Q(); m.Seed = r.I(); m.Spawn = r.Floats(3); m.Time = r.Floats(1)[0]; m.Host = r.Bool() ? ReadPlayer(r, m.Format) : null;
            var ids = new HashSet<string>(StringComparer.Ordinal); int count = r.Count();
            for (int i = 0; i < count; i++)
            {
                var o = new WorldObject { Guid = r.String(), Asset = r.String(), Seed = r.I(), Position = r.Floats(3), Rotation = r.Floats(4), Parent = r.String(), Kind = r.B(), Sub = r.B() };
                if (o.Guid.Length == 0 || o.Kind > 7) throw new ConversionError("Invalid object identifier or parent link.");
                if (!ids.Add(o.Guid)) throw new ConversionError("Duplicate object identifier.");
                int states = r.Count(256);
                for (int k = 0; k < states; k++) { string key = r.String(); if (o.State.ContainsKey(key)) throw new ConversionError("Duplicate object state."); o.State.Add(key, r.Blob()); }
                m.Objects.Add(o);
            }
            count = r.Count(); for (int i = 0; i < count; i++) m.Refs.Add(r.String());
            count = r.Count(256); for (int i = 0; i < count; i++) m.Remote.Add(ReadPlayer(r, m.Format));
            count = r.Count(5000000); for (int i = 0; i < count; i++) m.Seeds.Add(r.I());
            if (m.Format == 16) m.Economy = r.Q(); r.End(); return m;
        }
        internal static byte[] Serialize(Manifest m)
        {
            var w = new DataWriter(); w.I(m.Format); w.Q(m.SavedAt); w.I(m.Seed); w.Floats(m.Spawn); w.Floats(new[] { m.Time }); w.Bool(m.Host != null);
            if (m.Host != null) WritePlayer(w, m.Host, m.Format);
            w.I(m.Objects.Count);
            foreach (var o in m.Objects)
            {
                w.String(o.Guid); w.String(o.Asset); w.I(o.Seed); w.Floats(o.Position); w.Floats(o.Rotation); w.String(o.Parent); w.B(o.Kind); w.B(o.Sub); w.I(o.State.Count);
                foreach (var state in o.State) { w.String(state.Key); w.Blob(state.Value); }
            }
            w.I(m.Refs.Count); foreach (string key in m.Refs) w.String(key);
            w.I(m.Remote.Count); foreach (var p in m.Remote) WritePlayer(w, p, m.Format);
            w.I(m.Seeds.Count); foreach (int seed in m.Seeds) w.I(seed);
            if (m.Format == 16) w.Q(m.Economy); return w.Finish();
        }
        internal static void Verify(byte[] output, SaveData expected)
        {
            var actual = Decode(output);
            if (actual.Version != expected.Version || actual.Entries.Count != expected.Entries.Count || expected.Entries.Any(e => !actual.Entries.ContainsKey(e.Key) || !actual.Entries[e.Key].SequenceEqual(e.Value)))
                throw new ConversionError("Complete save read-back verification failed.");
            var manifest = Parse(actual.Entries["game.manifest"]);
            if (!Serialize(manifest).SequenceEqual(expected.Entries["game.manifest"])) throw new ConversionError("Manifest read-back verification failed.");
        }
    }
}
