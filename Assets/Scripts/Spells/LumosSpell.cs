using UnityEngine;

namespace Spells
{
    [DisallowMultipleComponent]
    public class LumosSpell : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Jesli puste, zostanie pobrane z tego samego obiektu (GetComponent).")]
        [SerializeField] private SpellRecognizer spellRecognizer;

        [Tooltip("Punkt startu wiazki = czubek rozdzki.")]
        [SerializeField] private Transform beamOrigin;

        [Tooltip("Widoczna wiazka.")]
        [SerializeField] private LineRenderer beam;

        [Tooltip("Opcjonalne swiatlo oswietlajace tor wiazki.")]
        [SerializeField] private Light beamLight;

        [Header("Spell")]
        [Tooltip("Indeks zaklecia Lumos wg labels.json (lumos = 3).")]
        [SerializeField] private int lumosSpellIndex = 3;


        [Tooltip("Jak dlugo wiazka jest aktywna ")]
        [SerializeField] private float duration = 3f;

        [Header("Beam shape")]
        [Tooltip("Lokalny kierunek wiazki wzgledem beamOrigin.")]
        [SerializeField] private Vector3 localBeamDirection = Vector3.forward;

        [Tooltip("Maksymalna dlugosc wiazki w metrach (gdy nie trafi w sciane).")]
        [SerializeField] private float maxBeamLength = 10f;

        [Tooltip("W co wiazka moze trafic (zatrzyma sie na scianie).")]
        [SerializeField] private LayerMask hitMask = ~0;

        [Tooltip("Szerokosc wiazki.")]
        [SerializeField] private float beamWidth = 0.04f;

        [Header("Look & feel")]
        [Tooltip("Czas plynnego zapalania wiazki (sekundy).")]
        [SerializeField] private float fadeInDuration = 0.25f;

        [Tooltip("Czas plynnego gaszenia wiazki (sekundy).")]
        [SerializeField] private float fadeOutDuration = 0.5f;

        [Tooltip("Sila migotania (0 = stabilna wiazka, 0.2 = lekko zywa).")]
        [SerializeField, Range(0f, 1f)] private float flickerAmount = 0.15f;

        [Tooltip("Szybkosc migotania.")]
        [SerializeField] private float flickerSpeed = 18f;

        [Tooltip("Predkosc przesuwania tekstury wzdluz wiazki (przeplyw energii). 0 = brak.")]
        [SerializeField] private float textureScrollSpeed = -2f;

        [Header("Impact (rozblysk na koncu)")]
        [Tooltip("Obiekt rozblysku w miejscu trafienia (np. swiecacy Quad/Sphere).")]
        [SerializeField] private Transform impactGlow;

        [Tooltip("Szybkosc pulsowania rozblysku.")]
        [SerializeField] private float impactPulseSpeed = 12f;

        [Tooltip("Sila pulsowania rozblysku (0 = brak).")]
        [SerializeField, Range(0f, 1f)] private float impactPulseAmount = 0.25f;

        [Tooltip("Opcjonalny rozblysk u nasady (na czubku rozdzki).")]
        [SerializeField] private Transform originGlow;

        private float _timer;
        private bool _active;
        private Vector3 _impactBaseScale = Vector3.one;
        private Vector3 _originBaseScale = Vector3.one;
        private float _baseLightIntensity = 1f;
        private float _flickerSeed;
        private Material _beamMaterial;

        private void Awake()
        {
            if (!spellRecognizer)
            {
                spellRecognizer = GetComponentInParent<SpellRecognizer>();
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
                _beamMaterial = beam.material;
            }

            if (beamLight)
            {
                _baseLightIntensity = beamLight.intensity;
            }

            if (impactGlow)
            {
                _impactBaseScale = impactGlow.localScale;
            }

            if (originGlow)
            {
                _originBaseScale = originGlow.localScale;
            }

            _flickerSeed = Random.value * 100f;

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

        private float CurrentEnvelope()
        {
            var elapsed = duration - _timer;

            var level = 1f;
            if (fadeInDuration > 0f && elapsed < fadeInDuration)
            {
                level = Mathf.Clamp01(elapsed / fadeInDuration);
            }
            else if (fadeOutDuration > 0f && _timer < fadeOutDuration)
            {
                level = Mathf.Clamp01(_timer / fadeOutDuration);
            }

            if (flickerAmount > 0f)
            {
                var noise = Mathf.PerlinNoise(_flickerSeed, Time.time * flickerSpeed);
                level *= 1f - flickerAmount * noise;
            }

            return Mathf.Clamp01(level);
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

            var level = CurrentEnvelope();

            if (beam)
            {
                beam.SetPosition(0, start);
                beam.SetPosition(1, end);
                beam.widthMultiplier = beamWidth * level;

                if (_beamMaterial && textureScrollSpeed != 0f)
                {
                    var offset = _beamMaterial.mainTextureOffset;
                    offset.x = Time.time * textureScrollSpeed;
                    _beamMaterial.mainTextureOffset = offset;
                }
            }

            if (beamLight)
            {
                beamLight.transform.position = start;
                beamLight.transform.rotation = Quaternion.LookRotation(dir);
                beamLight.intensity = _baseLightIntensity * level;
            }

            if (originGlow)
            {
                originGlow.localScale = _originBaseScale * level;
            }

            UpdateImpact(didHit, end, didHit ? hit.normal : -dir, level);
        }

        private void UpdateImpact(bool didHit, Vector3 point, Vector3 normal, float level)
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
            impactGlow.localScale = _impactBaseScale * pulse * level;
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

            if (originGlow)
            {
                originGlow.gameObject.SetActive(on);
            }

            if (!on && impactGlow && impactGlow.gameObject.activeSelf)
            {
                impactGlow.gameObject.SetActive(false);
            }
        }
    }
}