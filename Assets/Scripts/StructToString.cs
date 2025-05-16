using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
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
        private static readonly ConcurrentDictionary<Type, string> _cachedNativeTypeNames = new();
        private static readonly ConcurrentDictionary<Type, string> _cachedTypeNames = new();
        private static readonly ConcurrentDictionary<Type, string> _cachedTypeNameSpace = new();
        #endregion

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

        private static readonly string IndentCheckStr = "... 递归层级过深，停止记录 ...";
        /// <summary>
        /// Add Comment by White Hong - 2025-04-11 22:44:50
        /// 递归打印一个对象的全部信息
        /// <summary>
        public static void GetStructInfoStringRecur<T>(T structObj, StringBuilder sb, int indentLevel = 0, Type structObjType = null, bool isLogArray = false, bool isNeedIndent = true)   // 优化点，范型代替object，避免装箱
        {
            if (!typeof(T).IsValueType && structObj == null)    // 优化点，值类型不判null，避免装箱
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

            Type type = structObjType == null? typeof(T) : structObjType;      // 优化点：从object.GetType()改成typeof(T)
            var typeName = _cachedTypeNames.GetOrAdd(type, t => t.Name);    // 优化点，避免使用type.name
            var typeNameSpace = _cachedTypeNameSpace.GetOrAdd(type, t => t.Namespace);
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
            else if (typeName.Contains(FixedStringStr))
            {
                sb.Append(typeName).Append(NotLoggedStr).Append(NewLineStr);
            }
            // 安全处理 UnsafeHashMap 和 UnsafeList 类型
            else if (typeNameSpace?.Contains(UCollectionsStr) == true &&
                (typeName.Contains(UnsafeStr) || typeName.Contains(NativeStr)))
            {
                HandleUnityCollectionsType(type, structObj, sb, indent, indentLevel, isLogArray);
            }
            else
            {
                // 复杂结构递归打印
                sb.Append(typeName).Append(LeftBrackets).AppendLine();
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
        private static void AppendFieldInfo<T>(FieldInfo field, T obj, StringBuilder sb, string indent, int indentLevel, bool isLogArray, char endWith)
        {
            try
            {
                // 获取或创建字段访问器
                var getter = (FieldGetter<T, object>)_fieldAccessors.GetOrAdd(field, f => CreateFieldGetter<T>(f)); 
                // 获取字段值（无装箱）
                object value = getter(ref obj);    // 优化点，避免装箱，单独测过有明显gc降低
                // object value = field.GetValue(obj);

                // 添加公共前缀
                sb.Append(indent).Append(TabStr).Append(HyphenStr).Append(field.Name).Append(ColonStr);

                // 递归打印结构体
                if (field.FieldType.IsValueType && !field.FieldType.IsPrimitive && field.FieldType != typeof(decimal))
                {
                    sb.AppendLine();
                    GetStructInfoStringRecur(value, sb, indentLevel + 1, isLogArray: isLogArray, structObjType: field.FieldType, isNeedIndent: true);
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
        private static void HandleUnityCollectionsType<T>(Type type, T obj, StringBuilder sb, string indent, int indentLevel, bool isLogArray)
        {
            var typeName = _cachedTypeNames.GetOrAdd(type, t => t.Name);
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
                sb.Append(indent).Append(typeName).Append(NotCreatedStr).Append(NewLineStr);
                return;
            }

            // 2. 处理带 Length 的类型
            var lengthProp = _cachedLengthProps.GetOrAdd(type, t =>
                t.GetProperty(LengthStr));
            var typeNativeName = _cachedNativeTypeNames.GetOrAdd(type, t =>
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
                        sb.Append(indent).Append(typeNativeName).Append(LengthLeftStr).Append(length).Append(RightBrackets).Append(NewLineStr);
                    }
                    return;
                }
                catch { /* 忽略错误 */ }
            }

            // 3. 默认情况：仅打印类型名
            sb.Append(indent).Append(typeNativeName).Append(NewLineStr);
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
}