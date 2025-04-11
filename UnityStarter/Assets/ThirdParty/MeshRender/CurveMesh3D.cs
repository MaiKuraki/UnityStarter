using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Mathematics;
using static CycloneGames.MeshRender.CurveMesh3D;

namespace CycloneGames.MeshRender
{
    [BurstCompile]
    public static class CircleCalculator
    {
        // 1. Quarter circle with radius 1, centered at (0, 1)
        [BurstCompile]
        public static float GetYFromX_C_0_1(float x)
        {
            return 1 - math.sqrt(1 - x * x);
        }

        // 2. Quarter circle with radius 1, centered at (1, 0)
        [BurstCompile]
        public static float GetYFromX_C_1_0(float x)
        {
            return math.sqrt(2 * x - x * x);
        }
    }

    [BurstCompile]
    public static class BezierCurve
    {
        // Calculate a point on a quadratic Bezier curve 
        [BurstCompile]
        public static float3 GetQuadraticBezierPoint(float3 startPoint, float3 endPoint, float3 controlPoint, float t)
        {
            t = math.clamp(t, 0, 1);
            float oneMinusT = 1 - t;
            float oneMinusTSquared = oneMinusT * oneMinusT;
            float tSquared = t * t;
            float twoTOneMinusT = 2 * oneMinusT * t;

            return new float3(
                oneMinusTSquared * startPoint.x + twoTOneMinusT * controlPoint.x + tSquared * endPoint.x,
                oneMinusTSquared * startPoint.y + twoTOneMinusT * controlPoint.y + tSquared * endPoint.y,
                oneMinusTSquared * startPoint.z + twoTOneMinusT * controlPoint.z + tSquared * endPoint.z
            );
        }
    }

    [BurstCompile]
    public struct CurveMeshJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> leftEdgePoints;
        [ReadOnly] public NativeArray<float3> rightEdgePoints;
        [ReadOnly] public NativeArray<CurveType> curveTypes;
        [ReadOnly] public int segmentCount;
        [ReadOnly] public Algorithm algorithm;

        [WriteOnly] public NativeArray<float3> leftEdgeResults;
        [WriteOnly] public NativeArray<float3> rightEdgeResults;
        [WriteOnly] public NativeArray<float3> midPoints;

        public void Execute(int index)
        {
            int segmentIndex = index % (segmentCount + 1);
            int curveIndex = index / (segmentCount + 1);

            if (curveIndex >= leftEdgePoints.Length - 1) return;

            float t = segmentIndex / (float)segmentCount;
            CurveType curveType = curveTypes[curveIndex];

            float3 startL = leftEdgePoints[curveIndex];
            float3 endL = leftEdgePoints[curveIndex + 1];
            float3 startR = rightEdgePoints[curveIndex];
            float3 endR = rightEdgePoints[curveIndex + 1];

            float3 leftPoint, rightPoint;

            if (algorithm == Algorithm.Circle)
            {
                float deltaXL = endL.x - startL.x;
                float deltaYL = endL.y - startL.y;
                float deltaXR = endR.x - startR.x;
                float deltaYR = endR.y - startR.y;

                // Calculate left point 
                leftPoint.x = startL.x + GetStraightXPos(deltaXL, t);
                leftPoint.y = startL.y + GetYPos(deltaYL, t, curveType);
                leftPoint.z = 0;

                // Calculate right point 
                rightPoint.x = startR.x + GetStraightXPos(deltaXR, t);
                rightPoint.y = startR.y + GetYPos(deltaYR, t, curveType);
                rightPoint.z = 0;
            }
            else // Bezier 
            {
                leftPoint = GetQuadraticBezierPoint2D(startL, endL, t, curveType);
                rightPoint = GetQuadraticBezierPoint2D(startR, endR, t, curveType);
            }

            leftEdgeResults[index] = leftPoint;
            rightEdgeResults[index] = rightPoint;
            midPoints[index] = new float3(
                (leftPoint.x + rightPoint.x) * 0.5f,
                (leftPoint.y + rightPoint.y) * 0.5f,
                0
            );
        }

        private float GetYPos(float deltaY, float time, CurveType curveType)
        {
            switch (curveType)
            {
                case CurveType.Acceleration:
                    return deltaY * CircleCalculator.GetYFromX_C_0_1(time);
                case CurveType.Deceleration:
                    return deltaY * CircleCalculator.GetYFromX_C_1_0(time);
                default:
                    return deltaY * time;
            }
        }

        private float GetStraightXPos(float deltaX, float time)
        {
            return deltaX * time;
        }

        private float3 GetQuadraticBezierPoint2D(float3 startPoint, float3 endPoint, float t, CurveType curveType)
        {
            float3 controlPoint;

            switch (curveType)
            {
                case CurveType.Acceleration:
                    controlPoint = new float3(
                        endPoint.x,
                        startPoint.y + (endPoint.y - startPoint.y) * 0.5f,
                        0
                    );
                    return BezierCurve.GetQuadraticBezierPoint(startPoint, endPoint, controlPoint, t);

                case CurveType.Deceleration:
                    controlPoint = new float3(
                        startPoint.x,
                        startPoint.y + (endPoint.y - startPoint.y) * 0.5f,
                        0
                    );
                    return BezierCurve.GetQuadraticBezierPoint(startPoint, endPoint, controlPoint, t);

                default:
                    // Linear interpolation 
                    float oneMinusT = 1 - t;
                    return new float3(
                        startPoint.x * oneMinusT + endPoint.x * t,
                        startPoint.y * oneMinusT + endPoint.y * t,
                        0
                    );
            }
        }
    }

    public class CurveMesh3D : MonoBehaviour
    {
        public enum CurveType
        {
            Acceleration,
            Deceleration,
            Straight
        }

        public enum Algorithm
        {
            Bezier,
            Circle
        }

        [SerializeField] private List<Transform> leftEdgePointList;
        [SerializeField] private List<Transform> rightEdgePointList;
        [SerializeField] private List<CurveType> curveTypeList;
        [SerializeField] private int segmentCount = 96;
        [SerializeField] private Algorithm curveAlgorithm = Algorithm.Bezier;

        private Mesh curveMesh;
        private MeshRenderer meshRenderer;
        private MeshFilter meshFilter;
        private bool isInitialized = false;

        // Native collections for Job System 
        private NativeArray<float3> leftEdgePoints;
        private NativeArray<float3> rightEdgePoints;
        private NativeArray<CurveType> curveTypes;
        private NativeArray<float3> leftEdgeResults;
        private NativeArray<float3> rightEdgeResults;
        private NativeArray<float3> midPoints;

        // Reusable arrays for mesh data 
        private Vector3[] vertices;
        private int[] triangles;
        private int totalPointCount;

        private void Awake()
        {
            Initialize();
            SetEdgePointList(leftEdgePointList, rightEdgePointList, curveTypeList);
        }

        private void OnDestroy()
        {
            DisposeNativeArrays();
        }

        private void Initialize()
        {
            if (isInitialized) return;

            // Initialize components 
            meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer == null)
            {
                meshRenderer = gameObject.AddComponent<MeshRenderer>();
                meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            }

            meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null)
            {
                meshFilter = gameObject.AddComponent<MeshFilter>();
            }

            // Initialize mesh 
            curveMesh = new Mesh();
            curveMesh.MarkDynamic();
            meshFilter.mesh = curveMesh;

            isInitialized = true;
        }

        private void DisposeNativeArrays()
        {
            if (leftEdgePoints.IsCreated) leftEdgePoints.Dispose();
            if (rightEdgePoints.IsCreated) rightEdgePoints.Dispose();
            if (curveTypes.IsCreated) curveTypes.Dispose();
            if (leftEdgeResults.IsCreated) leftEdgeResults.Dispose();
            if (rightEdgeResults.IsCreated) rightEdgeResults.Dispose();
            if (midPoints.IsCreated) midPoints.Dispose();
        }

        public void SetEdgePointList(List<Transform> newLeftEdgePointList, List<Transform> newRightEdgePointList, List<CurveType> newCurveTypeList)
        {
            leftEdgePointList = newLeftEdgePointList;
            rightEdgePointList = newRightEdgePointList;
            curveTypeList = newCurveTypeList;

            // Calculate total point count 
            totalPointCount = (leftEdgePointList.Count - 1) * (segmentCount + 1);

            // Dispose old arrays if they exist 
            DisposeNativeArrays();

            // Allocate native arrays 
            leftEdgePoints = new NativeArray<float3>(leftEdgePointList.Count, Allocator.Persistent);
            rightEdgePoints = new NativeArray<float3>(rightEdgePointList.Count, Allocator.Persistent);
            curveTypes = new NativeArray<CurveType>(curveTypeList.Count, Allocator.Persistent);
            leftEdgeResults = new NativeArray<float3>(totalPointCount, Allocator.Persistent);
            rightEdgeResults = new NativeArray<float3>(totalPointCount, Allocator.Persistent);
            midPoints = new NativeArray<float3>(totalPointCount, Allocator.Persistent);

            // Allocate mesh data arrays 
            vertices = new Vector3[totalPointCount * 2];
            triangles = new int[(totalPointCount - 1) * 6];
        }

        public static CurveType GetCurveType(string stringKey)
        {
            if (string.IsNullOrEmpty(stringKey)) return CurveType.Straight;

            if (stringKey.Equals(CurveType.Acceleration.ToString()))
            {
                return CurveType.Acceleration;
            }
            else if (stringKey.Equals(CurveType.Deceleration.ToString()))
            {
                return CurveType.Deceleration;
            }
            else
            {
                return CurveType.Straight;
            }
        }

        private void LateUpdate()
        {
            if (!isInitialized || leftEdgePointList == null || leftEdgePointList.Count == 0)
                return;

            UpdateTransformPositions();
            ScheduleMeshJob();
        }

        private void UpdateTransformPositions()
        {
            // Update native arrays with transform positions 
            for (int i = 0; i < leftEdgePointList.Count; i++)
            {
                var transform = leftEdgePointList[i];
                leftEdgePoints[i] = new float3(transform.localPosition.x, transform.localPosition.y, transform.localPosition.z);

                transform = rightEdgePointList[i];
                rightEdgePoints[i] = new float3(transform.localPosition.x, transform.localPosition.y, transform.localPosition.z);

                curveTypes[i] = curveTypeList[i];
            }
        }

        private void ScheduleMeshJob()
        {
            // Create and schedule the job 
            var job = new CurveMeshJob
            {
                leftEdgePoints = leftEdgePoints,
                rightEdgePoints = rightEdgePoints,
                curveTypes = curveTypes,
                segmentCount = segmentCount,
                algorithm = curveAlgorithm,
                leftEdgeResults = leftEdgeResults,
                rightEdgeResults = rightEdgeResults,
                midPoints = midPoints
            };

            // Schedule with maximum parallelism 
            JobHandle handle = job.Schedule(totalPointCount, 64);
            handle.Complete();

            // Update the mesh 
            UpdateMesh();
        }

        private void UpdateMesh()
        {
            // Copy results to vertices array 
            for (int i = 0; i < totalPointCount; i++)
            {
                var leftPoint = leftEdgeResults[i];
                var rightPoint = rightEdgeResults[i];

                vertices[i] = new Vector3(leftPoint.x, leftPoint.y, leftPoint.z);
                vertices[totalPointCount + i] = new Vector3(rightPoint.x, rightPoint.y, rightPoint.z);
            }

            // Update triangles 
            for (int i = 0; i < totalPointCount - 1; i++)
            {
                int start = i;
                int startNext = i + 1;

                // First triangle 
                triangles[i * 6] = start;
                triangles[i * 6 + 1] = totalPointCount + start;
                triangles[i * 6 + 2] = totalPointCount + startNext;

                // Second triangle 
                triangles[i * 6 + 3] = start;
                triangles[i * 6 + 4] = totalPointCount + startNext;
                triangles[i * 6 + 5] = startNext;
            }

            // Update the mesh 
            curveMesh.Clear();
            curveMesh.vertices = vertices;
            curveMesh.triangles = triangles;
            curveMesh.RecalculateNormals();
        }
    }
}