using UnityEngine;

namespace PenaltyShootout.Kernel
{
    [DisallowMultipleComponent]
    public sealed class ContactMarker : MonoBehaviour
    {
        [SerializeField]
        private ContactKind kind;

        [SerializeField]
        private GoalkeeperContactPart goalkeeperPart;

        public ContactKind Kind
        {
            get => kind;
            set => kind = value;
        }

        public GoalkeeperContactPart GoalkeeperPart
        {
            get => goalkeeperPart;
            set => goalkeeperPart = value;
        }
    }
}
