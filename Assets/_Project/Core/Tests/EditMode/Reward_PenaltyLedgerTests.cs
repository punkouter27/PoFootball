using System.Collections.Generic;
using NUnit.Framework;
using PoFootball.Rewards;

namespace PoFootball.Tests
{
    /// <summary>
    /// Every control penalty term reaches TensorBoard on its own key
    /// (Reward/&lt;brain&gt;/Penalty/&lt;term&gt;), beside the total the ledger
    /// always had, with the play's sum of that term.
    /// </summary>
    public sealed class Reward_PenaltyLedgerTests
    {
        private static readonly Reward_Penalty.Weights Unit = new Reward_Penalty.Weights(1f, 1f, 1f, 1f);

        [Test]
        public void APlay_LogsTheTotalAndEveryTermUnderItsOwnKey()
        {
            Reward_Penalty.Terms play = default;

            // Two ticks, each charging one term more than the last.
            play = play.Plus(Reward_Penalty.PerTick(in Unit, 1f, 0f, 0.5f, 0f, 0f));
            play = play.Plus(Reward_Penalty.PerTick(in Unit, 0f, 0f, 0f, 2f, 0.3f));

            var logged = new Dictionary<string, float>();
            Reward_Penalty.Log(
                new Reward_Penalty.LedgerKeys("Reward/Offense/"), in play, (key, value) => logged.Add(key, value));

            Assert.That(logged.Count, Is.EqualTo(5), string.Join(", ", logged.Keys));
            Assert.That(logged["Reward/Offense/Penalty/Effort"], Is.EqualTo(-1f).Within(1e-6f));
            Assert.That(logged["Reward/Offense/Penalty/AngularVelocity"], Is.EqualTo(-0.25f).Within(1e-6f));
            Assert.That(logged["Reward/Offense/Penalty/ActionJitter"], Is.EqualTo(-4f).Within(1e-6f));
            Assert.That(logged["Reward/Offense/Penalty/Slide"], Is.EqualTo(-0.09f).Within(1e-6f));
            Assert.That(logged["Reward/Offense/Penalty"], Is.EqualTo(-5.34f).Within(1e-5f));
        }

        [Test]
        public void TheKeys_AreDistinctPerBrain()
        {
            var offense = new Reward_Penalty.LedgerKeys("Reward/Offense/");
            var defense = new Reward_Penalty.LedgerKeys("Reward/Defense/");

            var all = new HashSet<string>
            {
                offense.Total, offense.Effort, offense.AngularVelocity, offense.ActionJitter, offense.Slide,
                defense.Total, defense.Effort, defense.AngularVelocity, defense.ActionJitter, defense.Slide,
            };

            Assert.That(all.Count, Is.EqualTo(10));
        }
    }
}
