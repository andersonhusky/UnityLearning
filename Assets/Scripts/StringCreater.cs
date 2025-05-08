using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public class StringCreater : MonoBehaviour
{
    public enum CreateStringBy
    {
        New,
        Copy,
    }

    public enum CombineStringBy
    {
        Add,
        Append,
    }

    public enum BoxingSkipBy
    {
        None,
        ToString,
    }

    public enum TestPart
    {
        Create,
        Combine,
        Boxing
    }

    public CreateStringBy createStringBy;
    public CombineStringBy combineStringBy;
    public BoxingSkipBy boxingSkipBy;
    public TestPart testPart;
    public int timePerFrame = 1000;
    private StringBuilder s = new StringBuilder(1024 * 4);
    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        switch(testPart)
        {
            case TestPart.Create:
                TestCreate();
                break;
            case TestPart.Combine:
                TestCombine();
                break;
            case TestPart.Boxing:
                TestBoxing();
                break;
            default:
                break;
        }
    }

    #region 测试装箱
    private void TestBoxing()
    {
        switch(boxingSkipBy)
        {
            case BoxingSkipBy.None:
                BoxingSkipByNone();
                break;
            case BoxingSkipBy.ToString:
                BoxingSkipByToString();
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// Add Comment by White Hong - 2025-05-08 20:20:38
    /// 循环4500次，每帧耗时约0.8-0.9ms
    /// <summary>
    private void BoxingSkipByNone()
    {
        s.Clear();
        for (int i = 0; i < timePerFrame; ++i)
        {
            s.Append("测试用例").Append(i);
        }
    }

    /// <summary>
    /// Add Comment by White Hong - 2025-05-08 20:20:38
    /// 循环4500次，每帧耗时约0.65-0.75ms
    /// <summary>
    private void BoxingSkipByToString()
    {
        s.Clear();
        for (int i = 0; i < timePerFrame; ++i)
        {
            s.Append("测试用例").Append(i.ToString());
        }
    }
    #endregion

    #region 测试字符串拼接
    private void TestCombine()
    {
        switch(combineStringBy)
        {
            case CombineStringBy.Add:
                CombineStringByAdd();
                break;
            case CombineStringBy.Append:
                CombineStringByAppend();
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// Add Comment by White Hong - 2025-05-08 17:08:02
    /// 循环4500次，每帧耗时约5-6ms，但是gc频率几乎达到每帧collection
    /// string str1 = "Hello";
    // str1 += " World";
    // 实际等价于：str1 = string.Concat(str1, " World");
    // 创建新字符串对象
    // 原字符串 "Hello" 成为垃圾内存
    // 新字符串 "Hello World" 分配新内存
    /// <summary>
    private void CombineStringByAdd()
    {
        string s = default;
        for (int i = 0; i < timePerFrame; ++i)
        {
            s += "测试用例";
        }
    }

    /// <summary>
    /// Add Comment by White Hong - 2025-05-08 19:37:26
    /// 循环4500次，每帧耗时约0.059ms，没有gc
    /// <summary>
    private void CombineStringByAppend()
    {
        s.Clear();
        for (int i = 0; i < timePerFrame; ++i)
        {
            s.Append("测试用例");
        }
    }
    #endregion

    #region 测试字符串创建
    private void TestCreate()
    {
        switch(createStringBy)
        {
            case CreateStringBy.New:
                CreateStringByNew();
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// Add Comment by White Hong - 2025-05-08 16:55:32
    /// 循环100000次，每帧耗时约10ms
    /// <summary>
    private void CreateStringByNew()
    {
        for (int i = 0; i < timePerFrame; ++i)
        {
            var s = new string("测试用例");
        }
    }

    /// <summary>
    /// Add Comment by White Hong - 2025-05-08 16:55:46
    /// 循环100000次，每帧耗时约0.026ms，没有任何gc
    /// <summary>
    private void CreateStringByCopy()
    {
        for (int i = 0; i < timePerFrame; ++i)
        {
            var s = "测试用例";
        }
    }
    #endregion
}
