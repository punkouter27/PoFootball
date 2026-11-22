using NUnit.Framework;
using PoFootball.Agents;
using PoFootball.Models;
using UnityEngine;

namespace PoFootball.Tests
{
    /// <summary>
    /// What a velocity command does to the body (revision 17), case by case. The
    /// controller is the actuator every brain and every scripted player drives
    /// through, so each of these is a statement about what an action MEANS — the
    /// thing Agent_ActionContract's stamp exists to keep fixed for a brain's life.
    /// </summary>
    public sealed class Agent_VelocityControllerTests
    {
        private const float TOP_SPEED = 9f;

        private static readonly Vector2 North = Vector2.up;
        private static readonly Vector2 East = Vector2.right;

        // Commands are (forward, leftward), in the decision's frame.

        [Test]
        public void ASprintFromStanding_IsFullDriveStraightAhead()
        {
            Resolve(new Vector2(1f, 0f), North, North, Vector2.zero, out float drive, out float steer);

            Assert.That(drive, Is.EqualTo(1f));
            Assert.That(steer, Is.EqualTo(0f));
        }

        [Test]
        public void AtTopSpeed_ASprintHoldsFullDrive()
        {
            Resolve(new Vector2(1f, 0f), North, North, North * TOP_SPEED, out float drive, out _);

            Assert.That(drive, Is.EqualTo(1f).Within(1e-4f),
                "full drive is what holds top speed against damping: anything less decelerates");
        }

        [Test]
        public void Standing_BrakesARunningBody()
        {
            Resolve(Vector2.zero, North, North, North * 5f, out float drive, out float steer);

            Assert.That(drive, Is.LessThan(0f), "a stand request must brake, not coast");
            Assert.That(steer, Is.EqualTo(0f), "and hold the facing");
        }

        [Test]
        public void HalfSpeed_IsTrackedNotThrottled()
        {
            Resolve(new Vector2(0.5f, 0f), North, North, North * TOP_SPEED, out float fast, out _);
            Resolve(new Vector2(0.5f, 0f), North, North, North * (TOP_SPEED * 0.5f), out float held, out _);

            Assert.That(fast, Is.LessThan(0f), "above the requested speed: brake");
            Assert.That(held, Is.EqualTo(0.5f).Within(1e-4f), "at it: exactly the share that holds it");
        }

        [Test]
        public void ARightAngleTurn_IsFullLockWithoutDriving()
        {
            Resolve(new Vector2(0f, -1f), North, North, Vector2.zero, out float drive, out float steer);

            Assert.That(steer, Is.EqualTo(-1f), "to the right is clockwise, negative torque");
            Assert.That(drive, Is.EqualTo(0f).Within(1e-4f), "nothing to drive along until lined up");
        }

        [Test]
        public void SlowlyBehind_IsABackpedalFacingForward()
        {
            float slow = Systems_SimConstants.BACKPEDAL_SPEED_FRACTION * 0.9f;

            Resolve(new Vector2(-slow, 0f), North, North, Vector2.zero, out float drive, out float steer);

            Assert.That(steer, Is.EqualTo(0f).Within(1e-4f), "a corner keeps his eyes on the receiver");
            Assert.That(drive, Is.LessThan(0f), "and drives backwards");
        }

        [Test]
        public void FastBehind_TurnsAndRuns()
        {
            Resolve(new Vector2(-1f, 0f), North, North, Vector2.zero, out _, out float steer);

            Assert.That(Mathf.Abs(steer), Is.EqualTo(1f), "full speed backwards is a turn, not a backpedal");
        }

        [Test]
        public void TheBackpedal_IsCappedAtItsShareOfTopSpeed()
        {
            float cap = Systems_SimConstants.BACKPEDAL_SPEED_FRACTION;
            Vector2 backpedalling = -North * (TOP_SPEED * cap);

            Resolve(new Vector2(-cap, 0f), North, North, backpedalling, out float drive, out _);

            Assert.That(drive, Is.EqualTo(-cap).Within(1e-4f), "holds the cap, no faster");
        }

        /// <summary>
        /// The command is read in the frame the decision was made in and then held:
        /// a body that has since turned to face north keeps going east, which is
        /// what it was asked for, rather than turning a further right angle.
        /// </summary>
        [Test]
        public void SlotOne_IsLeftward_TheWayPositiveSteerTurned()
        {
            Resolve(new Vector2(0f, 1f), North, North, Vector2.zero, out _, out float steer);

            Assert.That(steer, Is.EqualTo(1f),
                "a revision 16 brain's positive steer turned left: so must its slot 1 now");
        }

        [Test]
        public void TheCommand_IsHeldInTheDecisionsFrame()
        {
            Resolve(new Vector2(1f, 0f), East, North, Vector2.zero, out _, out float steer);

            Assert.That(steer, Is.EqualTo(-1f), "forward-at-the-decision was east: turn right");
        }

        private static void Resolve(
            Vector2 command, Vector2 frameUp, Vector2 facing, Vector2 velocity,
            out float drive, out float steer)
        {
            Agent_VelocityController.Resolve(
                command, frameUp, facing, velocity, TOP_SPEED, out drive, out steer);
        }
    }
}
