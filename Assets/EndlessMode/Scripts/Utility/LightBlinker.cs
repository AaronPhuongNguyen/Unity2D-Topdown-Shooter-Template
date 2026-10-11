using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Server;

[RequireComponent(typeof(Light2D))]
public class BlinkSwingLight2D : MonoBehaviour
{
    [Header("Blink")]
    [SerializeField] private float minInterval = 3f;
    [SerializeField] private float maxInterval = 6f;
    [SerializeField] private float blinkDuration = 0.15f;   // time the light is dimmed
    [SerializeField, Range(0f, 1f)] private float blinkMinIntensityPercent = 0f; // 0 = fully off
    [SerializeField] private bool doubleBlink = false;      // quick blink-blink

    [Header("Swing")]
    [SerializeField] private float swingAngle = 25f;        // degrees each side
    [SerializeField] private float swingSpeed = 1.5f;       // swings per ~2π seconds
    [SerializeField] private bool randomStartPhase = true;

    private Light2D _light;
    private float _baseIntensity;
    private float _baseZ;
    private float _phase;

    private void Awake()
    {
        _light = GetComponent<Light2D>();
        _baseIntensity = _light.intensity;
        _baseZ = transform.localEulerAngles.z;
        _phase = randomStartPhase ? RNG.GetFloat(0f, Mathf.PI * 2f) : 0f;
    }

    private void OnEnable()
    {
        StartCoroutine(BlinkLoop());
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        if (_light != null) _light.intensity = _baseIntensity;
    }

    private void Update()
    {
        // Smooth left -> right -> left -> right
        _phase += Time.deltaTime * swingSpeed;
        float angle = Mathf.Sin(_phase) * swingAngle;
        transform.localRotation = Quaternion.Euler(0f, 0f, _baseZ + angle);
    }

    private IEnumerator BlinkLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(RNG.GetFloat(minInterval, maxInterval));

            yield return Blink();
            if (doubleBlink)
            {
                yield return new WaitForSeconds(blinkDuration);
                yield return Blink();
            }
        }
    }

    private IEnumerator Blink()
    {
        _light.intensity = _baseIntensity * blinkMinIntensityPercent;
        yield return new WaitForSeconds(blinkDuration);
        _light.intensity = _baseIntensity;
    }
}