using PoFootball.Models;
using UnityEngine;

namespace PoFootball.Agents
{
    /// <summary>
    /// Turns a requested VELOCITY into the drive and steer the body is pushed by.
    /// Revision 17: what every brain's two continuous outputs mean.
    ///
    /// WHY. Through revision 16 the two outputs were a throttle along the body's
    /// facing and a steering torque. To go anywhere a policy had to learn to turn
    /// a second-order rotating body first — and the defense, whose whole job is
    /// arriving somewhere, was the brain that never learned it: football_base22's
    /// defenders moved at 13-15% of top speed, faced the carrier no more than away
    /// from him, and base23 retrained the Defense brain from scratch. The scripted
    /// players never had that problem because Agent_FootballPlayer.Steer solved it
    /// for them, in nine lines. Those nine lines are now the actuator, for every
    /// player, trained or scripted, so a policy chooses WHERE and HOW FAST.
    ///
    /// THE FRAME IS THE BODY'S AT THE DECISION. The command is (forward, leftward)
    /// in the frame the observations were written in — own velocity, ball offset
    /// and rays are all body-frame — so "the ball is ahead and to my left" maps to
    /// "go ahead and to the left" with no rotation for the network to learn.
    ///
    /// FORWARD FIRST, THEN LEFT, so a revision 16 brain warm-starts pointing the
    /// right way: slot 0 was drive along the facing and is now speed along it;
    /// slot 1 was steer, positive turning LEFT (counterclockwise), and is now
    /// speed to the left. "Drive while turning left" and "go forward-left" are the
    /// same habit, which is what lets Config/FootballBase25.yaml start every brain
    /// from football_base24 instead of from nothing. It is
    /// latched to the WORLD at that moment (frameUp) and tracked
    /// as a world direction for the DECISION_PERIOD ticks the action is repeated;
    /// re-reading it in the turning body's frame every tick would chase its own
    /// tail and circle.
    ///
    /// What it does with the command, per tick:
    ///
    ///     length under DEADZONE   stand: brake to a stop, hold the facing
    ///     behind, and no faster   backpedal: face away from it and drive
    ///     than BACKPEDAL share    backwards, as a corner in coverage does
    ///     otherwise               turn toward it (full lock at 45 degrees off) and
    ///                             drive to |command| x top speed, scaled by how
    ///                             well lined up the body already is, so a player
    ///                             turning around slows and turns instead of
    ///                             driving off while he does
    ///
    /// Speed is tracked, not commanded: feed-forward for the requested speed plus
    /// SPEED_TRACKING_GAIN on the error, clamped to [-1, 1]. A full-speed request
    /// is full drive the whole way up, exactly as full throttle was; asking for less
    /// than the body is doing BRAKES, which no throttle could.
    ///
    /// Pure: no state, no Unity objects, so Agent_VelocityControllerTests pins it.
    /// </summary>
    internal static class Agent_VelocityController
    {
        /// <param name="command">x forward, y leftward, in the decision's frame.</param>
        internal static void Resolve(
            Vector2 command,
            Vector2 frameUp,
            Vector2 facing,
            Vector2 velocity,
            float topSpeed,
            out float drive,
            out float steer)
        {
            float length = command.magnitude;
            float forwardSpeed = Vector2.Dot(velocity, facing);

            if (length < Systems_SimConstants.VELOCITY_COMMAND_DEADZONE || topSpeed <= 0f)
            {
                steer = 0f;
                drive = Track(0f, forwardSpeed, topSpeed);
                return;
            }

            float magnitude = Mathf.Min(length, 1f);
            Vector2 frameLeft = new Vector2(-frameUp.y, frameUp.x);
            Vector2 desired = ((frameUp * command.x) + (frameLeft * command.y)) / length;

            float error = Vector2.SignedAngle(facing, desired);
            float targetSpeed = magnitude * topSpeed;

            if (Mathf.Abs(error) > 90f && magnitude <= Systems_SimConstants.BACKPEDAL_SPEED_FRACTION)
            {
                error = Vector2.SignedAngle(facing, -desired);
                targetSpeed = -targetSpeed;
            }

            steer = Mathf.Clamp(error / Systems_SimConstants.HEADING_FULL_LOCK_DEGREES, -1f, 1f);

            float alignment = Mathf.Clamp01(Mathf.Cos(error * Mathf.Deg2Rad));
            drive = Track(targetSpeed * alignment, forwardSpeed, topSpeed);
        }

        /// <summary>
        /// Drive for a forward-speed target: the share of full force that holds it
        /// against damping (F = m * d * v against DriveForceOf = m * d * top) plus a
        /// proportional correction.
        /// </summary>
        private static float Track(float targetSpeed, float forwardSpeed, float topSpeed)
        {
            if (topSpeed <= 0f)
            {
                return 0f;
            }

            float feedForward = targetSpeed / topSpeed;
            float correction = Systems_SimConstants.SPEED_TRACKING_GAIN
                * (targetSpeed - forwardSpeed) / topSpeed;

            return Mathf.Clamp(feedForward + correction, -1f, 1f);
        }
    }
}
