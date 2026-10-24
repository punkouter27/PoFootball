using PoFootball.Models;
using PoFootball.Systems;
using UnityEngine;
using UnityEngine.UIElements;

namespace PoFootball.Views
{
    /// <summary>
    /// The QB challenge's play sheet: the nine plays dealt for this down in a
    /// three-by-three grid, the punt and field goal under them on fourth down, and
    /// one line that says what the next click does — pick a play, hike, or, during
    /// the play, throw to or hand off to whoever is under the pointer.
    ///
    /// A plain class hosted by Systems_HudView, like Systems_CallSheetCard. It
    /// reads Systems_HumanControlModel and writes only one thing back, the card
    /// the human pressed (Pick); Systems_QbChallengeSystem applies it on the next
    /// physics tick.
    ///
    /// AT THE BOTTOM OF THE SCREEN, ABOVE THE TRANSPORT STRIP: every card is a
    /// control, and nothing but the MENU chip may sit in the top 40% (CLAUDE.md
    /// section 3, Systems_UiFitAudit). The sheet hides itself once the ball is
    /// snapped, leaving only the hint line over the field.
    /// </summary>
    public sealed class Systems_PlaySheetCard
    {
        private const int COLUMNS = 3;

        /// <summary>One ticker line and a gap; see the constructor.</summary>
        private const int TICKER_CLEARANCE = 72;

        private readonly Systems_HumanControlModel _control;
        private readonly Systems_PlayModel _play;
        private readonly Systems_PlayerRegistry _registry;

        private readonly VisualElement _grid;
        private readonly Label _prompt;
        private readonly Button[] _cards;

        private int _shownEpisode = -1;
        private int _shownCount = -1;
        private int _shownSelection = -2;
        private int _hintHovered = -2;
        private string _hint;
        private long _shownPromptKey = long.MinValue;

        public Systems_PlaySheetCard(
            Systems_HumanControlModel control, Systems_PlayModel play, Systems_PlayerRegistry registry)
        {
            _control = control;
            _play = play;
            _registry = registry;

            Root = Systems_UiTheme.Column();
            Root.name = "PlaySheet";
            Root.style.position = Position.Absolute;
            Root.style.left = Systems_UiTheme.SPACE_M;
            Root.style.right = Systems_UiTheme.SPACE_M;
            // ABOVE THE COMMENTARY TICKER, which Systems_BroadcastGraphicsView
            // docks at CARD_DOCK_BOTTOM: the first build put the hint line straight
            // over the play-by-play.
            Root.style.bottom = Systems_HudLayout.CARD_DOCK_BOTTOM + TICKER_CLEARANCE;
            Root.pickingMode = PickingMode.Ignore;

            _prompt = Systems_UiTheme.Text(
                string.Empty, Systems_UiTheme.TEXT_BODY, Systems_UiTheme.Accent, FontStyle.Bold);
            _prompt.style.unityTextAlign = TextAnchor.MiddleCenter;
            _prompt.style.marginBottom = Systems_UiTheme.SPACE_S;
            _prompt.style.backgroundColor = Systems_UiTheme.SurfaceOverField;
            _prompt.pickingMode = PickingMode.Ignore;
            Systems_UiTheme.SetRadius(_prompt, Systems_UiTheme.RADIUS);
            Systems_UiTheme.SetPadding(_prompt, Systems_UiTheme.SPACE_XS, Systems_UiTheme.SPACE_M);
            Root.Add(_prompt);

            _grid = Systems_UiTheme.Column();
            _grid.pickingMode = PickingMode.Ignore;
            Root.Add(_grid);

            _cards = new Button[control.Hand.Length];

            VisualElement row = null;

            for (int index = 0; index < _cards.Length; index++)
            {
                // The nine plays three to a row; the kicks share a last row.
                if (index % COLUMNS == 0 && index < Systems_Playbook.HAND_SIZE
                    || index == Systems_Playbook.HAND_SIZE)
                {
                    row = Systems_UiTheme.Row();
                    row.pickingMode = PickingMode.Ignore;
                    row.style.marginTop = Systems_UiTheme.SPACE_XS;
                    _grid.Add(row);
                }

                int card = index;
                Button button = Systems_UiTheme.Button(
                    string.Empty, Systems_UiTheme.SurfaceRaised, () => _control.Pick(card));
                button.style.flexGrow = 1f;
                button.style.flexBasis = 0f;
                button.style.marginLeft = Systems_UiTheme.SPACE_XS / 2;
                button.style.marginRight = Systems_UiTheme.SPACE_XS / 2;
                button.style.fontSize = Systems_UiTheme.TEXT_CAPTION;
                button.style.whiteSpace = WhiteSpace.Normal;
                button.style.color = Systems_UiTheme.TextPrimary;
                row.Add(button);
                _cards[index] = button;
            }

            Root.style.display = DisplayStyle.None;
        }

        public VisualElement Root { get; }

        /// <summary>Every frame from the HUD; writes only when something it shows changed.</summary>
        public void Refresh()
        {
            bool sheetUp = _control.IsAwaitingSnap && _play.Phase == Systems_PlayPhase.PreSnap;
            bool hintUp = _control.HumanOnOffense && _play.Phase == Systems_PlayPhase.Live;

            Root.style.display = sheetUp || hintUp ? DisplayStyle.Flex : DisplayStyle.None;
            _grid.style.display = sheetUp ? DisplayStyle.Flex : DisplayStyle.None;

            if (sheetUp && (_shownEpisode != _play.EpisodeIndex || _shownCount != _control.HandCount))
            {
                _shownEpisode = _play.EpisodeIndex;
                _shownCount = _control.HandCount;
                _shownSelection = -2;

                for (int index = 0; index < _cards.Length; index++)
                {
                    bool dealt = index < _control.HandCount;
                    _cards[index].style.display = dealt ? DisplayStyle.Flex : DisplayStyle.None;

                    if (dealt)
                    {
                        _cards[index].text = CardText(_control.Hand[index]);
                    }
                }
            }

            if (sheetUp && _shownSelection != _control.SelectedCard)
            {
                _shownSelection = _control.SelectedCard;

                for (int index = 0; index < _control.HandCount; index++)
                {
                    bool selected = index == _control.SelectedCard;
                    _cards[index].style.backgroundColor =
                        selected ? Systems_UiTheme.Accent : Systems_UiTheme.SurfaceRaised;
                    _cards[index].style.color =
                        selected ? Systems_UiTheme.Surface : Systems_UiTheme.TextPrimary;
                }
            }

            // Keyed, so the line is rebuilt only when what it says changes.
            long promptKey = sheetUp
                ? ((long)_play.EpisodeIndex << 8) + _control.SelectedCard + 2
                : hintUp ? -2L - _control.HoveredSlot : -1L;

            if (promptKey != _shownPromptKey)
            {
                _shownPromptKey = promptKey;
                _prompt.text = sheetUp
                    ? (_control.SelectedCard < 0
                        ? $"{DownText()} · PICK A PLAY"
                        : $"{_control.Hand[_control.SelectedCard].Name} · TAP THE FIELD TO HIKE")
                    : hintUp ? HintText() : string.Empty;
            }
        }

        private static string CardText(Systems_PlayDesign design)
        {
            string kind = design.IsKick
                ? "KICK"
                : design.Call == Systems_PlayCall.PassDeep
                    ? "DEEP PASS"
                    : design.IsPass ? "PASS" : "RUN";

            return design.IsKick ? $"{design.Name}\n{kind}" : $"{design.Name}\n{kind} · {design.Formation}";
        }

        private string DownText()
        {
            return Systems_DisplayText.DownAndDistance(_play.Down, _play.YardsToGo, false);
        }

        /// <summary>
        /// What a tap does now, for whoever is under the pointer. Rebuilt only
        /// when the hovered player changes, so a held hover allocates nothing.
        /// </summary>
        private string HintText()
        {
            int hovered = _control.HoveredSlot;

            if (hovered == _hintHovered && _hint != null)
            {
                return _hint;
            }

            _hintHovered = hovered;

            Systems_IPlayerHandle mate = hovered >= 0 && _registry != null ? _registry.Get(hovered) : null;

            _hint = mate == null || mate.Role == Systems_PlayerRole.OffensiveLine
                ? "TAP: RUN THERE · DOUBLE-TAP: PASS THERE"
                : $"TAP: BALL TO {Systems_DisplayText.RoleTag(mate.Role)} · DOUBLE-TAP: PASS";

            return _hint;
        }
    }
}
