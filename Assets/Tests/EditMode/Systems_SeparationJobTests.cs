using NUnit.Framework;
using PoFootball.Models;
using PoFootball.Views;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace PoFootball.Tests
{
    public sealed class Systems_SeparationJobTests
    {
        private const float TOLERANCE = 1e-4f;

        [Test]
        public void SeparationAndIntercept_MatchScalarReference()
        {
            const int COUNT = 4;

            var players = new NativeArray<float4>(COUNT, Allocator.TempJob);
            var sides = new NativeArray<int>(COUNT, Allocator.TempJob);
            var speeds = new NativeArray<float>(COUNT, Allocator.TempJob);
            var distances = new NativeArray<float>(COUNT, Allocator.TempJob);
            var intercepts = new NativeArray<float>(COUNT, Allocator.TempJob);

            try
            {
                // Slot 0: offense receiver at (0, 10)
                // Slot 1: defense corner at (3, 14) -> distance 5.0 from slot 0
                // Slot 2: offense carrier at (10, 0), velocity (0, 6)
                // Slot 3: defense linebacker at (18, 0), speed 8.5 -> intercepts carrier
                players[0] = new float4(0f, 10f, 0f, 0f);
                sides[0] = 0;
                speeds[0] = 9f;

                players[1] = new float4(3f, 14f, 0f, 0f);
                sides[1] = 1;
                speeds[1] = 9f;

                players[2] = new float4(10f, 0f, 0f, 6f);
                sides[2] = 0;
                speeds[2] = 8.9f;

                players[3] = new float4(18f, 0f, 0f, 0f);
                sides[3] = 1;
                speeds[3] = 8.5f;

                var job = new Systems_SeparationJob
                {
                    Players = players,
                    Sides = sides,
                    TopSpeeds = speeds,
                    CarrierPosition = new float2(10f, 0f),
                    CarrierVelocity = new float2(0f, 6f),
                    CarrierSide = 0,
                    HasCarrier = 1,
                    ComputeSeparation = 1,
                    DefaultSeparation = 5.4864f,
                    NearestOpponentDistances = distances,
                    InterceptTimes = intercepts
                };

                job.Schedule(COUNT, 1).Complete();

                Assert.That(distances[0], Is.EqualTo(5f).Within(TOLERANCE));
                Assert.That(distances[2], Is.EqualTo(8f).Within(TOLERANCE));

                float expectedIntercept1 = Systems_InterceptMath.InterceptSeconds(
                    new Vector2(3f, 14f), 9f, new Vector2(10f, 0f), new Vector2(0f, 6f));
                float expectedIntercept3 = Systems_InterceptMath.InterceptSeconds(
                    new Vector2(18f, 0f), 8.5f, new Vector2(10f, 0f), new Vector2(0f, 6f));

                Assert.That(intercepts[1], Is.EqualTo(expectedIntercept1).Within(TOLERANCE));
                Assert.That(intercepts[3], Is.EqualTo(expectedIntercept3).Within(TOLERANCE));
                Assert.That(float.IsPositiveInfinity(intercepts[0]), Is.True);
                Assert.That(float.IsPositiveInfinity(intercepts[2]), Is.True);
            }
            finally
            {
                players.Dispose();
                sides.Dispose();
                speeds.Dispose();
                distances.Dispose();
                intercepts.Dispose();
            }
        }
    }
}
