using UnityEngine;

namespace Spells
{
    [DisallowMultipleComponent]
    public class WingardiumLeviosaSpell : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Jeœli puste, zostanie pobrane z nadrzêdnego obiektu.")]
        [SerializeField] private SpellRecognizer spellRecognizer;

        [Tooltip("Komponent Line Renderer dla wizualnego efektu czaru.")]
        [SerializeField] private LineRenderer laserLineRenderer;

        [Tooltip("Punkt startu czaru = czubek ró¿d¿ki (WandTip).")]
        [SerializeField] private Transform beamOrigin;

        [Header("Spell Settings")]
        [Tooltip("Indeks zaklêcia Wingardium Leviosa wg tablicy w SpellRecognizer (Wingardium Leviosa = 2).")]
        [SerializeField] private int levitationSpellIndex = 2;

        [Tooltip("Jak d³ugo widoczna jest linia czaru ³¹cz¹ca ró¿d¿kê z obiektem.")]
        [SerializeField] private float beamDuration = 0.5f;

        [Tooltip("Maksymalny zasiêg celowania czarem (w metrach).")]
        [SerializeField] private float maxCastDistance = 25f;

        [Tooltip("Warstwy, przez które promieñ czaru mo¿e przenikaæ lub na nich siê zatrzymywaæ.")]
        [SerializeField] private LayerMask hitMask = ~0;

        [Header("Impact (Opcjonalne rozb³yski)")]
        [Tooltip("Opcjonalny ma³y rozb³ysk w miejscu trafienia czaru.")]
        [SerializeField] private Transform impactGlow;

        [Header("Script Rotation Override")]
        [Tooltip("Czy skrypt ma automatycznie naprawiaæ k¹t strza³u w dó³ (jak w Lumos).")]
        [SerializeField] private bool overrideRotationInScript = true;

        [Tooltip("K¹t korekty rotacji ró¿d¿ki VR.")]
        [SerializeField] private Vector3 customRotationOffset = new Vector3(-51f, -3.62f, 0f);

        private float _beamTimer;
        private bool _isBeamActive;

        private void Awake()
        {
            // Wymuszamy aktywacjê obiektu czaru, by Awake wykona³o siê poprawnie na starcie gry
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
            if (spellIndex != levitationSpellIndex)
            {
                return;
            }

            CastLevitation();
        }

        // Pomocnicza metoda obliczaj¹ca skorygowany kierunek strza³u
        private Vector3 GetCorrectedDirection()
        {
            if (overrideRotationInScript && beamOrigin.localRotation == Quaternion.identity)
            {
                // Jeœli WandTip ma czyst¹ rotacjê 0,0,0, sztucznie aplikujemy prawid³owy obrót ró¿d¿ki VR
                Quaternion correctionQuaternion = Quaternion.Euler(customRotationOffset);

                // Jeœli beamOrigin ma rodzica (RightHandAnchor), wyliczamy kierunek w oparciu o niego
                if (beamOrigin.parent)
                {
                    return (beamOrigin.parent.rotation * correctionQuaternion) * Vector3.forward;
                }

                return correctionQuaternion * Vector3.forward;
            }

            // W przeciwnym wypadku u¿ywamy standardowej osi forward transformu
            return beamOrigin.forward;
        }

        private void CastLevitation()
        {
            if (!beamOrigin) return;

            Vector3 start = beamOrigin.position;
            Vector3 dir = GetCorrectedDirection(); // U¿ycie skorygowanego kierunku
            Vector3 end = start + dir * maxCastDistance;

            // Wypuszczamy promieñ magii w poszukiwaniu lewituj¹cego obiektu
            bool didHit = Physics.Raycast(start, dir, out var hit, maxCastDistance, hitMask, QueryTriggerInteraction.Ignore);

            if (didHit)
            {
                end = hit.point;

                // Szukamy komponentu LevitableObject na trafionym obiekcie (lub jego rodzicach)
                LevitableObject targetObject = hit.collider.GetComponent<LevitableObject>();
                if (!targetObject)
                {
                    targetObject = hit.collider.GetComponentInParent<LevitableObject>();
                }

                // Jeœli obiekt posiada nasz skrypt, aktywujemy jego lewitacjê!
                if (targetObject != null)
                {
                    targetObject.StartLevitation();
                    Debug.Log($"[Wingardium Leviosa] Pomyœlnie trafiono i aktywowano: {targetObject.gameObject.name}");
                }
                else
                {
                    Debug.Log($"[Wingardium Leviosa] Trafiono w obiekt {hit.collider.gameObject.name}, ale nie posiada on komponentu LevitableObject.");
                }
            }

            // W³¹czamy wizualny promieñ ³¹cz¹cy ró¿d¿kê z punktem trafienia/koñca
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
            // TYMCZASOWY TEST: U¿yj klawisza 'K' do symulacji rzucenia czaru ró¿d¿k¹
            if (Input.GetKeyDown(KeyCode.K))
            {
                Debug.Log("[Wingardium Leviosa] Test klawiszowy - rzucenie czaru");
                CastLevitation();
            }

            if (!_isBeamActive) return;

            // Aktualizujemy pozycjê linii, gdyby gracz porusza³ ró¿d¿k¹ w trakcie trwania b³ysku
            if (laserLineRenderer && beamOrigin)
            {
                laserLineRenderer.SetPosition(0, beamOrigin.position);

                // Pobieramy aktualn¹, skorygowan¹ liniê strza³u
                Vector3 start = beamOrigin.position;
                Vector3 dir = GetCorrectedDirection(); // U¿ycie skorygowanego kierunku
                Vector3 end = start + dir * maxCastDistance;

                if (Physics.Raycast(start, dir, out var hit, maxCastDistance, hitMask, QueryTriggerInteraction.Ignore))
                {
                    end = hit.point;
                    if (impactGlow)
                    {
                        impactGlow.position = end;
                        impactGlow.rotation = Quaternion.LookRotation(hit.normal);
                    }
                }
                laserLineRenderer.SetPosition(1, end);
            }

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