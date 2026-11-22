using System;
using System.Linq;
using MegabonkAP.Core;
using MegabonkAP.Tests.FakeGame;
using Xunit;

namespace MegabonkAP.Tests
{
    /// <summary>What may be used: the rules, on the tracker alone.</summary>
    public class LockRuleTests
    {
        private readonly CheckList _list = CheckList.Load();
        private readonly FakeLink _link = new();
        private readonly ListLog _log = new();

        private Tracker Start(params string[] items)
        {
            var tracker = new Tracker(_list, _link, _log);
            _link.Give(items);
            return tracker;
        }

        [Fact]
        public void NothingIsLockedBeforeTheServerHasSaidWhatIsHeld()
        {
            _link.Connected = false;
            var tracker = new Tracker(_list, _link, _log);
            Assert.False(tracker.Locking);
            Assert.Null(tracker.CharacterRefusal("Fox"));
            Assert.Null(tracker.RunRefusal("Desert", 3));
            Assert.True(tracker.OfferAllowed("weapon", "Sword"));
        }

        [Fact]
        public void ACharacterIsRefusedUntilItsItemArrives()
        {
            Tracker tracker = Start("Character - Fox");
            Assert.Null(tracker.CharacterRefusal("Fox"));
            Assert.Equal("Calcium is locked: you do not hold Character - Calcium.", tracker.CharacterRefusal("Calcium"));
            Assert.True(_log.Has("Calcium is locked"));
            _link.Give("Character - Calcium");
            Assert.Null(tracker.CharacterRefusal("Calcium"));
        }

        [Fact]
        public void ATierNeedsAsManyTierItemsAsItsNumber()
        {
            Tracker tracker = Start("Progressive Forest Tier", "Progressive Forest Tier");
            Assert.Null(tracker.RunRefusal("Forest", 1));
            Assert.Null(tracker.RunRefusal("Forest", 2));
            Assert.Equal("Forest Tier 3 is locked: it needs 3 Progressive Forest Tier, you hold 2.", tracker.RunRefusal("Forest", 3));
            Assert.Equal("Desert Tier 1 is locked: it needs 1 Progressive Desert Tier, you hold 0.", tracker.RunRefusal("Desert", 1));
            Assert.Equal("Graveyard is locked: you do not hold Graveyard Access.", tracker.RunRefusal("Graveyard", 1));
            _link.Give("Graveyard Access");
            Assert.Null(tracker.RunRefusal("Graveyard", 1));
        }

        [Fact]
        public void ThingsTheWorldDoesNotKnowAreNeverRefused()
        {
            Tracker tracker = Start("Character - Fox");
            Assert.Null(tracker.CharacterRefusal("SomeNewCharacter"));
            Assert.Null(tracker.RunRefusal("Hell", 1));
            Assert.True(tracker.OfferAllowed("weapon", "Test"));
            Assert.True(tracker.OfferAllowed("tome", "Gambler"));
        }

        [Fact]
        public void OffersFollowHeldItemsAndTheCharactersOwnWeapon()
        {
            Tracker tracker = Start("Character - Fox", "Weapon - Sword", "Damage Tome");
            Assert.True(tracker.OfferAllowed("weapon", "Sword"));
            Assert.False(tracker.OfferAllowed("weapon", "Bone"));
            Assert.True(tracker.OfferAllowed("tome", "Damage"));
            Assert.False(tracker.OfferAllowed("tome", "Health"));

            // Fox starts with the Firestaff: usable once Fox is the character picked or played
            Assert.False(tracker.OfferAllowed("weapon", "FireStaff"));
            Assert.Null(tracker.CharacterRefusal("Fox"));
            Assert.True(tracker.OfferAllowed("weapon", "FireStaff"));
        }

        [Theory]
        [InlineData(false, true)]
        [InlineData(true, false)]
        public void WithLockOrStrictOffEverythingMayBeUsed(bool strict, bool locking)
        {
            Tracker tracker = Start("Character - Fox");
            tracker.Strict = strict;
            tracker.Lock = locking;
            Assert.False(tracker.Locking);
            Assert.Null(tracker.CharacterRefusal("Calcium"));
            Assert.Null(tracker.RunRefusal("Desert", 3));
            Assert.True(tracker.OfferAllowed("tome", "Health"));
        }
    }

    /// <summary>The same rules reached through stand-ins for the game's own objects.</summary>
    [Collection("fake game")]
    public class LockReaderTests
    {
        private readonly CheckList _list = CheckList.Load();
        private readonly FakeLink _link = new();
        private readonly ListLog _log = new();
        private readonly Tracker _tracker;
        private readonly GameReader _reader;

        public LockReaderTests()
        {
            _tracker = new Tracker(_list, _link, _log);
            var finder = new TypeFinder(() => new[] { typeof(GameManager).Assembly });
            _reader = new GameReader(_tracker, _log, finder.Find);
            GameManager.Instance = new GameManager();
        }

        private static MyButtonCharacter Button(ECharacter character) => new() { characterData = { eCharacter = character } };

        [Fact]
        public void ConfirmingALockedCharacterIsStopped()
        {
            _link.Give("Character - Fox");
            var menu = new CharacterMenu { selectedCharacter = ECharacter.Fox };
            Assert.True(_reader.MayConfirmCharacter(menu, Button(ECharacter.Fox)));
            Assert.False(_reader.MayConfirmCharacter(menu, Button(ECharacter.Calcium)));      // the button wins over a stale selection
        }

        [Fact]
        public void WhenTheButtonCannotBeReadTheMenusSelectionIsUsed()
        {
            _link.Give("Character - Fox");
            var button = new PlainButton();
            Assert.False(_reader.MayConfirmCharacter(new CharacterMenu { selectedCharacter = ECharacter.Calcium }, button));
            Assert.True(_reader.MayConfirmCharacter(new CharacterMenu { selectedCharacter = ECharacter.Fox }, button));
            Assert.True(_log.Has("Could not read the character on the button"));
        }

        [Fact]
        public void WhenNothingCanBeReadTheGameGoesOn()
        {
            _link.Give("Character - Fox");
            Assert.True(_reader.MayConfirmCharacter(new PlainButton(), new PlainButton()));
            Assert.True(_reader.MayConfirmCharacter(null, null));
            Assert.True(_reader.MayStartRun(null));
            Assert.True(_reader.MayStartRun(new PlainButton()));
            Assert.True(_reader.MayPickTier(new SelectionGroupToggleSingleButtonTier()));
        }

        [Fact]
        public void ALockedTierCanNeitherBePickedNorStarted()
        {
            _link.Give("Progressive Forest Tier");
            var forest = new MapData { eMap = EMap.Forest };
            var tier1 = new RunConfig { mapData = forest, mapTierIndex = 0 };
            var tier2 = new RunConfig { mapData = forest, mapTierIndex = 1 };
            Assert.True(_reader.MayStartRun(tier1));
            Assert.False(_reader.MayStartRun(tier2));
            Assert.True(_reader.MayPickTier(new SelectionGroupToggleSingleButtonTier { runConfig = tier1 }));
            Assert.False(_reader.MayPickTier(new SelectionGroupToggleSingleButtonTier { runConfig = tier2 }));
            Assert.False(_reader.MayStartRun(new RunConfig { mapData = new MapData { eMap = EMap.Desert }, mapTierIndex = 0 }));
        }

        private static GameList<WeaponData> Weapons(params EWeapon[] weapons)
        {
            var list = new GameList<WeaponData>();
            foreach (EWeapon weapon in weapons) list.Add(new WeaponData { eWeapon = weapon });
            return list;
        }

        private static EWeapon[] In(GameList<WeaponData> list) =>
            Dyn.Items(list).Cast<WeaponData>().Select(w => w.eWeapon).OrderBy(w => w).ToArray();

        [Fact]
        public void WeaponsNotHeldAreTakenOutOfTheOffers()
        {
            _link.Give("Character - Fox", "Weapon - Sword");
            _tracker.CharacterRefusal("Fox");                   // Fox was picked: the Firestaff is Fox's own
            GameList<WeaponData> offers = Weapons(EWeapon.FireStaff, EWeapon.Bone, EWeapon.Sword, EWeapon.Sniper, EWeapon.Test);
            _reader.LimitOffers("weapon", offers);
            Assert.Equal(new[] { EWeapon.FireStaff, EWeapon.Sword, EWeapon.Test }, In(offers));     // Test is not the world's to lock
            Assert.True(_log.Has("Level-up weapons limited to the 3 of 5 you hold: FireStaff, Sword, Test."));
        }

        [Fact]
        public void WhenTheGameReusesItsListAnItemThatArrivedComesBack()
        {
            _link.Give("Weapon - Sword");
            GameList<WeaponData> offers = Weapons(EWeapon.Bone, EWeapon.Sword);
            _reader.LimitOffers("weapon", offers);
            Assert.Equal(new[] { EWeapon.Sword }, In(offers));

            _link.Give("Weapon - Bone");
            _reader.LimitOffers("weapon", offers);
            Assert.Equal(new[] { EWeapon.Bone, EWeapon.Sword }, In(offers));
            _reader.LimitOffers("weapon", offers);              // and again: nothing doubles
            Assert.Equal(2, offers.Count);
        }

        [Fact]
        public void AFreshListEachTimeIsLimitedEachTime()
        {
            _link.Give("Weapon - Sword");
            _reader.LimitOffers("weapon", Weapons(EWeapon.Bone, EWeapon.Sword));
            GameList<WeaponData> second = Weapons(EWeapon.Sword, EWeapon.Sniper);
            _reader.LimitOffers("weapon", second);
            Assert.Equal(new[] { EWeapon.Sword }, In(second));
            Assert.Equal(1, _log.Count("Level-up weapons limited"));        // the same outcome is said once
        }

        [Fact]
        public void HoldingNoneOfThemLeavesTheOffersAlone()
        {
            _link.Give("Character - Fox");
            GameList<WeaponData> offers = Weapons(EWeapon.Bone, EWeapon.Sword);
            _reader.LimitOffers("weapon", offers);
            Assert.Equal(2, offers.Count);
            Assert.True(_log.Has("You hold none of the 2 weapons the game can offer"));
        }

        [Fact]
        public void TomesAreFoundByTheirEnumEvenUnderAnotherMemberName()
        {
            _link.Give("Damage Tome", "Bloody Tome");
            var offers = new GameList<RenamedTomeData>();
            foreach (ETome tome in new[] { ETome.Damage, ETome.Health, ETome.Blood, ETome.Bounce }) offers.Add(new RenamedTomeData { which = tome });
            _reader.LimitOffers("tome", offers);
            Assert.Equal(new[] { ETome.Damage, ETome.Blood, ETome.Bounce },
                Dyn.Items(offers).Cast<RenamedTomeData>().Select(t => t.which).ToArray());       // Bounce is not in the world: left alone
        }

        [Fact]
        public void WithLockOffTheOffersAreNotTouched()
        {
            _tracker.Lock = false;
            _link.Give("Weapon - Sword");
            GameList<WeaponData> offers = Weapons(EWeapon.Bone, EWeapon.Sword);
            _reader.LimitOffers("weapon", offers);
            Assert.Equal(2, offers.Count);
        }

        [Fact]
        public void AListThatCannotBeUnderstoodIsLeftAlone()
        {
            _link.Give("Weapon - Sword");
            var odd = new GameList<PlainButton>();
            odd.Add(new PlainButton());
            _reader.LimitOffers("weapon", odd);
            Assert.Equal(1, odd.Count);
            Assert.True(_log.Has("Could not read which weapon this is (EWeapon)"));
            _reader.LimitOffers("weapon", null);
            _reader.LimitOffers("weapon", "not a list at all");
        }
    }

    public class OverlayTests
    {
        private readonly CheckList _list = CheckList.Load();
        private readonly FakeLink _link = new();
        private readonly ListLog _log = new();
        private DateTime _now = new(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);

        private Tracker Start(params string[] items)
        {
            var tracker = new Tracker(_list, _link, _log, null, () => _now);
            _link.Give(items);
            return tracker;
        }

        private string Show(Tracker tracker, int mode = Overlay.Compact, bool slotSet = true, string? key = null) =>
            Overlay.Render(tracker.Look(), _list, mode, _now, slotSet, key);

        /// <summary>The text without its colour and size tags.</summary>
        private static string Plain(string text) => System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", "");

        [Fact]
        public void BeforeConnectingItSaysSo()
        {
            _link.Connected = false;
            var tracker = new Tracker(_list, _link, _log);
            Assert.Equal("Archipelago  not connected", Plain(Show(tracker)));
            Assert.Equal("Archipelago  no slot name set", Plain(Show(tracker, slotSet: false)));
            Assert.Equal("", Show(tracker, Overlay.Hidden));
        }

        [Fact]
        public void TheCompactViewCountsWhatIsHeld()
        {
            _link.CheckedAlready.Add("Fox - Complete a Run");
            Tracker tracker = Start("Character - Fox", "Character - Calcium", "Weapon - Sword", "Damage Tome",
                "Progressive Forest Tier", "Progressive Forest Tier");
            _now = _now.AddMinutes(1);          // the "Connected" note has gone
            Assert.Equal(
                "Archipelago  1/110 checks  DeathLink off\n"
                + "Held: 2 characters, 1 weapons, 1 tomes\n"
                + "Maps: Forest Tier 2, Desert locked, Graveyard locked",
                Plain(Show(tracker)));
        }

        [Fact]
        public void InARunItNamesTheRunAndWhatIsStillOpen()
        {
            Tracker tracker = Start("Character - Fox", "Progressive Forest Tier");
            tracker.RunStarted("Fox", "Forest", 1);
            string text = Plain(Show(tracker));
            Assert.Contains("Forest Tier 1 as Fox\n", text);
            Assert.Contains("Open here: Tier 1, Fox's run, Level 30\n", text);

            tracker.PlayerLevel(30);
            tracker.StageBossDied(true);
            Assert.DoesNotContain("Open here", Plain(Show(tracker)));
            Assert.Contains("> Check sent: Fox - Reach Level 30", Plain(Show(tracker)));
        }

        [Fact]
        public void ARunThatDoesNotCountSaysWhy()
        {
            Tracker tracker = Start("Character - Fox", "Progressive Forest Tier");
            tracker.RunStarted("Calcium", "Forest", 1);
            Assert.Contains("Does not count: you do not hold Character - Calcium", Plain(Show(tracker)));
        }

        [Fact]
        public void NotesShowWhatArrivedAndWhatWasRefusedThenFade()
        {
            Tracker tracker = Start("Character - Fox");
            _link.Give("Weapon - Sword");
            tracker.CharacterRefusal("Calcium");
            tracker.CharacterRefusal("Calcium");            // pressed twice: one line
            string text = Plain(Show(tracker));
            Assert.Contains("> Connected: you hold 1 items.", text);
            Assert.Contains("> Received: Weapon - Sword", text);
            Assert.Equal(1, text.Split("Calcium is locked").Length - 1);

            _now = _now.AddSeconds(30);
            Assert.DoesNotContain(">", Plain(Show(tracker)));
        }

        [Fact]
        public void TheFullViewListsWhatIsHeldAndMarksWhatIsDone()
        {
            _link.CheckedAlready.Add("Sword - Reach Level 10");
            Tracker tracker = Start("Character - Fox", "Weapon - Sword", "Weapon - Bone", "Damage Tome", "Graveyard Access");
            _now = _now.AddMinutes(1);
            string text = Show(tracker, Overlay.Full, key: "F8");
            Assert.Contains("Characters: Fox\n", Plain(text));
            Assert.Contains("Weapons: Bone, Sword\n", Plain(text));
            Assert.Contains("Tomes: Damage\n", Plain(text));
            Assert.Contains("Maps: Forest locked, Desert locked, Graveyard\n", Plain(text));
            Assert.Contains("<color=#9be29b>Sword</color>", text);
            Assert.DoesNotContain("<color=#9be29b>Bone</color>", text);
            Assert.EndsWith("F8: hide", Plain(text));
        }

        [Fact]
        public void ItSaysWhetherDeathLinkIsOnAndTheLogSaysWhyNot()
        {
            Tracker tracker = Start("Character - Fox");
            Assert.Contains("DeathLink off", Plain(Show(tracker)));
            Assert.True(_log.Has("DeathLink is off: this seed was generated with death_link off."));

            _link.Slot["death_link"] = 1L;
            _link.Announce();
            Assert.Contains("DeathLink on", Plain(Show(tracker)));
            Assert.True(_log.Has("DeathLink is on."));

            tracker.DeathLinkAllowed = false;
            _link.Announce();
            Assert.Contains("DeathLink off", Plain(Show(tracker)));
            Assert.True(_log.Has("DeathLink is off in the mod's settings."));
        }

        [Fact]
        public void TheKeyStepsThroughTheViews()
        {
            Assert.Equal(Overlay.Full, Overlay.Next(Overlay.Compact));
            Assert.Equal(Overlay.Hidden, Overlay.Next(Overlay.Full));
            Assert.Equal(Overlay.Compact, Overlay.Next(Overlay.Hidden));
            Assert.Equal(Overlay.Full, Overlay.ParseMode(" full "));
            Assert.Equal(Overlay.Hidden, Overlay.ParseMode("Hidden"));
            Assert.Equal(Overlay.Compact, Overlay.ParseMode("anything else"));
            Assert.Equal("Full", Overlay.ModeName(Overlay.ParseMode(Overlay.ModeName(Overlay.Full))));
        }

        [Fact]
        public void ALostConnectionAndWaitingChecksAreShown()
        {
            Tracker tracker = Start("Character - Fox", "Progressive Forest Tier");
            _link.Connected = false;
            tracker.ConnectionSeen(false);
            tracker.RunStarted("Fox", "Forest", 1);
            tracker.PlayerLevel(30);
            string text = Plain(Show(tracker));
            Assert.Contains("connection lost", text);
            Assert.Contains("(1 waiting to be sent)", text);
            Assert.Contains("> Check earned, kept until the server is back: Fox - Reach Level 30", text);
        }
    }
}
