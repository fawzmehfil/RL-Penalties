using System;
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
        private Rigidbody body;
        private long attemptId;

        public int PendingContactCount => pendingContacts.Count;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
        }

        private void OnCollisionEnter(Collision collision)
        {
            var marker = ResolveContactMarker(collision, out var contact);
            if (marker == null || marker.Kind == ContactKind.None)
            {
                return;
            }

            if (body == null)
            {
                body = GetComponent<Rigidbody>();
            }

            pendingContacts.Add(
                new PendingContact(
                    marker.Kind,
                    marker.GoalkeeperPart,
                    contact.otherCollider == null
                        ? null
                        : contact.otherCollider
                            .GetComponentInParent<GloveContactSurfaceV1>(),
                    new ContactKinematics
                    {
                        HasValue = collision.contactCount > 0,
                        PointWorld = contact.point,
                        NormalWorld = contact.normal,
                        ImpulseWorld = collision.impulse,
                        RelativeVelocityWorld = collision.relativeVelocity,
                        BallVelocityWorld = body == null
                            ? Vector3.zero
                            : body.linearVelocity,
                    }));
        }

        private ContactMarker ResolveContactMarker(
            Collision collision,
            out ContactPoint selectedContact)
        {
            ContactMarker selected = null;
            selectedContact = default;
            var selectedPriority = -1;
            for (var index = 0; index < collision.contactCount; index++)
            {
                var contact = collision.GetContact(index);
                var otherCollider = contact.otherCollider;
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
                    selectedContact = contact;
                    selectedPriority = priority;
                }
            }

            if (selected != null)
            {
                return selected;
            }

            if (collision.contactCount > 0)
            {
                selectedContact = collision.GetContact(0);
            }

            return collision.gameObject.GetComponentInParent<ContactMarker>();
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

        public void Drain(
            ContactHistory history,
            float attemptTime,
            Action<BallContactEventV1> contactCallback = null)
        {
            for (var index = 0; index < pendingContacts.Count; index++)
            {
                var pending = pendingContacts[index];
                history.Record(
                    pending.Kind,
                    attemptTime,
                    pending.GoalkeeperPart,
                    pending.Kinematics);
                contactCallback?.Invoke(new BallContactEventV1(
                    pending.Kind,
                    pending.GoalkeeperPart,
                    pending.Kinematics,
                    pending.GloveSurface));
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
            public readonly GloveContactSurfaceV1 GloveSurface;
            public readonly ContactKinematics Kinematics;

            public PendingContact(
                ContactKind kind,
                GoalkeeperContactPart goalkeeperPart,
                GloveContactSurfaceV1 gloveSurface,
                ContactKinematics kinematics)
            {
                Kind = kind;
                GoalkeeperPart = goalkeeperPart;
                GloveSurface = gloveSurface;
                Kinematics = kinematics;
            }
        }
    }
}
