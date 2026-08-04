using System;
using UnityEngine;

namespace PenaltyShootout.Kernel
{
    public static class GoalkeeperAuditContactPhysicsV1
    {
        public const string ContractId = "stage6-contact-candidate-v1";

        public static PhysicsMaterial CreateGloveMaterial(
            float bounciness,
            float friction)
        {
            if (!KernelMath.IsFinite(bounciness) ||
                !KernelMath.IsFinite(friction) ||
                bounciness < 0f || bounciness > 1f ||
                friction < 0f || friction > 1f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(bounciness),
                    "Audit contact parameters must be finite values in [0, 1].");
            }
            return new PhysicsMaterial("Stage6 Audit Glove Contact")
            {
                bounciness = bounciness,
                dynamicFriction = friction,
                staticFriction = friction,
                bounceCombine = PhysicsMaterialCombine.Maximum,
                frictionCombine = PhysicsMaterialCombine.Minimum,
            };
        }
    }
}
