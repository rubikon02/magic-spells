using UnityEngine;

[DisallowMultipleComponent]
public class LumosSpell : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Jesli puste, zostanie pobrane z tego samego obiektu (GetComponent).")]
    [SerializeField] private SpellRecognizer spellRecognizer;

    [Tooltip("Punkt startu wiazki = czubek rozdzki.")]
    [SerializeField] private Transform beamOrigin;

    [Tooltip("Widoczna wiazka (LineRenderer).")]
    [SerializeField] private LineRenderer beam;

    [Tooltip("Opcjonalne swiatlo oswietlajace tor wiazki (np. Spot Light).")]
    [SerializeField] private Light beamLight;

    [Header("Spell")]
    [Tooltip("Indeks zaklecia Lumos wg labels.json (lumos = 3).")]
    [SerializeField] private int lumosSpellIndex = 3;

    [Tooltip("Jak dlugo wiazka jest aktywna (sekundy).")]
    [SerializeField] private float duration = 0.5f;

    [Header("Beam shape")]
    [Tooltip("Lokalny kierunek wiazki wzgledem beamOrigin. Jesli wiazka idzie w zla strone, zmien os (np. 0,1,0 albo 1,0,0).")]
    [SerializeField] private Vector3 localBeamDirection = Vector3.forward;

    [Tooltip("Maksymalna dlugosc wiazki w metrach (gdy nie trafi w sciane).")]
    [SerializeField] private float maxBeamLength = 10f;

    [Tooltip("W co wiazka moze trafic (zatrzyma sie na scianie).")]
    [SerializeField] private LayerMask hitMask = ~0;

    [Tooltip("Szerokosc wiazki.")]
    [SerializeField] private float beamWidth = 0.08f;

    [Header("Impact (rozblysk na koncu)")]
    [Tooltip("Obiekt rozblysku w miejscu trafienia (np. swiecacy Quad/Sphere).")]
    [SerializeField] private Transform impactGlow;

    [Tooltip("Szybkosc pulsowania rozblysku.")]
    [SerializeField] private float impactPulseSpeed = 12f;

    [Tooltip("Sila pulsowania rozblysku (0 = brak).")]
    [SerializeField, Range(0f, 1f)] private float impactPulseAmount = 0.25f;

    private float _timer;
    private bool _active;
    private Vector3 _impactBaseScale = Vector3.one;

    private void Awake()
    {
        if (!spellRecognizer)
        {
            spellRecognizer = GetComponent<SpellRecognizer>();
        }

        if (!beamOrigin)
        {
            beamOrigin = transform;
        }

        if (beam)
        {
            beam.useWorldSpace = true;
            beam.positionCount = 2;
            beam.widthMultiplier = beamWidth;
        }

        if (impactGlow)
        {
            _impactBaseScale = impactGlow.localScale;
        }

        SetBeamActive(false);
    }

    private void OnEnable()
    {
        if (spellRecognizer)
        {
            spellRecognizer.OnSpellRecognized += HandleSpell;
        }
    }

    private void OnDisable()
    {
        if (spellRecognizer)
        {
            spellRecognizer.OnSpellRecognized -= HandleSpell;
        }
    }

    private void HandleSpell(string spellName, int spellIndex, float confidence)
    {
        if (spellIndex != lumosSpellIndex)
        {
            return;
        }

        CastBeam();
    }

    private void CastBeam()
    {
        _timer = duration;

        if (!_active)
        {
            _active = true;
            SetBeamActive(true);
        }

        Debug.Log($"[Lumos] Wiazka wlaczona na {duration:F0}s");
    }

    private void Update()
    {
        if (!_active)
        {
            return;
        }

        UpdateBeam();

        _timer -= Time.deltaTime;
        if (_timer <= 0f)
        {
            _active = false;
            SetBeamActive(false);
            Debug.Log("[Lumos] Wiazka zgaszona");
        }
    }

    private void UpdateBeam()
    {
        if (!beamOrigin)
        {
            return;
        }

        var start = beamOrigin.position;
        var dir = localBeamDirection.sqrMagnitude > 0.0001f
            ? beamOrigin.TransformDirection(localBeamDirection.normalized)
            : beamOrigin.forward;

        var end = start + dir * maxBeamLength;

        var didHit = Physics.Raycast(start, dir, out var hit, maxBeamLength, hitMask, QueryTriggerInteraction.Ignore);
        if (didHit)
        {
            end = hit.point;
        }

        if (beam)
        {
            beam.SetPosition(0, start);
            beam.SetPosition(1, end);
        }

        if (beamLight)
        {
            beamLight.transform.position = start;
            beamLight.transform.rotation = Quaternion.LookRotation(dir);
        }

        UpdateImpact(didHit, end, didHit ? hit.normal : -dir);
    }

    private void UpdateImpact(bool didHit, Vector3 point, Vector3 normal)
    {
        if (!impactGlow)
        {
            return;
        }

        if (!didHit)
        {
            if (impactGlow.gameObject.activeSelf)
            {
                impactGlow.gameObject.SetActive(false);
            }

            return;
        }

        if (!impactGlow.gameObject.activeSelf)
        {
            impactGlow.gameObject.SetActive(true);
        }

        impactGlow.position = point;
        impactGlow.rotation = Quaternion.LookRotation(normal);

        var pulse = 1f + impactPulseAmount * Mathf.Sin(Time.time * impactPulseSpeed);
        impactGlow.localScale = _impactBaseScale * pulse;
    }

    private void SetBeamActive(bool on)
    {
        if (beam)
        {
            beam.enabled = on;
        }

        if (beamLight)
        {
            beamLight.enabled = on;
        }

        if (!on && impactGlow && impactGlow.gameObject.activeSelf)
        {
            impactGlow.gameObject.SetActive(false);
        }
    }
}