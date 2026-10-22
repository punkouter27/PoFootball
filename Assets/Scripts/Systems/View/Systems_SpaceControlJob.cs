using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace PoFootball.Views
{
    /// <summary>
    /// One cell of the space-control map per Execute: which team gets there
    /// first, by how much, and how much the cell matters at all.
    ///
    /// THE MODEL IS DELIBERATELY THE SIMPLE ONE. A player is carried by his
    /// current velocity for <see cref="ReactionSeconds"/> — he cannot turn a body
    /// moving at seven metres a second instantly — and then runs straight at the
    /// cell at his top speed. That is the core of the pitch-control models the
    /// broadcast stats use, without the acceleration curve and the probability
    /// integral on top, both of which would cost more than the twenty-two
    /// distance reads per cell this does and would not change which colour a cell
    /// is drawn. It is a picture of the simulation's own numbers: the same
    /// positions, velocities and trait-scaled top speeds the physics step used.
    ///
    /// WHY THE ALPHA FALLS OFF WITH DISTANCE TO EVERYBODY. Control of the far
    /// corner of the field is real and irrelevant: it is wherever the nearest
    /// safety happens to be standing, and painting it would cover the whole field
    /// in one team's colour and teach the eye to ignore the overlay. A cell that
    /// nobody can reach for several seconds fades out, so what is left is the
    /// contested ground around the play.
    ///
    /// Burst, parallel, and writing straight into the pixel buffer the texture is
    /// filled from, so the whole thing is a few thousand cells times twenty-two
    /// players of arithmetic and no managed work at all.
    /// </summary>
    [BurstCompile]
    internal struct Systems_SpaceControlJob : IJobParallelFor
    {
        /// <summary>Side value for a slot with no player in it.</summary>
        public const int ABSENT = -1;

        /// <summary>Position in xy, velocity in zw, world metres.</summary>
        [ReadOnly] public NativeArray<float4> Players;

        /// <summary>Metres per second, trait-scaled.</summary>
        [ReadOnly] public NativeArray<float> TopSpeeds;

        /// <summary>0 offense, 1 defense, <see cref="ABSENT"/> for an empty slot.</summary>
        [ReadOnly] public NativeArray<int> Sides;

        public int Width;

        /// <summary>World position of the centre of cell (0, 0).</summary>
        public float2 Origin;

        public float2 CellSize;

        public float ReactionSeconds;

        /// <summary>A lead this many seconds or more is full colour.</summary>
        public float SaturateSeconds;

        /// <summary>Cells somebody reaches sooner than this are drawn at full strength.</summary>
        public float NearSeconds;

        /// <summary>Cells nobody reaches before this are not drawn at all.</summary>
        public float FarSeconds;

        public float MaxAlpha;

        public float3 OffenseColor;
        public float3 DefenseColor;

        [WriteOnly] public NativeArray<Color32> Pixels;

        public void Execute(int index)
        {
            int column = index % Width;
            int row = index / Width;

            float2 cell = Origin + (new float2(column, row) * CellSize);

            float offenseTime = float.MaxValue;
            float defenseTime = float.MaxValue;

            for (int slotIndex = 0; slotIndex < Sides.Length; slotIndex++)
            {
                int side = Sides[slotIndex];

                if (side == ABSENT)
                {
                    continue;
                }

                float4 player = Players[slotIndex];
                float2 carried = player.xy + (player.zw * ReactionSeconds);
                float time = ReactionSeconds
                    + (math.distance(cell, carried) / math.max(TopSpeeds[slotIndex], 0.1f));

                if (side == 0)
                {
                    offenseTime = math.min(offenseTime, time);
                }
                else
                {
                    defenseTime = math.min(defenseTime, time);
                }
            }

            if (offenseTime == float.MaxValue || defenseTime == float.MaxValue)
            {
                Pixels[index] = default;
                return;
            }

            float earliest = math.min(offenseTime, defenseTime);

            // Positive: the offense is there first.
            float lead = math.clamp((defenseTime - offenseTime) / SaturateSeconds, -1f, 1f);
            float strength = math.smoothstep(0f, 1f, math.abs(lead));
            float proximity = 1f - math.smoothstep(NearSeconds, FarSeconds, earliest);
            float alpha = MaxAlpha * strength * proximity;

            // THE COLOUR IS WRITTEN EVEN WHERE THE ALPHA IS ZERO. The texture is
            // straight alpha and bilinear: a transparent texel left black would be
            // averaged into its coloured neighbour and draw a dark fringe round
            // every patch of colour. A transparent texel in the right team colour
            // filters to the same colour, fainter, which is the edge wanted.
            float3 color = lead >= 0f ? OffenseColor : DefenseColor;

            Pixels[index] = new Color32(
                (byte)(math.saturate(color.x) * 255f),
                (byte)(math.saturate(color.y) * 255f),
                (byte)(math.saturate(color.z) * 255f),
                (byte)(math.saturate(alpha) * 255f));
        }
    }
}
