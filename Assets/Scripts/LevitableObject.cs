using UnityEngine;

[SelectionBase]
public class LevitableObject : MonoBehaviour
{
    enum LevitationState
    {
        Idle,       // Obiekt stoi na ziemi
        Rising,     // Obiekt p³ynnie leci w górê
        Hovering,   // Obiekt unosi siê w powietrzu (ko³ysze siê)
        Falling     // Obiekt wraca na swoje miejsce startowe
    }

    [Header("Levitation Settings")]
    [Tooltip("Jak wysoko obiekt ma siê unieœæ (w metrach).")]
    [SerializeField] private float floatHeight = 1f;

    [Tooltip("Ile sekund obiekt ma wisieæ w powietrzu przed opadniêciem.")]
    [SerializeField] private float hoverDuration = 4.0f;

    [Tooltip("Szybkoœæ unoszenia i opadania.")]
    [SerializeField] private float moveSpeed = 2.0f;

    [Header("Hover Animation (Subtelne ko³ysanie)")]
    [Tooltip("Amplituda ko³ysania w powietrzu (w metrach).")]
    [SerializeField] private float bobAmplitude = 0.05f;

    [Tooltip("Szybkoœæ ko³ysania w powietrzu.")]
    [SerializeField] private float bobFrequency = 2.5f;

    private LevitationState _currentState = LevitationState.Idle;

    private Vector3 _startPosition;
    private Quaternion _startRotation;
    private Vector3 _targetPosition;

    private float _stateTimer;
    private float _interpolationProgress;
    private Rigidbody _rigidbody;
    private bool _hadGravity;
    private bool _wasKinematic;

    private void Awake()
    {
        // Zapisujemy pozycjê i rotacjê pocz¹tkow¹, ¿eby obiekt zawsze wiedzia³ dok¹d wróciæ
        _startPosition = transform.position;
        _startRotation = transform.rotation;

        _rigidbody = GetComponent<Rigidbody>();
    }

    private void Update()
    {
        switch (_currentState)
        {
            case LevitationState.Idle:
                // Nic siê nie dzieje, obiekt czeka na czar
                break;

            case LevitationState.Rising:
                UpdateRising();
                break;

            case LevitationState.Hovering:
                UpdateHovering();
                break;

            case LevitationState.Falling:
                UpdateFalling();
                break;
        }
        //if (Input.GetKeyDown(KeyCode.L))
        //{
        //    StartLevitation();
        //}
    }

    /// <summary>
    /// Metoda wywo³ywana przez LevitationSpell, gdy czar trafi w ten obiekt.
    /// </summary>
    public void StartLevitation()
    {
        // Jeœli obiekt ju¿ lewituje, ignorujemy ponowne rzucenie czaru
        if (_currentState != LevitationState.Idle) return;

        // 1. Wy³¹czamy fizykê, ¿eby grawitacja Unity nie ci¹gnê³a obiektu w dó³ podczas magii
        if (_rigidbody)
        {
            _hadGravity = _rigidbody.useGravity;
            _wasKinematic = _rigidbody.isKinematic;

            _rigidbody.useGravity = false;
            _rigidbody.isKinematic = true; // Blokuje wp³yw si³ fizycznych
        }

        // 2. Obliczamy punkt docelowy bezpoœrednio nad obiektem
        _targetPosition = _startPosition + Vector3.up * floatHeight;

        // 3. Resetujemy zmienne ruchu i zmieniamy stan
        _interpolationProgress = 0f;
        _currentState = LevitationState.Rising;

        Debug.Log($"[Wingardium Leviosa] {gameObject.name} zaczyna siê unosiæ!");
    }

    private void UpdateRising()
    {
        _interpolationProgress += Time.deltaTime * moveSpeed;

        // P³ynne wyg³adzenie ruchu (SmoothStep zapobiega szarpniêciom na starcie i koñcu)
        float t = Mathf.SmoothStep(0f, 1f, _interpolationProgress);
        transform.position = Vector3.Lerp(_startPosition, _targetPosition, t);

        if (_interpolationProgress >= 1f)
        {
            transform.position = _targetPosition;
            _currentState = LevitationState.Hovering;
            _stateTimer = hoverDuration;
        }
    }

    private void UpdateHovering()
    {
        // Subtelne, magiczne ko³ysanie w powietrzu przy u¿yciu funkcji Sinus
        float bobbing = Mathf.Sin(Time.time * bobFrequency) * bobAmplitude;
        transform.position = _targetPosition + Vector3.up * bobbing;

        _stateTimer -= Time.deltaTime;
        if (_stateTimer <= 0f)
        {
            _interpolationProgress = 0f;
            _currentState = LevitationState.Falling;
        }
    }

    private void UpdateFalling()
    {
        // Pobieramy aktualn¹ pozycjê (z uwzglêdnieniem ko³ysania), ¿eby p³ynnie zacz¹æ opadaæ
        _interpolationProgress += Time.deltaTime * moveSpeed;

        float t = Mathf.SmoothStep(0f, 1f, _interpolationProgress);

        // P³ynnie wracamy do punktu startowego
        transform.position = Vector3.Lerp(_targetPosition, _startPosition, t);
        // Na wszelki wypadek upewniamy siê, ¿e rotacja te¿ wraca do normy
        transform.rotation = Quaternion.Slerp(transform.rotation, _startRotation, t);

        if (_interpolationProgress >= 1f)
        {
            // Reset do idealnych wartoœci startowych
            transform.position = _startPosition;
            transform.rotation = _startRotation;

            // Przywracamy fizykê, jeœli obiekt j¹ posiada³
            if (_rigidbody)
            {
                _rigidbody.useGravity = _hadGravity;
                _rigidbody.isKinematic = _wasKinematic;
            }

            _currentState = LevitationState.Idle;
            Debug.Log($"[Wingardium Leviosa] {gameObject.name} wróci³ na swoje miejsce.");
        }
    }
}