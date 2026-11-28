using UnityEngine;

namespace JourneyThroughXR.Player
{
    /// <summary>
    /// Diagnostic : retient le dernier obstacle touché par le Character Controller (hors sol).
    /// Ajouté tout seul sur le XR Origin par Height Debug Readout.
    /// </summary>
    public class BodyHitRecorder : MonoBehaviour
    {
        public string LastName { get; private set; } = "";
        public float LastHeight { get; private set; }
        public float LastTime { get; private set; } = float.NegativeInfinity;

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit.normal.y > 0.7f) return; // le sol sous les pieds
            LastName = hit.collider.name;
            LastHeight = hit.point.y;
            LastTime = Time.time;
        }
    }
}
