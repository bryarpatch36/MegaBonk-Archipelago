using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MegabonkAP.Core
{
    /// <summary>
    /// Puts the tracker's text on the screen: a dark panel with text on a canvas of its own, drawn
    /// over the game and never taking a click. Like the rest of the mod it names no Unity type
    /// at build time; every type and member is found by name when it is first needed, and a
    /// missing one is written to the log with what is there instead.
    ///
    /// Pump() must be called from the game's own thread, as often as possible; it does its work
    /// a few times a second. The text itself is made elsewhere and handed over through Text.
    /// </summary>
    public sealed class Screen
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly string[] UnityAssemblies =
        {
            "UnityEngine.CoreModule", "UnityEngine.UIModule", "UnityEngine.UI", "Unity.TextMeshPro",
            "UnityEngine.TextRenderingModule", "UnityEngine.InputLegacyModule",
        };

        private readonly ILog _log;
        private readonly TypeFinder _finder;
        private readonly string _corner;
        private readonly float _fontSize;
        private readonly float _width;
        private readonly string _keyName;
        private readonly Action _keyPressed;

        private volatile string _text = "";
        private string _shown = "\0";
        private long _nextLook;
        private long _keyQuietUntil;
        private int _builds;
        private bool _dead;

        private object? _root;          // the canvas' GameObject
        private object? _panel;         // the dark panel's RectTransform
        private object? _label;         // the TextMeshProUGUI
        private MethodInfo? _keyDown;
        private object? _key;
        private bool _keyTried;

        /// <summary>The text to show; empty hides the overlay. May be set from any thread.</summary>
        public string Text { set => _text = value ?? ""; }

        /// <summary>Whether the key that changes the view works; false until the first frame was seen.</summary>
        public bool KeyWorks => _keyDown != null;

        /// <summary>Whether the game's own thread has reached the overlay at least once.</summary>
        public bool Alive { get; private set; }

        /// <summary>Whether the overlay gave up after repeated failures.</summary>
        public bool Dead => _dead;

        public Screen(ILog log, TypeFinder finder, string corner, int fontSize, int width, string key, Action keyPressed)
        {
            _log = log;
            _finder = finder;
            _corner = corner;
            _fontSize = Math.Clamp(fontSize, 8, 64);
            _width = Math.Clamp(width, 200, 1600);
            _keyName = key.Trim();
            _keyPressed = keyPressed;
        }

        /// <summary>
        /// Load the Unity assemblies the overlay names, so their types can be found. folder: where
        /// BepInEx keeps its generated copies (BepInEx/interop).
        /// </summary>
        public static void LoadUnity(ILog log, string folder)
        {
            foreach (string name in UnityAssemblies)
            {
                if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == name)) continue;
                try { Assembly.Load(name); }
                catch (Exception)
                {
                    try { Assembly.LoadFrom(System.IO.Path.Combine(folder, name + ".dll")); }
                    catch (Exception problem) { log.Info($"Overlay: {name} is not there ({problem.GetType().Name}); going on without it."); }
                }
            }
        }

        public void Pump()
        {
            if (_dead) return;
            try
            {
                Alive = true;
                CheckKey();
                long now = Environment.TickCount64;
                if (now < _nextLook) return;
                _nextLook = now + 200;

                string text = _text;
                if (text == _shown && _root != null) return;
                if (_root == null)
                {
                    if (text.Length == 0) return;       // nothing to show yet: build when there is
                    Build();
                }
                Show(text);
                _shown = text;
            }
            catch (Exception problem)
            {
                Exception cause = problem is TargetInvocationException && problem.InnerException != null ? problem.InnerException : problem;
                _root = null;
                _shown = "\0";
                if (++_builds >= 3)
                {
                    _dead = true;
                    _log.Warn($"Overlay: switched off after three failures. The last: {cause.GetType().Name}: {cause.Message}");
                }
                else _log.Warn($"Overlay: {cause.GetType().Name}: {cause.Message}. Trying again.");
            }
        }

        // ------------------------------------------------------------------ building it

        private void Build()
        {
            Type gameObject = Need("UnityEngine.GameObject");
            Type canvasType = Need("UnityEngine.Canvas");
            Type imageType = Need("UnityEngine.UI.Image");
            Type labelType = Need("TMPro.TextMeshProUGUI");

            object root = Activator.CreateInstance(gameObject, "MegabonkAP Overlay")
                ?? throw new InvalidOperationException("a GameObject could not be made");
            CallStatic(Need("UnityEngine.Object"), "DontDestroyOnLoad", root);

            object canvas = Add(root, canvasType);
            Set(canvas, "renderMode", 0);               // ScreenSpaceOverlay: drawn over everything, no camera needed
            Set(canvas, "sortingOrder", 30000);
            Type? scaler = _finder.Find("UnityEngine.UI.CanvasScaler");
            if (scaler != null)
            {
                try
                {
                    object s = Add(root, scaler);
                    Set(s, "uiScaleMode", 1);           // ScaleWithScreenSize: the same share of the screen at any resolution
                    Set(s, "referenceResolution", Vector2(1920f, 1080f));
                    Set(s, "matchWidthOrHeight", 1f);
                }
                catch (Exception problem) { _log.Info($"Overlay: no scaling with the screen size ({Inner(problem).Message})."); }
            }

            (float x, float y) = Anchor();
            object panelObject = Activator.CreateInstance(gameObject, "Panel")!;
            object image = Add(panelObject, imageType);
            Set(image, "color", Color(0.05f, 0.07f, 0.1f, 0.72f));
            Set(image, "raycastTarget", false);
            object panel = Get(image, "rectTransform");
            Call(panel, "SetParent", Get(root, "transform"), false);
            Set(panel, "anchorMin", Vector2(x, y));
            Set(panel, "anchorMax", Vector2(x, y));
            Set(panel, "pivot", Vector2(x, y));
            Set(panel, "anchoredPosition", Vector2(x < 0.5f ? 14f : -14f, y > 0.75f ? -14f : y < 0.25f ? 14f : 0f));
            Set(panel, "sizeDelta", Vector2(_width, 60f));

            object labelObject = Activator.CreateInstance(gameObject, "Text")!;
            object label = Add(labelObject, labelType);
            object rect = Get(label, "rectTransform");
            Call(rect, "SetParent", panel, false);
            Set(rect, "anchorMin", Vector2(0f, 0f));
            Set(rect, "anchorMax", Vector2(1f, 1f));
            Set(rect, "offsetMin", Vector2(10f, 8f));
            Set(rect, "offsetMax", Vector2(-10f, -8f));
            Set(label, "fontSize", _fontSize);
            Set(label, "raycastTarget", false);
            Set(label, "alignment", 257);               // TextAlignmentOptions.TopLeft
            TrySet(label, "enableWordWrapping", true);
            TrySet(label, "richText", true);
            if (TryGet(label, "font") == null) BorrowFont(label);

            _root = root;
            _panel = panel;
            _label = label;
            _log.Info("Overlay: on screen.");
        }

        private void Show(string text)
        {
            if (_root == null || _label == null || _panel == null) return;
            if (text.Length == 0)
            {
                Call(_root, "SetActive", false);
                return;
            }
            Call(_root, "SetActive", true);
            Set(_label, "text", text);

            // as tall as the text: ask the label, or count the lines when it will not say
            float height = text.Count(c => c == '\n') * _fontSize * 1.25f + _fontSize * 1.25f;
            try
            {
                object? wanted = FindMethod(_label.GetType(), "GetPreferredValues", 3)?.Invoke(_label, new object[] { text, _width - 20f, 0f });
                if (wanted != null && TryGet(wanted, "y") is float y && y > 0f) height = y;
            }
            catch (Exception) { /* the line count will do */ }
            Set(_panel, "sizeDelta", Vector2(_width, height + 18f));
        }

        /// <summary>A label made in code has the game's default font. When there is none, take any font the game has loaded.</summary>
        private void BorrowFont(object label)
        {
            try
            {
                Type? fontType = _finder.Find("TMPro.TMP_FontAsset");
                Type? resources = _finder.Find("UnityEngine.Resources");
                if (fontType == null || resources == null) return;
                MethodInfo? find = resources.GetMethods(Any)
                    .FirstOrDefault(m => m.Name == "FindObjectsOfTypeAll" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
                object? fonts = find?.MakeGenericMethod(fontType).Invoke(null, null);
                object? font = Dyn.Items(fonts).FirstOrDefault(f => f != null);
                if (font != null) Set(label, "font", font);
                else _log.Info("Overlay: the game has no font loaded yet; the text may not show until it has.");
            }
            catch (Exception problem) { _log.Info($"Overlay: no font could be borrowed ({Inner(problem).Message})."); }
        }

        private (float x, float y) Anchor()
        {
            string corner = _corner.Replace(" ", "").ToLowerInvariant();
            float x = corner.EndsWith("right", StringComparison.Ordinal) ? 1f : 0f;
            float y = corner.StartsWith("top", StringComparison.Ordinal) ? 1f : corner.StartsWith("bottom", StringComparison.Ordinal) ? 0f : 0.5f;
            return (x, y);
        }

        // ------------------------------------------------------------------ the key

        private void CheckKey()
        {
            if (!_keyTried)
            {
                _keyTried = true;
                if (_keyName.Length == 0 || _keyName.Equals("none", StringComparison.OrdinalIgnoreCase)) return;
                try
                {
                    Type keyCode = Need("UnityEngine.KeyCode");
                    _key = Enum.Parse(keyCode, _keyName, ignoreCase: true);
                    _keyDown = Need("UnityEngine.Input").GetMethods(Any)
                        .FirstOrDefault(m => m.Name == "GetKeyDown" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == keyCode);
                    if (_keyDown == null) _log.Warn("Overlay: the game has no key input to read; change the view with Mode in the settings file.");
                }
                catch (Exception problem)
                {
                    _keyDown = null;
                    _log.Warn($"Overlay: the key '{_keyName}' cannot be used ({Inner(problem).Message}); change the view with Mode in the settings file.");
                }
            }
            if (_keyDown == null) return;
            bool down;
            try { down = _keyDown.Invoke(null, new[] { _key }) is bool b && b; }
            catch (Exception problem)
            {
                _keyDown = null;
                _log.Warn($"Overlay: reading the key failed ({Inner(problem).Message}); change the view with Mode in the settings file.");
                return;
            }
            if (!down) return;
            long now = Environment.TickCount64;
            if (now < _keyQuietUntil) return;       // the same press, seen again in the same frame
            _keyQuietUntil = now + 250;
            _nextLook = 0;
            _keyPressed();
        }

        // ------------------------------------------------------------------ Unity by name

        private Type Need(string name) =>
            _finder.Find(name) ?? throw new MissingMemberException($"the game has no type {name}");

        private object Vector2(float x, float y) => Activator.CreateInstance(Need("UnityEngine.Vector2"), x, y)!;

        private object Color(float r, float g, float b, float a) => Activator.CreateInstance(Need("UnityEngine.Color"), r, g, b, a)!;

        /// <summary>gameObject.AddComponent&lt;T&gt;().</summary>
        private static object Add(object gameObject, Type component)
        {
            MethodInfo add = gameObject.GetType().GetMethods(Any)
                .FirstOrDefault(m => m.Name == "AddComponent" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0)
                ?? throw new MissingMemberException("GameObject has no AddComponent<T>()");
            return add.MakeGenericMethod(component).Invoke(gameObject, null)
                ?? throw new InvalidOperationException($"{component.Name} could not be added");
        }

        private static MethodInfo? FindMethod(Type type, string name, int arguments)
        {
            for (Type? t = type; t != null; t = t.BaseType)
            {
                MethodInfo? found = t.GetMethods(Any | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(m => m.Name == name && !m.IsGenericMethodDefinition && m.GetParameters().Length == arguments);
                if (found != null) return found;
            }
            return null;
        }

        private static object? Call(object target, string name, params object?[] arguments)
        {
            MethodInfo method = FindMethod(target.GetType(), name, arguments.Length)
                ?? throw new MissingMemberException($"{target.GetType().Name} has no {name}() with {arguments.Length} argument(s). It has: {Dyn.Describe(target.GetType())}");
            return method.Invoke(target, arguments);
        }

        private static void CallStatic(Type type, string name, params object?[] arguments)
        {
            MethodInfo method = FindMethod(type, name, arguments.Length)
                ?? throw new MissingMemberException($"{type.Name} has no {name}()");
            method.Invoke(null, arguments);
        }

        private static object Get(object target, string member) =>
            TryGet(target, member) ?? throw new MissingMemberException($"{target.GetType().Name}.{member} is missing or empty. It has: {Dyn.Describe(target.GetType())}");

        private static object? TryGet(object target, string member)
        {
            for (Type? t = target.GetType(); t != null; t = t.BaseType)
            {
                PropertyInfo? property = t.GetProperty(member, Any | BindingFlags.DeclaredOnly);
                if (property != null && property.GetMethod != null) return property.GetValue(target);
                FieldInfo? field = t.GetField(member, Any | BindingFlags.DeclaredOnly);
                if (field != null) return field.GetValue(target);
            }
            return null;
        }

        private static void TrySet(object target, string member, object value)
        {
            try { Set(target, member, value); }
            catch (Exception) { /* a nicety, not needed */ }
        }

        /// <summary>Set a property or field, turning a number into the enum or number type the member wants.</summary>
        private static void Set(object target, string member, object value)
        {
            for (Type? t = target.GetType(); t != null; t = t.BaseType)
            {
                PropertyInfo? property = t.GetProperty(member, Any | BindingFlags.DeclaredOnly);
                if (property != null && property.SetMethod != null)
                {
                    property.SetValue(target, Fit(value, property.PropertyType));
                    return;
                }
                FieldInfo? field = t.GetField(member, Any | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    field.SetValue(target, Fit(value, field.FieldType));
                    return;
                }
            }
            throw new MissingMemberException($"{target.GetType().Name} has no {member} to set. It has: {Dyn.Describe(target.GetType())}");
        }

        private static object Fit(object value, Type wanted)
        {
            if (wanted.IsInstanceOfType(value)) return value;
            if (wanted.IsEnum) return Enum.ToObject(wanted, Convert.ToInt32(value));
            if (wanted.IsPrimitive) return Convert.ChangeType(value, wanted);
            return value;
        }

        private static Exception Inner(Exception problem) =>
            problem is TargetInvocationException && problem.InnerException != null ? problem.InnerException : problem;
    }
}
