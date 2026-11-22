using System.Linq;
using MegabonkAP.Core;
using MegabonkAP.Tests.FakeGame;
using UnityEngine;
using Xunit;

namespace MegabonkAP.Tests
{
    /// <summary>Locked offers at a level-up: dimmed, and refused while something else can be picked.</summary>
    [Collection("fake unity")]      // the buttons are stand-in Unity objects
    public class OfferPickTests
    {
        private readonly CheckList _list = CheckList.Load();
        private readonly FakeLink _link = new();
        private readonly ListLog _log = new();
        private readonly Tracker _tracker;
        private readonly GameReader _reader;

        public OfferPickTests()
        {
            GameObject.Broken = false;
            _tracker = new Tracker(_list, _link, _log);
            _reader = new GameReader(_tracker, _log, new TypeFinder(() => new[] { typeof(GameManager).Assembly }).Find);
        }

        private static IUpgradable Weapon(EWeapon weapon) => new(new WeaponData { eWeapon = weapon });
        private static IUpgradable Tome(ETome tome) => new(new TomeData { eTome = tome });

        private static UpgradePicker Picker(params IUpgradable?[] offers)
        {
            var picker = new UpgradePicker();
            foreach (IUpgradable? offer in offers) picker.buttons.Add(new UpgradeButton { upgradable = offer });
            return picker;
        }

        private static float[] Alphas(UpgradePicker picker) => Dyn.Items(picker.buttons).Cast<UpgradeButton>()
            .Select(b => b.gameObject.GetComponent<CanvasGroup>()?.alpha ?? 1f).ToArray();

        [Fact]
        public void ALockedOfferIsRefusedWhileAnotherCanBePicked()
        {
            _link.Give("Weapon - Sword", "Damage Tome");
            UpgradePicker picker = Picker(Weapon(EWeapon.Sword), Weapon(EWeapon.Bone), Tome(ETome.Health));
            Assert.True(_reader.MayPickOffer(picker, Weapon(EWeapon.Sword)));
            Assert.False(_reader.MayPickOffer(picker, Weapon(EWeapon.Bone)));
            Assert.False(_reader.MayPickOffer(picker, Tome(ETome.Health)));
            Assert.True(_log.Has("Bone is locked: you do not hold Weapon - Bone."));
            Assert.True(_log.Has("Health Tome is locked: you do not hold Health Tome."));
        }

        [Fact]
        public void WhenEveryOfferIsLockedAnyMayBePicked()
        {
            _link.Give("Weapon - Sword");
            UpgradePicker picker = Picker(Weapon(EWeapon.Bone), Tome(ETome.Health), Tome(ETome.Damage));
            Assert.True(_reader.MayPickOffer(picker, Weapon(EWeapon.Bone)));
            Assert.True(_log.Has("Every offer here is locked, so pick any: it will not count."));
            _reader.OffersShown(picker);
            Assert.Equal(new[] { 1f, 1f, 1f }, Alphas(picker));            // nothing is dimmed when nothing else can be picked
        }

        [Fact]
        public void ThingsThatAreNeitherWeaponNorTomeCountAsSomethingToPick()
        {
            _link.Give("Weapon - Sword");
            UpgradePicker picker = Picker(Weapon(EWeapon.Bone), new IUpgradable(new ItemData { name = "Anvil" }));
            Assert.True(_reader.MayPickOffer(picker, new IUpgradable(new ItemData { name = "Anvil" })));
            Assert.False(_reader.MayPickOffer(picker, Weapon(EWeapon.Bone)));
        }

        [Fact]
        public void ALockedOfferCanStillBeBanished()
        {
            _link.Give("Weapon - Sword");
            UpgradePicker picker = Picker(Weapon(EWeapon.Sword), Weapon(EWeapon.Bone));
            picker.banishMode = true;
            Assert.True(_reader.MayPickOffer(picker, Weapon(EWeapon.Bone)));
        }

        [Fact]
        public void WhenTheOtherOffersCannotBeSeenThePickIsLetThrough()
        {
            _link.Give("Weapon - Sword");
            Assert.True(_reader.MayPickOffer(new PlainButton(), Weapon(EWeapon.Bone)));
            Assert.True(_log.Has("other offers could not be seen"));
            Assert.True(_reader.MayPickOffer(null, null));
        }

        [Fact]
        public void ButtonsNotOnScreenAreNotCounted()
        {
            _link.Give("Weapon - Sword");
            UpgradePicker picker = Picker(Weapon(EWeapon.Bone), Weapon(EWeapon.Sword));
            Dyn.Items(picker.buttons).Cast<UpgradeButton>().Last().gameObject.SetActive(false);     // left over from an earlier level-up
            Assert.True(_reader.MayPickOffer(picker, Weapon(EWeapon.Bone)));
        }

        [Fact]
        public void LockedOffersAreDimmedAndComeBackWhenTheItemArrives()
        {
            _link.Give("Weapon - Sword");
            UpgradePicker picker = Picker(Weapon(EWeapon.Sword), Weapon(EWeapon.Bone), Tome(ETome.Health));
            _reader.OffersShown(picker);
            Assert.Equal(new[] { 1f, 0.3f, 0.3f }, Alphas(picker));

            _link.Give("Weapon - Bone");
            _reader.RefreshOffers();
            Assert.Equal(new[] { 1f, 1f, 0.3f }, Alphas(picker));
        }

        [Fact]
        public void TheSameButtonsShowTheNextLevelUpsOffers()
        {
            _link.Give("Weapon - Sword");
            UpgradePicker picker = Picker(Weapon(EWeapon.Bone), Weapon(EWeapon.Sword));
            _reader.OffersShown(picker);
            Assert.Equal(new[] { 0.3f, 1f }, Alphas(picker));
            UpgradeButton[] buttons = Dyn.Items(picker.buttons).Cast<UpgradeButton>().ToArray();
            buttons[0].upgradable = Weapon(EWeapon.Sword);
            buttons[1].upgradable = Tome(ETome.Health);
            _reader.OffersShown(picker);
            Assert.Equal(new[] { 1f, 0.3f }, Alphas(picker));
        }

        [Fact]
        public void WithLockOffNothingIsDimmedOrRefused()
        {
            _tracker.Lock = false;
            _link.Give("Weapon - Sword");
            UpgradePicker picker = Picker(Weapon(EWeapon.Sword), Weapon(EWeapon.Bone));
            _reader.OffersShown(picker);
            Assert.Equal(new[] { 1f, 1f }, Alphas(picker));
            Assert.True(_reader.MayPickOffer(picker, Weapon(EWeapon.Bone)));
        }
    }
}
