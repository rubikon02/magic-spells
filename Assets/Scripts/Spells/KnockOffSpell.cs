using UnityEngine;

namespace Spells
{
    [DisallowMultipleComponent]
    public class KnockOffSpell : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Jeœli puste, zostanie pobrane z nadrzêdnego obiektu.")]
        [SerializeField] private SpellRecognizer spellRecognizer;

        [Tooltip("Komponent Line Renderer dla b³ysku uderzenia.")]
        [SerializeField] private LineRenderer laserLineRenderer;

        [Tooltip("Punkt startu czaru = czubek ró¿d¿ki (WandTip).")]
        [SerializeField] private Transform beamOrigin;

        [Header("Spell Settings")]
        [Tooltip("Indeks zaklêcia Knock Off wg tablicy w SpellRecognizer (Knock Off = 5).")]
        [SerializeField] private int knockOffSpellIndex = 5;

        [Tooltip("Si³a uderzenia/pchniêcia obiektu.")]
        [SerializeField] private float hitForce = 15f;

        [Tooltip("Dodatkowa si³a skierowana w górê, aby obiekt ³adnie podskoczy³ przy str¹ceniu.")]
        [SerializeField] private float upwardModifier = 2f;

        [Tooltip("Jak d³ugo widoczna jest linia strza³u.")]
        [SerializeField] private float beamDuration = 0.2f;

        [Tooltip("Maksymalny zasiêg czaru.")]
        [SerializeField] private float maxCastDistance = 25f;

        [Tooltip("Warstwy, w które czar mo¿e trafiæ.")]
        [SerializeField] private LayerMask hitMask = ~0;

        [Header("Impact")]
        [Tooltip("Opcjonalny rozb³ysk w miejscu uderzenia.")]
        [SerializeField] private Transform impactGlow;

        private float _beamTimer;
        private bool _isBeamActive;

        private void Awake()
        {
            gameObject.SetActive(true);
            if (beamOrigin) beamOrigin.gameObject.SetActive(true);

            if (!spellRecognizer)
            {
                spellRecognizer = GetComponentInParent<SpellRecognizer>();
            }

            if (!beamOrigin)
            {
                beamOrigin = transform;
            }

            if (laserLineRenderer)
            {
                laserLineRenderer.useWorldSpace = true;
                laserLineRenderer.positionCount = 2;
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
            if (spellIndex != knockOffSpellIndex)
            {
                return;
            }

            CastKnockOff();
        }

        private void CastKnockOff()
        {
            if (!beamOrigin) return;

            Vector3 start = beamOrigin.position;
            Vector3 dir = beamOrigin.forward;
            Vector3 end = start + dir * maxCastDistance;

            bool didHit = Physics.Raycast(start, dir, out var hit, maxCastDistance, hitMask, QueryTriggerInteraction.Ignore);

            if (didHit)
            {
                end = hit.point;

                // Szukamy komponentu Rigidbody na trafionym obiekcie lub jego rodzicach
                Rigidbody rb = hit.collider.GetComponent<Rigidbody>();
                if (!rb)
                {
                    rb = hit.collider.GetComponentInParent<Rigidbody>();
                }
                // Zast¹p powy¿szy blok tym pancernej wersj¹:
                if (rb != null)
                {
                    // WYMUSZAMY ODBLOKOWANIE FIZYKI PRZED STR¥CENIEM:
                    if (rb.isKinematic)
                    {
                        rb.isKinematic = false;
                    }

                    // Obliczamy wektor si³y: kierunek czaru + lekka modyfikacja w górê
                    Vector3 forceVector = dir * hitForce + Vector3.up * upwardModifier;

                    // Aplikujemy si³ê dok³adnie w miejscu, w które trafi³ promieñ
                    rb.AddForceAtPosition(forceVector, hit.point, ForceMode.Impulse);

                    Debug.Log($"[Knock Off] Str¹cono obiekt: {rb.gameObject.name} z si³¹ {hitForce}");
                }
                else
                {
                    Debug.Log($"[Knock Off] Trafiono w {hit.collider.gameObject.name}, ale obiekt nie ma komponentu Rigidbody.");
                }

            }

            TriggerVisualBeam(start, end, didHit, hit.normal);
        }

        private void TriggerVisualBeam(Vector3 start, Vector3 end, bool didHit, Vector3 hitNormal)
        {
            _beamTimer = beamDuration;
            _isBeamActive = true;

            if (laserLineRenderer)
            {
                laserLineRenderer.enabled = true;
                laserLineRenderer.SetPosition(0, start);
                laserLineRenderer.SetPosition(1, end);
            }

            if (impactGlow && didHit)
            {
                impactGlow.gameObject.SetActive(true);
                impactGlow.position = end;
                impactGlow.rotation = Quaternion.LookRotation(hitNormal);
            }
        }

        private void Update()
        {
            // TYMCZASOWY TEST: U¿ywamy klawisza 'J' na klawiaturze do testowania bez gogli
            if (Input.GetKeyDown(KeyCode.J))
            {
                Debug.Log("[Knock Off] Test klawiszowy - rzucenie czaru str¹caj¹cego");
                CastKnockOff();
            }

            if (!_isBeamActive) return;

            _beamTimer -= Time.deltaTime;
            if (_beamTimer <= 0f)
            {
                _isBeamActive = false;
                SetBeamActive(false);
            }
        }

        private void SetBeamActive(bool on)
        {
            if (laserLineRenderer) laserLineRenderer.enabled = on;
            if (impactGlow) impactGlow.gameObject.SetActive(false);
        }
    }
}