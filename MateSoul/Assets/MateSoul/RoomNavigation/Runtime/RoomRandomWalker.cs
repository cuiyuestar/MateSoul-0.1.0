using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace MateSoul.RoomNavigation
{
    [DisallowMultipleComponent]
    public sealed class RoomRandomWalker : MonoBehaviour
    {
        private static readonly int WalkParameter = Animator.StringToHash("param_idletowalk");
        private static readonly int IdleParameter = Animator.StringToHash("param_toidle");

        [SerializeField, Min(0.1f)]
        private float moveSpeed = 1.1f;

        [SerializeField, Min(30f)]
        private float turnSpeedDegrees = 240f;

        [SerializeField, Min(0.1f)]
        private float minimumTravelDistance = 1.25f;

        [SerializeField]
        private Vector2 idleDurationRange = new Vector2(0.6f, 1.5f);

        [SerializeField, Min(0.01f)]
        private float arrivalTolerance = 0.12f;

        private RuntimeRoomNavigation navigation;
        private NavMeshAgent agent;
        private Animator animator;
        private System.Random random;
        private Coroutine wanderRoutine;
        private Vector3 previousPosition;
        private bool hasWalkParameter;
        private bool hasIdleParameter;

        public bool IsInitialized { get; private set; }
        public bool IsMoving { get; private set; }
        public int DestinationsSelected { get; private set; }
        public int DestinationsReached { get; private set; }
        public float TotalDistanceTravelled { get; private set; }
        public Vector3 CurrentDestination { get; private set; }

        public void Initialize(RuntimeRoomNavigation roomNavigation, Vector3 spawnPoint, int randomSeed)
        {
            if (roomNavigation == null || !roomNavigation.IsReady)
            {
                throw new InvalidOperationException("Room navigation must be ready before the walker starts.");
            }

            navigation = roomNavigation;
            random = new System.Random(randomSeed);
            animator = GetComponentInChildren<Animator>();
            CacheAnimatorParameters();

            agent = GetComponent<NavMeshAgent>();
            if (agent == null)
            {
                agent = gameObject.AddComponent<NavMeshAgent>();
            }

            agent.radius = 0.25f;
            agent.height = 1.65f;
            agent.baseOffset = 0f;
            agent.speed = moveSpeed;
            agent.angularSpeed = turnSpeedDegrees;
            agent.acceleration = 4f;
            agent.stoppingDistance = arrivalTolerance;
            agent.autoBraking = true;
            agent.updateRotation = false;

            if (!navigation.TrySamplePosition(
                    spawnPoint,
                    navigation.Snapshot.Bounds.extents.magnitude,
                    out var sampledSpawn) ||
                !agent.Warp(sampledSpawn))
            {
                throw new InvalidOperationException("Character spawn point is outside the Runtime NavMesh.");
            }

            previousPosition = transform.position;
            IsInitialized = true;
            SetWalkingAnimation(false);
            wanderRoutine = StartCoroutine(Wander());
        }

        private void Update()
        {
            if (!IsInitialized)
            {
                return;
            }

            var currentPosition = transform.position;
            TotalDistanceTravelled += Vector3.Distance(previousPosition, currentPosition);
            previousPosition = currentPosition;

            if (!IsMoving)
            {
                return;
            }

            var velocity = agent.desiredVelocity;
            velocity.y = 0f;
            if (velocity.sqrMagnitude <= 0.0025f)
            {
                return;
            }

            var targetRotation = Quaternion.LookRotation(velocity.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                turnSpeedDegrees * Time.deltaTime);
        }

        private IEnumerator Wander()
        {
            while (enabled)
            {
                var idleSeconds = Mathf.Lerp(
                    idleDurationRange.x,
                    idleDurationRange.y,
                    (float)random.NextDouble());
                yield return new WaitForSeconds(idleSeconds);

                if (!navigation.TryGetRandomReachablePoint(
                        transform.position,
                        random,
                        minimumTravelDistance,
                        out var destination,
                        out var path))
                {
                    Debug.LogWarning("[RoomNavigation] No complete random path was found.");
                    yield return new WaitForSeconds(0.5f);
                    continue;
                }

                CurrentDestination = destination;
                DestinationsSelected++;
                agent.isStopped = false;
                if (!agent.SetPath(path))
                {
                    Debug.LogWarning("[RoomNavigation] NavMeshAgent rejected a complete path.");
                    continue;
                }

                IsMoving = true;
                SetWalkingAnimation(true);
                while (enabled && agent.enabled && agent.isOnNavMesh)
                {
                    if (!agent.pathPending &&
                        agent.remainingDistance <= agent.stoppingDistance + arrivalTolerance)
                    {
                        break;
                    }

                    yield return null;
                }

                IsMoving = false;
                agent.isStopped = true;
                DestinationsReached++;
                SetWalkingAnimation(false);
            }
        }

        private void CacheAnimatorParameters()
        {
            if (animator == null)
            {
                return;
            }

            foreach (var parameter in animator.parameters)
            {
                if (parameter.nameHash == WalkParameter && parameter.type == AnimatorControllerParameterType.Bool)
                {
                    hasWalkParameter = true;
                }
                else if (parameter.nameHash == IdleParameter &&
                         parameter.type == AnimatorControllerParameterType.Bool)
                {
                    hasIdleParameter = true;
                }
            }
        }

        private void SetWalkingAnimation(bool isWalking)
        {
            if (animator == null)
            {
                return;
            }

            if (hasWalkParameter)
            {
                animator.SetBool(WalkParameter, isWalking);
            }

            if (hasIdleParameter)
            {
                animator.SetBool(IdleParameter, !isWalking);
            }
        }

        private void OnDisable()
        {
            if (wanderRoutine != null)
            {
                StopCoroutine(wanderRoutine);
                wanderRoutine = null;
            }

            if (agent != null && agent.enabled && agent.isOnNavMesh)
            {
                agent.isStopped = true;
            }

            IsMoving = false;
            SetWalkingAnimation(false);
        }
    }
}
