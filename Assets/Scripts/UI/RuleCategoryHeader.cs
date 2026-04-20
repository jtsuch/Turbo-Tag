using TMPro;
using UnityEngine;

/// <summary>
/// A visual section divider spawned between RuleRowBase rows when the category changes.
/// Attach to: RuleCategoryHeader prefab — contains a bold label and a horizontal rule Image.
/// </summary>
public class RuleCategoryHeader : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Text headerLabel;

    public void Initialize(string category)
    {
        headerLabel.text = category;
    }
}
