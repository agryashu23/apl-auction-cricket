using System.Collections.Generic;
using System.Linq;
using Apl.Core;
using UnityEngine;

namespace Apl.Client.Modes
{
    /// <summary>The match difficulty the player picks (SETTINGS > GAMEPLAY, match setup, career). It sets the ball speed too.</summary>
    public enum DifficultyLevel { Easy, Normal, Hard }

    /// <summary>
    /// EASY / NORMAL / HARD for the user's matches (round 8, modes). Two levers, both
    /// outside the engine so the season and auction stay exactly as they are:
    /// the opponent's XI plays to a shifted skill (their bowlers bowl and their
    /// batters bat worse on Easy, better on Hard — the engine reads those numbers
    /// for line, length, variation, shot choice and outs), and the user's timing
    /// window is wider or narrower (his tap's error is scaled before the engine
    /// grades it). Since owner v26 #5 it also sets the ball speed (BattingPrefs.Pace:
    /// EASY = slow, NORMAL = normal, HARD = real). Saved in PlayerPrefs; NORMAL changes nothing.
    /// </summary>
    public static class Difficulty
    {
        private const string Key = "apl.difficulty";

        public static DifficultyLevel Current
        {
            get
            {
                Migrate();
                return (DifficultyLevel)Mathf.Clamp(PlayerPrefs.GetInt(Key, (int)DifficultyLevel.Normal), 0, 2);
            }
            set
            {
                PlayerPrefs.SetInt(Key, (int)value);
                PlayerPrefs.Save();
            }
        }

        private static bool _migrated;

        /// <summary>
        /// Owner v26 #5: BALL SPEED folded into DIFFICULTY. A player who never chose a difficulty
        /// but did choose a ball speed keeps his speed: SLOW becomes EASY, REAL becomes HARD
        /// (NORMAL stays NORMAL). One who chose a difficulty keeps it (it drives more than the
        /// speed). The old key is deleted either way, so this runs once.
        /// </summary>
        private static void Migrate()
        {
            if (_migrated) return;
            _migrated = true;
            var legacy = Apl.Client.Match.BattingPrefs.LegacyPaceKey;
            if (!PlayerPrefs.HasKey(legacy)) return;
            if (!PlayerPrefs.HasKey(Key))
            {
                var pace = PlayerPrefs.GetInt(legacy, 1);
                PlayerPrefs.SetInt(Key, pace <= 0 ? (int)DifficultyLevel.Easy : pace >= 2 ? (int)DifficultyLevel.Hard : (int)DifficultyLevel.Normal);
            }
            PlayerPrefs.DeleteKey(legacy);
            PlayerPrefs.Save();
        }

        public static readonly string[] Labels = { "EASY", "NORMAL", "HARD" };

        public static string Label(DifficultyLevel d) => Labels[(int)d];

        /// <summary>How much wider the timing window is (1 = as the engine has it).</summary>
        public static double WindowScale(DifficultyLevel d) => d == DifficultyLevel.Easy ? 1.45 : d == DifficultyLevel.Hard ? 0.78 : 1.0;

        /// <summary>Skill points added to the opponent's batting and bowling (and half to fielding).</summary>
        public static int SkillShift(DifficultyLevel d) => d == DifficultyLevel.Easy ? -12 : d == DifficultyLevel.Hard ? 8 : 0;

        /// <summary>The XP multiplier for a match at this level (Progression).</summary>
        public static float XpScale(DifficultyLevel d) => d == DifficultyLevel.Easy ? 0.75f : d == DifficultyLevel.Hard ? 1.35f : 1f;

        /// <summary>One line for the settings and the challenges page.</summary>
        public static string Describe(DifficultyLevel d) => d switch
        {
            DifficultyLevel.Easy => "Slower ball, wider timing window; the opposition bowls and bats below its best. Less XP.",
            DifficultyLevel.Hard => "Real-pace ball, tight timing; the opposition at its sharpest. More XP.",
            _ => "Normal ball speed; the game as it's tuned.",
        };

        /// <summary>The user's tap as the engine should grade it: its error from the ideal moment scaled by the level.</summary>
        public static double Tap(double tap, double ideal)
        {
            if (double.IsNaN(tap) || double.IsNaN(ideal)) return tap;
            return ideal + (tap - ideal) / WindowScale(Current);
        }

        private static readonly Dictionary<string, Player> Clones = new Dictionary<string, Player>();

        /// <summary>
        /// The match's sheet for a side: the user's own as it is; the opponent's with the
        /// level's skill shift. The same clone comes back every call (the director compares
        /// players it got from several calls), equal by id to the dataset's player.
        /// </summary>
        public static TeamSheet Sheet(TeamSheet sheet, bool opponent)
        {
            var shift = SkillShift(Current);
            if (!opponent || shift == 0) return sheet;
            return new TeamSheet(sheet.TeamId, sheet.XI.Select(p => Shifted(p, shift)).ToList());
        }

        private static Player Shifted(Player p, int shift)
        {
            var key = p.Id + "@" + shift;
            if (Clones.TryGetValue(key, out var c)) return c;
            c = new Player
            {
                Id = p.Id, Name = p.Name, Country = p.Country, Overseas = p.Overseas, Capped = p.Capped, Role = p.Role,
                LeftHanded = p.LeftHanded, BowlType = p.BowlType, Form = p.Form, BasePrice = p.BasePrice, Set = p.Set,
                Tags = p.Tags, Image = p.Image, StarPower = p.StarPower, PreviousTeam = p.PreviousTeam, Value = p.Value,
                BowlArm = p.BowlArm, BowlStyle = p.BowlStyle, Recent = p.Recent,
                Batting = Mathf.Clamp(p.Batting + shift, 5, 99),
                // A non-bowler stays a non-bowler (the attack's shape is kept).
                Bowling = p.Bowling < 25 ? p.Bowling : Mathf.Clamp(p.Bowling + shift, 5, 99),
                Fielding = Mathf.Clamp(p.Fielding + shift / 2, 5, 99),
                Keeping = p.Keeping,
            };
            Clones[key] = c;
            return c;
        }
    }
}
