// ═══════════ BattleFile.cs — бой в файл для разбора (Алекс 13.10.2026: «сохранить бой для разбора») ═══════════
// Save(сессия, путь, заметка) — весь бой на этот миг (обычно начало хода): отряды, бойцы, схватки, стрелы в полёте, туман, поединки,
// карта с проломами, поручения ИИ, приказы сессии, журналы, состояние генератора — двоичный граф объектов, сжатый GZip. Load(путь) —
// та же сессия; дальше ход идёт боец в боец, как шёл бы в игре. Правила — ссылкой на набор (Rules.Base), в файл не пишутся; делегаты
// игры (CommanderOf, FactionName, обратные вызовы) не пишутся — при загрузке контекст свой (EngineContext по умолчанию с тем же
// генератором). Устроено как глубокая копия отката (Г112, DeepCopy): по полям, с общими ссылками и циклами.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;

namespace BattleCore
{
    public sealed class BattleFileInfo
    {
        public string Note = "";      // что не так (пишет Алекс) — ход, секунда, отряд, «ожидал вот так»
        public string Saved = "";     // когда сохранено (строкой, как дала игра)
        public string Build = "";     // сборка игры
        public int Turn;              // ход сессии
    }

    public static class BattleFile
    {
        const string Magic = "JBF2";   // JBF2 (14.10.2026): поля по именам — файл открывается и после новых полей движка (JBF1 — по порядку)
        enum Tag : byte { Null, Ref, Obj, Arr, PrimArr, List, Dict, Rng, Rules, RulesPart, Ctx, Type, Bool, Byte, SByte, Short, UShort, Int, UInt, Long, ULong, Float, Double, Char, Str, Enum, Decimal, Struct, DropDelegate }

        public static void Save(BattleSession session, string path, BattleFileInfo info)
        {
            using (var fs = File.Create(path))
            using (var gz = new GZipStream(fs, CompressionLevel.Optimal))
            using (var w = new BinaryWriter(gz, Encoding.UTF8))
            {
                w.Write(Magic);
                info = info ?? new BattleFileInfo(); info.Turn = session.Turn;
                w.Write(info.Note ?? ""); w.Write(info.Saved ?? ""); w.Write(info.Build ?? ""); w.Write(info.Turn);
                new Writer(w).Value(session);
            }
        }
        public static BattleSession Load(string path) => Load(path, out _);
        public static BattleSession Load(string path, out BattleFileInfo info)
        {
            using (var fs = File.OpenRead(path))
            using (var gz = new GZipStream(fs, CompressionMode.Decompress))
            using (var r = new BinaryReader(gz, Encoding.UTF8))
            {
                if (r.ReadString() != Magic) throw new InvalidDataException("не файл боя для разбора");
                info = new BattleFileInfo { Note = r.ReadString(), Saved = r.ReadString(), Build = r.ReadString(), Turn = r.ReadInt32() };
                return (BattleSession)new Reader(r).Value();
            }
        }

        // части набора правил (Rules.Move и т. п.) — по имени поля в Rules.Base
        static Dictionary<object, string> partName; static Dictionary<string, object> partByName;
        static void Parts()
        {
            if (partName != null) return;
            var pn = new Dictionary<object, string>(RefEq.I); var pb = new Dictionary<string, object>();
            foreach (var f in DeepCopy.FieldsOf(typeof(Rules)))
            {
                if (f.IsStatic) continue;
                var v = f.GetValue(Rules.Base);
                if (v == null || v.GetType().IsValueType || v is string) continue;
                if (!pn.ContainsKey(v)) { pn[v] = f.Name; pb[f.Name] = v; }
            }
            partByName = pb; partName = pn;
        }
        static bool Immutable(Type t) => t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal);

        sealed class Writer
        {
            readonly BinaryWriter w; readonly Dictionary<object, int> ids = new Dictionary<object, int>(RefEq.I);
            readonly Dictionary<Type, int> shapes = new Dictionary<Type, int>();
            // тип объекта или структуры: в первый раз — имя типа и имена его полей, дальше — номер
            void Shape(Type t)
            {
                if (shapes.TryGetValue(t, out int n)) { w.Write(n); return; }
                shapes[t] = shapes.Count; w.Write(-1); TypeName(t);
                var fs = DeepCopy.FieldsOf(t); int k = 0; foreach (var f in fs) if (!f.IsStatic) k++;
                w.Write(k); foreach (var f in fs) if (!f.IsStatic) w.Write(f.DeclaringType.Name + "." + f.Name);
            }
            public Writer(BinaryWriter w) { this.w = w; Parts(); }
            void T(Tag t) => w.Write((byte)t);
            void TypeName(Type t) => w.Write(t.AssemblyQualifiedName);
            public void Value(object v)
            {
                if (v == null) { T(Tag.Null); return; }
                var t = v.GetType();
                if (Prim(v, t)) return;
                if (t.IsEnum) { T(Tag.Enum); TypeName(t); w.Write(Convert.ToInt64(v)); return; }
                if (v is Type ty) { T(Tag.Type); w.Write(ty.AssemblyQualifiedName); return; }
                if (v is Delegate d)
                {
                    if (d.Target is Mulberry32 g && d.Method.Name == "Next") { T(Tag.Rng); TypeName(t); Value(g); return; }
                    T(Tag.DropDelegate); return;   // делегаты игры не пишутся
                }
                if (t.IsValueType) { T(Tag.Struct); Shape(t); Fields(v, t); return; }
                if (ids.TryGetValue(v, out int id)) { T(Tag.Ref); w.Write(id); return; }
                if (v is Rules) { T(Tag.Rules); return; }
                if (partName.TryGetValue(v, out var pname)) { T(Tag.RulesPart); w.Write(pname); return; }
                ids[v] = ids.Count;
                if (v is EngineContext ctx) { T(Tag.Ctx); Value(ctx.Rng); return; }   // свой контекст при загрузке, генератор — этот
                if (t.IsArray)
                {
                    var a = (Array)v; var et = t.GetElementType();
                    if (a.Rank != 1) throw new NotSupportedException("многомерный массив в бою: " + t);
                    if (et.IsPrimitive) { T(Tag.PrimArr); TypeName(et); w.Write(a.Length); var buf = new byte[Buffer.ByteLength(a)]; Buffer.BlockCopy(a, 0, buf, 0, buf.Length); w.Write(buf); return; }
                    T(Tag.Arr); TypeName(et); w.Write(a.Length); foreach (var x in a) Value(x); return;
                }
                if (t.IsGenericType)
                {
                    var gd = t.GetGenericTypeDefinition();
                    if (gd == typeof(List<>) || gd == typeof(HashSet<>) || gd == typeof(Queue<>) || gd == typeof(Stack<>))
                    {
                        T(Tag.List); TypeName(t);
                        var items = new List<object>(); foreach (var x in (IEnumerable)v) items.Add(x);
                        if (gd == typeof(Stack<>)) items.Reverse();
                        w.Write(items.Count); foreach (var x in items) Value(x); return;
                    }
                    if (gd == typeof(Dictionary<,>))
                    {
                        T(Tag.Dict); TypeName(t);
                        var cmp = t.GetProperty("Comparer")?.GetValue(v);
                        w.Write(cmp is RefEq || cmp != null && cmp.GetType().Name == "ReferenceEqualityComparer");
                        var dict = (IDictionary)v; w.Write(dict.Count);
                        foreach (DictionaryEntry e in dict) { Value(e.Key); Value(e.Value); }
                        return;
                    }
                }
                T(Tag.Obj); Shape(t); Fields(v, t);
            }
            void Fields(object v, Type t)
            {
                foreach (var f in DeepCopy.FieldsOf(t)) { if (f.IsStatic) continue; Value(f.GetValue(v)); }
            }
            bool Prim(object v, Type t)
            {
                switch (v)
                {
                    case bool x: T(Tag.Bool); w.Write(x); return true;
                    case byte x: T(Tag.Byte); w.Write(x); return true;
                    case sbyte x: T(Tag.SByte); w.Write(x); return true;
                    case short x: T(Tag.Short); w.Write(x); return true;
                    case ushort x: T(Tag.UShort); w.Write(x); return true;
                    case int x: T(Tag.Int); w.Write(x); return true;
                    case uint x: T(Tag.UInt); w.Write(x); return true;
                    case long x: T(Tag.Long); w.Write(x); return true;
                    case ulong x: T(Tag.ULong); w.Write(x); return true;
                    case float x: T(Tag.Float); w.Write(x); return true;
                    case double x: T(Tag.Double); w.Write(x); return true;
                    case char x: T(Tag.Char); w.Write(x); return true;
                    case string x: T(Tag.Str); w.Write(x); return true;
                    case decimal x: T(Tag.Decimal); w.Write(x); return true;
                }
                return false;
            }
        }

        sealed class Reader
        {
            readonly BinaryReader r; readonly List<object> objs = new List<object>();
            readonly List<(Type t, FieldInfo[] map)> shapes = new List<(Type, FieldInfo[])>();
            // тип и соответствие полей файла полям этой версии движка (по имени; поля, которых больше нет, — читаются и пропускаются)
            (Type t, FieldInfo[] map) Shape()
            {
                int n = r.ReadInt32();
                if (n >= 0) return shapes[n];
                var t = ReadType(); int k = r.ReadInt32();
                var byName = new Dictionary<string, FieldInfo>();
                foreach (var f in DeepCopy.FieldsOf(t)) if (!f.IsStatic) byName[f.DeclaringType.Name + "." + f.Name] = f;
                var map = new FieldInfo[k];
                for (int i = 0; i < k; i++) byName.TryGetValue(r.ReadString(), out map[i]);
                shapes.Add((t, map)); return (t, map);
            }
            static object New(Type t)
            {
                // с конструктором без параметров — через него: новые поля получают свои значения по умолчанию (NaN и т. п.), а не нули
                if (!t.IsValueType && t.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null) != null)
                    try { return Activator.CreateInstance(t, true); } catch { }
                return t.IsValueType ? Activator.CreateInstance(t) : FormatterServices.GetUninitializedObject(t);
            }
            readonly Dictionary<string, Type> types = new Dictionary<string, Type>();
            public Reader(BinaryReader r) { this.r = r; Parts(); }
            Type TypeOf(string n) { if (!types.TryGetValue(n, out var t)) types[n] = t = Type.GetType(n, true); return t; }
            Type ReadType() => TypeOf(r.ReadString());
            int Reserve() { objs.Add(null); return objs.Count - 1; }
            public object Value()
            {
                var tag = (Tag)r.ReadByte();
                switch (tag)
                {
                    case Tag.Null: case Tag.DropDelegate: return null;
                    case Tag.Bool: return r.ReadBoolean();
                    case Tag.Byte: return r.ReadByte();
                    case Tag.SByte: return r.ReadSByte();
                    case Tag.Short: return r.ReadInt16();
                    case Tag.UShort: return r.ReadUInt16();
                    case Tag.Int: return r.ReadInt32();
                    case Tag.UInt: return r.ReadUInt32();
                    case Tag.Long: return r.ReadInt64();
                    case Tag.ULong: return r.ReadUInt64();
                    case Tag.Float: return r.ReadSingle();
                    case Tag.Double: return r.ReadDouble();
                    case Tag.Char: return r.ReadChar();
                    case Tag.Str: return r.ReadString();
                    case Tag.Decimal: return r.ReadDecimal();
                    case Tag.Enum: { var t = ReadType(); return Enum.ToObject(t, r.ReadInt64()); }
                    case Tag.Type: return TypeOf(r.ReadString());
                    case Tag.Ref: return objs[r.ReadInt32()];
                    case Tag.Rules: return Rules.Base;
                    case Tag.RulesPart: return partByName[r.ReadString()];
                    case Tag.Rng: { var dt = ReadType(); var g = (Mulberry32)Value(); return Delegate.CreateDelegate(dt, g, typeof(Mulberry32).GetMethod("Next")); }
                    case Tag.Ctx: { int id = Reserve(); var c = new EngineContext(); objs[id] = c; c.Rng = (Func<double>)Value(); return c; }
                    case Tag.Struct: { var (t, map) = Shape(); object o = Activator.CreateInstance(t); Fields(o, map); return o; }
                    case Tag.PrimArr:
                        {
                            var et = ReadType(); int n = r.ReadInt32(); var a = Array.CreateInstance(et, n); objs.Add(a);
                            var buf = r.ReadBytes(Buffer.ByteLength(a)); Buffer.BlockCopy(buf, 0, a, 0, buf.Length); return a;
                        }
                    case Tag.Arr:
                        {
                            var et = ReadType(); int n = r.ReadInt32(); var a = Array.CreateInstance(et, n); objs.Add(a);
                            for (int i = 0; i < n; i++) a.SetValue(Value(), i);
                            return a;
                        }
                    case Tag.List:
                        {
                            var t = ReadType(); var o = Activator.CreateInstance(t); objs.Add(o);
                            var gd = t.GetGenericTypeDefinition();
                            var add = t.GetMethod(gd == typeof(Queue<>) ? "Enqueue" : gd == typeof(Stack<>) ? "Push" : "Add");
                            int n = r.ReadInt32(); var arg = new object[1];
                            for (int i = 0; i < n; i++) { arg[0] = Value(); add.Invoke(o, arg); }
                            return o;
                        }
                    case Tag.Dict:
                        {
                            var t = ReadType(); bool refEq = r.ReadBoolean();
                            var ga = t.GetGenericArguments();
                            object o = refEq && !ga[0].IsValueType ? Activator.CreateInstance(t, RefEqFor(ga[0])) : Activator.CreateInstance(t);
                            objs.Add(o); var d = (IDictionary)o;
                            int n = r.ReadInt32();
                            for (int i = 0; i < n; i++) { var k = Value(); var v = Value(); d.Add(k, v); }
                            return o;
                        }
                    case Tag.Obj:
                        {
                            var (t, map) = Shape(); var o = New(t); objs.Add(o);
                            Fields(o, map); return o;
                        }
                }
                throw new InvalidDataException("файл боя повреждён: метка " + tag);
            }
            void Fields(object o, FieldInfo[] map)
            {
                foreach (var f in map)
                {
                    var v = Value();
                    if (f == null) continue;   // поля больше нет — пропуск
                    if (v == null && f.FieldType.IsValueType) continue;
                    if (v != null && !f.FieldType.IsInstanceOfType(v)) continue;   // тип поля сменился — остаётся по умолчанию
                    f.SetValue(o, v);
                }
            }
            static object RefEqFor(Type k) => Activator.CreateInstance(typeof(RefEqT<>).MakeGenericType(k));
        }

        sealed class RefEq : IEqualityComparer<object>
        {
            public static readonly RefEq I = new RefEq();
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
        }
        sealed class RefEqT<K> : IEqualityComparer<K> where K : class
        {
            public bool Equals(K a, K b) => ReferenceEquals(a, b);
            public int GetHashCode(K o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
        }
    }
}
