using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Unity.Mathematics;
using UnityEngine;
using UnityMap.Managers;

public class StructToStringSimulator : MonoBehaviour
{
    public int timePerFrame = 1000;
    public bool isLogArray = false;
    public bool isLogMatrix = false;
    private TestData testData;

    // Start is called before the first frame update
    void Start()
    {
    }

    // Update is called once per frame
    void Update()
    {
        CircleCall();
    }

    private void CircleCall()
    {
        for (int i = 0; i < timePerFrame; ++i)
        {
            // 直接调用StructToString测试
            var sb = StringBuilderPool.Acquire();
            UnityLogManager.GetStructInfoStringRecur(testData, sb, isLogArray: isLogArray);
            StringBuilderPool.GetStringAndRelease(sb);
        }
    }
}
