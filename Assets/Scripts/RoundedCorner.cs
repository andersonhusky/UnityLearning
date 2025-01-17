using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
public class RoundedCorner : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        Image image = GetComponent<Image>();

        //获取RectTransform
        RectTransform rectTransform = GetComponent<RectTransform>();

        // 获取屏幕空间中的四个角
        Vector3[] worldCorners = new Vector3[4];
        rectTransform.GetWorldCorners(worldCorners);

        // 将世界空间坐标转换为屏幕空间坐标
        Vector3 screenCorner1 = Camera.main.WorldToScreenPoint(worldCorners[0]);
        Vector3 screenCorner2 = Camera.main.WorldToScreenPoint(worldCorners[2]);

        // 计算长宽
        float width = Mathf.Abs(screenCorner2.x - screenCorner1.x);
        float height = Mathf.Abs(screenCorner2.y - screenCorner1.y);

        Debug.Log($"{name} Image size in pixels: {width}/{height}, {width}");

        Vector2 size = new (width / 100, height / 100);

        Material material = new Material(image.material);
        material.SetVector("_Resolution", size);

        image.material = material;
    }
}
