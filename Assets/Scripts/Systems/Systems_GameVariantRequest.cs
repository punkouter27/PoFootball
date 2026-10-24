using PoFootball.Models;
using UnityEngine;

namespace PoFootball.Systems
{
    /// <summary>
    /// Carries the menu's choice of game across the load into SCN_GAME, the way
    /// Systems_EpisodeSeed carries a requested seed and for the same reason: the
    /// menu that chose it is gone by the time the scope that builds the game reads
    /// it, and nothing but a static outlives the load between them. Taken, not
    /// read, so a QB challenge can never leak into the next ordinary game.
    /// </summary>
    public static class Systems_GameVariantRequest
    {
        private static Systems_GameVariant _requested = Systems_GameVariant.Standard;

        public static void Request(Systems_GameVariant variant)
        {
            _requested = variant;
        }

        /// <summary>The requested variant, or Standard when there is none. Clears it.</summary>
        public static Systems_GameVariant Take()
        {
            Systems_GameVariant variant = _requested;
            _requested = Systems_GameVariant.Standard;
            return variant;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            _requested = Systems_GameVariant.Standard;
        }
    }
}
