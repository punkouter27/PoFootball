using UnityEngine;

namespace PoFootball.Views
{
    /// <summary>The closing half of Systems_TickTimer: runs after every other FixedUpdate.</summary>
    [DefaultExecutionOrder(32000)]
    [DisallowMultipleComponent]
    internal sealed class Systems_TickTimerEnd : MonoBehaviour
    {
        private Systems_TickTimer _timer;

        internal void Bind(Systems_TickTimer timer)
        {
            _timer = timer;
        }

        private void FixedUpdate()
        {
            if (_timer != null)
            {
                _timer.Close();
            }
        }
    }
}
