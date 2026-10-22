using UnityEngine;
using UnityEngine.UIElements;

namespace PoFootball.Views
{
    /// <summary>
    /// The play-by-play line: the last play in a sentence, on a plate above the
    /// transport controls, until the next play replaces it.
    ///
    /// IT OUTLIVES THE BANNER ON PURPOSE. The result banner announces a play and is
    /// gone at the next snap; the ticker keeps the sentence through the following
    /// play, so a viewer who looked away for the whistle can read what happened
    /// while the next one develops. Two lines at most, then an ellipsis — a third
    /// line is a paragraph, and nobody reads a paragraph over a live play.
    ///
    /// It steps aside for the focus card, which sits in the same slot: a viewer
    /// who has tapped a player asked to see that player.
    /// </summary>
    public sealed class Systems_CommentaryTicker
    {
        private const int MAX_WIDTH = 980;

        private readonly Label _line;

        private bool _visible;
        private bool _hasLine;

        public Systems_CommentaryTicker()
        {
            Root = Systems_UiTheme.Row();
            Root.style.justifyContent = Justify.Center;
            Root.pickingMode = PickingMode.Ignore;

            VisualElement plate = Systems_UiTheme.Row();
            plate.style.maxWidth = MAX_WIDTH;
            plate.style.backgroundColor = Systems_UiTheme.SurfaceOverField;
            plate.pickingMode = PickingMode.Ignore;
            Systems_UiTheme.SetRadius(plate, Systems_UiTheme.RADIUS);
            Systems_UiTheme.SetPadding(plate, Systems_UiTheme.SPACE_S, Systems_UiTheme.SPACE_M);

            _line = Systems_UiTheme.Text(
                string.Empty, Systems_UiTheme.TEXT_CAPTION + 2, Systems_UiTheme.TextPrimary);
            _line.style.whiteSpace = WhiteSpace.Normal;
            _line.style.maxHeight = (Systems_UiTheme.TEXT_CAPTION + 2) * 2.7f;
            _line.style.overflow = Overflow.Hidden;
            _line.style.textOverflow = TextOverflow.Ellipsis;

            plate.Add(_line);
            Root.Add(plate);

            Root.style.opacity = 0f;
            Systems_UiTheme.EnableTransition(Root, "opacity");
        }

        public VisualElement Root { get; }

        public void Show(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return;
            }

            _line.text = line;
            _hasLine = true;
        }

        public void Refresh(bool suppressed)
        {
            bool visible = _hasLine && !suppressed && Systems_BroadcastSettings.Commentary;

            if (visible == _visible)
            {
                return;
            }

            _visible = visible;
            Root.style.opacity = visible ? 1f : 0f;
        }
    }
}
