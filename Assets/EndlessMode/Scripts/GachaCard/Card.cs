using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GachaCard : MonoBehaviour
{
    // where Visual
    [Header("Properties")]
    public Button Button; // where logic is set
    public Image Visual; //where logo is set
    public TextMeshProUGUI _name; //where name is set
    public TextMeshProUGUI _description; // where description is set
    public Image Background;

    private CardEffect _effect;
    private Action<CardEffect> _onPicked;

    public void Setup(CardEffect effect, Color backgroundColor, Action<CardEffect> onPicked)
    {
        _effect = effect;
        _onPicked = onPicked;

        if (Visual != null) Visual.sprite = effect.Icon;
        if (_name != null) _name.text = effect.Name;
        if (_description != null) _description.text = effect.Description;
        if (Background != null) Background.color = backgroundColor;

        Button.onClick.RemoveAllListeners();
        Button.onClick.AddListener(OnClick);
    }

    private void OnClick() => _onPicked?.Invoke(_effect);

    private void OnDestroy()
    {
        if (Button != null) Button.onClick.RemoveAllListeners();
    }
}