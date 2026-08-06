using UnityEngine;
using UnityEngine.InputSystem;

namespace PenaltyShootout.Gameplay
{
    public static class PlayerPenaltyInputActionsV1
    {
        public static InputActionAsset Create()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            asset.name = "PenaltyGameplay";
            var map = new InputActionMap("Gameplay");
            map.AddAction(
                "AimPointer",
                InputActionType.PassThrough,
                "<Pointer>/delta");
            var aimKeyboard = map.AddAction(
                "AimKeyboard",
                InputActionType.Value);
            aimKeyboard.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");
            var shoot = map.AddAction("Shoot", InputActionType.Button);
            shoot.AddBinding("<Mouse>/leftButton");
            shoot.AddBinding("<Keyboard>/space");
            var curve = map.AddAction("Curve", InputActionType.Value);
            curve.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/q")
                .With("Positive", "<Keyboard>/e");
            map.AddAction(
                "Pause",
                InputActionType.Button,
                "<Keyboard>/escape");
            asset.AddActionMap(map);
            return asset;
        }
    }
}
