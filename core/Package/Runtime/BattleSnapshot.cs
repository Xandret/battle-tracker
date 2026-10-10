// ═══════════ BattleSnapshot.cs — снимок боя и откат хода (Г112 п.2) ═══════════
// Snapshot() — глубокая копия всего состояния боя (отряды, бойцы, схватки, стрелы в полёте, туман, поединки, карта с проломами,
// генератор); Restore(снимок) возвращает ТОТ ЖЕ объект Battle к снимку (ссылки игры на Battle остаются). Снимок можно восстанавливать
// много раз. Правила (Rules) и карты направлений (FlowField) — по ссылке: правила неизменяемы в бою, карты направлений считаются от
// карты снимка и ей же соответствуют после отката. Делегаты — по ссылке, кроме генератора Mulberry32: его состояние копируется.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;

namespace BattleCore
{
    public sealed partial class Battle
    {
        public object Snapshot() => DeepCopy.Clone(this);
        public void Restore(object snapshot)
        {
            if (!(snapshot is Battle snap)) throw new ArgumentException("не снимок боя");
            var liveCtx = Ctx; var seen = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
            foreach (var f in DeepCopy.FieldsOf(typeof(Battle)))
            {
                if (f.IsStatic) continue;
                f.SetValue(this, DeepCopy.Clone(f.GetValue(snap), seen));
            }
            // контекст — живой (делегаты игры), генератор — в состояние снимка
            if (Ctx != liveCtx && liveCtx != null)
            {
                if (liveCtx.Rng?.Target is Mulberry32 live && Ctx?.Rng?.Target is Mulberry32 saved) live.State = saved.State;
                else if (Ctx?.Rng != null) liveCtx.Rng = Ctx.Rng;
                Ctx = liveCtx; Ctx.Rules = R;
            }
        }
    }

    // Глубокая копия графа объектов движка: по полям, с учётом общих ссылок и циклов
    public static class DeepCopy
    {
        static readonly Dictionary<Type, FieldInfo[]> fieldCache = new Dictionary<Type, FieldInfo[]>();
        public static FieldInfo[] FieldsOf(Type t)
        {
            lock (fieldCache)
            {
                if (fieldCache.TryGetValue(t, out var fs)) return fs;
                var list = new List<FieldInfo>();
                for (var b = t; b != null && b != typeof(object); b = b.BaseType)
                    list.AddRange(b.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
                return fieldCache[t] = list.ToArray();
            }
        }
        static bool Shared(Type t) => t == typeof(Rules) || t.DeclaringType == typeof(Rules) || t == typeof(FlowField) || typeof(Delegate).IsAssignableFrom(t) || t == typeof(Type);
        static bool Immutable(Type t) => t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime);

        public static object Clone(object src) => Clone(src, new Dictionary<object, object>(ReferenceEqualityComparer.Instance));
        public static object Clone(object src, Dictionary<object, object> seen)
        {
            if (src == null) return null;
            var t = src.GetType();
            if (Immutable(t)) return src;
            if (src is Delegate d)
            {
                // генератор с зерном — копия с тем же состоянием, иначе откат давал бы другие броски
                if (d.Target is Mulberry32 g && d.Method.Name == "Next")
                {
                    if (!seen.TryGetValue(g, out var gc)) seen[g] = gc = new Mulberry32(g.State);
                    return Delegate.CreateDelegate(t, gc, d.Method);
                }
                return src;
            }
            if (Shared(t)) return src;
            if (t.IsValueType) return CloneStruct(src, t, seen);
            if (seen.TryGetValue(src, out var done)) return done;
            if (t.IsArray)
            {
                var a = (Array)src; var et = t.GetElementType();
                var copy = Array.CreateInstance(et, a.Length); seen[src] = copy;
                if (Immutable(et)) Array.Copy(a, copy, a.Length);
                else for (int i = 0; i < a.Length; i++) copy.SetValue(Clone(a.GetValue(i), seen), i);
                return copy;
            }
            if (t.IsGenericType)
            {
                var gd = t.GetGenericTypeDefinition();
                if (gd == typeof(List<>) || gd == typeof(HashSet<>) || gd == typeof(Queue<>) || gd == typeof(Stack<>))
                {
                    var copy = Activator.CreateInstance(t); seen[src] = copy;
                    var add = t.GetMethod(gd == typeof(Queue<>) ? "Enqueue" : gd == typeof(Stack<>) ? "Push" : "Add");
                    var items = new List<object>(); foreach (var x in (IEnumerable)src) items.Add(x);
                    if (gd == typeof(Stack<>)) items.Reverse();
                    foreach (var x in items) add.Invoke(copy, new[] { Clone(x, seen) });
                    return copy;
                }
                if (gd == typeof(Dictionary<,>))
                {
                    var comparer = t.GetProperty("Comparer")?.GetValue(src);
                    var copy = comparer != null ? Activator.CreateInstance(t, comparer) : Activator.CreateInstance(t); seen[src] = copy;
                    var dict = (IDictionary)copy;
                    foreach (DictionaryEntry e in (IDictionary)src) dict.Add(Clone(e.Key, seen), Clone(e.Value, seen));
                    return copy;
                }
            }
            var obj = FormatterServices.GetUninitializedObject(t); seen[src] = obj;
            foreach (var f in FieldsOf(t))
            {
                if (f.IsStatic) continue;
                f.SetValue(obj, Clone(f.GetValue(src), seen));
            }
            return obj;
        }
        static object CloneStruct(object src, Type t, Dictionary<object, object> seen)
        {
            bool refs = false;
            foreach (var f in FieldsOf(t)) if (!f.IsStatic && !Immutable(f.FieldType)) { refs = true; break; }
            if (!refs) return src;   // структура из чисел — копируется присваиванием
            object boxed = src;   // копия в коробке: поля — по одному
            object copy = Activator.CreateInstance(t);
            foreach (var f in FieldsOf(t)) if (!f.IsStatic) f.SetValue(copy, Clone(f.GetValue(boxed), seen));
            return copy;
        }
    }

    sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
        public new bool Equals(object a, object b) => ReferenceEquals(a, b);
        public int GetHashCode(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
    }
}
