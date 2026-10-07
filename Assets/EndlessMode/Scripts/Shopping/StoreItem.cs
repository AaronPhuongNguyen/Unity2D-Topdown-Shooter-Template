using UnityEngine;

public abstract class GameItem:ScriptableObject
{
    public int Price;
    public int Quantity;

    public abstract void Purchase();
    public abstract void Sell();
}