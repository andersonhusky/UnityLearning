using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class ObjectPoolTestMono : MonoBehaviour
{
    public GameObject prefabA;
    public GameObject prefabB;
    public GameObject prefabC;
    public GameObject prefabD;
    public Transform Root;
    private Queue<GameObject> gameObjectQueue;

    public int Radius = 10;

    public int num = 1000;

    // Start is called before the first frame update
    void Start()
    {
        Root = this.transform;
        GameObjectPoolManager.Instance.Init();
        gameObjectQueue = new Queue<GameObject>();

        for (int i = 0; i < num; ++i)
        {
            CreateGo(prefabA);
            CreateGo(prefabB);
            CreateGo(prefabC);
            CreateGo(prefabD);
        }
    }

    // Update is called once per frame
    void Update()
    {
        while(gameObjectQueue.Count > 0)
        {
            DeleteGo(gameObjectQueue.Dequeue());
        }

        for (int i = 0; i < num; ++i)
        {
            CreateGo(prefabA);
            CreateGo(prefabB);
            CreateGo(prefabC);
            CreateGo(prefabD);
        }
    }

    private void CreateGo(GameObject prefab)
    {
        gameObjectQueue.Enqueue(GameObjectPoolManager.Instance.RequestGameObject(prefab, Root));
        gameObjectQueue.Last().transform.position = RandomPosition();
    }

    private void DeleteGo(GameObject go)
    {
        GameObjectPoolManager.Instance.ReturnGameObject(go);
    }

    private Vector3 RandomPosition()
    {
        return Random.insideUnitSphere * Radius;
    }
}
