using System.Collections.Generic;
using UnityEngine;

namespace PenaltyShootout.Kernel
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class BallContactSensor : MonoBehaviour, IAttemptResettable
    {
        private readonly List<PendingContact> pendingContacts =
            new List<PendingContact>(8);
        private long attemptId;

        public int PendingContactCount => pendingContacts.Count;

        private void OnCollisionEnter(Collision collision)
        {
            var marker = ResolveContactMarker(collision);
            if (marker == null || marker.Kind == ContactKind.None)
            {
                return;
            }

            pendingContacts.Add(new PendingContact(marker.Kind, marker.GoalkeeperPart));
        }

        private ContactMarker ResolveContactMarker(Collision collision)
        {
            ContactMarker selected = null;
            var selectedPriority = -1;
            for (var index = 0; index < collision.contactCount; index++)
            {
                var otherCollider = collision.GetContact(index).otherCollider;
                if (otherCollider == null ||
                    otherCollider.transform == transform ||
                    otherCollider.transform.IsChildOf(transform))
                {
                    continue;
                }

                var candidate = otherCollider.GetComponentInParent<ContactMarker>();
                var priority = ContactPriority(candidate);
                if (priority > selectedPriority)
                {
                    selected = candidate;
                    selectedPriority = priority;
                }
            }

            return selected != null
                ? selected
                : collision.gameObject.GetComponentInParent<ContactMarker>();
        }

        private static int ContactPriority(ContactMarker marker)
        {
            if (marker == null)
            {
                return -1;
            }

            switch (marker.GoalkeeperPart)
            {
                case GoalkeeperContactPart.LeftGlove:
                case GoalkeeperContactPart.RightGlove:
                    return 5;
                case GoalkeeperContactPart.Arm:
                    return 4;
                case GoalkeeperContactPart.TorsoOrHead:
                    return 3;
                case GoalkeeperContactPart.Leg:
                    return 2;
                default:
                    return marker.Kind == ContactKind.None ? 0 : 1;
            }
        }

        public void Drain(ContactHistory history, float attemptTime)
        {
            for (var index = 0; index < pendingContacts.Count; index++)
            {
                var pending = pendingContacts[index];
                history.Record(pending.Kind, attemptTime, pending.GoalkeeperPart);
            }

            pendingContacts.Clear();
        }

        public void ResetForAttempt(long nextAttemptId, ulong seed)
        {
            attemptId = nextAttemptId;
            pendingContacts.Clear();
        }

        public bool ValidateReset(out string error)
        {
            if (pendingContacts.Count != 0)
            {
                error = $"Ball contact buffer was not cleared for attempt {attemptId}.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private readonly struct PendingContact
        {
            public readonly ContactKind Kind;
            public readonly GoalkeeperContactPart GoalkeeperPart;

            public PendingContact(
                ContactKind kind,
                GoalkeeperContactPart goalkeeperPart)
            {
                Kind = kind;
                GoalkeeperPart = goalkeeperPart;
            }
        }
    }
}
