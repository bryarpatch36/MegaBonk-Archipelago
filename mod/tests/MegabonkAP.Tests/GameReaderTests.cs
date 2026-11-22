using System;
using System.Linq;
using MegabonkAP.Core;
using MegabonkAP.Tests.FakeGame;
using Xunit;

namespace MegabonkAP.Tests
{
    /// <summary>
    /// The whole path from a game object to a check, with stand-in classes shaped after game
    /// version 1.0.69 as the probe recorded it. The events are given in the order the game gives
    /// them: the run starts before the player exists, and the character is named afterwards.
    /// </summary>
    [Collection("fake game")]       // the game's statics are shared
    public class GameReaderTests
    {
        private readonly CheckList _list = CheckList.Load();
        private readonly FakeLink _link = new();
        private readonly ListLog _log = new();
        private readonly Tracker _tracker;
        private readonly GameReader _reader;
        private readonly MyPlayer _player = new();
        private readonly MapData _forest = new() { eMap = EMap.Forest };
        private int _nextEnemy = 1;

        public GameReaderTests()
        {
            _tracker = new Tracker(_list, _link, _log);
            var finder = new TypeFinder(() => new[] { typeof(GameManager).Assembly });
            _reader = new GameReader(_tracker, _log, finder.Find) { StageBossEvent = true };
            GameManager.Instance = new GameManager();       // no player yet, as when a run starts
            MapController.runConfig = null;
            MapController.inventory = null;
            MapController.TierFinal = false;
            ChallengesTracker.Current = null;
        }

        private RunConfig Forest(int tier, ChallengeData? challenge = null) =>
            new() { mapData = _forest, mapTierIndex = tier - 1, challenge = challenge };

        /// <summary>The game's order: run settings first, then the player appears as a character.</summary>
        private void Begin(RunConfig config, ECharacter character)
        {
            MapController.runConfig = config;
            _reader.RunStarted(config);
            _player.character = character;
            GameManager.Instance!.player = _player;
            _reader.PlayerStarted(character);
        }

        private Enemy Dies(EEnemy name, EEnemyFlag flags = EEnemyFlag.None)
        {
            var enemy = new Enemy { enemyData = { enemyName = name }, enemyFlag = flags, Pointer = new IntPtr(_nextEnemy++) };
            _reader.EnemyDied(enemy);
            return enemy;
        }

        [Fact]
        public void AWholeTier2RunReadFromGameObjects()
        {
            _link.Give("Character - Fox", "Progressive Forest Tier", "Progressive Forest Tier", "Damage Tome");
            Begin(Forest(2), ECharacter.Fox);
            Assert.True(_log.Has("Run started: Forest Tier 2"));
            Assert.True(_log.Has("Playing as Fox."));
            Assert.False(_log.Has("This run does not count"));

            WeaponInventory weapons = _player.inventory.weaponInventory;
            weapons.weapons.Set(EWeapon.FireStaff, new WeaponBase { level = 10 });
            weapons.weapons.Set(EWeapon.Sword, new WeaponBase { level = 11 });
            _reader.WeaponsChanged(weapons);

            TomeInventory tomes = _player.inventory.tomeInventory;
            tomes.tomeLevels.Set(ETome.Damage, 8);
            tomes.tomeLevels.Set(ETome.Health, 9);
            tomes.tomeLevels.Set(ETome.Bounce, 9);          // a tome the world does not have
            _reader.TomesChanged(tomes);

            _reader.XpChanged(new PlayerXp { level = 30 });

            Dies(EEnemy.Goblin);
            Dies(EEnemy.Golem);                             // an ordinary enemy, not the miniboss
            Dies(EEnemy.MinibossPig, EEnemyFlag.Boss);
            Dies(EEnemy.Ent1, EEnemyFlag.StageBoss);
            _reader.StageBossDied();                        // first stage: the tier goes on
            Assert.DoesNotContain("Forest - Complete Tier 2 (Chadbark)", _link.Sent);

            Dies(EEnemy.Ent2, EEnemyFlag.StageBoss);
            MapController.TierFinal = true;
            _reader.StageBossDied();

            Assert.Equal(new[]
            {
                "Damage Tome - Reach Level 8",
                "Firestaff - Reach Level 10",
                "Forest - Complete Tier 2 (Chadbark)",
                "Forest - Defeat Chunkham the Terrible",
                "Fox - Complete a Run",
                "Fox - Reach Level 30",
            }, _link.Sent.OrderBy(s => s));
            Assert.False(_log.Has("matches nothing"));     // Bounce is known and passed over
            Assert.True(_log.Has("Boss killed on Forest: the game calls it 'Ent1'."));
            Assert.True(_log.Has("Seen this run: 5 enemy deaths (3 bosses), player level 30"));
        }

        [Fact]
        public void TheFirstStageBossOfATier1RunCompletesIt()
        {
            _link.Give("Character - Sir Oofie", "Progressive Forest Tier");
            Begin(Forest(1), ECharacter.SirOofie);
            MapController.TierFinal = true;
            _reader.StageBossDied();
            Assert.Equal(new[] { "Forest - Complete Tier 1 (Lil Bark)", "Sir Oofie - Complete a Run" }, _link.Sent.OrderBy(s => s));
        }

        [Fact]
        public void TheCharacterIsReadFromThePlayerWhenTheRunStartsWithOneAlreadyThere()
        {
            // a later stage of the same run, or a restart: the player object survives
            _link.Give("Character - CL4NK", "Progressive Forest Tier");
            _player.character = ECharacter.Cl4nk;
            GameManager.Instance!.player = _player;
            MapController.runConfig = Forest(1);
            _reader.RunRestarted();
            Assert.True(_log.Has("Run started: Forest Tier 1 as CL4NK"));
        }

        [Fact]
        public void ARunWithACharacterThatIsNotHeldIsRefusedAsSoonAsTheCharacterIsKnown()
        {
            _link.Give("Character - Fox", "Progressive Forest Tier");
            Begin(Forest(1), ECharacter.Calcium);
            Assert.Equal(1, _log.Count("This run does not count: you do not hold Character - Calcium"));
            MapController.TierFinal = true;
            _reader.StageBossDied();
            Assert.Empty(_link.Sent);
        }

        [Fact]
        public void WithoutTheStageBossEventStageBossesAreFoundAmongTheDeaths()
        {
            _reader.StageBossEvent = false;
            _link.Give("Character - Fox", "Progressive Forest Tier");
            Begin(Forest(1), ECharacter.Fox);
            MapController.TierFinal = true;
            Dies(EEnemy.Ent1, EEnemyFlag.StageBoss);
            Assert.Contains("Forest - Complete Tier 1 (Lil Bark)", _link.Sent);
        }

        [Fact]
        public void TwoWrappersOfOneEnemyAreOneDeath()
        {
            _link.Give("Character - Fox", "Progressive Forest Tier");
            Begin(Forest(1), ECharacter.Fox);
            _reader.EnemyDied(new Enemy { enemyData = { enemyName = EEnemy.Goblin }, Pointer = new IntPtr(77) });
            _reader.EnemyDied(new Enemy { enemyData = { enemyName = EEnemy.Goblin }, Pointer = new IntPtr(77) });
            _reader.PlayerDied();
            Assert.True(_log.Has("Seen this run: 1 enemy deaths"));
        }

        [Fact]
        public void TheGraveyardsBossIsTheBossRoomsOwnEnemyWhateverItIsCalled()
        {
            _link.Give("Character - Calcium", "Graveyard Access");
            Begin(new RunConfig { mapData = new MapData { eMap = EMap.Graveyard } }, ECharacter.Calcium);
            _reader.CryptOpened();

            var room = new GraveyardBossRoom();
            _reader.GraveyardEnemyDied(room, Dies(EEnemy.Skeleton));    // dies before the boss exists
            room.bossEnemy = new Enemy { enemyData = { enemyName = EEnemy.GhostKing }, Pointer = new IntPtr(900) };
            _reader.GraveyardEnemyDied(room, Dies(EEnemy.Skeleton));    // not the boss
            Assert.DoesNotContain("Graveyard - Defeat Big Bob", _link.Sent);

            // the same boss, reached through another wrapper object
            _reader.GraveyardEnemyDied(room, new Enemy { enemyData = { enemyName = EEnemy.GhostKing }, Pointer = new IntPtr(900) });
            Assert.Equal(new[]
            {
                "Calcium - Complete a Run", "Graveyard - Collect the 4 Crypt Keys", "Graveyard - Defeat Big Bob",
            }, _link.Sent.OrderBy(s => s));
            Assert.True(_log.Has("The Graveyard's boss is dead: the game calls it 'GhostKing'."));
        }

        [Fact]
        public void AChallengeIsCompletedWhenTheGameSaysSoUnderWhicheverNameMatches()
        {
            _link.Give("Character - Fox", "Progressive Forest Tier");
            var challenge = new ChallengeData { name = "Challenge_Forest_07", shown = "Speedrunner+" };
            Begin(Forest(1, challenge), ECharacter.Fox);
            Assert.True(_log.Has("challenge Speedrunner+"));

            MapController.TierFinal = true;
            _reader.StageBossDied();
            Assert.DoesNotContain("Forest Challenge - Speedrunner+", _link.Sent);   // winning the run is not the signal

            ChallengesTracker.Current = challenge;
            _reader.ChallengeCompleted();
            Assert.Contains("Forest Challenge - Speedrunner+", _link.Sent);
        }

        [Fact]
        public void AChallengeNoCheckMatchesIsReportedUnderBothItsNames()
        {
            _link.Give("Character - Fox", "Progressive Forest Tier");
            Begin(Forest(1), ECharacter.Fox);
            ChallengesTracker.Current = new ChallengeData { name = "Challenge_Forest_99", shown = "Brand New" };
            _reader.ChallengeCompleted();
            Assert.True(_log.Has("the game calls it 'Brand New' / 'Challenge_Forest_99', which matches no check"));
        }

        [Fact]
        public void LevelsArePassedOnOnlyWhenTheyChange()
        {
            _link.Give("Character - Fox", "Progressive Forest Tier");
            Begin(Forest(1), ECharacter.Fox);
            _player.inventory.weaponInventory.weapons.Set(EWeapon.Sword, new WeaponBase { level = 12 });
            MapController.inventory = _player.inventory;
            _reader.InventoryChanged();
            _reader.InventoryChanged();
            _reader.WeaponsChanged(_player.inventory.weaponInventory);
            Assert.Equal(1, _log.Count("Sword reached Level 12"));
        }

        [Fact]
        public void TheLevelUpScreenFindsTheInventoryThroughThePlayerOrTheMapController()
        {
            _link.Give("Character - Fox", "Progressive Forest Tier", "Weapon - Sword");
            Begin(Forest(1), ECharacter.Fox);
            _player.inventory.weaponInventory.weapons.Set(EWeapon.Sword, new WeaponBase { level = 10 });

            GameManager.Instance!.player = null;            // only the MapController knows it
            MapController.inventory = _player.inventory;
            _reader.InventoryChanged();
            Assert.Contains("Sword - Reach Level 10", _link.Sent);
        }

        [Fact]
        public void OlderInventoriesWithoutTheLevelMethodsAreWalkedInstead()
        {
            _link.Give("Character - Fox", "Progressive Forest Tier", "Weapon - Sword", "Bloody Tome");
            Begin(Forest(1), ECharacter.Fox);

            var weapons = new OlderWeaponInventory();
            weapons.weapons.Add(new GameDict<EWeapon, WeaponBase>.Entry { Key = EWeapon.Sword, Value = new WeaponBase { level = 10 } });
            _reader.WeaponsChanged(weapons);

            var tomes = new OlderTomeInventory();
            tomes.tomeLevels.Add(new GameDict<ETome, int>.Entry { Key = ETome.Blood, Value = 8 });
            _reader.TomesChanged(tomes);

            Assert.Equal(new[] { "Sword - Reach Level 10", "Bloody Tome - Reach Level 8" }, _link.Sent);
        }

        [Fact]
        public void ARenamedMemberIsReportedWithWhatTheObjectReallyHas()
        {
            _link.Give("Character - Fox", "Progressive Forest Tier");
            Begin(Forest(1), ECharacter.Fox);
            _reader.EnemyDied(new RenamedEnemy());
            _reader.EnemyDied(new RenamedEnemy());
            Assert.Equal(1, _log.Count("Could not read the enemy's name (enemyData.enemyName) from RenamedEnemy. It has: hp, kind"));
            Assert.Empty(_link.Sent);
        }

        [Fact]
        public void ARunWhoseSettingsCannotBeReadDoesNotCountAndSaysWhatIsMissing()
        {
            _link.Give("Character - Fox", "Progressive Forest Tier");
            Begin(new RunConfig(), ECharacter.Fox);
            Assert.True(_log.Has("Could not read the map (mapData.eMap) from RunConfig. It has: challenge, mapData, mapTierIndex, stageData"));
            _reader.XpChanged(new PlayerXp { level = 30 });
            Assert.True(_log.Has("This run does not count: the map could not be read"));
        }

        [Fact]
        public void ADeathLinkKillsThroughTheGamesOwnMethod()
        {
            var health = new PlayerHealth();
            _reader.Kill(health);
            Assert.Equal(1, health.Killed);
            _reader.Kill(new RenamedEnemy());
            Assert.True(_log.Has("DeathLink: the game's KillPlayer could not be found"));
        }

        private PlayerHealth DeathLinkRun()
        {
            _link.Slot["death_link"] = 1L;
            _link.Give("Character - Fox", "Progressive Forest Tier");
            Begin(Forest(1), ECharacter.Fox);
            return _player.inventory.playerHealth;
        }

        [Fact]
        public void ADeathIsNoticedByLookingAtTheHealthAndToldOnce()
        {
            PlayerHealth health = DeathLinkRun();
            _reader.Frame();
            Assert.Empty(_link.Deaths);

            health.hp = 0;
            _reader.Frame();
            _reader.Frame();
            _reader.PlayerDied();                       // the game's own PlayerDied, should it come as well
            Assert.Single(_link.Deaths);

            health.hp = 100;                            // a new life: the next death counts again
            _reader.Frame();
            health.hp = 0;
            _reader.Frame();
            Assert.Equal(2, _link.Deaths.Count);
        }

        [Fact]
        public void TheGamesOwnPlayerDiedComingFirstIsNotToldTwice()
        {
            PlayerHealth health = DeathLinkRun();
            health.hp = 0;
            _reader.PlayerDied();
            _reader.Frame();
            Assert.Single(_link.Deaths);
        }

        [Fact]
        public void AReceivedDeathKillsAtTheNextLookAndIsNotSentBack()
        {
            PlayerHealth health = DeathLinkRun();
            _tracker.KillRequested += (_, _) => _reader.RequestKill();
            _link.Die("Chief", "fell");
            _reader.Frame();
            Assert.Equal(1, health.Killed);
            Assert.True(_log.Has("DeathLink: the game's KillPlayer was called."));
            _reader.Frame();                            // now dead: seen, but it is Chief's death
            _reader.Frame();
            Assert.Equal(1, health.Killed);
            Assert.Empty(_link.Deaths);
        }

        [Fact]
        public void WhenKillPlayerDoesNothingTheHealthIsEmptied()
        {
            PlayerHealth health = DeathLinkRun();
            health.KillWorks = false;
            DateTime now = DateTime.UtcNow;
            _reader.Now = () => now;
            _reader.RequestKill();
            _reader.Frame();
            Assert.Equal(1, health.Killed);
            now = now.AddSeconds(2);
            _reader.Frame();
            Assert.Equal(0, health.hp);
            Assert.Equal(0f, health.shield);
            Assert.Equal(1, health.Checked);
            Assert.True(_log.Has("KillPlayer left the player alive, so their health was set to zero."));
        }

        [Fact]
        public void ADeathLinkThatArrivesBetweenRunsLapses()
        {
            DateTime now = DateTime.UtcNow;
            _reader.Now = () => now;
            _reader.RequestKill();
            _reader.Frame();                            // no player yet
            now = now.AddSeconds(30);
            _reader.Frame();
            Assert.True(_log.Has("DeathLink: no player was found within ten seconds"));
            PlayerHealth health = DeathLinkRun();
            _reader.Frame();
            Assert.Equal(0, health.Killed);
        }

        [Fact]
        public void NothingBreaksBeforeTheGameHasAnythingToRead()
        {
            GameManager.Instance = null;
            _reader.RunStarted(null);
            _reader.RunRestarted();
            _reader.PlayerStarted(null);
            _reader.InventoryChanged();
            _reader.WeaponsChanged(null);
            _reader.TomesChanged(null);
            _reader.XpChanged(null);
            _reader.EnemyDied(null);
            _reader.GraveyardEnemyDied(null, null);
            _reader.ChallengeCompleted();
            _reader.StageBossDied();
            _reader.Kill(null);
            _reader.RequestKill();
            _reader.Frame();
            Assert.Empty(_link.Sent);
        }
    }

    public class DynTests
    {
        private sealed class Inner { public int number = 7; private string hidden = "secret"; public string Shout() => "hey"; public int Twice(int n) => n * 2; }
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
            Assert.Equal(true, Dyn.GetStatic(typeof(MapControllerLike), "IsTierFinalStage()"));
        }

        private static class MapControllerLike { public static bool IsTierFinalStage() => true; }

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
        public void MethodsCanBeCalledWithArgumentsAndFailuresGiveNull()
        {
            Assert.Equal(10, Dyn.Call(new Inner(), "Twice", 5));
            Assert.Null(Dyn.Call(new Inner(), "Thrice", 5));
            Assert.Null(Dyn.Call(null, "Twice", 5));

            var weapons = new WeaponInventory();
            Assert.Null(Dyn.Call(weapons, "GetWeaponLevel", EWeapon.Sword));       // it throws: the weapon is not held
            Assert.Equal(new[] { EWeapon.FireStaff, EWeapon.Bone, EWeapon.Sword, EWeapon.Test, EWeapon.Sniper, EWeapon.None },
                Dyn.EnumOfFirstArgument(typeof(WeaponInventory), "GetWeaponLevel")!.Cast<EWeapon>());
            Assert.Null(Dyn.EnumOfFirstArgument(typeof(Inner), "Twice"));          // not an enum
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
        public void NamesAndNumbersComeFromEnumsNamedObjectsAndText()
        {
            Assert.Equal("Forest", Dyn.Name(EMap.Forest));
            Assert.Equal("ForestStage1", Dyn.Name(new StageData { name = "ForestStage1" }));
            Assert.Equal("plain", Dyn.Name("plain"));
            Assert.Null(Dyn.Name(null));
            Assert.Equal(6, Dyn.Int(EEnemyFlag.Boss | EEnemyFlag.StageBoss));
        }
    }

    public class ProbeTests
    {
        [Fact]
        public void TheReportListsMembersStaticsEnumValuesAndWhatIsMissing()
        {
            string report = Probe.Report(typeof(GameManager).Assembly.GetTypes(),
                new[] { "Enemy", "EMap", "MapController", "FinalFightController" }, "stage|renamed");

            Assert.Contains("type MegabonkAP.Tests.FakeGame.Enemy", report);
            Assert.Contains("EnemyData enemyData", report);
            Assert.Contains("enum MegabonkAP.Tests.FakeGame.EMap", report);
            Assert.Contains("Graveyard = 8", report);
            Assert.Contains("static RunConfig runConfig", report);
            Assert.Contains("static Boolean IsTierFinalStage()", report);
            Assert.Contains("MISSING  FinalFightController", report);
            Assert.Contains("type MegabonkAP.Tests.FakeGame.StageData", report);
            Assert.Contains("type MegabonkAP.Tests.FakeGame.RenamedEnemy", report);
        }

        [Fact]
        public void TheFinderFindsATypeByItsShortName()
        {
            var finder = new TypeFinder(() => new[] { typeof(GameManager).Assembly });
            Assert.Equal(typeof(GameManager), finder.Find("GameManager"));
            Assert.Equal(typeof(MapController), finder.Find("MapController"));
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
