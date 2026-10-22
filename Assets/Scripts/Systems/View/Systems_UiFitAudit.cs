using UnityEngine;
using UnityEngine.UIElements;

namespace PoFootball.Views
{
    /// <summary>
    /// Checks every visible piece of text on every UI document in the scene
    /// against the screen it is on, for the DEBUG sheet. CLAUDE.md section 3 asks
    /// for legible text that fits one portrait screen without scrolling; this is
    /// the run-time check of that, on whatever device is in someone's hand.
    ///
    /// Three questions, each a finding when the answer is wrong:
    ///
    ///   CUT OFF — text partly outside the screen. Partly, not wholly: a sheet slid
    ///   off-screen to hide it is entirely outside and is not a fault.
    ///
    ///   OVERFLOWING — single-line text wider than the box it was given, so it
    ///   runs into its neighbour or under an ellipsis.
    ///
    ///   SCROLLING — a ScrollView whose content is taller than it is. The rule is
    ///   that nothing scrolls; a screen that does has not been fitted.
    ///
    /// And one reading, not a fault on its own: the smallest text on screen in
    /// points on this display, which is what "legible" means on a phone.
    ///
    /// WHY THE SHEET RUNS IT AND NOT A TEST. The panel scales on width, so layout
    /// is the same on every device — except where it is not: the safe-area inset,
    /// a long team name, a device whose aspect is taller than any capture. Those
    /// show up on the handset, which is where this runs.
    ///
    /// Only while the DEBUG sheet is open, four times a second. It walks the tree
    /// and measures text, which allocates a little; that is the sheet's price,
    /// and the sheet is closed in a release build.
    /// </summary>
    internal static class Systems_UiFitAudit
    {
        /// <summary>Panel units of slack before a box counts as overflowed or clipped. Sub-pixel rounding is not a finding.</summary>
        private const float SLACK = 2f;

        /// <summary>Longest stretch of offending text quoted back.</summary>
        private const int QUOTE_LENGTH = 28;

        /// <summary>Real seconds between rescans of the scene for UI documents.</summary>
        private const float DOCUMENT_RESCAN_SECONDS = 2f;

        private static UIDocument[] _documents = new UIDocument[0];
        private static float _lastScan = float.NegativeInfinity;

        internal struct Result
        {
            public int TextChecked;

            public int CutOff;
            public string CutOffExample;

            public int Overflowing;
            public string OverflowExample;

            public int Scrolling;
            public string ScrollingScreen;

            /// <summary>Smallest visible text in typographic points on this display; 0 when the display reports no DPI.</summary>
            public float SmallestPoints;
            public string SmallestExample;
        }

        public static Result Run()
        {
            Result result = default;
            result.CutOffExample = string.Empty;
            result.OverflowExample = string.Empty;
            result.ScrollingScreen = string.Empty;
            result.SmallestExample = string.Empty;

            if (Time.realtimeSinceStartup - _lastScan > DOCUMENT_RESCAN_SECONDS)
            {
                _lastScan = Time.realtimeSinceStartup;
                _documents = Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude);
            }

            for (int index = 0; index < _documents.Length; index++)
            {
                UIDocument document = _documents[index];

                if (document == null || !document.isActiveAndEnabled)
                {
                    continue;
                }

                VisualElement root = document.rootVisualElement;

                if (root == null || root.panel == null)
                {
                    continue;
                }

                Rect screen = root.panel.visualTree.worldBound;

                if (screen.width <= 0f || screen.height <= 0f)
                {
                    continue;
                }

                // Physical points per panel unit. The panel is scaled to the screen's
                // width, so a unit is Screen.width / panel width pixels; Screen.dpi
                // turns pixels into inches and 72 inches into points.
                float pointsPerUnit = Screen.dpi > 0f
                    ? Screen.width / screen.width / Screen.dpi * 72f
                    : 0f;

                Walk(root, screen, pointsPerUnit, document.gameObject.name, ref result);
            }

            return result;
        }

        private static void Walk(
            VisualElement element, Rect screen, float pointsPerUnit, string screenName, ref Result result)
        {
            IResolvedStyle style = element.resolvedStyle;

            if (style.display == DisplayStyle.None
                || style.visibility == Visibility.Hidden
                || style.opacity <= 0.01f)
            {
                return;
            }

            if (element is ScrollView scroll)
            {
                float content = scroll.contentContainer.layout.height;
                float viewport = scroll.contentViewport.layout.height;

                if (viewport > 0f && content > viewport + SLACK)
                {
                    if (result.Scrolling == 0)
                    {
                        result.ScrollingScreen = screenName;
                    }

                    result.Scrolling++;
                }
            }

            if (element is TextElement text && !string.IsNullOrEmpty(text.text))
            {
                CheckText(text, style, screen, pointsPerUnit, ref result);
            }

            int childCount = element.hierarchy.childCount;

            for (int child = 0; child < childCount; child++)
            {
                Walk(element.hierarchy[child], screen, pointsPerUnit, screenName, ref result);
            }
        }

        private static void CheckText(
            TextElement text, IResolvedStyle style, Rect screen, float pointsPerUnit, ref Result result)
        {
            Rect bounds = text.worldBound;

            if (bounds.width <= 0f || bounds.height <= 0f || !bounds.Overlaps(screen))
            {
                return;
            }

            result.TextChecked++;

            bool cutOff = bounds.xMin < screen.xMin - SLACK || bounds.xMax > screen.xMax + SLACK
                || bounds.yMin < screen.yMin - SLACK || bounds.yMax > screen.yMax + SLACK;

            if (cutOff)
            {
                if (result.CutOff == 0)
                {
                    result.CutOffExample = Quote(text.text);
                }

                result.CutOff++;
            }

            // Wrapping text grows downward to fit and is judged by the cut-off test
            // above; only single-line text can run past its own box sideways.
            if (style.whiteSpace == WhiteSpace.NoWrap)
            {
                Vector2 wanted = text.MeasureTextSize(
                    text.text, 0f, VisualElement.MeasureMode.Undefined,
                    0f, VisualElement.MeasureMode.Undefined);

                if (wanted.x > text.contentRect.width + SLACK)
                {
                    if (result.Overflowing == 0)
                    {
                        result.OverflowExample = Quote(text.text);
                    }

                    result.Overflowing++;
                }
            }

            if (pointsPerUnit > 0f)
            {
                float points = style.fontSize * pointsPerUnit;

                if (result.SmallestPoints <= 0f || points < result.SmallestPoints)
                {
                    result.SmallestPoints = points;
                    result.SmallestExample = Quote(text.text);
                }
            }
        }

        private static string Quote(string text)
        {
            string line = text.Replace('\n', ' ');
            return line.Length > QUOTE_LENGTH ? line.Substring(0, QUOTE_LENGTH) + "..." : line;
        }
    }
}
