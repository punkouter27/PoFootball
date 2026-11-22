using UnityEngine;

namespace PoFootball.Rewards
{
    /// <summary>
    /// The weights of the four control penalties (Reward_Penalty), as an asset so
    /// they are tuned in the Inspector rather than in code:
    /// Assets/_Project/Data/Resources/PoFootballRewardPenalties.asset.
    ///
    /// Each weight multiplies a dimensionless term that lands in [0, ~4] per
    /// physics tick, and is charged every tick of a live play. At the defaults a
    /// player who spends a whole 10 s play (500 ticks) at full drive, full steer,
    /// full spin, full jitter and full skid pays about -0.4 in total, against a
    /// touchdown's +1.0: enough to prefer a smooth body, never enough to prefer
    /// standing still. Zero switches a term off.
    ///
    /// ADDED 2026-11-22. Every brain on disk was trained without these terms;
    /// they shape the next run, not the shipped policies.
    /// </summary>
    [CreateAssetMenu(menuName = "PoFootball/Reward Penalty Weights", fileName = "PoFootballRewardPenalties")]
    public sealed class Reward_PenaltyWeights : ScriptableObject
    {
        /// <summary>Resources path of the asset the agents load.</summary>
        public const string RESOURCE_PATH = "PoFootballRewardPenalties";

        public const float DEFAULT_EFFORT = 0.0002f;
        public const float DEFAULT_ANGULAR_VELOCITY = 0.0002f;
        public const float DEFAULT_ACTION_JITTER = 0.0002f;
        public const float DEFAULT_SLIDE = 0.0002f;

        [Tooltip("Per tick, times (drive/max)^2 + (steer torque/max)^2. The 2D body's 'joint torque'.")]
        [SerializeField] private float _effort = DEFAULT_EFFORT;

        [Tooltip("Per tick, times (angular velocity / max turn rate)^2.")]
        [SerializeField] private float _angularVelocity = DEFAULT_ANGULAR_VELOCITY;

        [Tooltip("Per tick, times |this decision's velocity command - the last one|^2 (action jitter).")]
        [SerializeField] private float _actionJitter = DEFAULT_ACTION_JITTER;

        [Tooltip("Per tick, times (speed across the body's facing / top speed)^2. A flat body's foot sliding.")]
        [SerializeField] private float _slide = DEFAULT_SLIDE;

        public float Effort => _effort;
        public float AngularVelocity => _angularVelocity;
        public float ActionJitter => _actionJitter;
        public float Slide => _slide;

        /// <summary>The weights as the pure function takes them.</summary>
        public Reward_Penalty.Weights ToWeights()
        {
            return new Reward_Penalty.Weights(_effort, _angularVelocity, _actionJitter, _slide);
        }
    }
}
