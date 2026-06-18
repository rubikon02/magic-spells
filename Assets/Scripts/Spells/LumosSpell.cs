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

        [Tooltip("Jak dlugo wiazka jest aktywna.")]
        [SerializeField] private float duration = 3f;

        [Header("Beam shape")]
        [Tooltip("Maksymalna dlugosc wiazki w metrach.")]
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

        [Tooltip("Sila migotania (0 = stabilna wiazka).")]
        [SerializeField, Range(0f, 1f)] private float flickerAmount = 0.15f;

        [Tooltip("Szybkosc migotania.")]
        [SerializeField] private float flickerSpeed = 18f;

        [Tooltip("Predkosc przesuwania tekstury wzdluz wiazki. 0 = brak.")]
        [SerializeField] private float textureScrollSpeed = -2f;

        [Tooltip("Opcjonalny rozblysk u nasady (na czubku rozdzki).")]
        [SerializeField] private Transform originGlow;

        [Header("Impact")]
        [Tooltip("LineRenderer rysujacy kolko w miejscu trafienia.")]
        [SerializeField] private LineRenderer impactCircle;

        [Tooltip("Promien kolka (metry).")]
        [SerializeField] private float impactCircleRadius = 0.15f;

        [Tooltip("Swiatlo punktowe w miejscu trafienia.")]
        [SerializeField] private Light impactLight;

        [Tooltip("Jak dlugo trwa rozblysk przy pierwszym trafieniu (sekundy).")]
        [SerializeField] private float impactFlashDuration = 0.5f;

        [Tooltip("Ile razy jaśniejsze jest swiatlo w chwili trafienia.")]
        [SerializeField] private float impactFlashMultiplier = 4f;

        [Header("Debug (PC testing)")]
        [Tooltip("Klawisz do recznego odpalenia wiazki na komputerze.")]
        [SerializeField] private KeyCode debugKey = KeyCode.L;

        private float _timer;
        private bool _active;
        private Vector3 _originBaseScale = Vector3.one;
        private float _baseLightIntensity = 1f;
        private float _baseImpactLightIntensity = 3f;
        private float _flickerSeed;
        private Material _beamMaterial;
        private bool _wasHitting;
        private float _impactFlashTimer;

        private const int CircleSegments = 32;

        private void Awake()
        {
            if (!spellRecognizer)
                spellRecognizer = GetComponentInParent<SpellRecognizer>();

            if (!beamOrigin)
                beamOrigin = transform;

            if (beam)
            {
                beam.useWorldSpace = true;
                beam.positionCount = 2;
                beam.widthMultiplier = beamWidth;
                _beamMaterial = beam.material;
            }

            if (beamLight)
                _baseLightIntensity = beamLight.intensity;

            if (impactLight)
            {
                _baseImpactLightIntensity = impactLight.intensity;
                impactLight.enabled = false;
            }

            if (impactCircle)
            {
                impactCircle.useWorldSpace = true;
                impactCircle.loop = true;
                impactCircle.positionCount = CircleSegments;
                impactCircle.enabled = false;
            }

            if (originGlow)
                _originBaseScale = originGlow.localScale;

            _flickerSeed = Random.value * 100f;

            SetBeamActive(false);
        }

        private void OnEnable()
        {
            if (spellRecognizer)
                spellRecognizer.OnSpellRecognized += HandleSpell;
        }

        private void OnDisable()
        {
            if (spellRecognizer)
                spellRecognizer.OnSpellRecognized -= HandleSpell;
        }

        private void HandleSpell(string spellName, int spellIndex, float confidence)
        {
            if (spellIndex != lumosSpellIndex)
                return;

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
            if (Input.GetKeyDown(debugKey))
                CastBeam();

            if (!_active)
                return;

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
                level = Mathf.Clamp01(elapsed / fadeInDuration);
            else if (fadeOutDuration > 0f && _timer < fadeOutDuration)
                level = Mathf.Clamp01(_timer / fadeOutDuration);

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
                return;

            var start = beamOrigin.position;
            var cam = Camera.main;
            var dir = cam != null
                ? cam.transform.forward
                : beamOrigin.forward;

            var end = start + dir * maxBeamLength;

            var didHit = Physics.Raycast(start, dir, out var hit, maxBeamLength, hitMask, QueryTriggerInteraction.Ignore);
            if (didHit)
                end = hit.point;

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
                originGlow.localScale = _originBaseScale * level;

            UpdateImpact(didHit, end, didHit ? hit.normal : -dir, level);
        }

        private void UpdateImpact(bool didHit, Vector3 point, Vector3 normal, float level)
        {
            if (!didHit)
            {
                if (impactCircle && impactCircle.enabled) impactCircle.enabled = false;
                if (impactLight && impactLight.enabled) impactLight.enabled = false;
                _wasHitting = false;
                return;
            }

            if (!_wasHitting)
            {
                _impactFlashTimer = impactFlashDuration;
                _wasHitting = true;
            }

            _impactFlashTimer -= Time.deltaTime;
            var flashBoost = _impactFlashTimer > 0f
                ? Mathf.Clamp01(_impactFlashTimer / impactFlashDuration) * (impactFlashMultiplier - 1f)
                : 0f;

            if (impactCircle)
            {
                if (!impactCircle.enabled) impactCircle.enabled = true;
                DrawImpactCircle(point, normal, level);
            }

            if (impactLight)
            {
                impactLight.transform.position = point;
                if (!impactLight.enabled) impactLight.enabled = true;
                impactLight.intensity = _baseImpactLightIntensity * (1f + flashBoost) * level;
            }
        }

        private void DrawImpactCircle(Vector3 center, Vector3 normal, float level)
        {
            var tangent = Vector3.Cross(normal, Vector3.up);
            if (tangent.sqrMagnitude < 0.01f)
                tangent = Vector3.Cross(normal, Vector3.right);
            tangent.Normalize();
            var bitangent = Vector3.Cross(normal, tangent).normalized;

            var wallOffset = normal * 0.005f;
            for (var i = 0; i < CircleSegments; i++)
            {
                var angle = i * Mathf.PI * 2f / CircleSegments;
                var pos = center + wallOffset
                    + (Mathf.Cos(angle) * tangent + Mathf.Sin(angle) * bitangent) * impactCircleRadius;
                impactCircle.SetPosition(i, pos);
            }

            impactCircle.widthMultiplier = 0.025f * level;
        }

        private void SetBeamActive(bool on)
        {
            if (beam) beam.enabled = on;
            if (beamLight) beamLight.enabled = on;
            if (impactLight) impactLight.enabled = false;
            if (impactCircle) impactCircle.enabled = false;
            if (originGlow) originGlow.gameObject.SetActive(on);
            _wasHitting = false;
        }
    }
}
