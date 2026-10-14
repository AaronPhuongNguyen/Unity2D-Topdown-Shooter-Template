using UnityEngine;

/// <summary>
/// Picking this card spawns NPC(s) from the given package at the player's position.
/// </summary>
[CreateAssetMenu(fileName = "Spawn NPC Card", menuName = "Endless/Card/Spawn NPC")]
public class CardEffect_SpawnNpc : CardEffect
{
    [Header("NPC")]
    [Tooltip("The NPC to spawn (the package carries its own prefab).")]
    public NpcPackage npc;

    [Tooltip("How many NPCs to spawn when the card is picked.")]
    [Min(1)] public int count = 1;

    public override void DrawCard(UnitAttribute attribute)
    {
        if (npc == null)
        {
            Debug.LogWarning($"{name}: no NpcPackage assigned.");
            return;
        }

        for (int i = 0; i < count; i++)
            NpcSpawner.SpawnAtPlayer(npc);
    }
}