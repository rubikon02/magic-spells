using UnityEngine;

namespace Spells
{
    [DisallowMultipleComponent]
    public class LightningSpell : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Jeœli puste, zostanie pobrane z nadrzêdnego obiektu.")]
        [SerializeField] private SpellRecognizer spellRecognizer;

        [Tooltip("Komponent Line Renderer znajduj¹cy siê na obiekcie WandTip.")]
        [SerializeField] private LineRenderer lightningLineRenderer;

        [Tooltip("Punkt startu b³yskawicy = czubek ró¿d¿ki (WandTip).")]
        [SerializeField] private Transform beamOrigin;

        [Header("Spell Settings")]
        [Tooltip("Indeks zaklêcia Lightning wg tablicy w SpellRecognizer (Lightning = 0).")]
        [SerializeField] private int lightningSpellIndex = 0;

        [Tooltip("Jak d³ugo piorun pozostaje aktywny na ekranie.")]
        [SerializeField] private float duration = 0.8f;

        [Tooltip("Maksymalna d³ugoœæ pioruna w metrach (gdy nie trafi w ¿aden obiekt).")]
        [SerializeField] private float maxBeamLength = 20f;

        [Tooltip("Warstwy, w które piorun mo¿e trafiæ.")]
        [SerializeField] private LayerMask hitMask = ~0;

        [Header("Lightning Shape (Zygzak)")]
        [Tooltip("Na ile odcinków ma byæ podzielony piorun. Wiêcej = bardziej szczegó³owy zygzak.")]
        [SerializeField] private int segments = 12;

        [Tooltip("Maksymalne odchylenie linii pioruna od osi prostej (w metrach).")]
        [SerializeField] private float displacementAmount = 0.3f;

        [Header("Look & Feel")]
        [Tooltip("Szybkoœæ zmian kszta³tu zygzaka. Wy¿sza wartoœæ = bardziej drapie¿ne migotanie.")]
        [SerializeField] private float JitterSpeed = 25f;

        [Header("Impact (Rozb³ysk)")]
        [Tooltip("Opcjonalny obiekt rozb³ysku w miejscu uderzenia pioruna.")]
        [SerializeField] private Transform impactGlow;

        [Tooltip("Opcjonalny rozb³ysk na czubku ró¿d¿ki.")]
        [SerializeField] private Transform originGlow;

        private float _timer;
        private bool _active;
        private float _jitterSeed;

        private void Awake()
        {
            // Wymuszamy aktywacjê nas samych oraz wszystkich obiektów dzieci (WandTip, Impact)
            gameObject.SetActive(true);
            if (beamOrigin) beamOrigin.gameObject.SetActive(true);
            if (impactGlow) impactGlow.gameObject.SetActive(true);

            if (!spellRecognizer)
            {
                spellRecognizer = GetComponentInParent<SpellRecognizer>();
            }

            if (!beamOrigin)
            {
                beamOrigin = transform;
            }

            if (lightningLineRenderer)
            {
                lightningLineRenderer.useWorldSpace = true;
            }

            SetLightningActive(false);
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
            if (spellIndex != lightningSpellIndex)
            {
                return;
            }

            CastLightning();
        }

        private void CastLightning()
        {
            _timer = duration;
            _jitterSeed = Random.value * 100f;

            if (!_active)
            {
                _active = true;
                SetLightningActive(true);
            }

            Debug.Log($"[Lightning] Piorun aktywowany na {duration}s");
        }

        private void Update()
        {
            // TYMCZASOWY TEST: Naciœnij klawisz L na klawiaturze, aby wywo³aæ piorun
            if (Input.GetKeyDown(KeyCode.L))
            {
                Debug.Log("Test lightning - nacisnieto klawisz L");
                CastLightning();
            }

            if (!_active) return;

            UpdateLightning();

            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                _active = false;
                SetLightningActive(false);
            }
        }

        private void UpdateLightning()
        {
            if (!beamOrigin || !lightningLineRenderer) return;

            Vector3 start = beamOrigin.position;
            Vector3 dir = beamOrigin.forward;
            Vector3 end = start + dir * maxBeamLength;

            bool didHit = Physics.Raycast(start, dir, out var hit, maxBeamLength, hitMask, QueryTriggerInteraction.Ignore);
            if (didHit)
            {
                end = hit.point;
            }

            GenerateLightningPath(start, end);

            if (originGlow)
            {
                originGlow.position = start;
            }

            if (impactGlow)
            {
                if (didHit)
                {
                    if (!impactGlow.gameObject.activeSelf) impactGlow.gameObject.SetActive(true);
                    impactGlow.position = end;
                    impactGlow.rotation = Quaternion.LookRotation(hit.normal);
                }
                else
                {
                    if (impactGlow.gameObject.activeSelf) impactGlow.gameObject.SetActive(false);
                }
            }
        }

        private void GenerateLightningPath(Vector3 start, Vector3 end)
        {
            int vertexCount = Mathf.Max(2, segments + 1);
            lightningLineRenderer.positionCount = vertexCount;

            lightningLineRenderer.SetPosition(0, start);
            lightningLineRenderer.SetPosition(vertexCount - 1, end);

            Vector3 mainAxis = end - start;
            Vector3 upDir = Vector3.Cross(mainAxis, Vector3.forward).normalized;
            if (upDir.sqrMagnitude < 0.001f) upDir = Vector3.up;
            Vector3 rightDir = Vector3.Cross(mainAxis, upDir).normalized;

            float timeOffset = Time.time * JitterSpeed + _jitterSeed;

            for (int i = 1; i < vertexCount - 1; i++)
            {
                float fraction = (float)i / (vertexCount - 1);
                Vector3 pointOnStraightLine = Vector3.Lerp(start, end, fraction);

                float noiseX = Mathf.PerlinNoise(fraction * 10f, timeOffset) * 2f - 1f;
                float noiseY = Mathf.PerlinNoise(fraction * 10f + 50f, timeOffset) * 2f - 1f;

                float envelope = Mathf.Sin(fraction * Mathf.PI);

                Vector3 displacement = (rightDir * noiseX + upDir * noiseY) * displacementAmount * envelope;

                lightningLineRenderer.SetPosition(i, pointOnStraightLine + displacement);
            }
        }

        private void SetLightningActive(bool on)
        {
            if (lightningLineRenderer) lightningLineRenderer.enabled = on;
            if (originGlow) originGlow.gameObject.SetActive(on);
            if (impactGlow && !on) impactGlow.gameObject.SetActive(false);
        }
    }
}