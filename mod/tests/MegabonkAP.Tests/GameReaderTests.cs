using System;
using System.Linq;
using MegabonkAP.Core;
using MegabonkAP.Tests.FakeGame;
using Xunit;

namespace MegabonkAP.Tests
{
    /// <summary>
    /// The whole path from a game object to a check, with stand-in classes that carry the game's
    /// member names. What this cannot show is whether the real game uses those names.
    /// </summary>
    [Collection("fake game")]       // GameManager.Instance is shared
    public class GameReaderTests
    {
        private readonly CheckList _list = CheckList.Load();
        private readonly FakeLink _link = new();
        private readonly ListLog _log = new();
        private readonly Tracker _tracker;
        private readonly GameReader _reader;
        private readonly MyPlayer _player = new();
        private readonly MapData _forest = new() { eMap = EMap.Forest };

        public GameReaderTests()
        {
            _tracker = new Tracker(_list, _link, _log);
            var finder = new TypeFinder(() => new[] { typeof(GameManager).Assembly });
            _reader = new GameReader(_tracker, _log, finder.Find);
            GameManager.Instance = new GameManager { player = _player };
            foreach (string name in new[] { "ForestStage1", "ForestStage2", "ForestStage3" })
                _forest.stages.Add(new StageData { name = name });
        }

        private StageData Stage(int tier) => Dyn.Items(_forest.stages).Cast<StageData>().ElementAt(tier - 1);

        [Fact]
        public void AWholeRunReadFromGameObjects()
        {
            _link.Give("Character - Fox", "Progressive Forest Tier", "Progressive Forest Tier", "Damage Tome");
            _player.characterData.eCharacter = ECharacter.Fox;

            _reader.RunStarted(new RunConfig { mapData = _forest, stageData = Stage(2) });
            Assert.True(_log.Has("Run started: Fox on Forest Tier 2"));

            _player.inventory.weaponInventory.weapons.Set(EWeapon.FireStaff, new WeaponBase { level = 10 });
            _player.inventory.weaponInventory.weapons.Set(EWeapon.Sword, new WeaponBase { level = 11 });
            _player.inventory.tomeInventory.tomeLevels.Set(ETome.Damage, 8);
            _player.inventory.tomeInventory.tomeLevels.Set(ETome.Hp, 9);
            _reader.InventoryChanged();

            _reader.XpChanged(new PlayerXp { level = 30 });

            _reader.EnemyDied(new Enemy { enemyData = { enemyName = EEnemy.Goblin }, Pointer = new IntPtr(1) });
            _reader.EnemyDied(new Enemy { enemyData = { enemyName = EEnemy.MinibossPig }, enemyFlag = 2, Pointer = new IntPtr(2) });
            _reader.EnemyDied(new Enemy { enemyData = { enemyName = EEnemy.ForestBoss1 }, enemyFlag = 4, Pointer = new IntPtr(3) });
            Assert.DoesNotContain("Forest - Complete Tier 2 (Chadbark)", _link.Sent);
            _reader.EnemyDied(new Enemy { enemyData = { enemyName = EEnemy.ForestBoss1 }, enemyFlag = 32, Pointer = new IntPtr(4) });

            Assert.Equal(new[]
            {
                "Damage Tome - Reach Level 8",
                "Firestaff - Reach Level 10",
                "Forest - Complete Tier 2 (Chadbark)",
                "Forest - Defeat Chunkham the Terrible",
                "Fox - Complete a Run",
                "Fox - Reach Level 30",
            }, _link.Sent.OrderBy(s => s));
        }

        [Fact]
        public void TwoWrappersOfOneEnemyAreOneDeath()
        {
            _link.Give("Character - Fox", "Progressive Forest Tier", "Progressive Forest Tier");
            _player.characterData.eCharacter = ECharacter.Fox;
            _reader.RunStarted(new RunConfig { mapData = _forest, stageData = Stage(2) });

            // the same boss reported through both of the game's death methods
            _reader.EnemyDied(new Enemy { enemyData = { enemyName = EEnemy.ForestBoss1 }, enemyFlag = 4, Pointer = new IntPtr(77) });
            _reader.EnemyDied(new Enemy { enemyData = { enemyName = EEnemy.ForestBoss1 }, enemyFlag = 4, Pointer = new IntPtr(77) });
            Assert.Empty(_link.Sent);       // one stage boss so far, and Tier 2 needs two
        }

        [Fact]
        public void TheGraveyardIsReadWithoutTiers()
        {
            _link.Give("Character - Calcium", "Graveyard Access");
            _player.characterData.eCharacter = ECharacter.Calcium;
            var graveyard = new MapData { eMap = EMap.Graveyard };
            var only = new StageData { name = "Graveyard" };
            graveyard.stages.Add(only);

            _reader.RunStarted(new RunConfig { mapData = graveyard, stageData = only });
            _reader.CryptOpened();
            _reader.EnemyDied(new Enemy { enemyData = { enemyName = EEnemy.GraveyardBob }, enemyFlag = 4, Pointer = new IntPtr(5) });
            Assert.Equal(new[]
            {
                "Calcium - Complete a Run", "Graveyard - Collect the 4 Crypt Keys", "Graveyard - Defeat Big Bob",
            }, _link.Sent.OrderBy(s => s));
        }

        [Fact]
        public void LevelsArePassedOnOnlyWhenTheyChange()
        {
            _link.Give("Character - Fox", "Progressive Forest Tier");
            _player.characterData.eCharacter = ECharacter.Fox;
            _reader.RunStarted(new RunConfig { mapData = _forest, stageData = Stage(1) });
            _player.inventory.weaponInventory.weapons.Set(EWeapon.Sword, new WeaponBase { level = 12 });
            _reader.InventoryChanged();
            _reader.InventoryChanged();
            _reader.InventoryChanged();
            Assert.Equal(1, _log.Count("Sword reached Level 12"));
        }

        [Fact]
        public void ARenamedMemberIsReportedWithWhatTheObjectReallyHas()
        {
            _link.Give("Character - Fox", "Progressive Forest Tier");
            _player.characterData.eCharacter = ECharacter.Fox;
            _reader.RunStarted(new RunConfig { mapData = _forest, stageData = Stage(1) });

            _reader.EnemyDied(new RenamedEnemy());
            _reader.EnemyDied(new RenamedEnemy());
            Assert.Equal(1, _log.Count("Could not read the enemy's name (enemyData.enemyName) from RenamedEnemy. It has: hp, kind"));
            Assert.Empty(_link.Sent);
        }

        [Fact]
        public void ARunWhoseSettingsCannotBeReadDoesNotCountAndSaysWhatIsMissing()
        {
            _link.Give("Character - Fox", "Progressive Forest Tier");
            _player.characterData.eCharacter = ECharacter.Fox;
            _reader.RunStarted(new RunConfig());
            Assert.True(_log.Has("Could not read the map (mapData.eMap) from RunConfig. It has: mapData, stageData"));
            Assert.True(_log.Has("This run does not count: the map could not be read"));
        }

        [Fact]
        public void NothingBreaksWhenTheGameHasNoPlayerYet()
        {
            GameManager.Instance = null;
            _reader.RunStarted(new RunConfig { mapData = _forest, stageData = Stage(1) });
            _reader.InventoryChanged();
            _reader.XpChanged(null);
            _reader.EnemyDied(null);
            Assert.Empty(_link.Sent);
            Assert.True(_log.Has("The player's inventory could not be read"));
        }
    }

    public class DynTests
    {
        private sealed class Inner { public int number = 7; private string hidden = "secret"; public string Shout() => "hey"; }
        private sealed class Outer { public Inner? inner = new(); public static Outer Shared = new(); public string Label { get; } = "label"; }

        [Fact]
        public void PathsFollowFieldsPropertiesAndMethods()
        {
            var outer = new Outer();
            Assert.Equal(7, Dyn.Get(outer, "inner.number"));
            Assert.Equal("secret", Dyn.Get(outer, "inner.hidden"));
            Assert.Equal("hey", Dyn.Get(outer, "inner.Shout()"));
            Assert.Equal("label", Dyn.Get(outer, "Label"));
            Assert.Equal(7, Dyn.GetStatic(typeof(Outer), "Shared.inner.number"));
        }

        [Fact]
        public void MissingOrNullPartsGiveNullNotAnError()
        {
            var outer = new Outer { inner = null };
            Assert.Null(Dyn.Get(outer, "inner.number"));
            Assert.Null(Dyn.Get(outer, "nothing.here"));
            Assert.Null(Dyn.Get(null, "inner"));
            Assert.Null(Dyn.GetStatic(null, "Shared"));
            Assert.Equal("label", Dyn.First(outer, "nope", "inner.number", "Label"));
        }

        [Fact]
        public void TheGamesOwnCollectionsCanBeWalked()
        {
            var list = new GameList<string>();
            list.Add("a");
            list.Add("b");
            Assert.Equal(new object?[] { "a", "b" }, Dyn.Items(list));

            var levels = new GameDict<ETome, int>();
            levels.Set(ETome.Damage, 3);
            levels.Set(ETome.Chaos, 5);
            Assert.Equal(new[] { ("Damage", 3), ("Chaos", 5) },
                Dyn.Pairs(levels).Select(p => (Dyn.Name(p.key)!, Dyn.Int(p.value)!.Value)));

            Assert.Equal(new[] { ("x", 1) },
                Dyn.Pairs(new System.Collections.Generic.Dictionary<string, int> { ["x"] = 1 })
                    .Select(p => ((string)p.key!, (int)p.value!)));
        }

        [Fact]
        public void NamesComeFromEnumsNamedObjectsAndText()
        {
            Assert.Equal("Forest", Dyn.Name(EMap.Forest));
            Assert.Equal("ForestStage1", Dyn.Name(new StageData { name = "ForestStage1" }));
            Assert.Equal("plain", Dyn.Name("plain"));
            Assert.Null(Dyn.Name(null));
        }
    }

    public class ProbeTests
    {
        [Fact]
        public void TheReportListsMembersEnumValuesAndWhatIsMissing()
        {
            string report = Probe.Report(typeof(GameManager).Assembly.GetTypes(),
                new[] { "Enemy", "EMap", "FinalFightController" }, "stage|renamed");

            Assert.Contains("type MegabonkAP.Tests.FakeGame.Enemy", report);
            Assert.Contains("Boolean IsStageBoss()", report);
            Assert.Contains("EnemyData enemyData", report);
            Assert.Contains("enum MegabonkAP.Tests.FakeGame.EMap", report);
            Assert.Contains("Graveyard = 3", report);
            Assert.Contains("MISSING  FinalFightController", report);
            Assert.Contains("type MegabonkAP.Tests.FakeGame.StageData", report);
            Assert.Contains("type MegabonkAP.Tests.FakeGame.RenamedEnemy", report);
        }

        [Fact]
        public void TheFinderFindsATypeByItsShortName()
        {
            var finder = new TypeFinder(() => new[] { typeof(GameManager).Assembly });
            Assert.Equal(typeof(GameManager), finder.Find("GameManager"));
            Assert.Null(finder.Find("NoSuchType"));
        }
    }

    public class AddressTests
    {
        [Theory]
        [InlineData("archipelago.gg:38281", "archipelago.gg", 38281)]
        [InlineData("localhost", "localhost", 38281)]
        [InlineData(" 10.0.0.5:1234 ", "10.0.0.5", 1234)]
        [InlineData("wss://archipelago.gg:54321", "wss://archipelago.gg", 54321)]
        [InlineData("ws://localhost:38281/", "ws://localhost", 38281)]
        public void ServerAddressesAreSplitIntoHostAndPort(string address, string host, int port)
        {
            Assert.Equal((host, port), ArchipelagoLink.SplitAddress(address));
        }
    }
}
