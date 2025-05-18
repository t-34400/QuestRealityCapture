# nullable enable

using UnityEngine;
using UnityEngine.Events;

public class ButtonPressedUnityEvent : MonoBehaviour
{
    [SerializeField] private OVRInput.Button button = default!;
    [SerializeField] private OVRInput.Controller controller = default!;
    [SerializeField] private UnityEvent onButtonPressed = default!;

    void Update()
    {
        if (OVRInput.GetDown(button, controller))
        {
            onButtonPressed?.Invoke();
        }
    }
}
