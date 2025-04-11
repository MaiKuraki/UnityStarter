using System.Collections.Generic;
using CycloneGames.MeshRender;
using UnityEngine;
using static CycloneGames.MeshRender.CurveMesh3D;

public class CurveMeshController : MonoBehaviour
{
    [SerializeField] private CurveMesh3D curveMesh3D;

    [SerializeField] List<Transform> leftEdgePointList = new List<Transform>();
    [SerializeField] List<Transform> rightEdgePointList = new List<Transform>();
    [SerializeField] List<CurveType> curveTypeList = new List<CurveType>();

    public void Start()
    {
        curveMesh3D.SetEdgePointList(leftEdgePointList, rightEdgePointList, curveTypeList);
    }
}
