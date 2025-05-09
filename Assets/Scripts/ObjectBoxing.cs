using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Unity.Mathematics;
using UnityEngine;

public class ObjectBoxing : MonoBehaviour
{
    public enum TestPart
    {
        InputParam,
        GetType,
    }
    public enum InputParam
    {
        Object,
        Struct
    }

    public enum GetTypeEnum
    {
        GetType,
    }
    public int timePerFrame = 1000;
    public TestPart testPart;
    public InputParam inputParam;
    public GetTypeEnum getTypeEnum;
    private TestData testData;
    private FieldInfo[] fieldInfos;

    // Start is called before the first frame update
    void Start()
    {
        Type t = testData.GetType();
        fieldInfos = t.GetFields();
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
            switch (testPart)
            {
                case TestPart.InputParam:
                    TestInputParam();
                    break;
                case TestPart.GetType:
                    TestGetType();
                    break;
                default:
                    break;
            }
        }
    }

    #region 装箱值获取检测
    private void TestGetType()
    {
        switch (getTypeEnum)
        {
            case GetTypeEnum.GetType:
                TestGetDirectly();
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// Add Comment by White Hong - 2025-05-09 17:53:24
    /// 完全没有gc
    /// <summary>
    private void TestGetDirectly()
    {
        for (int i = 0; i < fieldInfos.Length; ++i)
        {
            G(fieldInfos[i], testData);
        }
    }

    private void G(FieldInfo fieldInfo, object obj)
    {
        var v = fieldInfo.GetValue(obj);
    }
    #endregion

    #region 入参gc测试
    private void TestInputParam()
    {
        switch (inputParam)
        {
            case InputParam.Object:
                TestObjectBoxing(testData);
                break;
            case InputParam.Struct:
                TestGenericFunction(testData);
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// Add Comment by White Hong - 2025-05-09 15:48:40
    /// 循环10W次，每帧耗时约8.9ms
    /// <summary>
    private void TestObjectBoxing(object obj)
    {
    }

    /// <summary>
    /// Add Comment by White Hong - 2025-05-09 15:51:07
    /// 循环10W次，每帧耗时约0.1ms，没有任何gc
    /// <summary>
    private void TestGenericFunction<T>(T obj) where T : struct
    {

    }
    #endregion

}
