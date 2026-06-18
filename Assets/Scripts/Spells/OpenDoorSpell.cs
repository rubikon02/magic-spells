using UnityEngine;

namespace Spells
{
    [DisallowMultipleComponent]
    public class OpenDoorSpell : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private SpellRecognizer spellRecognizer;

        [Header("Spell")]
        [SerializeField] private int openDoorSpellIndex = 4;
        [SerializeField, Range(0f, 100f)] private float minConfidence = 0f;

        [Header("Door")]
        [SerializeField] private string doorObjectName = "PFB_DoorDouble";
        [SerializeField] private string doorBoolName = "isOpen_Obj_1";

        private Animator _doorAnimator;

        private void Awake()
        {
            if (!spellRecognizer)
            {
                spellRecognizer = GetComponentInParent<SpellRecognizer>();
            }

            var door = GameObject.Find(doorObjectName);
            if (door)
            {
                _doorAnimator = door.GetComponent<Animator>();
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
            if (spellIndex != openDoorSpellIndex || confidence < minConfidence)
            {
                return;
            }

            ToggleDoor();
        }

        private void ToggleDoor()
        {
            if (!_doorAnimator)
            {
                Debug.LogWarning("[OpenDoor] Nie znaleziono Animatora drzwi.");
                return;
            }

            _doorAnimator.enabled = true;

            bool isOpen = _doorAnimator.GetBool(doorBoolName);
            _doorAnimator.SetBool(doorBoolName, !isOpen);

            Debug.Log(!isOpen ? "[OpenDoor] Drzwi otwarte." : "[OpenDoor] Drzwi zamkniete.");
        }
    }
}