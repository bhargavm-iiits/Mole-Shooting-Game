using UnityEngine;

namespace MoleShot
{
    /// <summary>Marks the mole's trigger collider and points back at the owning hole.</summary>
    [RequireComponent(typeof(Collider))]
    public class MoleHitbox : MonoBehaviour
    {
        [SerializeField] HoleController hole;
        public HoleController Hole => hole;
    }
}
