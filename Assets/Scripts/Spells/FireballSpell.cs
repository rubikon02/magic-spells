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

            PlayParticles(fireball, true);
            StartCoroutine(MoveFireball(fireball, direction));

            Debug.Log("[Fireball] Wystrzelony z rozdzki");
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
                    HandleHit(fireball);
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

        private void HandleHit(GameObject fireball)
        {
            if (!stopOnHit)
            {
                Destroy(fireball);
                return;
            }

            PlayParticles(fireball, false);
            Destroy(fireball, impactLifetime);
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
