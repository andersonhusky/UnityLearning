using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Unity.Mathematics;
using UnityEngine;
using UnityMap.Managers;

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
        GetDirectly,
        GetByDynamicMethod,
    }
    public int timePerFrame = 1000;
    public TestPart testPart;
    public InputParam inputParam;
    public GetTypeEnum getTypeEnum;
    private TestData testData;

    private FieldInfo[] fieldInfos;
    private delegate TValue FieldGetter<T, TValue>(ref T obj);
    private static readonly ConcurrentDictionary<FieldInfo, Delegate> _fieldAccessors = new();
    private static FieldGetter<T, object> CreateFieldGetter<T>(FieldInfo field)
    {
        var method = new DynamicMethod(
            $"Get_{field.DeclaringType.Name}_{field.Name}",
            typeof(object),
            new[] { typeof(T).MakeByRefType() },
            field.DeclaringType,
            skipVisibility: true);

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, field);
        if (field.FieldType.IsValueType)
            il.Emit(OpCodes.Box, field.FieldType);
        il.Emit(OpCodes.Ret);

        return (FieldGetter<T, object>)method.CreateDelegate(typeof(FieldGetter<T, object>));
    }

    // Start is called before the first frame update
    void Start()
    {
        Type t = testData.GetType();
        fieldInfos = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Debug.Log("fieldInfos.Length: " + fieldInfos.Length);
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
            case GetTypeEnum.GetDirectly:
                TestGetDirectly();
                break;
            case GetTypeEnum.GetByDynamicMethod:
                TestGetByDynamicMethod();
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// Add Comment by White Hong - 2025-05-09 17:53:24
    /// 循环1000次，每帧耗时约9ms
    /// <summary>
    private void TestGetDirectly()
    {
        for (int i = 0; i < fieldInfos.Length; ++i)
        {
            var v = fieldInfos[i].GetValue(testData);
            // G(fieldInfos[i], testData);
        }
    }

    /// <summary>
    /// Add Comment by White Hong - 2025-05-09 19:26:50
    /// 循环1000次，每帧耗时约2.5ms
    /// <summary>
    private void TestGetByDynamicMethod()
    {
        for (int i = 0; i < fieldInfos.Length; ++i)
        {
            // 获取或创建字段访问器
            var getter = (FieldGetter<TestData, object>)_fieldAccessors.GetOrAdd(fieldInfos[i], f => CreateFieldGetter<TestData>(f));
            var v = getter(ref testData);
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
