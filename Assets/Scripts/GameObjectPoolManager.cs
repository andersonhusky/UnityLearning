using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameObjectPoolManager : SingletonManager<GameObjectPoolManager>
{
    // key:prefab id, value game object pool
    private Dictionary<int, Queue<GameObject>> mapGoPools;
    // store game object outside pool
    private Dictionary<GameObject, int> mapGOTag;
    private Vector3 farPosition;
    public void Init()
    {
        mapGoPools = new Dictionary<int, Queue<GameObject>>();
        mapGOTag = new Dictionary<GameObject, int>();
        farPosition = new Vector3(0, -50000, 0);
    }

    /// <summary>
    /// request a game object, type is "prefab", under "root", mark in the mapGOTag
    /// </summary>
    /// <param name="prefab"></param>
    /// <param name="root"></param>
    /// <returns></returns>
    public GameObject RequestGameObject(GameObject prefab, Transform root)
    {
        if(prefab is null)
        {
            Debug.LogError($"Receive empty prefab in GOPool, Check it!");
        }

        int tag = prefab.GetInstanceID();

        GameObject obj = GetFromPool(tag);

        if(obj is null)
        {
            obj = Object.Instantiate(prefab, root);
        }
        MarkAsOut(obj, tag);

        return obj;
    }

    /// <summary>
    /// return a go
    /// </summary>
    /// <param name="go"></param>
    public void ReturnGameObject(GameObject go)
    {
        // go.SetActive(false);
        go.transform.position = farPosition;

        if(!mapGOTag.ContainsKey(go))
        {
            Debug.LogError($"gamObject has not been marked!");
            return;
        }
        int tag = mapGOTag[go];
        RemoveMark(go);

        if(!mapGoPools.ContainsKey(tag))
        {
            Debug.LogError($"Return a go that is not generated from the object pool, check it!");
        }
        mapGoPools[tag].Enqueue(go);
    }

    private GameObject GetFromPool(int tag)
    {
        // tag not exist, init queue
        if(!mapGoPools.ContainsKey(tag))
        {
            mapGoPools[tag] = new Queue<GameObject>();
        }
        // tag exist and still remaining objects
        else if(mapGoPools[tag].Count > 0)
        {
            GameObject obj = mapGoPools[tag].Dequeue();
            // obj.SetActive(true);
            return obj;
        }

        return null;
    }

    private void MarkAsOut(GameObject go, int tag)
    {
        if(mapGOTag.ContainsKey(go))
        {
            Debug.LogError($"GameObject has been marked before, check it!");
        }
        else
        {
            mapGOTag.Add(go, tag);
        }
    }

    private void RemoveMark(GameObject go)
    {
        if(mapGOTag.ContainsKey(go))
        {
            mapGOTag.Remove(go);
        }
        else
        {
            Debug.LogError($"Remove out mark error, gamObject has not been marked!");
        }
    }
}
