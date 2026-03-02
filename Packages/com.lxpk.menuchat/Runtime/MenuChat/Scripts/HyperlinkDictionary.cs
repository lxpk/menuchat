using System;
using UnityEngine;

namespace CardChat.UI
{
    /// <summary>
    /// ScriptableObject defining known hyperlink terms (card names, lore, rules keywords).
    /// Used for automatic link detection and for Info Popup (card text, rule text).
    /// </summary>
    [CreateAssetMenu(fileName = "HyperlinkDictionary", menuName = "TcgEngine/UI/Hyperlink Dictionary")]
    public class HyperlinkDictionary : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public string term;
            public string url;
            [TextArea(2, 6)]
            public string ruleText;
            public LinkType type = LinkType.Generic;

            public enum LinkType
            {
                Generic,
                Card,
                Rule,
                Lore
            }
        }

        public Entry[] entries = Array.Empty<Entry>();

        public bool TryGetEntry(string term, out Entry entry)
        {
            if (entries == null)
            {
                entry = default;
                return false;
            }
            string key = term?.Trim();
            if (string.IsNullOrEmpty(key))
            {
                entry = default;
                return false;
            }
            foreach (var e in entries)
            {
                if (string.Equals(e.term, key, StringComparison.OrdinalIgnoreCase))
                {
                    entry = e;
                    return true;
                }
            }
            entry = default;
            return false;
        }

        public string GetCardText(string cardName)
        {
            if (!TryGetEntry(cardName, out var e) || e.type != Entry.LinkType.Card)
                return null;
            return null; // Override or extend to resolve card data (e.g. from GameplayData)
        }

        public string GetRuleText(string keyword)
        {
            if (!TryGetEntry(keyword, out var e) || e.type != Entry.LinkType.Rule)
                return null;
            return !string.IsNullOrWhiteSpace(e.ruleText) ? e.ruleText : e.url;
        }
    }
}
