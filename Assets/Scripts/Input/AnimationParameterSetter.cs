# nullable enable

using UnityEngine;

public class AnimationParameterSetter : MonoBehaviour
{
    [SerializeField] private Animator animator = default!;
    [SerializeField] private string parameterName = string.Empty;

    public void SetBool(bool value) => animator.SetBool(parameterName, value);
    public void SetInteger(int value) => animator.SetInteger(parameterName, value);
    public void SetFloat(float value) => animator.SetFloat(parameterName, value);
}
