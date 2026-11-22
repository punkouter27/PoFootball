using NUnit.Framework;
using PoFootball.Rewards;
using UnityEngine;

namespace PoFootball.Tests
{
    /// <summary>
    /// The four control penalties (Reward_Penalty) and the asset their weights
    /// live in. Systems_ShowcasePlayTests checks each is charged in a live game.
    /// </summary>
    public sealed class Reward_PenaltyTests
    {
        private static readonly Reward_Penalty.Weights Unit = new Reward_Penalty.Weights(1f, 1f, 1f, 1f);

        [Test]
        public void AStillBody_PaysNothing()
        {
            Reward_Penalty.Terms terms = Reward_Penalty.PerTick(in Unit, 0f, 0f, 0f, 0f, 0f);
            Assert.That(terms.Total, Is.EqualTo(0f));
        }

        [Test]
        public void EachTerm_IsChargedByItsOwnInput_AndOnlyThatOne()
        {
            Reward_Penalty.Terms effort = Reward_Penalty.PerTick(in Unit, 1f, 0.5f, 0f, 0f, 0f);
            Assert.That(effort.Effort, Is.EqualTo(-1.25f).Within(1e-6f));
            Assert.That(effort.Total, Is.EqualTo(effort.Effort));

            Reward_Penalty.Terms spin = Reward_Penalty.PerTick(in Unit, 0f, 0f, 0.5f, 0f, 0f);
            Assert.That(spin.AngularVelocity, Is.EqualTo(-0.25f).Within(1e-6f));
            Assert.That(spin.Total, Is.EqualTo(spin.AngularVelocity));

            Reward_Penalty.Terms jitter = Reward_Penalty.PerTick(in Unit, 0f, 0f, 0f, 2f, 0f);
            Assert.That(jitter.ActionJitter, Is.EqualTo(-4f).Within(1e-6f));
            Assert.That(jitter.Total, Is.EqualTo(jitter.ActionJitter));

            Reward_Penalty.Terms slide = Reward_Penalty.PerTick(in Unit, 0f, 0f, 0f, 0f, 0.3f);
            Assert.That(slide.Slide, Is.EqualTo(-0.09f).Within(1e-6f));
            Assert.That(slide.Total, Is.EqualTo(slide.Slide));
        }

        [Test]
        public void AZeroWeight_SwitchesItsTermOff()
        {
            var weights = new Reward_Penalty.Weights(0f, 1f, 1f, 1f);
            Reward_Penalty.Terms terms = Reward_Penalty.PerTick(in weights, 1f, 1f, 0f, 0f, 0f);
            Assert.That(terms.Effort, Is.EqualTo(0f));
        }

        /// <summary>
        /// A whole 10 s play at every input's maximum must cost less than a
        /// touchdown pays, or standing still becomes the best policy.
        /// </summary>
        [Test]
        public void TheDefaultWeights_NeverOutweighATouchdown()
        {
            Reward_Penalty.Weights weights = Reward_Penalty.Weights.Defaults;
            Reward_Penalty.Terms worst = Reward_Penalty.PerTick(in weights, 1f, 1f, 1f, 2f, 1f);
            float play = worst.Total * 500f;
            Assert.That(play, Is.GreaterThan(-Models.Systems_SimConstants.TOUCHDOWN_REWARD));
        }

        [Test]
        public void TheWeightsAsset_IsInResources_WithEveryTermOn()
        {
            Reward_PenaltyWeights asset = Resources.Load<Reward_PenaltyWeights>(Reward_PenaltyWeights.RESOURCE_PATH);
            Assert.That(asset, Is.Not.Null, "no PoFootballRewardPenalties asset in Resources");
            Assert.That(asset.Effort, Is.GreaterThan(0f));
            Assert.That(asset.AngularVelocity, Is.GreaterThan(0f));
            Assert.That(asset.ActionJitter, Is.GreaterThan(0f));
            Assert.That(asset.Slide, Is.GreaterThan(0f));
        }

        [Test]
        public void TheShowcaseAsset_StartsTheDemoAfterThirtySeconds()
        {
            Views.Systems_ShowcaseSettings asset =
                Resources.Load<Views.Systems_ShowcaseSettings>(Views.Systems_ShowcaseSettings.RESOURCE_PATH);
            Assert.That(asset, Is.Not.Null, "no PoFootballShowcase asset in Resources");
            Assert.That(asset.IdleSeconds, Is.EqualTo(30f));
            Assert.That(asset.BenchmarkSeconds, Is.GreaterThan(0f));
        }
    }
}
