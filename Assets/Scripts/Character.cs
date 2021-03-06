﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using DG.Tweening;

public class Character : MonoBehaviour
{
    public UnityAction<Vector2, Character> DashLanded;

    [Header("Character data")]
    public int playerId;
    public PlayMode playMode = PlayMode.Action;
    public enum PlayMode
    {
        Action,
        Tetris,
    }

    public CharacterState charState = CharacterState.Walking;
    public enum CharacterState
    {
        Walking,
        Dashing,
        Dodging,
        Dizzy
    }

    [Header("Movement speeds")]
    public float speed = 200f;
    public float dashSpeed = 200f;
    public float dodgeSpeed = 200f;

    [Tooltip("Can't dash or dodge if below this movement treshold")]
    public float dashDodgeSpeedTreshold = .2f;
    public float dashPushStrength = 200f;

    [Header("Recovery delays")]
    [Tooltip("When using the dash")]
    public float endDashDelay = 1f;
    [Tooltip("When using the dodge")]
    public float endDodgeDelay = 1f;
    [Tooltip("When getting hit by a dash")]
    public float endDizzyDelay = 1f;

    [Header("Sounds")]
    [FMODUnity.EventRef]
    public string dashEvent;
    [FMODUnity.EventRef]
    public string dodgeEvent;
    [FMODUnity.EventRef]
    public string bumpEvent;
    [FMODUnity.EventRef]
    public string pickupObjectSFXEvent;
    [FMODUnity.EventRef]
    public string dropObjectSFXEvent;

    // private FMOD.Studio.EventInstance dashSound, dodgeSound;
    // public float walkDecayTreshold = .7f, walkDecayAmount = .5f;

    private Rigidbody2D rigidbody2D;
    private Animator animator;
    private Vector2 inputVector; //cached input
    private PickupObject intersectedPickup = null, carriedPickup = null;
    private HomeBox intersectedHomeBox = null;
    private Vector2 platformOffset = Vector2.zero;
    private Transform movingPlatform = null;
    private Vector3 movingPlatformLastPos;
    private float originalSpeed;
    private float speedReductionWhenCarrying = .8f;

    private int hashedDodge, hashedDash, hashedEndSpecial, hashedHorizontal, hashedVertical;

    private void Awake()
    {
        rigidbody2D = GetComponent<Rigidbody2D>();
        animator = GetComponentInChildren<Animator>();

        hashedDodge = Animator.StringToHash("Dodge");
        hashedDash = Animator.StringToHash("Dash");
        hashedEndSpecial = Animator.StringToHash("EndSpecial");
        hashedHorizontal = Animator.StringToHash("Horizontal");
        hashedVertical = Animator.StringToHash("Vertical");

        originalSpeed = speed;

        //Sounds
        // dashSound = FMODUnity.RuntimeManager.CreateInstance(dashEvent);
        // dodgeSound = FMODUnity.RuntimeManager.CreateInstance(dodgeEvent);
    }

    //------------------- Modes
    public void StartTetrisMode()
    {
        playMode = PlayMode.Tetris;
        Move(Vector2.zero);
        
        //TODO: handle case when Tetris mode starts while dodging/dashing
    }

    public void StartActionMode()
    {
        playMode = PlayMode.Action;
    }

    //Called on FixedUpdate by the GameManager
    public void Move(Vector2 input)
    {
        if(charState == CharacterState.Walking)
        {
            inputVector = input;

            if(movingPlatform != null)
            {
                platformOffset = (Vector2)(movingPlatform.position - movingPlatformLastPos);
                movingPlatformLastPos = movingPlatform.position;
            }

            rigidbody2D.MovePosition((rigidbody2D.position + inputVector * speed * Time.fixedDeltaTime) + platformOffset);

            //animator.SetBool(hashedWalking, inputVector.sqrMagnitude > .05f);
            animator.SetFloat(hashedHorizontal, inputVector.x);
            animator.SetFloat(hashedVertical, inputVector.y);
        }
    }

    public void Respawn(Vector3 newPos)
    {
        if(carriedPickup != null)
        {
            DropPickup();
        }

        intersectedPickup = null;
        intersectedHomeBox = null;
        movingPlatform = null;
        movingPlatformLastPos = Vector2.zero;

        rigidbody2D.MovePosition(newPos);
    }


    //--------------------- Primary action: pickup and release
    public void PerformAction()
    {
        if(carriedPickup == null
            && intersectedPickup != null)
        {
            PickupObject();
        }
        else if(carriedPickup != null)
        {
            if(intersectedHomeBox == null)
            {
                DropPickup();
            }
            else
            {
                GameManager.Instance.StartTetrisMode(playerId, carriedPickup.tetrisPiecePrefab);
                Destroy(carriedPickup.gameObject);
                speed = originalSpeed; //restore full speed
                carriedPickup = null;
            }
        }

        //TODO: Use homebox and go into Tetris mode
    }

    private void PickupObject()
    {
        //pick up what's on the ground
        intersectedPickup.PickedUp(this);
        carriedPickup = intersectedPickup;
        intersectedPickup = null;

        //parent and carry animation
        carriedPickup.transform.SetParent(this.transform, true);
        carriedPickup.transform.DOLocalMove(new Vector2(0f, 3f), .2f);

        FMODUnity.RuntimeManager.PlayOneShot(pickupObjectSFXEvent, transform.position);

        speed = originalSpeed * speedReductionWhenCarrying;
    }

    private void DropPickup()
    {
        //drop what it has in its hands
        carriedPickup.DroppedDown();

        //release the gameobject
        carriedPickup.transform.localPosition = new Vector2(0f, 0f);
        carriedPickup.transform.SetParent(null, true);

        FMODUnity.RuntimeManager.PlayOneShot(dropObjectSFXEvent, transform.position);

        speed = originalSpeed; //restore full speed
        carriedPickup = null;
    }


    //--------------------- Secondary actions: dashing and dodging

    public void PerformSecondary()
    {
        if(inputVector.sqrMagnitude >= dashDodgeSpeedTreshold)
        {
            switch(charState)
            {
                case CharacterState.Walking:
                    if(carriedPickup == null)
                    {
                        Dash();
                    }
                    else
                    {
                        Dodge();
                    }
                    break;
            }
        }
    }
    
    public void Dash()
    {
        StartCoroutine(EndDash());
    }

    private IEnumerator EndDash()
    {
        charState = CharacterState.Dashing;
        animator.SetTrigger(hashedDash);
        rigidbody2D.AddForce(inputVector.normalized * dashSpeed, ForceMode2D.Impulse);
        FMODUnity.RuntimeManager.PlayOneShot(dashEvent, transform.position);

        yield return new WaitForSeconds(endDashDelay);

        GoToWalkingState();
    }

    public void Dodge()
    {
        StartCoroutine(EndDodge());
    }

    private IEnumerator EndDodge()
    {
        charState = CharacterState.Dodging;
        animator.SetTrigger(hashedDodge);
        rigidbody2D.AddForce(inputVector.normalized * dodgeSpeed, ForceMode2D.Impulse);
        gameObject.layer = LayerMask.NameToLayer("DodgingCharacters");
        FMODUnity.RuntimeManager.PlayOneShot(dodgeEvent, transform.position);

        yield return new WaitForSeconds(endDodgeDelay);

        gameObject.layer = LayerMask.NameToLayer("Characters");
        GoToWalkingState();
    }

    private void GoToWalkingState()
    {
        if(charState == CharacterState.Dodging || charState == CharacterState.Dashing)
            animator.SetTrigger(hashedEndSpecial);

        charState = CharacterState.Walking;
        rigidbody2D.velocity = Vector2.zero;
    }

    //--------------------- Collision detection
    private void OnCollisionEnter2D(Collision2D coll)
    {
        //Dashing into objects or other players
        if(charState == CharacterState.Dashing)
        {
            FMODUnity.RuntimeManager.PlayOneShot(bumpEvent, transform.position); //play bump sound

            if(coll.gameObject.CompareTag("Player"))
            {
                //Collision happened with another player
                Character otherCharacter = coll.gameObject.GetComponent<Character>();
                rigidbody2D.velocity = Vector2.zero;
                DashLanded.Invoke(inputVector * dashPushStrength, otherCharacter); //notifies the ActionController
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D coll)
    {
        //Any pickup is in the vicinity
        if(coll.gameObject.CompareTag("Pickup"))
        {
            intersectedPickup = coll.transform.parent.GetComponent<PickupObject>();
        }

        //Stepping on a homebox
        if(coll.gameObject.CompareTag("HomeBox"))
        {
            if(coll.transform.parent.GetComponent<HomeBox>().playerId == playerId)
            {
                intersectedHomeBox = coll.transform.parent.GetComponent<HomeBox>();
            }
        }

        if(coll.gameObject.CompareTag("MovingPlatform"))
        {
            movingPlatform = coll.transform;
            movingPlatformLastPos = movingPlatform.position;
        }
    }

    private void OnTriggerExit2D(Collider2D coll)
    {
        //A pickup left on the ground
        if(coll.gameObject.CompareTag("Pickup"))
        {
            if(coll.transform.parent.GetComponent<PickupObject>() == intersectedPickup)
            {
                intersectedPickup = null;
            }
        }

        //Stepping away from a homebox
        if(coll.gameObject.CompareTag("HomeBox"))
        {
            if(coll.transform.parent.GetComponent<HomeBox>().playerId == playerId)
            {
                intersectedHomeBox = null;
            }
        }

        if(coll.gameObject.CompareTag("MovingPlatform"))
        {
            if(coll.gameObject.transform == movingPlatform)
            {
                movingPlatform = null;
                platformOffset = Vector2.zero;
            }
        }
    }

     public void OnHit(Vector2 push)
    {
        charState = CharacterState.Dizzy;
        rigidbody2D.velocity = Vector2.zero;
        rigidbody2D.AddForce(push, ForceMode2D.Impulse);

        //Drop object if carrying one
        if(carriedPickup != null)
        {
            DropPickup();
        }
        
        StartCoroutine(EndDizzy());
    }

    private IEnumerator EndDizzy()
    {
        yield return new WaitForSeconds(endDizzyDelay);

        GoToWalkingState();
    }

    //--------------------- 

    private void FixedUpdate()
    {
        switch(charState)
        {
            case CharacterState.Walking:
                break;
        }
    }

}
