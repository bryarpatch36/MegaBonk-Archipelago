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

        private static string Repo(params string[] parts)
        {
            // tests run from mod/tests/MegabonkAP.Tests/bin/...: walk up to the folder that holds the world
            for (DirectoryInfo? at = new(AppContext.BaseDirectory); at != null; at = at.Parent)
            {
                string candidate = Path.Combine(new[] { at.FullName }.Concat(parts).ToArray());
                if (File.Exists(candidate)) return candidate;
            }
            throw new FileNotFoundException(string.Join("/", parts));
        }

        private static string WorldData(string file) => Repo("manual_megabonk_bbush", "data", file);

        /// <summary>The value names of an enum as the probe of the real game recorded them.</summary>
        private static string[] GameEnum(string name)
        {
            string[] lines = File.ReadAllLines(Repo("mod", "docs", "probes", "probe-1.0.69.txt"));
            int start = Array.FindIndex(lines, l => l.StartsWith("enum ") && (l.EndsWith(" " + name) || l.EndsWith("." + name)));
            Assert.True(start >= 0, "the probe has no enum " + name);
            return lines.Skip(start + 1).TakeWhile(l => l.StartsWith("    ")).Select(l => l.Trim().Split(' ')[0]).ToArray();
        }

        [Fact]
        public void EveryCheckTheModCanSendIsALocationOfTheWorld()
        {
            using JsonDocument locations = JsonDocument.Parse(File.ReadAllText(WorldData("locations.json")));
            var names = locations.RootElement.EnumerateArray().Select(l => l.GetProperty("name").GetString()).ToHashSet();
            Assert.DoesNotContain(List.AllChecks(), c => !names.Contains(c));
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
        [InlineData("character", "SirOofie", "Sir Oofie")]
        [InlineData("character", "TonyMcZoom", "Tony McZoom")]
        [InlineData("character", "Cl4nk", "CL4NK")]
        [InlineData("tome", "Damage", "Damage")]
        [InlineData("tome", "ProjectileSpeed", "Projectile Speed")]
        [InlineData("tome", "Regeneration", "Regen")]
        [InlineData("tome", "Blood", "Bloody")]
        [InlineData("tome", "Xp", "XP")]
        [InlineData("map", "Forest", "Forest")]
        public void TheGamesNamesResolveToTheWorldsNames(string kind, string gameName, string expected)
        {
            Assert.Equal(expected, List.Resolve(kind, gameName));
        }

        [Theory]
        [InlineData("weapon", "EWeapon")]
        [InlineData("tome", "ETome")]
        [InlineData("character", "ECharacter")]
        [InlineData("map", "EMap")]
        public void EveryNameInTheRealGamesListsIsKnownOrDeliberatelyPassedOver(string kind, string gameEnum)
        {
            string[] game = GameEnum(gameEnum);
            Assert.True(game.Length > 3, gameEnum + " was not read from the probe");
            Assert.DoesNotContain(game, g => List.Resolve(kind, g) == null && !List.IsIgnored(kind, g));

            // and nothing of the world is unreachable: each of its names is what some game name resolves to
            var reached = game.Select(g => List.Resolve(kind, g)).Where(n => n != null).ToHashSet();
            var world = kind switch
            {
                "weapon" => List.Weapons.Keys,
                "tome" => List.Tomes.Keys,
                "character" => List.Characters.Keys,
                _ => List.Maps.Keys,
            };
            Assert.DoesNotContain(world, w => !reached.Contains(w));
        }

        [Fact]
        public void EveryBossEnemyIsARealEnemyOfTheGame()
        {
            string[] enemies = GameEnum("EEnemy");
            foreach (string enemy in new[] { "MinibossPig", "MinibossGolem", "Bush", "MinibossGolemSand", "MinibossScorpion", "Bandit" })
            {
                Assert.Contains(enemy, enemies);
                Assert.NotNull(List.EnemyCheck(enemy));
            }
        }

        [Fact]
        public void UnknownNamesResolveToNothing()
        {
            Assert.Null(List.Resolve("weapon", "Banhammer"));
            Assert.Null(List.Resolve("weapon", null));
            Assert.Null(List.Resolve("map", "Hell"));
            Assert.False(List.IsIgnored("weapon", "Banhammer"));
            Assert.True(List.IsIgnored("tome", "Gambler"));
            Assert.True(List.IsIgnored("weapon", "Test"));
        }

        [Theory]
        [InlineData("MinibossPig", "Forest - Defeat Chunkham the Terrible")]
        [InlineData("MinibossGolem", "Forest - Defeat Stone Golem")]
        [InlineData("Bush", "Forest - Defeat the Suspicious Bush")]
        [InlineData("MinibossGolemSand", "Desert - Defeat Sand Golem")]
        [InlineData("MinibossScorpion", "Desert - Defeat Scorpionussy")]
        [InlineData("Bandit", "Desert - Defeat the Bandit")]
        public void BossesAreRecognisedByTheGamesExactNameForThem(string enemy, string check)
        {
            Assert.Equal(check, List.EnemyCheck(enemy));
        }

        [Theory]
        [InlineData("Golem")]               // an ordinary enemy; only MinibossGolem counts
        [InlineData("Scorpion")]
        [InlineData("ScorpionMedium")]
        [InlineData("Goblin")]
        [InlineData("Ent1")]                // a tier boss: that is the tier check, not a boss check
        [InlineData(null)]
        public void OrdinaryEnemiesEarnNothing(string? enemy)
        {
            Assert.Null(List.EnemyCheck(enemy));
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
        public void MinibossesAddUpOverRunsAndEarnAtEachStep()
        {
            Tracker tracker = Start("Character - Fox", "Progressive Forest Tier");
            tracker.RunStarted("Fox", "Forest", 1);
            tracker.EnemyKilled("MinibossPig", true, miniboss: true);
            tracker.EnemyKilled("Goblin", false);
            tracker.EnemyKilled("MinibossGolem", true, miniboss: true);
            Assert.Contains("Miniboss Hunt - Defeat 2 Minibosses", _link.Sent);
            Assert.DoesNotContain("Miniboss Hunt - Defeat 3 Minibosses", _link.Sent);

            tracker.RunStarted("Fox", "Forest", 1);                 // the next run goes on counting
            for (int i = 0; i < 6; i++) tracker.EnemyKilled("MinibossPig", true, miniboss: true);
            Assert.Contains("Miniboss Hunt - Defeat 8 Minibosses", _link.Sent);
            Assert.DoesNotContain("Miniboss Hunt - Defeat 10 Minibosses", _link.Sent);
            Assert.True(_log.Has("Miniboss defeated (MinibossPig): 8 in this seed, next check at 10."));
        }

        [Fact]
        public void MinibossesInARunThatDoesNotCountAreNotCounted()
        {
            Tracker tracker = Start("Character - Fox", "Progressive Forest Tier");
            tracker.RunStarted("Calcium", "Forest", 1);
            tracker.EnemyKilled("MinibossPig", true, miniboss: true);
            Assert.Empty(_link.Sent);
        }

        [Fact]
        public void ASeedWithoutTheHuntCountsNothing()
        {
            _link.NotInSeed.UnionWith(_list.MinibossHunt.Values);
            Tracker tracker = Start("Character - Fox", "Progressive Forest Tier");
            tracker.RunStarted("Fox", "Forest", 1);
            tracker.EnemyKilled("MinibossPig", true, miniboss: true);
            Assert.Equal(new[] { "Forest - Defeat Chunkham the Terrible" }, _link.Sent);
            Assert.False(_log.Has("Miniboss defeated"));
        }

        [Fact]
        public void TheCountSurvivesARestartAndIsTakenFromTheServerWhenLost()
        {
            string file = Path.Combine(Path.GetTempPath(), "megabonkap-hunt-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var first = new Tracker(_list, _link, _log, file);
                _link.Give("Character - Fox", "Progressive Forest Tier");
                first.RunStarted("Fox", "Forest", 1);
                for (int i = 0; i < 3; i++) first.EnemyKilled("MinibossPig", true, miniboss: true);

                var second = new Tracker(_list, _link, _log, file);         // the game was restarted
                _link.Announce();
                second.RunStarted("Fox", "Forest", 1);
                second.EnemyKilled("MinibossPig", true, miniboss: true);
                Assert.Contains("Miniboss Hunt - Defeat 4 Minibosses", _link.Sent);

                var fresh = new Tracker(_list, _link, _log);                // another computer: no file, only the server
                _link.Announce();
                fresh.RunStarted("Fox", "Forest", 1);
                fresh.EnemyKilled("MinibossPig", true, miniboss: true);
                Assert.Contains("Miniboss Hunt - Defeat 5 Minibosses", _link.Sent);
            }
            finally { File.Delete(file); }
        }

        [Fact]
        public void ATier1RunWithAHeldCharacterEarnsTheTierAndTheRun()
        {
            Tracker tracker = Start("Character - Fox", "Progressive Forest Tier");
            tracker.RunStarted("Fox", "Forest", 1);
            tracker.EnemyKilled("Goblin", false);
            Assert.Empty(_link.Sent);
            tracker.StageBossDied(true);
            Assert.Equal(new[] { "Forest - Complete Tier 1 (Lil Bark)", "Fox - Complete a Run" }, _link.Sent.OrderBy(s => s));
        }

        [Fact]
        public void TheCharacterMayBeNamedAfterTheRunHasStarted()
        {
            Tracker tracker = Start("Character - Fox", "Progressive Forest Tier");
            tracker.RunStarted(null, "Forest", 1);
            Assert.False(_log.Has("This run does not count"));      // not judged before the game has said who
            tracker.CharacterSeen("Fox");
            tracker.PlayerLevel(30);
            Assert.Equal(new[] { "Fox - Reach Level 30" }, _link.Sent);
        }

        [Fact]
        public void EventsBeforeTheCharacterIsKnownDoNotCount()
        {
            Tracker tracker = Start("Character - Fox", "Progressive Forest Tier");
            tracker.RunStarted(null, "Forest", 1);
            tracker.EnemyKilled("MinibossPig", true);
            Assert.Empty(_link.Sent);
            Assert.True(_log.Has("This run does not count: the game has not said which character this is"));
        }

        [Fact]
        public void ARunWithACharacterThatIsNotHeldEarnsNothingAndSaysWhy()
        {
            Tracker tracker = Start("Character - Fox", "Progressive Forest Tier");
            tracker.RunStarted("Calcium", "Forest", 1);
            tracker.PlayerLevel(40);
            tracker.StageBossDied(true);
            Assert.Empty(_link.Sent);
            Assert.Equal(1, _log.Count("This run does not count: you do not hold Character - Calcium"));
        }

        [Fact]
        public void AnItemArrivingMidRunMakesTheRunCount()
        {
            Tracker tracker = Start("Progressive Forest Tier");
            tracker.RunStarted("Fox", "Forest", 1);
            tracker.PlayerLevel(30);
            Assert.Empty(_link.Sent);
            _link.Give("Character - Fox");
            tracker.PlayerLevel(31);
            Assert.Equal(new[] { "Fox - Reach Level 30" }, _link.Sent);
        }

        [Fact]
        public void ATierNeedsThatManyTierItems()
        {
            Tracker tracker = Start("Character - Fox", "Progressive Forest Tier");
            tracker.RunStarted("Fox", "Forest", 2);
            tracker.StageBossDied(false);
            tracker.StageBossDied(true);
            Assert.Empty(_link.Sent);
            Assert.True(_log.Has("Forest Tier 2 needs 2 Progressive Forest Tier, you hold 1"));
        }

        [Fact]
        public void ATier2RunIsWonWhenTheGameSaysTheTiersLastStageIsDone()
        {
            Tracker tracker = Start("Character - Fox", "Progressive Forest Tier", "Progressive Forest Tier");
            tracker.RunStarted("Fox", "Forest", 2);
            tracker.StageBossDied(false);
            Assert.Empty(_link.Sent);
            tracker.StageBossDied(true);
            Assert.Contains("Forest - Complete Tier 2 (Chadbark)", _link.Sent);
            Assert.DoesNotContain("Forest - Complete Tier 1 (Lil Bark)", _link.Sent);
            Assert.Contains("Fox - Complete a Run", _link.Sent);
        }

        [Fact]
        public void WhenTheGameDoesNotSayStageBossesAreCountedAgainstTheTier()
        {
            Tracker tracker = Start("Character - Fox", "Progressive Forest Tier", "Progressive Forest Tier");
            tracker.RunStarted("Fox", "Forest", 2);
            tracker.StageBossDied(null);
            Assert.Empty(_link.Sent);
            tracker.StageBossDied(null);
            Assert.Contains("Forest - Complete Tier 2 (Chadbark)", _link.Sent);
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
            tracker.TomeLevel("Health", 9);
            Assert.Equal(new[] { "Damage Tome - Reach Level 8" }, _link.Sent);
        }

        [Fact]
        public void ATomeTheWorldDoesNotHaveIsPassedOverWithoutAWord()
        {
            Tracker tracker = Start("Character - Fox", "Progressive Forest Tier");
            tracker.RunStarted("Fox", "Forest", 1);
            tracker.TomeLevel("Gambler", 12);
            tracker.WeaponLevel("Test", 12);
            Assert.Empty(_link.Sent);
            Assert.False(_log.Has("matches nothing"));
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
        public void MinibossesGiveTheirCheckAndOrdinaryEnemiesOfASimilarNameDoNot()
        {
            Tracker tracker = Start("Character - Fox", "Progressive Forest Tier", "Progressive Desert Tier");
            tracker.RunStarted("Fox", "Forest", 1);
            tracker.EnemyKilled("Golem", false);
            tracker.EnemyKilled("MinibossGolem", true);
            tracker.EnemyKilled("MinibossPig", true);
            tracker.RunStarted("Fox", "Desert", 1);
            tracker.EnemyKilled("Scorpion", false);
            tracker.EnemyKilled("MinibossGolemSand", true);
            Assert.Equal(new[]
            {
                "Forest - Defeat Stone Golem", "Forest - Defeat Chunkham the Terrible", "Desert - Defeat Sand Golem",
            }, _link.Sent);
        }

        [Fact]
        public void ABossTheListDoesNotKnowIsWrittenDownOnce()
        {
            Tracker tracker = Start("Character - Fox", "Progressive Forest Tier");
            tracker.RunStarted("Fox", "Forest", 1);
            tracker.EnemyKilled("Ent1", true);
            tracker.EnemyKilled("Ent1", true);
            tracker.EnemyKilled("Goblin", false);
            Assert.Equal(1, _log.Count("Boss killed on Forest: the game calls it 'Ent1'."));
            Assert.False(_log.Has("Goblin"));
        }

        [Fact]
        public void TheGraveyardHasKeysAndBigBobEndsTheRun()
        {
            Tracker tracker = Start("Character - Calcium", "Graveyard Access");
            tracker.RunStarted("Calcium", "Graveyard", 1);
            tracker.CryptOpened();
            tracker.BossDefeated("Big Bob", endsRun: true);
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
            tracker.BossDefeated("Big Bob", endsRun: true);
            Assert.Empty(_link.Sent);
            Assert.True(_log.Has("you do not hold Graveyard Access"));
        }

        [Fact]
        public void AChallengeIsEarnedWhenItIsCompletedNotWhenTheRunIsWon()
        {
            Tracker tracker = Start("Character - Fox", "Progressive Forest Tier");
            tracker.RunStarted("Fox", "Forest", 1, "Speedrunner+");
            tracker.StageBossDied(true);
            Assert.DoesNotContain("Forest Challenge - Speedrunner+", _link.Sent);
            tracker.ChallengeCompleted();                               // the name the run began with
            Assert.Contains("Forest Challenge - Speedrunner+", _link.Sent);

            tracker.RunStarted("Fox", "Forest", 1);
            tracker.ChallengeCompleted("Challenge_Asset_3", "Fragile"); // the first name that matches wins
            Assert.Contains("Forest Challenge - Fragile", _link.Sent);
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
        public void ARunIsWonOnce()
        {
            Tracker tracker = Start("Character - Fox", "Progressive Forest Tier");
            tracker.RunStarted("Fox", "Forest", 1);
            tracker.StageBossDied(true);
            tracker.StageBossDied(true);
            tracker.RunWon();
            Assert.Equal(2, _link.Sent.Count);
            Assert.Equal(_link.Sent.Count, _link.Sent.Distinct().Count());
        }

        [Fact]
        public void NothingHappensBeforeTheFirstRun()
        {
            Tracker tracker = Start("Character - Fox", "Progressive Forest Tier");
            tracker.CharacterSeen("Fox");
            tracker.PlayerLevel(50);
            tracker.WeaponLevel("FireStaff", 20);
            tracker.StageBossDied(true);
            tracker.BossDefeated("Big Bob", endsRun: true);
            tracker.ChallengeCompleted("Fragile");
            Assert.Empty(_link.Sent);
        }

        [Fact]
        public void AnUnknownTierSendsNoTierCheckAndSaysSo()
        {
            Tracker tracker = Start("Character - Fox", "Progressive Forest Tier");
            tracker.RunStarted("Fox", "Forest", 0);
            tracker.StageBossDied(null);
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
        public void ACheckTheSeedDoesNotHaveIsDropped()
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
            tracker.StageBossDied(true);
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
            tracker.StageBossDied(true);
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
