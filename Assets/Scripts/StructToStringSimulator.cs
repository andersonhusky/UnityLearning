using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using UnityMap.Managers;

public class StructToStringSimulator : MonoBehaviour
{
    public enum TestPart
    {
        StringBuilderPool,
        StructToString
    }
    public enum StackType
    {
        Stack,
        ConcurrentStack
    }
    public enum InputType
    {
        Object,
        Generic
    }
    public int timePerFrame = 1000;
    public bool isLogArray = false;
    public bool isLogMatrix = false;
    private TestData testData;
    private StringBuilder strPerFrame;
    public TestPart testPart;
    public StackType stackType;
    public InputType inputType;

    // Start is called before the first frame update
    void Start()
    {
        strPerFrame = new StringBuilder(2048);
    }

    // Update is called once per frame
    void Update()
    {
        CircleCall();
        // Debug.Log("StringBuilderPool.PoolSize(): " + StringBuilderPool.PoolSize());
    }

    private void CircleCall()
    {
        switch(testPart)
        {
            case TestPart.StringBuilderPool:
                TestStringBuilderPool();
                break;
            case TestPart.StructToString:
                TestStructToString();
                break;
            default:
                Debug.LogError("Error enum received, check!");
                break;
        }
    }

    private void TestStructToString()
    {
        strPerFrame.Clear();
        switch(inputType)
        {
            case InputType.Object:
                UseUnityLogManagerObj();
                break;
            case InputType.Generic:
                UseUnityLogManager();
                break;
            default:
                Debug.LogError("Error enum received, check!");
                break;
        }
        Debug.Log(strPerFrame.ToString());
    }

    private void UseUnityLogManagerObj()
    {
        for (int i = 0; i < timePerFrame; ++i)
        {
            // 直接调用StructToString测试
            var sb = StringBuilderPool.Acquire();
            UnityLogManagerObj.GetStructInfoStringRecur(testData, sb, isLogArray: isLogArray);
            // strPerFrame.Append(StringBuilderPool.GetStringAndRelease(sb));
            
            StringBuilderPool.Release(sb);
        }
    }

    private void UseUnityLogManager()
    {
        for (int i = 0; i < timePerFrame; ++i)
        {
            // 直接调用StructToString测试
            var sb = StringBuilderPool.Acquire();
            UnityLogManager.GetStructInfoStringRecur(testData, sb, isLogArray: isLogArray);
            // strPerFrame.Append(StringBuilderPool.GetStringAndRelease(sb));
            
            StringBuilderPool.Release(sb);
        }
    }

    #region 测试stringBuilder池的gc
    private void TestStringBuilderPool()
    {
        switch(stackType)
        {
            case StackType.Stack:
                UseStackPool();
                break;
            case StackType.ConcurrentStack:
                UseConcurrentStackPool();
                break;
            default:
                Debug.LogError("Error enum received, check!");
                break;
        }
    }

    /// <summary>
    /// Add Comment by White Hong - 2025-05-14 11:24:10
    /// 循环10000次，每帧约0.66ms，没有任何gc
    /// <summary>
    private void UseStackPool()
    {
        for (int i = 0; i < timePerFrame; ++i)
        {
            var sb = StringBuilderPool.Acquire();
            StringBuilderPool.Release(sb);
        }
    }

    /// <summary>
    /// Add Comment by White Hong - 2025-05-14 11:24:38
    /// 循环10000次，每帧约1.77ms，有gc
    /// <summary>
    private void UseConcurrentStackPool()
    {
        for (int i = 0; i < timePerFrame; ++i)
        {
            var sb = StringBuilderPoolOld.Acquire();
            StringBuilderPoolOld.Release(sb);
        }
    }
    #endregion
}
