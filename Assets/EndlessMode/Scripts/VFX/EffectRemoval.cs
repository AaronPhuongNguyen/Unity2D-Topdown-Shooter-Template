using UnityEngine;

public class EffectRemoval : MonoBehaviour
{
    private void OnDisable()
    {
       if(gameObject.activeSelf) PoolingSystem.instance.RemoveToPool(this.gameObject);
    }
}