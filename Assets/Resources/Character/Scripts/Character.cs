using UnityEngine;
using UnityEngine.Networking;
using System.Collections.Generic;
using System;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(ObjSpringable))]
public class Character : GameBehaviour {
    public float terminalSpeed = 16.5F;
    public float smoothRotationThreshold = 45;

    // ========================================================================

    [HideInInspector]
    public List<CharacterCapability> capabilities = new List<CharacterCapability>();

    public CharacterCapability GetCapability(string capabilityName) {
        foreach (CharacterCapability capability in capabilities) {
            if (capability.name == capabilityName) return capability;
        }
        return null;
    }

    public CharacterCapability WithCapability(string capabilityName, Action<CharacterCapability> callback) {
        CharacterCapability capability = GetCapability(capabilityName);
        if (capability != null) callback(capability);
        return capability;
    }

    // ========================================================================

    [HideInInspector]
    public string statePrev = "ground";
    string _stateCurrent = "ground";
    public string stateCurrent {
        get { return _stateCurrent; }
        set { StateChange(value); }
    }

    void StateChange(string newState) {
        string stateCurrentCheck = stateCurrent;
        if (_stateCurrent == newState) return;
        foreach (CharacterCapability capability in capabilities)
            capability.StateDeinit(_stateCurrent, newState);

        if (stateCurrentCheck != stateCurrent) return; // Allows changing state in Deinit

        statePrev = _stateCurrent;
        _stateCurrent = newState;
        foreach (CharacterCapability capability in capabilities)
            capability.StateInit(_stateCurrent, statePrev);
    }

    Dictionary<string, List<string>> _stateGroups = new Dictionary<string, List<string>>();
    public bool InStateGroup(string groupName) {
        return InStateGroup(groupName, stateCurrent);
    }
    public bool InStateGroup(string groupName, string stateName) {
        if (!_stateGroups.ContainsKey(groupName)) return false;
        return _stateGroups[groupName].Contains(stateName);
    }

    public void AddStateGroup(string groupName, string stateName) {
        if (!_stateGroups.ContainsKey(groupName)) {
            _stateGroups[groupName] = new List<string> { stateName };
        } else {
            _stateGroups[groupName].Add(stateName);
        }
    }

    // ========================================================================

    public List<CharacterEffect> effects = new List<CharacterEffect>();

    public void UpdateEffects(float deltaTime) {
        // iterate backwards to prevent index from shifting
        // (effects remove themselves once complete)
        for (int i = effects.Count - 1; i >= 0; i--) {
            CharacterEffect effect = effects[i];
            effect.UpdateBase(deltaTime);
        }
    }

    public void RemoveEffect(string effectName) {
        // iterate backwards to prevent index from shifting
        for (int i = effects.Count - 1; i >= 0; i--) {
            CharacterEffect effect = effects[i];
            if (effectName != effect.name) continue;
            effect.DestroyBase();
        }
    }

    public CharacterEffect GetEffect(string effectName) {
        foreach (CharacterEffect effect in effects) {
            if (effectName == effect.name) return effect;
        }
        return null;
    }

    public bool HasEffect(string effectName) {
        return GetEffect(effectName) != null;
    }

    public void ClearEffects() {
        // iterate backwards to prevent index from shifting
        for (int i = effects.Count - 1; i >= 0; i--) {
            CharacterEffect effect = effects[i];
            effect.DestroyBase();
        }
    }

    // ========================================================================
    
    public Vector3 position {
        get { return transform.position; }
        set { transform.position = value; }
    }

    public Quaternion rotation {
        get { return transform.rotation; }
        set { transform.rotation = value; }
    }

    public Vector3 eulerAngles {
        get { return transform.eulerAngles; }
        set { transform.eulerAngles = value; }
    }

    public float forwardAngle {
        get { return eulerAngles.z; }
        set { 
            Vector3 angle = eulerAngles;
            angle.z = value;
            eulerAngles = angle;
        }
    }

    // ========================================================================

    [HideInInspector]
    public new Rigidbody rigidbody;
    public Vector3 velocity {
        get { return rigidbody.velocity; }
        set { rigidbody.velocity = value; }
    }
    [HideInInspector]
    public Vector3 velocityPrev;

    // ========================================================================

    [HideInInspector]
    public float groundSpeedPrev = 0;
    float _groundSpeed = 0;
    public float groundSpeed {
        get { return _groundSpeed; }
        set {
            _groundSpeed = value;
            groundSpeedRigidbody = _groundSpeed;
        }
    }

    public float groundSpeedRigidbody {
        get {
            return (
                (velocity.x * Mathf.Cos(forwardAngle * Mathf.Deg2Rad)) +
                (velocity.y * Mathf.Sin(forwardAngle * Mathf.Deg2Rad))
            );
        }
        set {
            velocity = new Vector3(
                Mathf.Cos(forwardAngle * Mathf.Deg2Rad),
                Mathf.Sin(forwardAngle * Mathf.Deg2Rad),
                velocity.z
            ) * value;
        }
    }

    public void GroundSpeedSync() { _groundSpeed = groundSpeedRigidbody; }

    // ========================================================================
    
    public static LayerMask? _solidRaycastMask = null;
    public static LayerMask solidRaycastMask {
        get {
            if (_solidRaycastMask != null) return (LayerMask)_solidRaycastMask;
            _solidRaycastMask = LayerMask.GetMask(
                "Ignore Raycast",
                "Player - Ignore Top Solid and Raycast",
                "Player - Ignore Top Solid",
                "Player - Rolling",
                "Player - Rolling and Ignore Top Solid",
                "Player - Rolling and Ignore Top Solid",
                "Object - Player Only and Ignore Raycast",
                "Object - Ring"
            );
            return (LayerMask)_solidRaycastMask;
        }
    }

    // ========================================================================
    
    public RaycastHit GetGroundRaycast() {
        return GetSolidRaycast(-transform.up);
    }

    // 3D-Ready: YES
    public RaycastHit GetSolidRaycast(Vector3 direction, float maxDistance = 0.8F) {
        RaycastHit hit;
        Physics.Raycast(
            position, // origin
            direction.normalized, // direction
            out hit,
            maxDistance * sizeScale, // max distance
            ~solidRaycastMask // layer mask
        );
        return hit;
    }

    bool GetIsGrounded() {
        RaycastHit hit = GetGroundRaycast();
        return GetIsGrounded(hit);
    }

    // 3D-Ready: NO
    bool GetIsGrounded(RaycastHit hit) { // Potentially avoid recomputing raycast
        if (hit.collider == null) return false;

        float hitAngle = Quaternion.FromToRotation(Vector3.up, hit.normal).eulerAngles.z;
        float angleDiff = Mathf.DeltaAngle(
            hitAngle,
            forwardAngle
        );

        CharacterCollisionModifier collisionModifier = hit.transform.GetComponentInParent<CharacterCollisionModifier>();
        if (collisionModifier != null) {
            switch (collisionModifier.type) {
                case CharacterCollisionModifier.CollisionModifierType.NoGrounding:
                    return false;
                case CharacterCollisionModifier.CollisionModifierType.NoGroundingLRB:
                    if (hitAngle > 90 && hitAngle < 270) return false;
                    break;
                case CharacterCollisionModifier.CollisionModifierType.NoGroundingLRBHigher:
                    if (hitAngle > 45 && hitAngle < 315) return false;
                    break;
            }
        }

        return angleDiff < 67.5F;
    }

    // ========================================================================

    CharacterGroundedDetector _groundedDetectorCurrent;
    public CharacterGroundedDetector groundedDetectorCurrent {
        get { return _groundedDetectorCurrent; }
        set {
            if (value == _groundedDetectorCurrent) return;
            if (_groundedDetectorCurrent != null) _groundedDetectorCurrent.Leave(this);
            _groundedDetectorCurrent = value;
            if (_groundedDetectorCurrent != null) _groundedDetectorCurrent.Enter(this);
        }
    }

    public enum BalanceState {
        None,
        Left, // platform is to the left
        Right // platform is to the right
    }

    [HideInInspector]
    public BalanceState balanceState = BalanceState.None;

    // Keeps character locked to ground while in ground state
    public bool GroundSnap() {
        RaycastHit hit = GetGroundRaycast();
        balanceState = BalanceState.None;

        if (GetIsGrounded(hit)) {
            transform.eulerAngles = new Vector3(
                0,
                0,
                Quaternion.FromToRotation(Vector3.up, hit.normal).eulerAngles.z
            );

            Vector3 newPos = hit.point + (transform.up * 0.5F * sizeScale);
            newPos.z = position.z; // Comment this for 3D movement
            position = newPos;
            groundedDetectorCurrent = hit.transform.GetComponentInChildren<CharacterGroundedDetector>();
            return true;
        } 

        // Didn't find the ground from the player center?
        // We might be on a ledge. Better check to the left and right of
        // the character to be sure.
        for (int dir = -1; dir <= 1; dir += 2) {
            RaycastHit hitLedge;
            Physics.Raycast(
                position + (dir * transform.right * 0.375F * sizeScale * sizeScale), // origin
                -transform.up, // direction
                out hitLedge,
                0.8F * sizeScale, // max distance
                ~solidRaycastMask // layer mask
            );
            if (GetIsGrounded(hitLedge)) {
                balanceState = dir < 0 ? BalanceState.Left : BalanceState.Right;
                groundedDetectorCurrent = hitLedge.transform.GetComponentInChildren<CharacterGroundedDetector>();

                Vector3 newPos = (
                    hitLedge.point -
                    (dir * transform.right * 0.375F * sizeScale) +
                    (transform.up * 0.5F * sizeScale)
                );
                newPos.x = position.x;
                newPos.z = position.z;
                position = newPos;
                return true;
            }
        }

        if (stateCurrent == "rolling") stateCurrent = "rollingAir";
        else stateCurrent = "air";
        
        return false;
    }

    // ========================================================================
    
    [HideInInspector]
    public AudioSource audioSource;
    [HideInInspector]
    public Animator spriteAnimator;

    [HideInInspector]
    string spriteAnimatorStatePrev;
    // [HideInInspector, SyncVar]
    [HideInInspector]
    public string spriteAnimatorState;
    // [HideInInspector, SyncVar]
    [HideInInspector]
    public float spriteAnimatorSpeed;

    public void AnimatorPlay(string stateName, float normalizedTime = float.NegativeInfinity) {
        spriteAnimatorStatePrev = spriteAnimatorState;
        spriteAnimator.Play(stateName, -1, normalizedTime);
        spriteAnimatorState = stateName;
    }

    // ========================================================================

    // [SyncVar]
    [HideInInspector]
    public float sizeScale = 1F;

    public float physicsScale => sizeScale * Utils.physicsScale;

    // [SyncVar]
    [HideInInspector]
    public bool flipX = false;
    
    public enum CharacterFlipModes {
        Scale,
        Rotation
    };
    public CharacterFlipModes flipMode = CharacterFlipModes.Scale;

    // ========================================================================

    [HideInInspector]
    public bool facingRight = true;

    // ========================================================================
    
    [HideInInspector]
    public Transform sprite;
    [HideInInspector]
    public Transform spriteContainer;

    // Gets rotation for sprite
    // 3D-Ready: No
    public Vector3 GetSpriteRotation(float deltaTime) {
        if (!GlobalOptions.GetBool("smoothRotation"))
            return (transform.eulerAngles / 45F).Round(0) * 45F;
    
        Vector3 spriteRotation = spriteContainer.transform.eulerAngles;
        bool shouldRotate = (
            Mathf.Abs(Mathf.DeltaAngle(0, forwardAngle)) >
            smoothRotationThreshold
        );
        
        float targetRotationZ = transform.eulerAngles.z;

        if (shouldRotate) {
            // targetAngle = transform.eulerAngles;
            // if (forwardAngle > 180 && spriteRotation.z == 0)
                // spriteRotation.z = 360;
        } else {
            targetRotationZ = 0;
            // targetAngle = spriteRotation.z > 180 ?
                // new Vector3(0,0,360) : Vector3.zero;
        }

        float rotationSign = (flipMode == CharacterFlipModes.Rotation ? (flipX ? -1 : 1) : 1);

        spriteRotation.z = targetRotationZ * rotationSign;
        return spriteRotation;
        // return Vector3.RotateTowards(
        //     spriteRotation, // current
        //     targetAngle, // target // TODO: 3D
        //     10F * deltaTime * 60F, // max rotation
        //     10F * deltaTime * 60F // magnitude
        // );
    }

    [HideInInspector]
    public float opacity = 1;

    // ========================================================================

    [HideInInspector]
    public Level currentLevel;
    [HideInInspector]
    public float timer = 0;
    [HideInInspector]
    public bool timerPause = false;

    int _score = 0;
    public int score {
        get { return _score; }
        set {
            if (Mathf.Floor(value / 50000F) > Mathf.Floor(_score / 50000F))
                lives++;

            _score = value;
        }
    }
    [HideInInspector]
    public int destroyEnemyChain = 0;

    int _ringLivesMax = 0;
    int _rings;
    public int rings { 
        get { return _rings; }
        set {
            _rings = value;
            int livesPrev = lives;
            lives += Mathf.Max(0, (int)Mathf.Floor(_rings / 100F) - _ringLivesMax);
            _ringLivesMax = Mathf.Max(_ringLivesMax, (int)Mathf.Floor(_rings / 100F));
        }
    }
    int _lives = 3;
    public int lives {
        get { return _lives; }
        set {
            if (value > _lives) {
                MusicManager.current.Add(new MusicManager.MusicStackEntry{
                    introPath = "Music/1-Up",
                    disableSfx = true,
                    fadeInAfter = true,
                    priority = 10,
                    ignoreClear = true
                });
            }
            _lives = value;
        }
    }

    // ========================================================================

    [HideInInspector]
    public CharacterCamera characterCamera;

    public class RespawnData {
        public Vector3 position = Vector3.zero;
        public float timer = 0;
    }
    public RespawnData respawnData = new RespawnData();

    [HideInInspector]
    public int checkpointId = 0;

    public void Respawn() {
        SoftRespawn();
        if (checkpointId == 0) {
            if (currentLevel.cameraZoneStart != null)
                currentLevel.cameraZoneStart.Set(this);
        }

        if (characterCamera != null) {
            characterCamera.MinMaxPositionSnap();
            characterCamera.position = transform.position;
        }
    }

    public void SoftRespawn() { // Should only be used in multiplayer; for full respawns reload scene
        _rings = 0;
        _ringLivesMax = 0;
        effects.Clear();
        position = respawnData.position;
        timer = respawnData.timer;
        stateCurrent = "ground";
        velocity = Vector3.zero;
        _groundSpeed = 0;
        transform.eulerAngles = Vector3.zero;
        facingRight = true;

        timerPause = false;
        WithCapability("victory", (CharacterCapability capability) => {
            ((CharacterCapabilityVictory)capability).victoryLock = false;
        });
    }

    // ========================================================================

    [HideInInspector]
    public Vector2 positionMin = new Vector2(
        -Mathf.Infinity,
        -Mathf.Infinity
    );
    [HideInInspector]
    public Vector2 positionMax = new Vector2(
        Mathf.Infinity,
        Mathf.Infinity
    );

    public void LimitPosition() {
        Vector2 positionNew = Vector2.Min(
            Vector2.Max(
                position,
                positionMin
            ),
            positionMax
        );

        if ((Vector2)position != positionNew) {
            position = positionNew;
            _groundSpeed = 0;
        }
    }

    // ========================================================================

    [HideInInspector]
    public bool controlLock { get { return (
        Time.timeScale == 0 ||
        InStateGroup("noControl") ||
        HasEffect("controlLock") ||
        !isLocalPlayer
    ); }}

    // ========================================================================

    Transform _modeGroupCurrent;
    [HideInInspector]
    public Collider colliderCurrent;
    public Transform modeGroupCurrent {
        get { return _modeGroupCurrent; }
        set {
            if (_modeGroupCurrent == value) return;
  
            if (_modeGroupCurrent != null)
                _modeGroupCurrent.gameObject.SetActive(false);
  
            _modeGroupCurrent = value;
            colliderCurrent = null;
    
            if (_modeGroupCurrent != null) {
                _modeGroupCurrent.gameObject.SetActive(true);
                colliderCurrent = _modeGroupCurrent.Find("Collider").GetComponent<Collider>();
            }
        }
    }

    // ========================================================================

    public bool isInvulnerable => (
        InStateGroup("ignore") ||
        HasEffect("invulnerable") ||
        HasEffect("invincible")
    );

    public bool isHarmful => (
        InStateGroup("harmful") ||
        HasEffect("invincible")
    );

    // 3D-Ready: NO
    public void Hurt(bool moveLeft = true, string source = "") {
        if (isInvulnerable) return;
        
        if (HasEffect("shield")) {
            SFX.Play(audioSource, "sfxHurt");
            RemoveEffect("shield");
        } else if (rings == 0) {
            if (source == "spikes") SFX.Play(audioSource, "sfxDieSpikes");
            else SFX.Play(audioSource, "sfxDie");
            stateCurrent = "dying";
            return;
        } else {
            ObjRing.ExplodeRings(transform.position, Mathf.Min(rings, 32));
            rings = 0;
            SFX.Play(audioSource, "sfxHurtRings");
        }

        stateCurrent = "hurt";
        velocity = new Vector3( // TODO: 3D
            2 * (moveLeft ? -1 : 1)  * physicsScale,
            4 * physicsScale,
            velocity.z
        );
        position += velocity / 30F; // HACK
    }

    // ========================================================================

    [HideInInspector]
    HUD hud;

    // ========================================================================
    public virtual void Start() {
        if (isLocalPlayer) {
            characterCamera = Instantiate(cameraPrefab).GetComponent<CharacterCamera>();
            characterCamera.character = this;
            characterCamera.UpdateDelta(0);

            // ObjTitleCard titleCard = ObjTitleCard.Make(this);

            hud = Instantiate(hudPrefab).GetComponent<HUD>();
            hud.character = this;
            hud.Update();
        }

        respawnData.position = position;
        Respawn();
    }

    // ========================================================================
    public bool pressingLeft => input.GetAxisNegative("Horizontal");
    public bool pressingRight => input.GetAxisPositive("Horizontal");

    // ========================================================================

    [HideInInspector]
    public bool isGhost = false;
    [HideInInspector]
    public bool isLocalPlayer = true;

    public override void UpdateDelta(float deltaTime) {
        UpdateEffects(deltaTime);

        if (!isLocalPlayer) {
            isGhost = true;
            if (spriteAnimatorState != spriteAnimatorStatePrev) {
                spriteAnimator.Play(spriteAnimatorState);
                spriteAnimatorStatePrev = spriteAnimatorState;
            }
        } else {
            groundSpeedPrev = groundSpeed;
            if (!timerPause) timer += deltaTime * Time.timeScale;
            if (!isHarmful) destroyEnemyChain = 0;

            foreach (CharacterCapability capability in capabilities) {
                capability.CharUpdate(deltaTime);
                input.enabled = !controlLock;
            }

            velocityPrev = velocity;
        }

        spriteAnimator.speed = spriteAnimatorSpeed;
        transform.localScale = new Vector3(sizeScale, sizeScale, sizeScale);

        switch (flipMode) {
            case CharacterFlipModes.Scale:
                spriteContainer.localScale = new Vector3( // Hacky
                    sizeScale * (flipX ? -1 : 1),
                    sizeScale * Mathf.Sign(spriteContainer.localScale.y),
                    sizeScale * Mathf.Sign(spriteContainer.localScale.z)
                );
                break;
            case CharacterFlipModes.Rotation:
                Vector3 currentRotation = spriteContainer.eulerAngles;
                currentRotation.y = flipX ? 180 : 0;
                spriteContainer.eulerAngles = currentRotation;
                break;
        }
        // Color colorTemp = sprite.color;
        // colorTemp.a = opacity * (isGhost ? 0.5F : 1);
        // sprite.color = colorTemp;

        LimitPosition();
    }

    public override void LateUpdateDelta(float deltaTime) {
        spriteContainer.transform.position = position;

        if (characterCamera != null)
            characterCamera.UpdateDelta(deltaTime);
    }

    // ========================================================================

    public void OnCollisionEnter(Collision collision) {
        foreach (CharacterCapability capability in capabilities)
            capability.OnCharCollisionEnter(collision);
    }

    public void OnCollisionStay(Collision collision) {
        foreach (CharacterCapability capability in capabilities)
            capability.OnCharCollisionStay(collision);
    }

    public void OnCollisionExit(Collision collision) {
        foreach (CharacterCapability capability in capabilities)
            capability.OnCharCollisionExit(collision);
    }

    public void OnTriggerEnter(Collider other) {
        foreach (CharacterCapability capability in capabilities)
            capability.OnCharTriggerEnter(other);
    }

    public void OnTriggerStay(Collider other) {
        foreach (CharacterCapability capability in capabilities)
            capability.OnCharTriggerStay(other);
    }

    public void OnTriggerExit(Collider other) {
        foreach (CharacterCapability capability in capabilities)
            capability.OnCharTriggerExit(other);
    }

    // ========================================================================

    public override void OnDestroy() {
        base.OnDestroy();

        LevelManager.current.characters.Remove(this);
        Destroy(characterCamera.gameObject);
        Destroy(spriteContainer.gameObject);
        Destroy(hud.gameObject);

        // Keep player IDs sequential
        if (playerId < 0) return;
        foreach (Character character in LevelManager.current.characters) {
            if (character.playerId > playerId)        
                character.playerId--;
        }
    }

    public GameObject cameraPrefab;
    public GameObject spriteContainerPrefab;
    public GameObject hudPrefab;

    void InitReferences() {
        rigidbody = GetComponent<Rigidbody>();
        audioSource = GetComponent<AudioSource>();

        spriteContainer = Instantiate(spriteContainerPrefab).transform;
        sprite = spriteContainer.Find("Sprite");//.GetComponent<SpriteRenderer>();
        spriteAnimator = spriteContainer.Find("Sprite").GetComponent<Animator>();
    }

    public InputCustom input;
    [HideInInspector]
    public int playerId = -1;

    public override void Awake() {
        base.Awake();

        LevelManager.current.characters.Add(this);
        playerId = LevelManager.current.GetFreePlayerId();
        input = new InputCustom(1);

        InitReferences();

        Level levelDefault = FindObjectOfType<Level>();

        if (currentLevel == null) {
            currentLevel = levelDefault;
            respawnData.position = levelDefault.spawnPosition;
            Respawn();
        }

        if (GlobalOptions.GetBool("tinyMode"))
            sizeScale = 0.5F;
    }

}