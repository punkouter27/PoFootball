using PoFootball.Models;
using UnityEngine;
using UnityEngine.UIElements;

namespace PoFootball.Views
{
    /// <summary>
    /// The SCORING tab of the final sheet: points by quarter, and every scoring
    /// play in order — the broadcast's line score and scoring summary.
    ///
    /// WHY THIS IS ON THE RESULT SCREEN. The box score says how much happened and
    /// nothing about when. "21-14" from three early touchdowns and a shutout half
    /// is a different game from 21-14 settled in the last minute, and nothing on
    /// any screen said which one had just been watched.
    ///
    /// SIZED BY THE BOX SCORE, NOT BY ITSELF. Systems_HudView lays this over the
    /// box score card, pinned to its edges, so switching tabs never changes the
    /// sheet's height — a sheet that resized under the thumb would move REMATCH.
    /// That fixes how many rows fit: the box score's twelve lines, which is the
    /// line score's three, a caption, and <see cref="MAX_LISTED"/> plays. A game
    /// with more than that shows the latest and says how many earlier ones there
    /// were, rather than scrolling (CLAUDE.md section 3: nothing scrolls).
    ///
    /// Built once, at the final whistle, from entries Systems_HudView collected as
    /// the downs resolved; allocates then and never again.
    /// </summary>
    internal static class Systems_ScoringCard
    {
        /// <summary>Scoring plays listed. Seven lines plus the three of the line score and a caption fill the box score's height.</summary>
        public const int MAX_LISTED = 7;

        /// <summary>Four quarters and overtime.</summary>
        public const int PERIODS = 5;

        private const int TIME_WIDTH = 150;
        private const int TEAM_WIDTH = 90;
        private const int SCORE_WIDTH = 110;
        private const float LABEL_WEIGHT = 1.5f;

        /// <summary>One scoring play as the summary prints it. A try is folded into the touchdown before it.</summary>
        internal struct Entry
        {
            public string When;
            public Systems_TeamId Team;
            public string What;
            public int HomeScore;
            public int AwayScore;
        }

        public static VisualElement Build(
            Entry[] entries, int count, int[] homeByPeriod, int[] awayByPeriod, bool overtime)
        {
            VisualElement card = Systems_UiTheme.Column();
            card.style.backgroundColor = Systems_UiTheme.SurfaceRaised;
            Systems_UiTheme.SetPadding(card, Systems_UiTheme.SPACE_M);
            Systems_UiTheme.SetRadius(card, Systems_UiTheme.RADIUS);
            card.pickingMode = PickingMode.Ignore;

            Label heading = Systems_UiTheme.Caption("SCORING");
            heading.style.unityTextAlign = TextAnchor.MiddleCenter;
            heading.style.marginBottom = Systems_UiTheme.SPACE_S;
            card.Add(heading);

            int periods = overtime ? PERIODS : PERIODS - 1;

            card.Add(LineScoreHeader(periods));
            card.Add(LineScoreRow(Systems_TeamId.Home, homeByPeriod, periods));
            card.Add(LineScoreRow(Systems_TeamId.Away, awayByPeriod, periods));

            Label plays = Systems_UiTheme.Caption("SCORING PLAYS");
            plays.style.marginTop = Systems_UiTheme.SPACE_M;
            plays.style.marginBottom = Systems_UiTheme.SPACE_XS;
            card.Add(plays);

            if (count == 0)
            {
                card.Add(Systems_UiTheme.Text(
                    "No points scored.", Systems_UiTheme.TEXT_CAPTION, Systems_UiTheme.TextMuted));
                return card;
            }

            int first = 0;

            if (count > MAX_LISTED)
            {
                // The latest, because the end of a game is the part a viewer has
                // just watched and is looking for; one line says what was left off.
                first = count - (MAX_LISTED - 1);
                card.Add(Systems_UiTheme.Text(
                    "+" + first + " earlier", Systems_UiTheme.TEXT_CAPTION, Systems_UiTheme.TextMuted));
            }

            for (int index = first; index < count; index++)
            {
                card.Add(PlayRow(entries[index]));
            }

            return card;
        }

        private static VisualElement LineScoreHeader(int periods)
        {
            VisualElement row = Systems_UiTheme.Row();
            row.Add(Cell(string.Empty, Systems_UiTheme.TextMuted, LABEL_WEIGHT));

            for (int period = 1; period <= periods; period++)
            {
                row.Add(Cell(
                    period > PERIODS - 1 ? "OT" : period.ToString(), Systems_UiTheme.TextMuted, 1f));
            }

            row.Add(Cell("T", Systems_UiTheme.TextMuted, 1f, FontStyle.Bold));
            return row;
        }

        private static VisualElement LineScoreRow(Systems_TeamId team, int[] byPeriod, int periods)
        {
            VisualElement row = Systems_UiTheme.Row();
            row.Add(Cell(
                Systems_DisplayText.TeamName(team), Systems_UiTheme.ColorOf(team),
                LABEL_WEIGHT, FontStyle.Bold));

            int total = 0;

            for (int period = 0; period < periods; period++)
            {
                total += byPeriod[period];
                row.Add(Cell(byPeriod[period].ToString(), Systems_UiTheme.TextPrimary, 1f));
            }

            row.Add(Cell(total.ToString(), Systems_UiTheme.TextPrimary, 1f, FontStyle.Bold));
            return row;
        }

        private static VisualElement PlayRow(Entry entry)
        {
            VisualElement row = Systems_UiTheme.Row();
            row.pickingMode = PickingMode.Ignore;

            Label when = Systems_UiTheme.Text(
                entry.When, Systems_UiTheme.TEXT_CAPTION, Systems_UiTheme.TextMuted);
            when.style.width = TIME_WIDTH;

            Label team = Systems_UiTheme.Text(
                Systems_DisplayText.TeamTag(entry.Team), Systems_UiTheme.TEXT_CAPTION,
                Systems_UiTheme.ColorOf(entry.Team), FontStyle.Bold);
            team.style.width = TEAM_WIDTH;

            // The one cell that can be long, so the one that gives: it shrinks and
            // clips with an ellipsis rather than pushing the score off the row.
            Label what = Systems_UiTheme.Text(
                entry.What, Systems_UiTheme.TEXT_CAPTION, Systems_UiTheme.TextPrimary);
            what.style.flexGrow = 1f;
            what.style.flexShrink = 1f;
            what.style.minWidth = 0f;
            what.style.overflow = Overflow.Hidden;
            what.style.textOverflow = TextOverflow.Ellipsis;

            Label score = Systems_UiTheme.Text(
                entry.HomeScore + "-" + entry.AwayScore, Systems_UiTheme.TEXT_CAPTION,
                Systems_UiTheme.TextPrimary, FontStyle.Bold);
            score.style.width = SCORE_WIDTH;
            score.style.unityTextAlign = TextAnchor.MiddleRight;

            row.Add(when);
            row.Add(team);
            row.Add(what);
            row.Add(score);
            return row;
        }

        private static Label Cell(
            string value, Color color, float weight, FontStyle fontStyle = FontStyle.Normal)
        {
            Label cell = Systems_UiTheme.Text(value, Systems_UiTheme.TEXT_CAPTION, color, fontStyle);
            cell.style.flexGrow = weight;
            cell.style.flexBasis = 0f;
            cell.style.unityTextAlign = TextAnchor.MiddleCenter;
            return cell;
        }
    }
}
