using System.Collections.Generic;

// Stand-ins for the handful of Unity and TextMeshPro classes the overlay uses, with Unity's own
// names for types and members, so the overlay's by-name code can be run without the game.
namespace UnityEngine
{
    public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } }
    public struct Color { public float r, g, b, a; public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; } }
    public enum RenderMode { ScreenSpaceOverlay = 0, ScreenSpaceCamera = 1, WorldSpace = 2 }
    public enum KeyCode { None = 0, Tab = 9, F8 = 289 }

    public class Object
    {
        public static readonly List<Object> Kept = new();
        public static void DontDestroyOnLoad(Object target) => Kept.Add(target);
    }

    public class Component : Object { public GameObject gameObject = null!; }

    public class Transform : Component
    {
        public Transform? parent;
        public void SetParent(Transform parent, bool worldPositionStays) => this.parent = parent;
    }

    public class RectTransform : Transform
    {
        public Vector2 anchorMin { get; set; }
        public Vector2 anchorMax { get; set; }
        public Vector2 pivot { get; set; }
        public Vector2 anchoredPosition { get; set; }
        public Vector2 sizeDelta { get; set; }
        public Vector2 offsetMin { get; set; }
        public Vector2 offsetMax { get; set; }
    }

    public class GameObject : Object
    {
        public static readonly List<GameObject> All = new();
        public static bool Broken;                  // makes every new object fail, as a stripped build might
        public readonly string name;
        public readonly List<Component> components = new();
        public bool active = true;
        private readonly RectTransform _transform = new();

        public GameObject(string name)
        {
            if (Broken) throw new System.InvalidOperationException("no objects today");
            this.name = name;
            _transform.gameObject = this;
            All.Add(this);
        }

        public Transform transform => _transform;
        public void SetActive(bool value) => active = value;

        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T { gameObject = this };
            components.Add(component);
            return component;
        }
    }

    public class Canvas : Component
    {
        public RenderMode renderMode { get; set; } = RenderMode.WorldSpace;
        public int sortingOrder { get; set; }
    }

    public static class Input
    {
        public static readonly HashSet<KeyCode> Down = new();
        public static bool GetKeyDown(KeyCode key) => Down.Contains(key);
        public static bool GetKeyDown(string name) => false;
    }
}

namespace UnityEngine.UI
{
    public class Graphic : Component
    {
        public Color color { get; set; }
        public bool raycastTarget { get; set; } = true;
        public RectTransform rectTransform => (RectTransform)gameObject.transform;
    }

    public class Image : Graphic { }

    public class CanvasScaler : Component
    {
        public enum ScaleMode { ConstantPixelSize = 0, ScaleWithScreenSize = 1 }
        public ScaleMode uiScaleMode { get; set; }
        public Vector2 referenceResolution { get; set; }
        public float matchWidthOrHeight { get; set; }
    }
}

namespace TMPro
{
    public enum TextAlignmentOptions { TopLeft = 257, Center = 514 }
    public class TMP_FontAsset : UnityEngine.Object { }

    public class TMP_Text : UnityEngine.UI.Graphic
    {
        public string text { get; set; } = "";
        public float fontSize { get; set; }
        public TextAlignmentOptions alignment { get; set; }
        public bool enableWordWrapping { get; set; }
        public bool richText { get; set; }
        public TMP_FontAsset? font { get; set; } = new();
        public UnityEngine.Vector2 GetPreferredValues(string text, float width, float height) =>
            new(width, 30f * (text.Split('\n').Length));
    }

    public class TextMeshProUGUI : TMP_Text { }
}
