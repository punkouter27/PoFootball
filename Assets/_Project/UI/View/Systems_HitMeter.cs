using PoFootball.Models;
using UnityEngine;
using UnityEngine.UIElements;

namespace PoFootball.Views
{
    /// <summary>
    /// The chip that pops on a tackle: how fast the two bodies closed, roughly how
    /// hard they met, a power bar, and "HIT OF THE GAME" when it is the biggest one
    /// yet.
    ///
    /// FROM THE PHYSICS THE SIMULATION ALREADY RAN. The closing speed is the
    /// referee's own number off Systems_TackleMessage. The force is the momentum
    /// the collision had to absorb — the reduced mass of the two bodies, each from
    /// Systems_RoleTable.MassOf with its drawn traits, times the closing speed —
    /// spread over CONTACT_SECONDS, a typical duration for a football collision.
    /// That last number is an assumption, and it is why the readout is prefixed
    /// with "~": the ranking between two hits is exact, the kilonewtons are an
    /// estimate.
    ///
    /// Right side, below the result banner's band: a tackle ends the play, so the
    /// banner arrives the same instant and owns the band under the scoreboard.
    /// </summary>
    public sealed class Systems_HitMeter
    {
        /// <summary>Closing speed, m/s, under which a tackle is not worth a chip.</summary>
        public const float MIN_SPEED = 3.5f;

        /// <summary>Closing speed at which the power bar is full.</summary>
        public const float FULL_SPEED = 9f;

        /// <summary>Assumed duration of the collision, seconds. See the summary.</summary>
        public const float CONTACT_SECONDS = 0.12f;

        /// <summary>Tackles before a record is announced; the first hit of a game is always the biggest.</summary>
        private const int RECORD_AFTER_HITS = 4;

        private const float HOLD_SECONDS = 2.4f;

        private const float METRES_PER_SECOND_TO_MPH = 2.23694f;

        private const int BAR_HEIGHT = 8;

        private readonly Label _tag;
        private readonly Label _speed;
        private readonly Label _force;
        private readonly VisualElement _fill;

        private float _hideAt = -1f;
        private bool _visible;
        private float _biggestImpulse;
        private int _hits;

        public Systems_HitMeter()
        {
            Root = Systems_UiTheme.Column();
            Root.style.minWidth = 260;
            Root.style.backgroundColor = Systems_UiTheme.SurfaceOverField;
            Root.style.opacity = 0f;
            Root.pickingMode = PickingMode.Ignore;
            Systems_UiTheme.SetRadius(Root, Systems_UiTheme.RADIUS);
            Systems_UiTheme.SetPadding(Root, Systems_UiTheme.SPACE_S, Systems_UiTheme.SPACE_M);
            Systems_UiTheme.ApplyElevation(Root);
            Systems_UiTheme.EnableTransition(Root, "opacity");
            Systems_UiTheme.EnablePulse(Root);

            _tag = Systems_UiTheme.Caption("HIT");

            VisualElement numbers = Systems_UiTheme.Row();
            _speed = Systems_UiTheme.Text(
                string.Empty, Systems_UiTheme.TEXT_TITLE, Systems_UiTheme.TextPrimary, FontStyle.Bold);
            _force = Systems_UiTheme.Text(
                string.Empty, Systems_UiTheme.TEXT_CAPTION, Systems_UiTheme.TextMuted);
            _force.style.marginLeft = Systems_UiTheme.SPACE_M;
            numbers.Add(_speed);
            numbers.Add(_force);

            VisualElement track = new VisualElement();
            track.style.height = BAR_HEIGHT;
            track.style.marginTop = Systems_UiTheme.SPACE_XS;
            track.style.backgroundColor = new Color(1f, 1f, 1f, 0.14f);
            track.pickingMode = PickingMode.Ignore;
            Systems_UiTheme.SetRadius(track, BAR_HEIGHT / 2);

            _fill = new VisualElement();
            _fill.style.height = Length.Percent(100f);
            _fill.pickingMode = PickingMode.Ignore;
            Systems_UiTheme.SetRadius(_fill, BAR_HEIGHT / 2);
            track.Add(_fill);

            Root.Add(_tag);
            Root.Add(numbers);
            Root.Add(track);
        }

        public VisualElement Root { get; }

        /// <summary>
        /// The momentum the collision absorbed, N·s: reduced mass times closing
        /// speed. Public so a test can pin it.
        /// </summary>
        public static float ImpulseOf(float massA, float massB, float closingSpeed)
        {
            float total = massA + massB;
            return total <= 0f ? 0f : (massA * massB / total) * Mathf.Max(0f, closingSpeed);
        }

        /// <summary>
        /// Shows the chip for a tackle and returns its strength on 0..1, for the
        /// haptic pulse — or a negative number when the hit was too soft to show.
        /// </summary>
        public float Show(float closingSpeed, float massA, float massB, float now)
        {
            if (closingSpeed < MIN_SPEED)
            {
                return -1f;
            }

            float impulse = ImpulseOf(massA, massB, closingSpeed);
            float strength = Mathf.InverseLerp(MIN_SPEED, FULL_SPEED, closingSpeed);

            _hits++;
            bool record = impulse > _biggestImpulse && _hits > RECORD_AFTER_HITS;
            _biggestImpulse = Mathf.Max(_biggestImpulse, impulse);

            _tag.text = record
                ? "HIT OF THE GAME"
                : (closingSpeed >= Systems_CommentaryWriter.BIG_HIT_SPEED ? "BIG HIT" : "HIT");
            _tag.style.color = record ? Systems_UiTheme.Accent : Systems_UiTheme.TextMuted;

            _speed.text = Mathf.RoundToInt(closingSpeed * METRES_PER_SECOND_TO_MPH) + " MPH";
            _force.text = "~" + (impulse / CONTACT_SECONDS / 1000f).ToString("0.0") + " kN";

            _fill.style.width = Length.Percent(Mathf.Max(6f, strength * 100f));
            _fill.style.backgroundColor = Color.Lerp(Systems_UiTheme.Accent, Systems_UiTheme.Negative, strength);

            _hideAt = now + HOLD_SECONDS;
            SetVisible(true);
            Systems_UiTheme.Pulse(Root);
            return strength;
        }

        public void Refresh(float now)
        {
            if (_visible && now >= _hideAt)
            {
                SetVisible(false);
            }
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
    }
}
