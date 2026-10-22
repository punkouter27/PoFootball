using PoFootball.Models;
using PoFootball.Systems;
using UnityEngine;
using UnityEngine.UIElements;

namespace PoFootball.Views
{
    /// <summary>
    /// The two coordinators' call sheet for the play on the field: the front the
    /// defense called and how likely it was to call it, and the offense's run and
    /// pass chances with the call it committed to once it commits.
    ///
    /// WHAT IT SHOWS THAT NOTHING ELSE DOES. The scoreboard's call chip names the
    /// offense's call after the dropback; nothing anywhere named the defense's
    /// front at all, and nothing said whether either call was a surprise. A blitz
    /// a brain calls one time in twenty is a different event from one it calls
    /// every third down, and only the chances can say which this was.
    ///
    /// IT SAYS WHERE THE NUMBERS CAME FROM. SCRIPTED is the heuristic's own
    /// table, exact; AI is a trained brain's policy, sampled on this snap's
    /// observation (Agent_PolicySampler); AI CALL is a trained brain whose
    /// chances could not be read, so only its choice is shown. A percentage with
    /// no provenance invites exactly the misreading this project has made before —
    /// a scripted quarterback mistaken for a collapsed policy.
    ///
    /// WHEN. The defense calls ten ticks into a twenty-tick pre-snap, so a card
    /// that lived only before the snap would be on screen for a fifth of a second.
    /// It appears with the front and stays until CALL_SHEET_HOLD_SECONDS after the
    /// offense commits, then fades — long enough to read the reveal, gone before
    /// the play it describes is decided.
    ///
    /// TOP LEFT, UNDER THE RIBBON, NARROW. The carrier chip owns the centre of the
    /// same band while the ball is live; this card is held to CARD_WIDTH so the
    /// two never touch on a 1080-wide panel.
    /// </summary>
    public sealed class Systems_CallSheetCard
    {
        /// <summary>Wide enough for the run/pass bar to read; the carrier chip starts at 404.</summary>
        public const int CARD_WIDTH = 372;

        /// <summary>Game seconds the card stays after the offense's call latches.</summary>
        public const float CALL_SHEET_HOLD_SECONDS = 1.8f;

        private const int BAR_HEIGHT = 8;

        private readonly Label _defenseSource;
        private readonly Label _defenseCall;
        private readonly Label _defenseChance;
        private readonly Label _offenseSource;
        private readonly Label _runLabel;
        private readonly Label _passLabel;
        private readonly VisualElement _runFill;
        private readonly Label _offenseCall;

        private int _shownVersion = -1;
        private int _shownEpisode = -1;
        private Systems_PlayCall _shownCall = Systems_PlayCall.None;
        private bool _visible;
        private float _hideAt = float.MaxValue;

        public Systems_CallSheetCard()
        {
            Root = Systems_UiTheme.Column();
            Root.style.width = CARD_WIDTH;
            Root.style.backgroundColor = Systems_UiTheme.SurfaceOverField;
            Root.pickingMode = PickingMode.Ignore;
            Root.style.opacity = 0f;
            Systems_UiTheme.SetRadius(Root, Systems_UiTheme.RADIUS);
            Systems_UiTheme.SetPadding(Root, Systems_UiTheme.SPACE_S, Systems_UiTheme.SPACE_M);
            Systems_UiTheme.ApplyElevation(Root);
            Systems_UiTheme.EnableTransition(Root, "opacity");

            Label title = Systems_UiTheme.Caption("CALL SHEET");
            title.style.marginBottom = Systems_UiTheme.SPACE_XS;
            Root.Add(title);

            // DEF  COVER 3          41%
            VisualElement defenseRow = Systems_UiTheme.Row();
            defenseRow.Add(SideTag("DEF"));
            _defenseCall = Systems_UiTheme.Text(
                string.Empty, Systems_UiTheme.TEXT_CAPTION, Systems_UiTheme.TextPrimary, FontStyle.Bold);
            _defenseCall.style.flexGrow = 1f;
            _defenseChance = Systems_UiTheme.Text(
                string.Empty, Systems_UiTheme.TEXT_CAPTION, Systems_UiTheme.Accent, FontStyle.Bold);
            defenseRow.Add(_defenseCall);
            defenseRow.Add(_defenseChance);
            Root.Add(defenseRow);

            _defenseSource = SourceLine();
            Root.Add(_defenseSource);

            // OFF  RUN 78% [=======   ] 22% PASS
            VisualElement offenseRow = Systems_UiTheme.Row();
            offenseRow.style.marginTop = Systems_UiTheme.SPACE_S;
            offenseRow.Add(SideTag("OFF"));

            _runLabel = Systems_UiTheme.Text(
                string.Empty, Systems_UiTheme.TEXT_CAPTION, Systems_UiTheme.TextPrimary, FontStyle.Bold);
            _runLabel.style.width = 92;

            VisualElement track = new VisualElement();
            track.style.flexGrow = 1f;
            track.style.height = BAR_HEIGHT;
            track.style.backgroundColor = new Color(1f, 1f, 1f, 0.16f);
            track.pickingMode = PickingMode.Ignore;
            Systems_UiTheme.SetRadius(track, BAR_HEIGHT / 2);

            _runFill = new VisualElement();
            _runFill.style.height = Length.Percent(100f);
            _runFill.style.width = Length.Percent(50f);
            _runFill.style.backgroundColor = Systems_UiTheme.Positive;
            _runFill.pickingMode = PickingMode.Ignore;
            Systems_UiTheme.SetRadius(_runFill, BAR_HEIGHT / 2);
            track.Add(_runFill);

            _passLabel = Systems_UiTheme.Text(
                string.Empty, Systems_UiTheme.TEXT_CAPTION, Systems_UiTheme.TextPrimary, FontStyle.Bold);
            _passLabel.style.width = 92;
            _passLabel.style.unityTextAlign = TextAnchor.MiddleRight;

            offenseRow.Add(_runLabel);
            offenseRow.Add(track);
            offenseRow.Add(_passLabel);
            Root.Add(offenseRow);

            _offenseSource = SourceLine();
            Root.Add(_offenseSource);

            _offenseCall = Systems_UiTheme.Text(
                string.Empty, Systems_UiTheme.TEXT_BODY, Systems_UiTheme.Accent, FontStyle.Bold);
            _offenseCall.style.marginTop = Systems_UiTheme.SPACE_XS;
            Root.Add(_offenseCall);
        }

        public VisualElement Root { get; }

        /// <summary>Called every frame; writes styles only when something changed.</summary>
        public void Refresh(Systems_ICallSheetSource sheet, Systems_PlayModel play, float now)
        {
            if (sheet == null || !sheet.Available || play == null || !Systems_BroadcastSettings.CallSheet)
            {
                SetVisible(false);
                return;
            }

            int episode = play.EpisodeIndex;
            bool hasDefense = sheet.DefenseEpisode == episode && play.DefenseIsCalled;
            bool hasOffense = sheet.OffenseEpisode == episode;

            if (!hasDefense && !hasOffense)
            {
                SetVisible(false);
                return;
            }

            if (episode != _shownEpisode)
            {
                _shownEpisode = episode;
                _shownCall = Systems_PlayCall.None;
                _hideAt = float.MaxValue;
                _offenseCall.text = string.Empty;
            }

            if (sheet.Version != _shownVersion)
            {
                _shownVersion = sheet.Version;
                WriteDefense(sheet, play, hasDefense);
                WriteOffense(sheet, hasOffense);
            }

            if (play.CallIsLatched && play.Call != _shownCall)
            {
                _shownCall = play.Call;
                // "CALLED", not an arrow: neither Oswald nor Bebas Neue carries
                // U+2192, and a missing glyph renders as an empty box.
                _offenseCall.text = "CALLED " + Systems_DisplayText.PlayCallLabel(play.Call)
                    + ChanceSuffix(sheet, play.Call);
                _hideAt = now + CALL_SHEET_HOLD_SECONDS;
            }

            bool live = play.Phase == Systems_PlayPhase.PreSnap
                || (play.Phase == Systems_PlayPhase.Live && now < _hideAt);

            SetVisible(live);
        }

        private void WriteDefense(Systems_ICallSheetSource sheet, Systems_PlayModel play, bool hasDefense)
        {
            if (!hasDefense)
            {
                _defenseCall.text = "—";
                _defenseChance.text = string.Empty;
                _defenseSource.text = string.Empty;
                return;
            }

            int front = play.DefenseCallIndex;
            _defenseCall.text = FrontName((Systems_DefensiveFormation)front);
            _defenseChance.text = Percent(sheet.DefenseChance(front));
            _defenseSource.text = SourceText(sheet.DefenseSource, true);
        }

        private void WriteOffense(Systems_ICallSheetSource sheet, bool hasOffense)
        {
            if (!hasOffense || sheet.OffenseSource == Systems_CallSource.PolicyChoiceOnly)
            {
                _runLabel.text = "RUN ?";
                _passLabel.text = "? PASS";
                _runFill.style.width = Length.Percent(50f);
                _offenseSource.text = hasOffense ? SourceText(sheet.OffenseSource, false) : string.Empty;
                return;
            }

            float run = sheet.OffenseChance(Systems_PlayCall.HandoffHalfback)
                + sheet.OffenseChance(Systems_PlayCall.HandoffFullback)
                + sheet.OffenseChance(Systems_PlayCall.KeepQuarterback);
            float pass = sheet.OffenseChance(Systems_PlayCall.Pass)
                + sheet.OffenseChance(Systems_PlayCall.PassDeep);
            float total = run + pass;
            float runShare = total > 0f ? run / total : 0.5f;

            _runLabel.text = "RUN " + Percent(runShare);
            _passLabel.text = Percent(1f - runShare) + " PASS";
            _runFill.style.width = Length.Percent(runShare * 100f);
            _offenseSource.text = SourceText(sheet.OffenseSource, false);
        }

        private static string ChanceSuffix(Systems_ICallSheetSource sheet, Systems_PlayCall call)
        {
            if (sheet.OffenseSource != Systems_CallSource.Scripted
                && sheet.OffenseSource != Systems_CallSource.Policy)
            {
                return string.Empty;
            }

            float chance = sheet.OffenseChance(call);
            return chance > 0f ? "  " + Percent(chance) : string.Empty;
        }

        private void SetVisible(bool visible)
        {
            if (visible == _visible)
            {
                return;
            }

            _visible = visible;
            Root.style.opacity = visible ? 1f : 0f;
        }

        private static Label SideTag(string text)
        {
            Label tag = Systems_UiTheme.Text(
                text, Systems_UiTheme.TEXT_CAPTION, Systems_UiTheme.TextMuted, FontStyle.Bold);
            tag.style.width = 56;
            tag.style.letterSpacing = 2f;
            return tag;
        }

        private static Label SourceLine()
        {
            Label line = Systems_UiTheme.Text(
                string.Empty, Systems_UiTheme.TEXT_CAPTION - 4, Systems_UiTheme.TextMuted);
            line.style.marginLeft = 56;
            return line;
        }

        public static string SourceText(Systems_CallSource source, bool defense)
        {
            switch (source)
            {
                case Systems_CallSource.Scripted:
                    return defense ? "SCRIPTED · drawn evenly from 8" : "SCRIPTED · exact odds";
                case Systems_CallSource.Policy:
                    return "AI · sampled " + Systems_CallSheet.POLICY_SAMPLES + "×";
                case Systems_CallSource.PolicyChoiceOnly:
                    return "AI CALL · odds not readable";
                default:
                    return string.Empty;
            }
        }

        public static string FrontName(Systems_DefensiveFormation front)
        {
            switch (front)
            {
                case Systems_DefensiveFormation.FourThreeBase: return "4-3 BASE";
                case Systems_DefensiveFormation.FourThreeOver: return "4-3 OVER";
                case Systems_DefensiveFormation.FourThreeUnder: return "4-3 UNDER";
                case Systems_DefensiveFormation.Cover2: return "COVER 2";
                case Systems_DefensiveFormation.Cover3: return "COVER 3";
                case Systems_DefensiveFormation.Nickel: return "NICKEL";
                case Systems_DefensiveFormation.Bear46: return "46 BEAR";
                case Systems_DefensiveFormation.ZeroBlitz: return "ZERO BLITZ";
                default: return front.ToString().ToUpperInvariant();
            }
        }

        private static string Percent(float chance)
        {
            return Mathf.RoundToInt(Mathf.Clamp01(chance) * 100f) + "%";
        }
    }
}
