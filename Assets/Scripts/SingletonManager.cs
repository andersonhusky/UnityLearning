using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SingletonManager<T> where T : new()
{
    private static readonly Lazy<T> _lazy = new Lazy<T>(() => new T());
    public static T Instance => _lazy.Value;
}
