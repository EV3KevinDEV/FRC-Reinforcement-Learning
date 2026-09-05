using UnityEngine;

namespace MoSimRL
{
    // Receive before drivetrain physics and gameplay Update consumers. Recording
    // remains in the late bridge so rendering never determines input cadence.
    [DefaultExecutionOrder(-10000)]
    public sealed class RlRealtimeInputPump : MonoBehaviour
    {
        public RlEnvironmentController Bridge { get; set; }

        private void FixedUpdate() => Bridge?.ProcessRealtimeControls();
        private void Update() => Bridge?.ProcessRealtimeControls();
    }
}
