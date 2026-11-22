using PoFootball.Models;
using UnityEngine;
using UnityEngine.UIElements;

namespace PoFootball.Views
{
    /// <summary>
    /// The thin strip under the scoreboard: each team's chance of winning at the
    /// two ends, and the whole game's line between them — above the middle is the
    /// home team's, below is the away team's, filled in each team's colour.
    ///
    /// WHY IT IS THE ONE PIECE OF CHROME WORTH ITS HEIGHT. The scoreboard says
    /// what the score is; this says what the score MEANS, and how it got there.
    /// A ten-point lead with two minutes left and the same lead at kickoff look
    /// identical on the bar and nothing alike here, and a game that swung three
    /// times reads as a line that crossed the middle three times. The largest
    /// single-play swing is marked with a dot — the turning point, the play the
    /// highlight reel ought to be about.
    ///
    /// DRAWN WITH Painter2D, ONLY WHEN A PLAY RESOLVES. generateVisualContent
    /// runs on MarkDirtyRepaint, which is called when the model's Version moves —
    /// once a down. The path is two filled polygons and one stroke over at most
    /// Systems_WinProbabilityModel.CAPACITY points; every other frame costs a
    /// single integer comparison.
    ///
    /// The weather caption hangs under the strip's right end, small and muted:
    /// the city and the sky are context, not news, and a full row for one line of
    /// it would have cost the field another 30 units.
    /// </summary>
    public sealed class Systems_WinProbabilityRibbon
    {
        /// <summary>Height of the strip, panel units.</summary>
        public const int HEIGHT = 40;

        /// <summary>Width of each percentage label at the ends.</summary>
        private const int LABEL_WIDTH = 130;

        private const float LINE_WIDTH = 2.5f;

        private const float FILL_ALPHA = 0.42f;

        private const float MIDLINE_ALPHA = 0.25f;

        private const float SWING_DOT_RADIUS = 4.5f;

        private readonly Systems_WinProbabilityModel _model;
        private readonly Label _homeLabel;
        private readonly Label _awayLabel;
        private readonly Label _weatherLabel;
        private readonly VisualElement _chart;

        private int _drawnVersion = -1;
        private int _drawnWeatherRevision = -1;
        private int _lastHomePercent = -1;

        public Systems_WinProbabilityRibbon(Systems_WinProbabilityModel model)
        {
            _model = model;

            Root = Systems_UiTheme.Row();
            Root.style.height = HEIGHT;
            Root.style.backgroundColor = Systems_UiTheme.SurfaceOverField;
            Root.pickingMode = PickingMode.Ignore;
            Systems_UiTheme.SetPadding(Root, Systems_UiTheme.SPACE_XS, Systems_UiTheme.SPACE_M);

            _homeLabel = BuildEndLabel(Systems_TeamId.Home, TextAnchor.MiddleLeft);
            _awayLabel = BuildEndLabel(Systems_TeamId.Away, TextAnchor.MiddleRight);

            _chart = new VisualElement();
            _chart.style.flexGrow = 1f;
            _chart.style.height = Length.Percent(100f);
            _chart.style.marginLeft = Systems_UiTheme.SPACE_S;
            _chart.style.marginRight = Systems_UiTheme.SPACE_S;
            _chart.pickingMode = PickingMode.Ignore;
            _chart.generateVisualContent += DrawChart;

            // Hung under the strip's right end on a plate of its own. It started
            // inside the chart's corner, and a capture showed the 50% line running
            // straight through the city's name.
            _weatherLabel = Systems_UiTheme.Text(
                string.Empty, Systems_UiTheme.TEXT_CAPTION - 4, Systems_UiTheme.TextMuted);
            _weatherLabel.style.position = Position.Absolute;
            _weatherLabel.style.right = Systems_UiTheme.SPACE_M;
            _weatherLabel.style.top = HEIGHT + Systems_UiTheme.SPACE_XS;
            _weatherLabel.style.backgroundColor = Systems_UiTheme.SurfaceOverField;
            _weatherLabel.style.display = DisplayStyle.None;
            Systems_UiTheme.SetRadius(_weatherLabel, Systems_UiTheme.RADIUS / 2);
            Systems_UiTheme.SetPadding(_weatherLabel, 2, Systems_UiTheme.SPACE_S);

            Root.Add(_homeLabel);
            Root.Add(_chart);
            Root.Add(_awayLabel);
            Root.Add(_weatherLabel);
        }

        public VisualElement Root { get; }

        /// <summary>Redraws when the model or the weather has moved since the last call.</summary>
        public void Refresh(Systems_WeatherModel weather)
        {
            if (weather != null && weather.Revision != _drawnWeatherRevision)
            {
                _drawnWeatherRevision = weather.Revision;
                _weatherLabel.text = weather.IsReady ? weather.DisplayLine : string.Empty;
                _weatherLabel.style.display = weather.IsReady && weather.DisplayLine.Length > 0
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            if (_model == null || _model.Version == _drawnVersion)
            {
                return;
            }

            _drawnVersion = _model.Version;

            int homePercent = Mathf.Clamp(Mathf.RoundToInt(_model.HomeNow * 100f), 0, 100);

            if (homePercent != _lastHomePercent)
            {
                _lastHomePercent = homePercent;
                _homeLabel.text = Systems_DisplayText.TeamTag(Systems_TeamId.Home) + " " + homePercent + "%";
                _awayLabel.text = (100 - homePercent) + "% " + Systems_DisplayText.TeamTag(Systems_TeamId.Away);
            }

            _chart.MarkDirtyRepaint();
        }

        private static Label BuildEndLabel(Systems_TeamId team, TextAnchor anchor)
        {
            Label label = Systems_UiTheme.Text(
                string.Empty, Systems_UiTheme.TEXT_CAPTION, Systems_UiTheme.ColorOf(team), FontStyle.Bold);
            label.style.width = LABEL_WIDTH;
            label.style.unityTextAlign = anchor;
            label.style.letterSpacing = 1f;
            return label;
        }

        private void DrawChart(MeshGenerationContext context)
        {
            Rect rect = _chart.contentRect;

            if (_model == null || rect.width <= 1f || rect.height <= 1f)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            float middle = rect.height * 0.5f;

            painter.lineWidth = 1f;
            painter.strokeColor = WithAlpha(Systems_UiTheme.TextMuted, MIDLINE_ALPHA);
            painter.BeginPath();
            painter.MoveTo(new Vector2(0f, middle));
            painter.LineTo(new Vector2(rect.width, middle));
            painter.Stroke();

            int count = _model.Count;

            if (count < 2)
            {
                return;
            }

            // Home's share above the line, away's below: the same polyline clamped
            // to each side of the middle and closed back along it.
            FillSide(painter, rect, count, middle, true, WithAlpha(Systems_UiTheme.ColorOf(Systems_TeamId.Home), FILL_ALPHA));
            FillSide(painter, rect, count, middle, false, WithAlpha(Systems_UiTheme.ColorOf(Systems_TeamId.Away), FILL_ALPHA));

            painter.lineWidth = LINE_WIDTH;
            painter.lineJoin = LineJoin.Round;
            painter.strokeColor = Systems_UiTheme.TextPrimary;
            painter.BeginPath();

            for (int index = 0; index < count; index++)
            {
                Vector2 point = PointAt(rect, count, index);

                if (index == 0)
                {
                    painter.MoveTo(point);
                }
                else
                {
                    painter.LineTo(point);
                }
            }

            painter.Stroke();

            int swingIndex = _model.BiggestSwingIndex;

            if (swingIndex > 0 && swingIndex < count)
            {
                painter.fillColor = Systems_UiTheme.Accent;
                painter.BeginPath();
                painter.Arc(PointAt(rect, count, swingIndex), SWING_DOT_RADIUS, 0f, 360f);
                painter.Fill();
            }
        }

        private void FillSide(Painter2D painter, Rect rect, int count, float middle, bool home, Color color)
        {
            painter.fillColor = color;
            painter.BeginPath();
            painter.MoveTo(new Vector2(0f, middle));

            for (int index = 0; index < count; index++)
            {
                Vector2 point = PointAt(rect, count, index);
                point.y = home ? Mathf.Min(point.y, middle) : Mathf.Max(point.y, middle);
                painter.LineTo(point);
            }

            painter.LineTo(new Vector2(PointAt(rect, count, count - 1).x, middle));
            painter.ClosePath();
            painter.Fill();
        }

        /// <summary>
        /// Point <paramref name="index"/> of the history in the chart's own space:
        /// the game spread across the full width, home's certainty at the top.
        /// </summary>
        private Vector2 PointAt(Rect rect, int count, int index)
        {
            float x = count <= 1 ? 0f : rect.width * index / (count - 1);
            float y = rect.height * (1f - Mathf.Clamp01(_model.HistoryAt(index)));
            return new Vector2(x, y);
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            return new Color(color.r, color.g, color.b, alpha);
        }
    }
}
