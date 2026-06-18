using System.Collections;
using UnityEngine;

namespace Spells
{
    [DisallowMultipleComponent]
    public class FireballSpell : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Jesli puste, zostanie pobrane z rodzica (GetComponentInParent).")]
        [SerializeField] private SpellRecognizer spellRecognizer;

        [Tooltip("Punkt startu ognia = czubek rozdzki.")]
        [SerializeField] private Transform launchOrigin;

        [Tooltip("Prefab efektu ognia wystrzeliwany z rozdzki.")]
        [SerializeField] private GameObject fireballPrefab;

        [Header("Spell")]
        [Tooltip("Indeks zaklecia Fireball wg SpellRecognizer (Fireball = 1).")]
        [SerializeField] private int fireballSpellIndex = 1;

        [Tooltip("Minimalna pewnosc rozpoznania wymagana do rzucenia zaklecia.")]
        [SerializeField, Range(0f, 100f)] private float minConfidence = 0f;

        [Tooltip("Odstep od czubka rozdzki, zeby efekt nie startowal wewnatrz modelu.")]
        [SerializeField] private float spawnOffset = 0.15f;

        [Tooltip("Skala wystrzeliwanego efektu fireballa.")]
        [SerializeField] private float fireballScale = 0.1f;

        [Tooltip("Czas wzrostu fireballa do pelnej skali.")]
        [SerializeField, Min(0f)] private float growthDuration = 1f;

        [Header("Projectile")]
        [Tooltip("Lokalny kierunek lotu wzgledem launchOrigin.")]
        [SerializeField] private Vector3 localLaunchDirection = Vector3.forward;

        [Tooltip("Predkosc lotu fireballa w metrach na sekunde.")]
        [SerializeField] private float speed = 8f;

        [Tooltip("Jak dlugo fireball zyje, jesli w nic nie trafi.")]
        [SerializeField] private float lifetime = 3f;

        [Tooltip("Maksymalny dystans lotu.")]
        [SerializeField] private float maxDistance = 20f;

        [Tooltip("W co fireball moze trafic.")]
        [SerializeField] private LayerMask hitMask = ~0;

        [Header("Impact")]
        [Tooltip("Czy zatrzymac fireball po trafieniu.")]
        [SerializeField] private bool stopOnHit = true;

        [Tooltip("Jak dlugo efekt zostaje po trafieniu.")]
        [SerializeField] private float impactLifetime = 1.25f;
        
        [Tooltip("Efekt odpalany w miejscu uderzenia fireballa.")]
        [SerializeField] private GameObject impactPrefab;


        [Tooltip("Skala efektu uderzenia.")]
        [SerializeField] private Vector3 impactScale = new Vector3(0.2f, 0.2f, 0.02f);

        private void Awake()
        {
            if (!spellRecognizer)
            {
                spellRecognizer = GetComponentInParent<SpellRecognizer>();
            }

            if (!launchOrigin)
            {
                launchOrigin = transform;
            }
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
            if (spellIndex != fireballSpellIndex || confidence < minConfidence)
            {
                return;
            }

            CastFireball();
        }

        private void CastFireball()
        {
            if (!fireballPrefab || !launchOrigin)
            {
                Debug.LogWarning("[Fireball] Brakuje fireballPrefab albo launchOrigin.");
                return;
            }

            var direction = GetLaunchDirection();
            var rotation = Quaternion.LookRotation(direction, launchOrigin.up);
            var position = launchOrigin.position + direction * spawnOffset;

            var fireball = Instantiate(fireballPrefab, position, rotation);
            fireball.name = "Fireball";
            var targetScale = fireball.transform.localScale * fireballScale;
            fireball.transform.localScale = Vector3.zero;

            PlayParticles(fireball, true);
            StartCoroutine(GrowFireball(fireball, targetScale));
            StartCoroutine(MoveFireball(fireball, direction));

            Debug.Log("[Fireball] Wystrzelony z rozdzki");
        }

        private IEnumerator GrowFireball(GameObject fireball, Vector3 targetScale)
        {
            if (growthDuration <= 0f)
            {
                if (fireball)
                {
                    fireball.transform.localScale = targetScale;
                }

                yield break;
            }

            var elapsed = 0f;
            while (fireball && elapsed < growthDuration)
            {
                elapsed += Time.deltaTime;
                var progress = Mathf.Clamp01(elapsed / growthDuration);
                progress = Mathf.SmoothStep(0f, 1f, progress);
                fireball.transform.localScale = Vector3.LerpUnclamped(Vector3.zero, targetScale, progress);
                yield return null;
            }

            if (fireball)
            {
                fireball.transform.localScale = targetScale;
            }
        }

        private Vector3 GetLaunchDirection()
        {
            if (localLaunchDirection.sqrMagnitude > 0.0001f)
            {
                return launchOrigin.TransformDirection(localLaunchDirection.normalized);
            }

            return launchOrigin.forward;
        }

        private IEnumerator MoveFireball(GameObject fireball, Vector3 direction)
        {
            var age = 0f;
            var distance = 0f;
            direction.Normalize();

            while (fireball && age < lifetime && distance < maxDistance)
            {
                var step = speed * Time.deltaTime;
                if (Physics.Raycast(fireball.transform.position, direction, out var hit, step, hitMask, QueryTriggerInteraction.Ignore))
                {
                    fireball.transform.position = hit.point;
                    fireball.transform.rotation = Quaternion.LookRotation(hit.normal);
                    HandleHit(fireball, hit);
                    yield break;
                }

                fireball.transform.position += direction * step;
                age += Time.deltaTime;
                distance += step;

                yield return null;
            }

            if (fireball)
            {
                Destroy(fireball);
            }
        }

        private void HandleHit(GameObject fireball, RaycastHit hit)
        {
            SpawnImpact(hit.point, hit.normal);

            if (!stopOnHit)
            {
                Destroy(fireball);
                return;
            }

            PlayParticles(fireball, false);
            Destroy(fireball, impactLifetime);
        }

        private void SpawnImpact(Vector3 position, Vector3 normal)
        {
            if (!impactPrefab)
            {
                return;
            }

            var rotation = normal.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(normal)
                : Quaternion.identity;
            var impact = Instantiate(impactPrefab, position, rotation);
            impact.name = impactPrefab.name;
            impact.transform.localScale = impactScale;

            PlayParticles(impact, true);
            // Destroy(impact, impactLifetime);
        }

        private static void PlayParticles(GameObject target, bool play)
        {
            foreach (var particle in target.GetComponentsInChildren<ParticleSystem>())
            {
                if (play)
                {
                    particle.Play(true);
                }
                else
                {
                    particle.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }
        }
    }
}
