using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using MegabonkAP.Core;
using Xunit;

namespace MegabonkAP.Tests
{
    public class CheckListTests
    {
        private static readonly CheckList List = CheckList.Load();

        private static string WorldData(string file)
        {
            // tests run from mod/tests/MegabonkAP.Tests/bin/...: walk up to the folder that holds the world
            for (DirectoryInfo? at = new(AppContext.BaseDirectory); at != null; at = at.Parent)
            {
                string candidate = Path.Combine(at.FullName, "manual_megabonk_bbush", "data", file);
                if (File.Exists(candidate)) return candidate;
            }
            throw new FileNotFoundException("manual_megabonk_bbush/data/" + file);
        }

        [Fact]
        public void EveryCheckTheModCanSendIsALocationOfTheWorld()
        {
            using JsonDocument locations = JsonDocument.Parse(File.ReadAllText(WorldData("locations.json")));
            var names = locations.RootElement.EnumerateArray().Select(l => l.GetProperty("name").GetString()).ToHashSet();
            var unknown = List.AllChecks().Where(c => !names.Contains(c)).ToList();
            Assert.Empty(unknown);
            // and the other way round: nothing in the world is left for the player to tick by hand, bar the goals
            var covered = List.AllChecks().ToHashSet();
            var byHand = locations.RootElement.EnumerateArray()
                .Where(l => !l.TryGetProperty("victory", out _))
                .Select(l => l.GetProperty("name").GetString()!).Where(n => !covered.Contains(n)).ToList();
            Assert.Empty(byHand);
        }

        [Fact]
        public void EveryItemTheModReadsIsAnItemOfTheWorld()
        {
            using JsonDocument items = JsonDocument.Parse(File.ReadAllText(WorldData("items.json")));
            var names = items.RootElement.EnumerateArray().Select(i => i.GetProperty("name").GetString()).ToHashSet();
            Assert.DoesNotContain(List.AllItems(), i => !names.Contains(i));
        }

        [Fact]
        public void TheGoalsAreTheWorldsGoalsInOrder()
        {
            using JsonDocument locations = JsonDocument.Parse(File.ReadAllText(WorldData("locations.json")));
            var goals = locations.RootElement.EnumerateArray()
                .Where(l => l.TryGetProperty("victory", out JsonElement v) && v.GetBoolean())
                .Select(l => l.GetProperty("name").GetString()!).ToList();
            Assert.Equal(goals, List.Goals);
        }

        [Theory]
        [InlineData("weapon", "FireStaff", "Firestaff")]
        [InlineData("weapon", "Sniper", "Sniper Rifle")]
        [InlineData("weapon", "Rockets", "Slutty Rocket")]
        [InlineData("weapon", "Mine", "Mines")]
        [InlineData("weapon", "CorruptSword", "Corrupted Sword")]
        [InlineData("weapon", "BluetoothDagger", "Wireless Dagger")]
        [InlineData("weapon", "DragonsBreath", "Dragon's Breath")]
        [InlineData("weapon", "SpaceNoodle", "Space Noodle")]
        [InlineData("character", "SirOofie", "Sir Oofie")]
        [InlineData("character", "TonyMcZoom", "Tony McZoom")]
        [InlineData("character", "Cl4nk", "CL4NK")]
        [InlineData("character", "Clank", "CL4NK")]
        [InlineData("tome", "Damage", "Damage")]
        [InlineData("tome", "DamageTome", "Damage")]
        [InlineData("tome", "ProjectileSpeed", "Projectile Speed")]
        [InlineData("tome", "Hp", "Health")]
        [InlineData("tome", "Xp", "XP")]
        [InlineData("map", "Forest", "Forest")]
        public void TheGamesNamesResolveToTheWorldsNames(string kind, string gameName, string expected)
        {
            Assert.Equal(expected, List.Resolve(kind, gameName));
        }

        [Fact]
        public void EveryWeaponOfTheGamesOwnListResolves()
        {
            // EWeapon as documented in lukeod/megabonk_research (2026-01-28), without None and Test
            string[] game =
            {
                "FireStaff", "Bone", "Sword", "Revolver", "Aura", "Axe", "Bow", "Aegis", "LightningStaff",
                "Flamewalker", "Rockets", "Bananarang", "Tornado", "Dexecutioner", "Sniper", "Frostwalker",
                "SpaceNoodle", "DragonsBreath", "Chunkers", "Mine", "PoisonFlask", "BlackHole", "Katana",
                "BloodMagic", "BluetoothDagger", "Dice", "HeroSword", "CorruptSword", "Shotgun", "Scythe",
            };
            var resolved = game.Select(g => List.Resolve("weapon", g)).ToList();
            Assert.DoesNotContain(null, resolved);
            Assert.Equal(List.Weapons.Count, resolved.Distinct().Count());      // all 30, none twice
        }

        [Fact]
        public void UnknownNamesResolveToNothing()
        {
            Assert.Null(List.Resolve("weapon", "Banhammer"));
            Assert.Null(List.Resolve("weapon", null));
            Assert.Null(List.Resolve("map", "Hell"));
        }

        [Theory]
        [InlineData("Forest", "MinibossPig", "Chunkham the Terrible")]
        [InlineData("Forest", "MinibossGolem", "Stone Golem")]
        [InlineData("Desert", "MinibossGolem", "Sand Golem")]
        [InlineData("Desert", "Scorpionussy", "Scorpionussy")]
        [InlineData("Desert", "CharacterBandit", "Bandit")]
        [InlineData("Forest", "SusBush", "Suspicious Bush")]
        [InlineData("Graveyard", "BigBob", "Big Bob")]
        [InlineData("Graveyard", "SpookySteve", "Spooky Steve")]
        public void BossesAreRecognisedByAWordInTheirNamePerMap(string map, string gameName, string expected)
        {
            Assert.Equal(expected, List.ResolveBoss(map, gameName));
        }

        [Fact]
        public void OrdinaryEnemiesAndOtherMapsBossesAreNotBosses()
        {
            Assert.Null(List.ResolveBoss("Forest", "Goblin"));
            Assert.Null(List.ResolveBoss("Forest", "Scorpionussy"));     // a Desert boss
            Assert.Null(List.ResolveBoss("Hell", "MinibossGolem"));      // not a map of the world
        }

        [Fact]
        public void SpeedrunnerAndSpeedrunnerPlusAreDifferentChallenges()
        {
            Assert.Equal("Forest Challenge - Speedrunner", List.ResolveChallenge("Forest", "Speedrunner"));
            Assert.Equal("Forest Challenge - Speedrunner+", List.ResolveChallenge("Forest", "Speedrunner+"));
            Assert.Equal("Desert Challenge - The Floor is Lava", List.ResolveChallenge("Desert", "TheFloorIsLava"));
            Assert.Null(List.ResolveChallenge("Desert", "Fragile"));     // a Forest challenge
        }
    }

    public class TrackerTests
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
        public void ATier1RunWithAHeldCharacterEarnsTheTierAndTheRun()
        {
            Tracker tracker = Start("Character - Fox", "Progressive Forest Tier");
            tracker.RunStarted("Fox", "Forest", 1);
            tracker.EnemyKilled("Goblin", false);
            Assert.Empty(_link.Sent);
            tracker.EnemyKilled("ForestBoss1", true);
            Assert.Equal(new[] { "Forest - Complete Tier 1 (Lil Bark)", "Fox - Complete a Run" }, _link.Sent.OrderBy(s => s));
        }

        [Fact]
        public void ARunWithACharacterThatIsNotHeldEarnsNothingAndSaysWhy()
        {
            Tracker tracker = Start("Character - Fox", "Progressive Forest Tier");
            tracker.RunStarted("Calcium", "Forest", 1);
            tracker.PlayerLevel(40);
            tracker.EnemyKilled("ForestBoss1", true);
            Assert.Empty(_link.Sent);
            Assert.Equal(1, _log.Count("This run does not count: you do not hold Character - Calcium"));
        }

        [Fact]
        public void ATierNeedsThatManyTierItems()
        {
            Tracker tracker = Start("Character - Fox", "Progressive Forest Tier");
            tracker.RunStarted("Fox", "Forest", 2);
            tracker.EnemyKilled("ForestBoss1", true);
            tracker.EnemyKilled("ForestBoss2", true);
            Assert.Empty(_link.Sent);
            Assert.True(_log.Has("Forest Tier 2 needs 2 Progressive Forest Tier, you hold 1"));
        }

        [Fact]
        public void ATier2RunIsWonAtTheSecondStageBoss()
        {
            Tracker tracker = Start("Character - Fox", "Progressive Forest Tier", "Progressive Forest Tier");
            tracker.RunStarted("Fox", "Forest", 2);
            tracker.EnemyKilled("ForestBoss1", true);
            Assert.Empty(_link.Sent);
            tracker.EnemyKilled("ForestBoss2", true);
            Assert.Contains("Forest - Complete Tier 2 (Chadbark)", _link.Sent);
            Assert.DoesNotContain("Forest - Complete Tier 1 (Lil Bark)", _link.Sent);
            Assert.Contains("Fox - Complete a Run", _link.Sent);
        }

        [Fact]
        public void TheCharactersOwnWeaponCountsWithoutItsItemAndOthersDoNot()
        {
            Tracker tracker = Start("Character - Fox", "Progressive Forest Tier");
            tracker.RunStarted("Fox", "Forest", 1);
            tracker.WeaponLevel("FireStaff", 9);
            Assert.Empty(_link.Sent);
            tracker.WeaponLevel("FireStaff", 10);
            Assert.Equal(new[] { "Firestaff - Reach Level 10" }, _link.Sent);

            tracker.WeaponLevel("Sword", 12);
            Assert.DoesNotContain("Sword - Reach Level 10", _link.Sent);
            Assert.True(_log.Has("Sword reached Level 12, but it does not count: you do not hold Weapon - Sword"));

            _link.Give("Weapon - Sword");
            tracker.WeaponLevel("Sword", 13);
            Assert.Contains("Sword - Reach Level 10", _link.Sent);
        }

        [Fact]
        public void ATomeCountsOnlyWhenHeld()
        {
            Tracker tracker = Start("Character - Fox", "Progressive Forest Tier", "Damage Tome");
            tracker.RunStarted("Fox", "Forest", 1);
            tracker.TomeLevel("Damage", 8);
            tracker.TomeLevel("Hp", 9);
            Assert.Equal(new[] { "Damage Tome - Reach Level 8" }, _link.Sent);
        }

        [Fact]
        public void TheCharacterLevelCheckComesAtLevel30()
        {
            Tracker tracker = Start("Character - Sir Oofie", "Progressive Forest Tier");
            tracker.RunStarted("SirOofie", "Forest", 1);
            tracker.PlayerLevel(29);
            Assert.Empty(_link.Sent);
            tracker.PlayerLevel(30);
            tracker.PlayerLevel(31);
            Assert.Equal(new[] { "Sir Oofie - Reach Level 30" }, _link.Sent);
        }

        [Fact]
        public void MinibossesGiveTheCheckOfTheMapTheyAreOn()
        {
            Tracker tracker = Start("Character - Fox", "Progressive Forest Tier", "Progressive Desert Tier");
            tracker.RunStarted("Fox", "Forest", 1);
            tracker.EnemyKilled("MinibossGolem", false);
            tracker.EnemyKilled("MinibossPig", false);
            tracker.RunStarted("Fox", "Desert", 1);
            tracker.EnemyKilled("MinibossGolem", false);
            Assert.Equal(new[]
            {
                "Forest - Defeat Stone Golem", "Forest - Defeat Chunkham the Terrible", "Desert - Defeat Sand Golem",
            }, _link.Sent);
        }

        [Fact]
        public void TheGraveyardHasKeysAndBigBobEndsTheRun()
        {
            Tracker tracker = Start("Character - Calcium", "Graveyard Access");
            tracker.RunStarted("Calcium", "Graveyard", 1);
            tracker.CryptOpened();
            tracker.EnemyKilled("GraveyardBob", true);
            Assert.Equal(new[]
            {
                "Calcium - Complete a Run", "Graveyard - Collect the 4 Crypt Keys", "Graveyard - Defeat Big Bob",
            }, _link.Sent.OrderBy(s => s));
        }

        [Fact]
        public void TheGraveyardNeedsItsAccessItem()
        {
            Tracker tracker = Start("Character - Calcium", "Progressive Forest Tier");
            tracker.RunStarted("Calcium", "Graveyard", 1);
            tracker.CryptOpened();
            Assert.Empty(_link.Sent);
            Assert.True(_log.Has("you do not hold Graveyard Access"));
        }

        [Fact]
        public void AChallengeRunEarnsItsChallengeCheckWhenWon()
        {
            Tracker tracker = Start("Character - Fox", "Progressive Forest Tier");
            tracker.RunStarted("Fox", "Forest", 1, "Speedrunner+");
            tracker.EnemyKilled("ForestBoss1", true);
            Assert.Contains("Forest Challenge - Speedrunner+", _link.Sent);
        }

        [Fact]
        public void WithStrictOffEverythingCounts()
        {
            Tracker tracker = Start();
            tracker.Strict = false;
            tracker.RunStarted("Vlad", "Desert", 3);
            tracker.WeaponLevel("Shotgun", 10);
            tracker.TomeLevel("Chaos", 8);
            Assert.Equal(new[] { "Shotgun - Reach Level 10", "Chaos Tome - Reach Level 8" }, _link.Sent);
        }

        [Fact]
        public void AStageBossIsCountedOncePerRunWon()
        {
            Tracker tracker = Start("Character - Fox", "Progressive Forest Tier");
            tracker.RunStarted("Fox", "Forest", 1);
            tracker.EnemyKilled("ForestBoss1", true);
            tracker.EnemyKilled("ForestBoss1", true);
            Assert.Equal(2, _link.Sent.Count);
            Assert.Equal(_link.Sent.Count, _link.Sent.Distinct().Count());
        }

        [Fact]
        public void NothingHappensOutsideARun()
        {
            Tracker tracker = Start("Character - Fox", "Progressive Forest Tier");
            tracker.PlayerLevel(50);
            tracker.WeaponLevel("FireStaff", 20);
            tracker.EnemyKilled("ForestBoss1", true);
            tracker.RunStarted("Fox", "Forest", 1);
            tracker.RunEnded();
            tracker.PlayerLevel(50);
            Assert.Empty(_link.Sent);
        }

        [Fact]
        public void AnUnknownTierSendsNoTierCheckAndSaysSo()
        {
            Tracker tracker = Start("Character - Fox", "Progressive Forest Tier");
            tracker.RunStarted("Fox", "Forest", 0);
            tracker.EnemyKilled("ForestBoss1", true);
            Assert.Empty(_link.Sent);
            Assert.True(_log.Has("the tier of this run could not be read"));
        }

        [Fact]
        public void NamesTheListDoesNotKnowAreLoggedOnce()
        {
            Tracker tracker = Start("Character - Fox", "Progressive Forest Tier");
            tracker.RunStarted("Fox", "Forest", 1);
            tracker.WeaponLevel("Banhammer", 10);
            tracker.WeaponLevel("Banhammer", 11);
            Assert.Equal(1, _log.Count("The game named a weapon 'Banhammer'"));
        }

        [Fact]
        public void ChecksEarnedOfflineAreKeptAndSentOnceConnected()
        {
            string file = Path.Combine(Path.GetTempPath(), "megabonkap-test-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                _link.Connected = false;
                var tracker = new Tracker(_list, _link, _log, file) { Strict = false };
                tracker.RunStarted("Fox", "Forest", 1);
                tracker.PlayerLevel(30);
                Assert.Empty(_link.Sent);
                Assert.Equal(new[] { "Fox - Reach Level 30" }, tracker.Owed);
                Assert.Contains("Fox - Reach Level 30", File.ReadAllText(file));

                // the game is closed and started again; the server is back
                var link2 = new FakeLink();
                var tracker2 = new Tracker(_list, link2, _log, file);
                link2.Announce();
                Assert.Equal(new[] { "Fox - Reach Level 30" }, link2.Sent);
                Assert.Empty(tracker2.Owed);
            }
            finally { File.Delete(file); }
        }

        [Fact]
        public void ChecksKeptFromAnotherSeedAreNotSentToThisOne()
        {
            string file = Path.Combine(Path.GetTempPath(), "megabonkap-test-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var tracker = new Tracker(_list, _link, _log, file) { Strict = false };
                _link.Announce();                          // adopts seed-a
                _link.Connected = false;
                tracker.RunStarted("Fox", "Forest", 1);
                tracker.PlayerLevel(30);                   // owed, for seed-a

                var other = new FakeLink { Session = "seed-b:1" };
                var tracker2 = new Tracker(_list, other, _log, file);
                other.Announce();
                Assert.Empty(other.Sent);
                Assert.Empty(tracker2.Owed);
            }
            finally { File.Delete(file); }
        }

        [Fact]
        public void AChecksTheSeedDoesNotHaveIsDropped()
        {
            _link.NotInSeed.Add("Fox - Reach Level 30");   // include_character_levels was off
            Tracker tracker = Start("Character - Fox", "Progressive Forest Tier");
            tracker.RunStarted("Fox", "Forest", 1);
            tracker.PlayerLevel(30);
            Assert.Empty(_link.Sent);
            Assert.Empty(tracker.Owed);
        }

        [Fact]
        public void TheGoalIsSentOnceBothMapsHaveTheGoalsTier()
        {
            _link.Slot["goal"] = 0L;                                        // Tier 1 Bosses
            _link.CheckedAlready.Add("Desert - Complete Tier 1 (Lil Anubis)");   // ticked by hand earlier
            Tracker tracker = Start("Character - Fox", "Progressive Forest Tier");
            Assert.Equal(0, _link.Goals);
            tracker.RunStarted("Fox", "Forest", 1);
            tracker.EnemyKilled("ForestBoss1", true);
            Assert.Equal(1, _link.Goals);
            tracker.Flush();
            _link.Announce();
            Assert.Equal(1, _link.Goals);
        }

        [Fact]
        public void TheGoalIsNotSentForALowerTier()
        {
            _link.Slot["goal"] = 1L;                                        // Tier 2 Bosses
            _link.CheckedAlready.Add("Desert - Complete Tier 1 (Lil Anubis)");
            Tracker tracker = Start("Character - Fox", "Progressive Forest Tier");
            tracker.RunStarted("Fox", "Forest", 1);
            tracker.EnemyKilled("ForestBoss1", true);
            Assert.Equal(0, _link.Goals);
        }

        [Fact]
        public void DeathLinkSendsADeathAndDoesNotEchoOneItWasSent()
        {
            _link.Slot["death_link"] = 1L;
            DateTime clock = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
            var tracker = new Tracker(_list, _link, _log, null, () => clock);
            string? killed = null;
            tracker.KillRequested += (who, why) => killed = who;
            _link.Give("Character - Fox", "Progressive Forest Tier");
            tracker.RunStarted("Fox", "Forest", 1);

            tracker.PlayerDied();
            Assert.Single(_link.Deaths);

            _link.Die("Chief", "fell off Halo");
            Assert.Equal("Chief", killed);
            clock = clock.AddSeconds(3);
            tracker.PlayerDied();                       // that is the death Chief caused
            Assert.Single(_link.Deaths);

            clock = clock.AddSeconds(30);
            tracker.PlayerDied();
            Assert.Equal(2, _link.Deaths.Count);
        }

        [Fact]
        public void WithDeathLinkOffNothingIsSentOrActedOn()
        {
            Tracker tracker = Start("Character - Fox", "Progressive Forest Tier");
            bool killed = false;
            tracker.KillRequested += (_, _) => killed = true;
            tracker.RunStarted("Fox", "Forest", 1);
            tracker.PlayerDied();
            _link.Die("Chief", "fell");
            Assert.Empty(_link.Deaths);
            Assert.False(killed);

            _link.Slot["death_link"] = 1L;
            tracker.DeathLinkAllowed = false;           // switched off in the mod's settings
            tracker.PlayerDied();
            Assert.Empty(_link.Deaths);
        }
    }
}
