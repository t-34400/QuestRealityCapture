# nullable enable

using UnityEngine;
using UnityEngine.Events;

public class ToggleValueUnityEvent : MonoBehaviour
{
    [SerializeField] private bool currentValue = false;
    [SerializeField] private UnityEvent<bool> valueChanged = default!;
    [SerializeField] private UnityEvent activated = default!;
    [SerializeField] private UnityEvent deactivated = default!;
    
    public bool Value
    {
        get => currentValue;
        set
        {
            if (currentValue != value)
            {
                currentValue = value;
                valueChanged.Invoke(currentValue);

                if (currentValue)
                {
                    activated.Invoke();
                }
                else
                {
                    deactivated.Invoke();
                }
            }
        }
    }

    public void ChangeValue()
    {
        Value = !Value;
    }
}
