using System;
using System.Linq;
using MegabonkAP.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Xunit;

namespace MegabonkAP.Tests
{
    /// <summary>The overlay's drawing code, run against stand-ins that carry Unity's own names.</summary>
    [Collection("fake unity")]      // Unity's statics are shared
    public class ScreenTests
    {
        private readonly ListLog _log = new();
        private int _presses;

        public ScreenTests()
        {
            GameObject.All.Clear();
            GameObject.Broken = false;
            UnityEngine.Object.Kept.Clear();
            Input.Down.Clear();
        }

        private Screen Make(string corner = "MiddleLeft", string key = "F8") =>
            new(_log, new TypeFinder(() => new[] { typeof(GameObject).Assembly }), corner, 20, 520, key, () => _presses++);

        /// <summary>Pump as a new frame some time later: the overlay only looks a few times a second.</summary>
        private static void Frame(Screen screen)
        {
            System.Threading.Thread.Sleep(220);
            screen.Pump();
        }

        private static GameObject Named(string name) => GameObject.All.Single(o => o.name == name);
        private static TextMeshProUGUI Label() => Named("Text").components.OfType<TextMeshProUGUI>().Single();
        private static RectTransform Panel() => (RectTransform)Named("Panel").transform;

        [Fact]
        public void NothingIsBuiltUntilThereIsTextToShow()
        {
            Screen screen = Make();
            screen.Pump();
            Assert.Empty(GameObject.All);
            Assert.True(screen.Alive);
        }

        [Fact]
        public void TheTextGoesOnACanvasOverTheGameThatTakesNoClicks()
        {
            Screen screen = Make();
            screen.Text = "one\ntwo";
            screen.Pump();

            GameObject root = Named("MegabonkAP Overlay");
            Assert.Contains(root, UnityEngine.Object.Kept);                 // survives a change of scene
            Canvas canvas = root.components.OfType<Canvas>().Single();
            Assert.Equal(RenderMode.ScreenSpaceOverlay, canvas.renderMode);
            Assert.Equal(30000, canvas.sortingOrder);
            Assert.Equal(CanvasScaler.ScaleMode.ScaleWithScreenSize, root.components.OfType<CanvasScaler>().Single().uiScaleMode);

            Image image = Named("Panel").components.OfType<Image>().Single();
            Assert.False(image.raycastTarget);
            Assert.Same(root.transform, Panel().parent);

            TextMeshProUGUI label = Label();
            Assert.Equal("one\ntwo", label.text);
            Assert.Equal(20f, label.fontSize);
            Assert.Equal(TextAlignmentOptions.TopLeft, label.alignment);
            Assert.False(label.raycastTarget);
            Assert.True(label.enableWordWrapping);
            Assert.Same(Panel(), label.rectTransform.parent);
            Assert.True(_log.Has("Overlay: on screen."));
        }

        [Theory]
        [InlineData("MiddleLeft", 0f, 0.5f, 14f, 0f)]
        [InlineData("TopRight", 1f, 1f, -14f, -14f)]
        [InlineData("bottom left", 0f, 0f, 14f, 14f)]
        public void ThePanelSitsWhereTheSettingSays(string corner, float x, float y, float offsetX, float offsetY)
        {
            Screen screen = Make(corner);
            screen.Text = "text";
            screen.Pump();
            RectTransform panel = Panel();
            Assert.Equal((x, y), (panel.anchorMin.x, panel.anchorMin.y));
            Assert.Equal((x, y), (panel.pivot.x, panel.pivot.y));
            Assert.Equal((offsetX, offsetY), (panel.anchoredPosition.x, panel.anchoredPosition.y));
        }

        [Fact]
        public void ThePanelIsAsTallAsItsTextAndFollowsNewText()
        {
            Screen screen = Make();
            screen.Text = "one";
            screen.Pump();
            Assert.Equal(520f, Panel().sizeDelta.x);
            Assert.Equal(30f + 18f, Panel().sizeDelta.y);

            screen.Text = "one\ntwo\nthree";
            Frame(screen);
            Assert.Equal("one\ntwo\nthree", Label().text);
            Assert.Equal(90f + 18f, Panel().sizeDelta.y);
            Assert.Equal(3, GameObject.All.Count);                          // built once
        }

        [Fact]
        public void EmptyTextHidesItAndTextBringsItBack()
        {
            Screen screen = Make();
            screen.Text = "shown";
            screen.Pump();
            screen.Text = "";
            Frame(screen);
            Assert.False(Named("MegabonkAP Overlay").active);
            screen.Text = "again";
            Frame(screen);
            Assert.True(Named("MegabonkAP Overlay").active);
        }

        [Fact]
        public void TheKeyIsSeenOncePerPress()
        {
            Screen screen = Make();
            screen.Pump();
            Assert.True(screen.KeyWorks);
            Input.Down.Add(KeyCode.F8);
            screen.Pump();
            screen.Pump();                  // the same frame, reached through another hook
            Assert.Equal(1, _presses);
            Input.Down.Clear();
            screen.Pump();
            Assert.Equal(1, _presses);
        }

        [Theory]
        [InlineData("None")]
        [InlineData("NoSuchKey")]
        public void WithoutAUsableKeyTheOverlayStillShows(string key)
        {
            Screen screen = Make(key: key);
            screen.Text = "text";
            screen.Pump();
            Assert.False(screen.KeyWorks);
            Assert.Equal("text", Label().text);
            Assert.Equal(key == "NoSuchKey", _log.Has("cannot be used"));
        }

        [Fact]
        public void WhenUnityRefusesItGivesUpQuietlyAfterThreeTries()
        {
            GameObject.Broken = true;
            Screen screen = Make();
            screen.Text = "text";
            for (int i = 0; i < 6; i++) Frame(screen);
            Assert.True(screen.Dead);
            Assert.Equal(2, _log.Count("Trying again"));
            Assert.Equal(1, _log.Count("switched off after three failures. The last: InvalidOperationException: no objects today"));
        }

        [Fact]
        public void AMissingTypeIsNamedInTheLog()
        {
            var screen = new Screen(_log, new TypeFinder(() => new[] { typeof(string).Assembly }), "TopLeft", 20, 520, "None", () => { });
            screen.Text = "text";
            screen.Pump();
            Assert.True(_log.Has("the game has no type UnityEngine.GameObject"));
        }
    }
}
