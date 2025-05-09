using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Unity.Collections;

namespace UnityMap.Managers
{
    public struct UnityLogManager
    {
        #region 缓存减少gc
        private static readonly string NullStr = "null";
        private static readonly int MaxIndentLevel = 10;
        private static readonly int MaxElementsToShow = 10; // 限制打印的元素数量
        private static readonly Lazy<string[]> LazyIndentCache = new Lazy<string[]>(() =>
        {
            var cache = new string[MaxIndentLevel + 1];
            for (int i = 0; i < cache.Length; ++i)
            {
                cache[i] = new string(' ', i * 4);
            }
            return cache;
        });
        private static readonly string TabStr = "    ";
        private static readonly string HyphenStr = "- ";
        private static readonly char ColonStr = ':';
        private static readonly string NewLineStr = ";\n";
        private static readonly char LeftBrackets = '{';
        private static readonly char RightBrackets = '}';
        private static readonly char LeftSquareBrackets = '[';
        private static readonly char RightSquareBrackets = ']';
        private static readonly char DotStr = ',';
        private static readonly string NotLoggedStr = " (Not Logged)";
        private static readonly string NotCreatedStr = " (Not Created)";

        private static readonly string FixedStringStr = "FixedString";
        private static readonly string UCollectionsStr = "Unity.Collections";
        private static readonly string UnsafeStr = "Unsafe";
        private static readonly string NativeStr = "Native";

        private static readonly ConcurrentDictionary<Type, FieldInfo[]> _cachedFields = new();
        private static readonly ConcurrentDictionary<Type, PropertyInfo> _cachedIsCreatedProps = new();
        private static readonly ConcurrentDictionary<Type, PropertyInfo> _cachedLengthProps = new();
        private static readonly ConcurrentDictionary<Type, string> _cachedTypeNames = new();
        #endregion

        private static readonly string IndentCheckStr = "... 递归层级过深，停止记录 ...";
        /// <summary>
        /// Add Comment by White Hong - 2025-04-11 22:44:50
        /// 递归打印一个对象的全部信息
        /// <summary>
        public static void GetStructInfoStringRecur(object structObj, StringBuilder sb, int indentLevel = 0, bool isLogArray = false, bool isNeedIndent = true)
        {
            if (structObj == null)
            {
                sb.Append(NullStr);
                return;
            }

            // 添加递归深度保护，防止栈溢出
            if (indentLevel > MaxIndentLevel)
            {
                sb.Append(IndentCheckStr);
                return;
            }

            Type type = structObj.GetType();
            string indent = LazyIndentCache.Value[indentLevel];
            // var sb = StringBuilderPool.Acquire();

            // 某些递归调用的结构在此处不需要增加tab对齐
            if(isNeedIndent)    
            {
                sb.Append(indent);
            }

            // 处理基本类型
            if (type.IsPrimitive || type == typeof(string) || type.IsEnum)
            {
                sb.Append(structObj).Append(NewLineStr);
            }
            // 不打印的特殊类型
            else if (type.Name.Contains(FixedStringStr))
            {
                sb.Append(type.Name).Append(NotLoggedStr).Append(NewLineStr);
            }
            // 安全处理 UnsafeHashMap 和 UnsafeList 类型
            else if (type.Namespace?.Contains(UCollectionsStr) == true &&
                (type.Name.Contains(UnsafeStr) || type.Name.Contains(NativeStr)))
            {
                HandleUnityCollectionsType(type, structObj, sb, indent, indentLevel, isLogArray);
            }
            else
            {
                // 复杂结构递归打印
                sb.Append(type.Name).Append(LeftBrackets).AppendLine();
                try
                {
                    var fields = _cachedFields.GetOrAdd(type, t =>
                        t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));

                    for (int i = 0; i < fields.Length; ++i)
                    {
                        AppendFieldInfo(fields[i], structObj, sb, indent, indentLevel, isLogArray,
                                        i < fields.Length - 1 ? DotStr : RightBrackets);
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"{indent}    获取字段错误: {ex.Message}");
                }
            }

            return;
        }

        /// <summary>
        /// Add Comment by White Hong - 2025-05-07 17:10:10
        /// 按照field info打印结构体，每个类型的结构体的field info存在一个dictionary中
        /// <summary>
        private static void AppendFieldInfo(FieldInfo field, object obj, StringBuilder sb, string indent, int indentLevel, bool isLogArray, char endWith)
        {
            try
            {
                var value = field.GetValue(obj);
                // 添加公共前缀
                sb.Append(indent).Append(TabStr).Append(HyphenStr).Append(field.Name).Append(ColonStr);

                // 递归打印结构体
                if (field.FieldType.IsValueType && !field.FieldType.IsPrimitive && field.FieldType != typeof(decimal))
                {
                    sb.AppendLine();
                    GetStructInfoStringRecur(value, sb, indentLevel + 1, isLogArray: isLogArray, isNeedIndent: true);
                }
                // 常见类型，直接打印
                else
                {
                    AppendValue(sb, value);
                    sb.Append(endWith).AppendLine();
                }
            }
            catch (Exception ex)
            {
                sb.Append(indent).Append("    - Field[").Append(field.Name).Append("]: 访问错误(")
                    .Append(ex.Message).AppendLine(")");
            }
        }

        /// <summary>
        /// Add Comment by White Hong - 2025-05-07 17:09:58
        /// 打印简单类型
        /// <summary>
        private static void AppendValue(StringBuilder sb, object value)
        {
            switch (value)
            {
                case null:
                    sb.Append(NullStr);
                    break;
                case int i:
                    sb.Append(i);
                    break;
                case float f:
                    sb.Append(f);
                    break;
                case bool b:
                    sb.Append(b);
                    break;
                default:
                    sb.Append(value.ToString());
                    break;
            }
        }

        private static readonly string IsCreatedStr = "IsCreated";
        private static readonly string LengthStr = "Length";
        private static string LengthLeftStr = " (Length: ";
        private static void HandleUnityCollectionsType(Type type, object obj, StringBuilder sb, string indent, int indentLevel, bool isLogArray)
        {
            // 1. 检查 IsCreated 属性
            bool isCreated = false;
            var isCreatedProp = _cachedIsCreatedProps.GetOrAdd(type, t =>
                t.GetProperty(IsCreatedStr));
            if (isCreatedProp != null)
            {
                try { isCreated = (bool)isCreatedProp.GetValue(obj); }
                catch { isCreated = false; }
            }
            if (!isCreated)
            {
                sb.Append(indent).Append(type.Name).Append(NotCreatedStr).Append(NewLineStr);
                return;
            }

            // 2. 处理带 Length 的类型
            var lengthProp = _cachedLengthProps.GetOrAdd(type, t =>
                t.GetProperty(LengthStr));
            var typeName = _cachedTypeNames.GetOrAdd(type, t =>
                t.GetGenericArguments()[0].Name);
            if (lengthProp != null)
            {
                try
                {
                    int length = (int)lengthProp.GetValue(obj);

                    // 特殊处理 NativeArray 的详细内容打印
                    if (isLogArray && type.IsGenericType &&
                        type.GetGenericTypeDefinition() == typeof(NativeArray<>))
                    {
                        GetNativeArrayString(obj, sb, indent, typeName, length, indentLevel, isLogArray);
                    }
                    // 其他集合类型仅打印摘要信息
                    else
                    {
                        sb.Append(indent).Append(typeName).Append(LengthLeftStr).Append(length).Append(RightBrackets).Append(NewLineStr);
                    }
                    return;
                }
                catch { /* 忽略错误 */ }
            }

            // 3. 默认情况：仅打印类型名
            sb.Append(indent).Append(typeName).Append(NewLineStr);
        }
        private static readonly string NativeArrayLeftStr = "NativeArray<";
        private static readonly string NativeArrayMidStr = "> (Length: ";
        private static readonly string NativeArrayRightStr = ") {";
        private static readonly string EllipsesLeftStr = "    ... (showing first ";
        private static readonly string EllipsesMidStr = " of ";
        private static readonly string EllipsesRightStr = ")";
        private static void GetNativeArrayString(object structObj, StringBuilder sb, string indent, string typeName, int length,
                                                    int indentLevel, bool isLogArray)
        {
            // 详细打印 NativeArray 内容
            sb.Append(indent).Append(NativeArrayLeftStr).Append(typeName).Append(NativeArrayMidStr).Append(length).Append(NativeArrayRightStr).AppendLine();

            // 获取 NativeArray 的迭代器或索引访问
            var enumerable = structObj as System.Collections.IEnumerable;
            int count = 0;

            foreach (var item in enumerable)
            {
                if (count >= MaxElementsToShow)
                {
                    sb.Append(indent).Append(EllipsesLeftStr).Append(MaxElementsToShow).Append(EllipsesMidStr).Append(length).Append(EllipsesRightStr).AppendLine();
                    break;
                }

                // 没有换行，因此不用加indent
                sb.Append(indent).Append(TabStr).Append(LeftSquareBrackets).Append(count).Append(RightSquareBrackets).Append(ColonStr);
                GetStructInfoStringRecur(item, sb, indentLevel + 1, isLogArray: isLogArray, isNeedIndent: false);
                count++;
            }

            sb.Append(indent).Append(RightBrackets).AppendLine();
        }
    }

    /// <summary>
    /// Add Comment by White Hong - 2025-04-26 00:58:25
    /// 核心优化，缓存StringBuilder
    /// <summary>
    public static class StringBuilderPool
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
}