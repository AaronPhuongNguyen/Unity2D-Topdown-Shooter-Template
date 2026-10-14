using Server;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "Card Data Base", menuName = "Endless/DataBase/Card")]
public class CardDataBase : ScriptableObject
{
    public List<CardEffect> cards = new List<CardEffect>();

    // Safety cap so a pool full of near-zero rarities can never hang the game.
    // After this many rejected rolls the last candidate is accepted anyway.
    private const int MaxRerollsPerCard = 64;

    private static readonly List<CardEffect> _pool = new List<CardEffect>(32);

    /// <summary>
    /// Draws up to <paramref name="count"/> DISTINCT cards of the given category into
    /// <paramref name="result"/> (cleared first), using the game's seeded RNG.
    /// Each candidate is picked uniformly, then must pass a rarity roll
    /// (RNG.GetPercent() &lt; card.Rarity); if it fails, we reroll until one passes.
    /// Cards in <paramref name="exclude"/> (may be null) are never offered.
    /// If fewer distinct cards exist than requested, only those are returned.
    /// Returns how many were drawn.
    /// </summary>
    public int Draw(Category category, int count, List<CardEffect> result, HashSet<CardEffect> exclude = null)
    {
        result.Clear();
        _pool.Clear();

        for (int i = 0; i < cards.Count; i++)
        {
            CardEffect c = cards[i];
            if (c == null || c.Category != category) continue;
            if (exclude != null && exclude.Contains(c)) continue;
            if (_pool.Contains(c)) continue; // same asset listed twice in the database
            _pool.Add(c);
        }

        count = Mathf.Min(count, _pool.Count);
        for (int i = 0; i < count; i++)
        {
            int pick = 0;
            for (int attempt = 0; attempt < MaxRerollsPerCard; attempt++)
            {
                pick = RNG.GetInt(0, _pool.Count); // max is exclusive
                if (RNG.GetPercent() < _pool[pick].Rarity) break; // rarity accepted
            }

            result.Add(_pool[pick]);
            _pool.RemoveAt(pick); // rejected cards stay in the pool; accepted one can't repeat
        }
        return result.Count;
    }
}