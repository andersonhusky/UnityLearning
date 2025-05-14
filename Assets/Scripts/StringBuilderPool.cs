using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Add Comment by White Hong - 2025-04-26 00:58:25
/// 核心优化，缓存StringBuilder
/// <summary>
public static class StringBuilderPool
{
    // 每个线程独立的对象池（万一在线程中用到，避免线程竞争）
    [ThreadStatic]
    private static Stack<StringBuilder> _pool;
    private const int DefaultCapacity = 1024;

    public static StringBuilder Acquire(int capacity = DefaultCapacity)
    {
        _pool ??= new Stack<StringBuilder>();
        if (_pool.TryPop(out var sb))
        {
            if (sb.Capacity < capacity)
            {
                sb.Capacity = capacity;
            }
            sb.Clear();
            return sb;
        }
        return new StringBuilder(DefaultCapacity);  // 新建
    }

    public static string GetStringAndRelease(StringBuilder sb)
    {
        string result = sb.ToString();
        Release(sb);
        return result;
    }

    public static void Release(StringBuilder sb)
    {
        if (sb == null) return;

        sb.Clear();
        _pool.Push(sb);
    }
}

public static class StringBuilderPoolOld
{
    // 每个线程独立的对象池（万一在线程中用到，避免线程竞争）
    [ThreadStatic]
    private static ConcurrentStack<StringBuilder> _pool;
    private const int DefaultCapacity = 1024;

    public static StringBuilder Acquire(int capacity = DefaultCapacity)
    {
        _pool ??= new ConcurrentStack<StringBuilder>();
        if (_pool.TryPop(out var sb))
        {
            if (sb.Capacity < capacity)
            {
                sb.Capacity = capacity;
            }
            sb.Clear();
            return sb;
        }
        return new StringBuilder(DefaultCapacity);  // 新建
    }

    public static string GetStringAndRelease(StringBuilder sb)
    {
        string result = sb.ToString();
        Release(sb);
        return result;
    }

    public static void Release(StringBuilder sb)
    {
        if (sb == null) return;

        sb.Clear();
        _pool.Push(sb);
    }
}
