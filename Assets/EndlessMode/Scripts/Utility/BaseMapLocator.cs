using UnityEngine;

[DefaultExecutionOrder(100)]
public class MapLocator : MonoBehaviour
{
    [SerializeField] private Vector2 Original = Vector2.one;

    private void OnEnable()
    {
        Vector3 finalPos = Original * DomainManager.instance.mapSize/2;
        transform.position = finalPos;
    }
}