using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MegabonkAP.Core
{
    /// <summary>
    /// Reads game objects by member name instead of by compiled type. The mod is built without
    /// the game's files, and a game update that renames something then costs one feature and a
    /// line in the log instead of a mod that will not load.
    /// </summary>
    public static class Dyn
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public
            | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

        private static readonly ConcurrentDictionary<(Type, string), Func<object?, object?>?> Readers = new();

        /// <summary>
        /// Follow a dotted path from an object: "inventory.weaponInventory.weapons". A part is a
        /// property or a field; a part ending in "()" is a method without arguments. Null when any
        /// part is missing or null.
        /// </summary>
        public static object? Get(object? start, string path)
        {
            object? current = start;
            foreach (string part in path.Split('.'))
            {
                if (current == null) return null;
                Func<object?, object?>? reader = Reader(current.GetType(), part);
                if (reader == null) return null;
                try { current = reader(current); }
                catch (Exception) { return null; }
            }
            return current;
        }

        /// <summary>The first of several paths that leads somewhere, or null.</summary>
        public static object? First(object? start, params string[] paths)
        {
            foreach (string path in paths)
            {
                object? value = Get(start, path);
                if (value != null) return value;
            }
            return null;
        }

        /// <summary>A static member of a type: "Instance", "Instance.player".</summary>
        public static object? GetStatic(Type? type, string path)
        {
            if (type == null) return null;
            string[] parts = path.Split(new[] { '.' }, 2);
            Func<object?, object?>? reader = Reader(type, parts[0]);
            if (reader == null) return null;
            object? value;
            try { value = reader(null); }
            catch (Exception) { return null; }
            return parts.Length == 1 ? value : Get(value, parts[1]);
        }

        public static bool Has(Type type, string member) => Reader(type, member) != null;

        private static readonly ConcurrentDictionary<(Type, string, int), MethodInfo?> Methods = new();

        /// <summary>A method by name and number of arguments, or null. Looked up once per type.</summary>
        public static MethodInfo? Method(Type type, string name, int arguments)
        {
            return Methods.GetOrAdd((type, name, arguments), key =>
            {
                for (Type? t = key.Item1; t != null; t = t.BaseType)
                {
                    foreach (MethodInfo candidate in t.GetMethods(Any | BindingFlags.DeclaredOnly))
                        if (candidate.Name == key.Item2 && candidate.GetParameters().Length == key.Item3
                            && !candidate.IsGenericMethodDefinition)
                            return candidate;
                }
                return null;
            });
        }

        /// <summary>Call a method with arguments: weapons.ContainsKey(weapon). Null when it is missing or fails.</summary>
        public static object? Call(object? target, string name, params object?[] arguments)
        {
            if (target == null) return null;
            MethodInfo? method = Method(target.GetType(), name, arguments.Length);
            if (method == null) return null;
            try { return method.Invoke(method.IsStatic ? null : target, arguments); }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// Call a method and say how it went: null when it ran, otherwise why it did not (the
        /// method is missing, or the game threw). A number is turned into the enum or number
        /// type the method asks for, so a flag can be passed as 2.
        /// </summary>
        public static string? Invoke(object? target, string name, out object? result, params object?[] arguments)
        {
            result = null;
            if (target == null) return $"there is nothing to call {name} on";
            MethodInfo? method = Method(target.GetType(), name, arguments.Length);
            if (method == null)
                return $"{target.GetType().Name} has no {name}() with {arguments.Length} argument(s). It has: {Describe(target.GetType())}";
            try
            {
                ParameterInfo[] wanted = method.GetParameters();
                object?[] fitted = new object?[arguments.Length];
                for (int i = 0; i < arguments.Length; i++) fitted[i] = Fit(arguments[i], wanted[i].ParameterType);
                result = method.Invoke(method.IsStatic ? null : target, fitted);
                return null;
            }
            catch (Exception problem)
            {
                Exception cause = problem is TargetInvocationException && problem.InnerException != null ? problem.InnerException : problem;
                return $"{target.GetType().Name}.{name} failed: {cause.GetType().Name}: {cause.Message}";
            }
        }

        private static object? Fit(object? value, Type wanted)
        {
            if (value == null || wanted.IsInstanceOfType(value)) return value;
            if (wanted.IsEnum) return Enum.ToObject(wanted, Convert.ToInt32(value));
            if (wanted.IsPrimitive) return Convert.ChangeType(value, wanted);
            return value;
        }

        /// <summary>
        /// False for a Unity object the game has destroyed: its wrapper outlives it, and so does a
        /// static "Instance" that still points at it. True for anything that cannot be asked.
        /// </summary>
        public static bool Alive(object? thing)
        {
            if (thing == null) return false;
            MethodInfo? exists = AliveChecks.GetOrAdd(thing.GetType(), type =>
            {
                for (Type? t = type; t != null; t = t.BaseType)
                {
                    if (t.FullName != "UnityEngine.Object") continue;
                    return t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly).FirstOrDefault(m =>
                        m.Name == "op_Implicit" && m.ReturnType == typeof(bool) && m.GetParameters().Length == 1);
                }
                return null;
            });
            if (exists == null) return true;
            try { return exists.Invoke(null, new[] { thing }) is bool alive ? alive : true; }
            catch (Exception) { return true; }
        }

        private static readonly ConcurrentDictionary<Type, MethodInfo?> AliveChecks = new();

        /// <summary>
        /// Call a method that takes a type instead of arguments: button.GetComponent&lt;CanvasGroup&gt;(),
        /// thing.TryCast&lt;WeaponData&gt;(). Null when it is missing or fails.
        /// </summary>
        public static object? Generic(object? target, string name, Type type)
        {
            if (target == null) return null;
            for (Type? t = target.GetType(); t != null; t = t.BaseType)
            {
                foreach (MethodInfo candidate in t.GetMethods(Any | BindingFlags.DeclaredOnly))
                {
                    if (candidate.Name != name || !candidate.IsGenericMethodDefinition
                        || candidate.GetGenericArguments().Length != 1 || candidate.GetParameters().Length != 0) continue;
                    try { return candidate.MakeGenericMethod(type).Invoke(candidate.IsStatic ? null : target, null); }
                    catch (Exception) { return null; }
                }
            }
            return null;
        }

        /// <summary>Set a field or property by name. False when there is none or it refuses the value.</summary>
        public static bool Set(object? target, string member, object value)
        {
            if (target == null) return false;
            try
            {
                for (Type? t = target.GetType(); t != null; t = t.BaseType)
                {
                    PropertyInfo? property = t.GetProperty(member, Any | BindingFlags.DeclaredOnly);
                    if (property != null && property.SetMethod != null) { property.SetValue(target, value); return true; }
                    FieldInfo? field = t.GetField(member, Any | BindingFlags.DeclaredOnly);
                    if (field != null) { field.SetValue(target, value); return true; }
                }
            }
            catch (Exception) { /* refused */ }
            return false;
        }

        /// <summary>
        /// Members of a game object that may hold one of the named types: fields and properties
        /// declared by the game's own classes (not Unity's), whose type is one of these names or
        /// has the word in its name. Found once per type.
        /// </summary>
        public static IReadOnlyList<string> MembersOfType(Type type, string word, params string[] typeNames)
        {
            return TypedMembers.GetOrAdd((type, word + "|" + string.Join("|", typeNames)), _ =>
            {
                var found = new List<string>();
                for (Type? t = type; t != null && t != typeof(object); t = t.BaseType)
                {
                    if ((t.Namespace ?? "").StartsWith("UnityEngine", StringComparison.Ordinal) || t.Name == "Il2CppObjectBase") break;
                    foreach (MemberInfo m in t.GetMembers(Any | BindingFlags.DeclaredOnly))
                    {
                        Type? holds = m is FieldInfo f ? f.FieldType
                            : m is PropertyInfo p && p.GetIndexParameters().Length == 0 && p.GetMethod != null ? p.PropertyType : null;
                        if (holds == null || holds.IsValueType || holds == typeof(string) || m.Name.Contains('<')
                            || m.Name.StartsWith("Native", StringComparison.Ordinal)) continue;
                        if (typeNames.Contains(holds.Name) || holds.Name.Contains(word, StringComparison.OrdinalIgnoreCase)) found.Add(m.Name);
                    }
                }
                return found;
            });
        }

        private static readonly ConcurrentDictionary<(Type, string), IReadOnlyList<string>> TypedMembers = new();

        /// <summary>Every value of the enum a method takes as its first argument: all weapons, from GetWeaponLevel(EWeapon).</summary>
        public static Array? EnumOfFirstArgument(Type type, string method)
        {
            Type? argument = Method(type, method, 1)?.GetParameters()[0].ParameterType;
            return argument != null && argument.IsEnum ? Enum.GetValues(argument) : null;
        }

        /// <summary>
        /// The value of the first field or property whose type is the enum of this name: a weapon's
        /// EWeapon, whatever the member itself is called. Null when there is none.
        /// </summary>
        public static object? OfEnum(object? target, string enumName)
        {
            if (target == null) return null;
            string? member = EnumMembers.GetOrAdd((target.GetType(), enumName), key =>
            {
                for (Type? t = key.Item1; t != null; t = t.BaseType)
                {
                    foreach (MemberInfo m in t.GetMembers(Any | BindingFlags.DeclaredOnly))
                    {
                        Type? type = m is FieldInfo f ? f.FieldType
                            : m is PropertyInfo p && p.GetIndexParameters().Length == 0 && p.GetMethod != null ? p.PropertyType : null;
                        if (type != null && type.IsEnum && type.Name == key.Item2) return m.Name;
                    }
                }
                return null;
            });
            return member == null ? null : Get(target, member);
        }

        private static readonly ConcurrentDictionary<(Type, string), string?> EnumMembers = new();

        public static int? Int(object? value)
        {
            switch (value)
            {
                case null: return null;
                case int i: return i;
                case long l: return (int)l;
                case short s: return s;
                case byte b: return b;
                case uint u: return (int)u;
                case float f: return (int)f;
                case double d: return (int)d;
                case Enum e: return Convert.ToInt32(e);
                default: return int.TryParse(value.ToString(), out int parsed) ? parsed : (int?)null;
            }
        }

        public static bool Bool(object? value) => value is bool b && b;

        /// <summary>What a thing is called: an enum's name, a Unity object's name, or its text.</summary>
        public static string? Name(object? value)
        {
            if (value == null) return null;
            if (value is string text) return text;
            if (value is Enum) return value.ToString();
            object? named = First(value, "name", "Name");
            return named as string ?? value.ToString();
        }

        /// <summary>
        /// The elements of a collection. The game's lists and dictionaries are not .NET ones, so
        /// when a thing is not IEnumerable its own GetEnumerator, MoveNext and Current are used.
        /// </summary>
        public static IEnumerable<object?> Items(object? collection)
        {
            if (collection == null) yield break;
            if (collection is IEnumerable plain)
            {
                foreach (object? item in plain) yield return item;
                yield break;
            }

            int walked = 0;
            object? enumerator = Get(collection, "GetEnumerator()");
            Type? type = enumerator?.GetType();
            MethodInfo? moveNext = type?.GetMethod("MoveNext", Any, null, Type.EmptyTypes, null);
            Func<object?, object?>? current = type == null ? null : Reader(type, "Current");
            if (enumerator != null && moveNext != null && current != null)
            {
                for (int guard = 0; guard < 100000; guard++)
                {
                    object? more;
                    try { more = moveNext.Invoke(enumerator, null); }
                    catch (Exception) { break; }
                    if (!(more is bool b) || !b) break;
                    walked++;
                    yield return current(enumerator);
                }
            }
            if (walked > 0) yield break;

            // a list the game will not walk from outside (one of plain values, say) can still be asked for each place
            int count = Int(First(collection, "Count", "count")) ?? 0;
            MethodInfo? at = Method(collection.GetType(), "get_Item", 1);
            if (count <= 0 || at == null || at.GetParameters()[0].ParameterType != typeof(int)) yield break;
            for (int i = 0; i < count && i < 100000; i++)
            {
                object? value;
                try { value = at.Invoke(collection, new object[] { i }); }
                catch (Exception) { yield break; }
                yield return value;
            }
        }

        /// <summary>The key and value of each entry of a dictionary, the game's kind or .NET's.</summary>
        public static IEnumerable<(object? key, object? value)> Pairs(object? dictionary)
        {
            foreach (object? entry in Items(dictionary))
            {
                if (entry == null) continue;
                if (entry is DictionaryEntry de) { yield return (de.Key, de.Value); continue; }
                yield return (First(entry, "Key", "key"), First(entry, "Value", "value"));
            }
        }

        /// <summary>The members a type really has, for the log when an expected one is missing.</summary>
        public static string Describe(Type type, int limit = 60)
        {
            IEnumerable<string> names = type.GetMembers(Any)
                .Where(m => m is PropertyInfo || m is FieldInfo
                    || (m is MethodInfo method && !method.IsSpecialName && method.DeclaringType != typeof(object)))
                .Select(m => m is MethodInfo ? m.Name + "()" : m.Name)
                .Where(n => !n.Contains('<') && !n.StartsWith("NativeFieldInfoPtr", StringComparison.Ordinal)
                    && !n.StartsWith("NativeMethodInfoPtr", StringComparison.Ordinal))
                .Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            return string.Join(", ", names.Take(limit)) + (names.Count() > limit ? ", ..." : "");
        }

        private static Func<object?, object?>? Reader(Type type, string part)
        {
            return Readers.GetOrAdd((type, part), key =>
            {
                (Type t, string name) = key;
                if (name.EndsWith("()", StringComparison.Ordinal))
                {
                    MethodInfo? method = Up(t, x => x.GetMethod(name.Substring(0, name.Length - 2), Any, null, Type.EmptyTypes, null));
                    if (method == null) return null;
                    return target => method.Invoke(method.IsStatic ? null : target, null);
                }
                PropertyInfo? property = Up(t, x => x.GetProperty(name, Any));
                if (property != null && property.GetIndexParameters().Length == 0 && property.GetMethod != null)
                    return target => property.GetValue(property.GetMethod.IsStatic ? null : target);
                FieldInfo? field = Up(t, x => x.GetField(name, Any));
                if (field != null) return target => field.GetValue(field.IsStatic ? null : target);
                return null;
            });
        }

        /// <summary>Look on the type, then on each base type (private members are not inherited by reflection).</summary>
        private static T? Up<T>(Type type, Func<Type, T?> find) where T : class
        {
            for (Type? t = type; t != null; t = t.BaseType)
            {
                try
                {
                    T? found = find(t);
                    if (found != null) return found;
                }
                catch (AmbiguousMatchException) { return null; }
            }
            return null;
        }
    }
}
