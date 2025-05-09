using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

public struct TestData
{
    private NativeArray<float3> _vertices;
    private NativeArray<float3> _offsetNormals;
    private NativeArray<int> _indices;
    private NativeArray<float2> _uvs;
    private NativeArray<float> _linkIndex;
    public MXEngineAttributeRoute _attribute;
    private Matrix4x4 _matrix;
    private ulong _tileID;
    private ulong _meshID;

    private int _courseID;
    private int _routeIndex;
    private ViewUnityVector2 _oneDotScale;
    private int _routeType;
    private int _currentLinkIndex;
    private bool _isGlobalMap;
    private uint _iDrawOrder;
    private uint _nRouteTotalLength;
    private NativeArray<TrafficTransition> _trafficStatus;

    private uint deviceID;
    private int viewID;
    private ulong _mapFrame;
}

public struct MXEngineAttributeRoute
{
    public int CourseID;
    public int RouteIndex;
    public IntPtr LinkIndex;
    public int LinkIndexCount;
    public int DrawType;
    public IntPtr Status;
    public int StatusCount;
    public ViewUnityVector2 OneDotScale;
    public int RouteType;
    [MarshalAs(UnmanagedType.I1)] public bool IsGlobalMap;
    public float EarthRadius;
    public int CurrentLinkIndex;
    public uint IDrawOrder;
    public uint NRouteTotalLength;
}

public struct ViewUnityVector2
{
    public float x;
    public float y;
}

public struct TrafficTransition
{
    public byte startStatus;/**< [含义]: 前置渐变信息：  | [取值范围]:(0-未知；1-畅通；2-缓行；3-拥堵；4-严重拥堵；5-无交通流；6-急速畅通) */
    public byte endStatus; /**< [含义]: 后置渐变信息：  | [取值范围]:(0-未知；1-畅通；2-缓行；3-拥堵；4-严重拥堵；5-无交通流；6-急速畅通) */
    public float mixRatio; /**< [含义]: 前置渐变信息:traffic渐变，最终颜色为 startStatus + (endStatus-startStatus）* ratio | [取值范围]:0.0 ~ 1 */
}