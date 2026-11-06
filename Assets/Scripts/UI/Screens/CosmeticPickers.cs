using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// The cosmetic pickers: a ‹ choice › selector per slot (Hat, Back), "None" first, applied at once and remembered
/// (<see cref="Cosmetics.SetLocal"/>). Used on the main menu player card (one row per slot) and in the Lobby's
/// "Your penguin" card (both on one row).
/// </summary>
public static class CosmeticPickers
{
    public static void Build(Transform parent, Action<Cosmetics.Slot> changed, bool compact = false)
    {
        Transform rowParent = parent;
        if (compact)
        {
            var row = UIKit.Rect("Cosmetics", parent);
            UIKit.HStack(row, 18f, null, TextAnchor.MiddleLeft, expandHeight: false);
            UIKit.Size(row, height: 60f);
            rowParent = row;
        }
        foreach (Cosmetics.Slot slot in Enum.GetValues(typeof(Cosmetics.Slot)))
        {
            Transform line = rowParent;
            if (!compact)
            {
                var r = UIKit.Rect(slot + " Row", parent);
                UIKit.HStack(r, 14f, null, TextAnchor.MiddleCenter, expandHeight: false);
                UIKit.Size(r, height: 60f);
                line = r;
            }
            var label = UIKit.Text(line, slot.ToString(), 22f, UIKit.Font.Bold, UIKit.Theme.text, TextAlignmentOptions.MidlineRight);
            UIKit.Size(label, compact ? 56f : 70f, 56f);

            var items = Cosmetics.InSlot(slot);
            var names = new List<string> { "None" };
            var ids = new List<string> { Cosmetics.None };
            foreach (var item in items) { names.Add(item.Name); ids.Add(item.Id); }
            int index = Mathf.Max(0, ids.IndexOf(Cosmetics.Saved(slot)));
            var s = slot;
            UIKit.Selector(line, names.ToArray(), index, i =>
            {
                Cosmetics.SetLocal(s, ids[i]);
                changed?.Invoke(s);
            }, compact ? 200f : 340f);
        }
    }
}
