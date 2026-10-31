using UnityEngine;

namespace SlopCity
{
    /// <summary>A physical hinged door whose swing never advances into the player capsule.</summary>
    public sealed class SlopCityDoor : MonoBehaviour
    {
        [SerializeField] private Transform leaf;
        [SerializeField] private Collider blocker;
        [SerializeField] private Vector3 interactionPoint;
        [SerializeField] private string displayName = "DOOR";
        [SerializeField] private float openAngle = 95f;
        [SerializeField] private Quaternion closedLocalRotation = Quaternion.identity;
        [SerializeField] private float degreesPerSecond = 110f;
        private CharacterController[] users;
        private float angle;
        private float targetAngle;

        public bool IsOpen => Mathf.Abs(angle) > 1f;
        public bool IsMoving => Mathf.Abs(targetAngle - angle) > 0.05f;
        public Vector3 InteractionPoint => leaf ? leaf.TransformPoint(interactionPoint) : transform.position;
        public string DisplayName => displayName;
        public Transform Leaf => leaf;
        public Collider Blocker => blocker;

        private void Awake()
        {
            // A new game always starts with physical doors shut, including authored
            // leaves that were left open while previewing the scene in the Editor.
            if (leaf) leaf.localRotation = closedLocalRotation;
            angle = targetAngle = 0f;
        }

        public void Configure(Transform leaf, Collider blocker, Vector3 interactionPoint, string label, float openAngle = 95f)
        {
            this.leaf = leaf;
            this.blocker = blocker;
            this.interactionPoint = leaf ? leaf.InverseTransformPoint(interactionPoint) : interactionPoint;
            displayName = string.IsNullOrWhiteSpace(label) ? "DOOR" : label;
            this.openAngle = Mathf.Clamp(Mathf.Abs(openAngle), 60f, 120f) * (openAngle < 0f ? -1f : 1f);
            closedLocalRotation = leaf ? leaf.localRotation : Quaternion.identity;
            angle = targetAngle = 0f;
        }

        public bool Toggle(Transform user)
        {
            if (!leaf || !blocker || IsMoving) return false;
            CharacterController capsule = user ? user.GetComponentInParent<CharacterController>() : null;
            users = capsule ? new[] { capsule } : new CharacterController[0];
            float destination = IsOpen ? 0f : openAngle;
            if (!IsOpen && user)
            {
                // Prefer the direction whose midpoint moves the leaf away from the
                // caller, so the same handle works from either side of the doorway.
                Vector3 center = blocker.bounds.center;
                Quaternion plus = WorldRotation(openAngle * 0.5f) * Quaternion.Inverse(leaf.rotation);
                Quaternion minus = WorldRotation(-openAngle * 0.5f) * Quaternion.Inverse(leaf.rotation);
                Vector3 a = leaf.position + plus * (center - leaf.position);
                Vector3 b = leaf.position + minus * (center - leaf.position);
                if ((b - user.position).sqrMagnitude > (a - user.position).sqrMagnitude) destination = -openAngle;
            }
            if (!HasClearSwing(destination)) return false;
            targetAngle = destination;
            return true;
        }

        private void Update()
        {
            if (!leaf || !blocker || !IsMoving || Time.deltaTime <= 0f) return;
            // Small substeps check the complete swept arc even after a slow frame.
            float remaining = degreesPerSecond * Time.deltaTime;
            while (remaining > 0f && IsMoving)
            {
                float step = Mathf.Min(remaining, 2f);
                float next = Mathf.MoveTowards(angle, targetAngle, step);
                if (TouchesUser(next)) { targetAngle = angle; break; }
                angle = next;
                leaf.localRotation = closedLocalRotation * Quaternion.Euler(0f, angle, 0f);
                remaining -= step;
            }
            Physics.SyncTransforms();
        }

        private bool HasClearSwing(float destination)
        {
            float probe = angle;
            while (Mathf.Abs(destination - probe) > 0.05f)
            {
                probe = Mathf.MoveTowards(probe, destination, 2f);
                if (TouchesUser(probe)) return false;
            }
            return true;
        }

        private Quaternion WorldRotation(float value)
        {
            Quaternion local = closedLocalRotation * Quaternion.Euler(0f, value, 0f);
            return leaf.parent ? leaf.parent.rotation * local : local;
        }

        private bool TouchesUser(float value)
        {
            if (users == null) return false;
            Quaternion delta = WorldRotation(value) * Quaternion.Inverse(leaf.rotation);
            Vector3 position = leaf.position + delta * (blocker.transform.position - leaf.position);
            Quaternion rotation = delta * blocker.transform.rotation;
            foreach (CharacterController user in users)
            {
                if (!user || !user.enabled || !user.gameObject.activeInHierarchy) continue;
                if (Physics.ComputePenetration(blocker, position, rotation, user,
                    user.transform.position, user.transform.rotation, out _, out float depth) && depth > 0.001f)
                    return true;
            }
            return false;
        }
    }
}
