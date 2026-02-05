using HT.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Experimental.Rendering;
//通过透明度来检测不规则按钮，需要在图片设置中打开可读
public class ButtonClickAlpha : HTBehaviour, IUpdateFrame, ICanvasRaycastFilter
{
    private Button _button;
    private Image _image;

    [Range(0, 1)]
    [SerializeField]
    private float alphaThreshold = 0.5f; // 可调透明度范围

    private bool _hasWarnedFallback;

    //初始化操作在 Awake 中完成（必须确保 base.Awake() 的存在）
    protected override void Awake()
    {
        base.Awake();

        // 自动挂载 Button 和 Image 组件
        _button = gameObject.GetComponent<Button>();
        if (_button == null)
        {
            _button = gameObject.AddComponent<Button>();
        }

        _image = gameObject.GetComponent<Image>();
        if (_image == null)
        {
            _image = gameObject.AddComponent<Image>();
        }

        // 需要让 Image 参与 GraphicRaycaster
        _image.raycastTarget = true;
        _button.targetGraphic = _image;

        WarnIfFallbackWillBeUsed();
    }

    private void OnValidate()
    {
        if (alphaThreshold < 0) alphaThreshold = 0;
        if (alphaThreshold > 1) alphaThreshold = 1;

        WarnIfFallbackWillBeUsed();
    }

    /// <summary>
    /// UI 射线检测过滤：优先按像素 alpha（可读纹理且非 Crunch），否则退化为 Sprite Mesh（Tight 网格）区域。
    /// </summary>
    public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
    {
        if (alphaThreshold <= 0)
            return true;

        if (alphaThreshold > 1)
            return false;

        if (_image == null)
            _image = GetComponent<Image>();

        if (_image == null || _image.sprite == null)
            return true;

        var sprite = _image.sprite;
        var texture2D = sprite.texture as Texture2D;

        bool canSampleAlpha = texture2D != null
                              && texture2D.isReadable
                              && !GraphicsFormatUtility.IsCrunchFormat(texture2D.format);

        if (canSampleAlpha)
            return AlphaHitTest(screenPoint, eventCamera, sprite, texture2D);

        return SpriteMeshHitTest(screenPoint, eventCamera, sprite);
    }

    private bool AlphaHitTest(Vector2 screenPoint, Camera eventCamera, Sprite sprite, Texture2D texture2D)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_image.rectTransform, screenPoint, eventCamera, out var local))
            return false;

        Rect rect = _image.GetPixelAdjustedRect();
        rect = ApplyPreserveAspect(rect, sprite);

        // 以左下角为原点
        local.x += _image.rectTransform.pivot.x * rect.width;
        local.y += _image.rectTransform.pivot.y * rect.height;

        if (local.x < 0 || local.y < 0 || local.x > rect.width || local.y > rect.height)
            return false;

        // Image.Type.Simple / Filled 的坐标映射（足够覆盖按钮常用场景）
        Rect spriteRect = sprite.rect;
        float texSpaceX = spriteRect.x + local.x * spriteRect.width / rect.width;
        float texSpaceY = spriteRect.y + local.y * spriteRect.height / rect.height;

        float u = texSpaceX / texture2D.width;
        float v = texSpaceY / texture2D.height;

        try
        {
            return texture2D.GetPixelBilinear(u, v).a >= alphaThreshold;
        }
        catch
        {
            // 避免因为运行时纹理状态变化导致点击彻底失效
            return true;
        }
    }

    private bool SpriteMeshHitTest(Vector2 screenPoint, Camera eventCamera, Sprite sprite)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_image.rectTransform, screenPoint, eventCamera, out var local))
            return false;

        Rect rect = _image.GetPixelAdjustedRect();
        rect = ApplyPreserveAspect(rect, sprite);

        // 以左下角为原点
        local.x += _image.rectTransform.pivot.x * rect.width;
        local.y += _image.rectTransform.pivot.y * rect.height;

        if (local.x < 0 || local.y < 0 || local.x > rect.width || local.y > rect.height)
            return false;

        var vertices = sprite.vertices;
        var triangles = sprite.triangles;
        if (vertices == null || vertices.Length < 3 || triangles == null || triangles.Length < 3)
            return true;

        float nx = rect.width <= 0 ? 0 : (local.x / rect.width);
        float ny = rect.height <= 0 ? 0 : (local.y / rect.height);

        Bounds b = sprite.bounds;
        var point = new Vector2(
            Mathf.Lerp(b.min.x, b.max.x, nx),
            Mathf.Lerp(b.min.y, b.max.y, ny)
        );

        for (int i = 0; i < triangles.Length; i += 3)
        {
            int ia = triangles[i];
            int ib = triangles[i + 1];
            int ic = triangles[i + 2];

            if (ia < 0 || ib < 0 || ic < 0 || ia >= vertices.Length || ib >= vertices.Length || ic >= vertices.Length)
                continue;

            if (PointInTriangle(point, vertices[ia], vertices[ib], vertices[ic]))
                return true;
        }

        return false;
    }

    private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        // Barycentric technique
        Vector2 v0 = c - a;
        Vector2 v1 = b - a;
        Vector2 v2 = p - a;

        float dot00 = Vector2.Dot(v0, v0);
        float dot01 = Vector2.Dot(v0, v1);
        float dot02 = Vector2.Dot(v0, v2);
        float dot11 = Vector2.Dot(v1, v1);
        float dot12 = Vector2.Dot(v1, v2);

        float denom = dot00 * dot11 - dot01 * dot01;
        if (Mathf.Approximately(denom, 0f))
            return false;

        float invDenom = 1f / denom;
        float u = (dot11 * dot02 - dot01 * dot12) * invDenom;
        float v = (dot00 * dot12 - dot01 * dot02) * invDenom;

        return (u >= 0) && (v >= 0) && (u + v <= 1);
    }

    private Rect ApplyPreserveAspect(Rect rect, Sprite sprite)
    {
        if (_image == null || !_image.preserveAspect)
            return rect;

        if (sprite == null)
            return rect;

        float spriteW = sprite.rect.width;
        float spriteH = sprite.rect.height;
        if (spriteW <= 0 || spriteH <= 0 || rect.width <= 0 || rect.height <= 0)
            return rect;

        float spriteRatio = spriteW / spriteH;
        float rectRatio = rect.width / rect.height;

        if (spriteRatio > rectRatio)
        {
            float newH = rect.width / spriteRatio;
            float yOffset = (rect.height - newH) * 0.5f;
            rect.y += yOffset;
            rect.height = newH;
        }
        else
        {
            float newW = rect.height * spriteRatio;
            float xOffset = (rect.width - newW) * 0.5f;
            rect.x += xOffset;
            rect.width = newW;
        }

        return rect;
    }

    private void WarnIfFallbackWillBeUsed()
    {
        if (_hasWarnedFallback)
            return;

        if (alphaThreshold <= 0)
            return;

        if (_image == null)
            _image = GetComponent<Image>();

        if (_image == null || _image.sprite == null)
            return;

        var texture2D = _image.sprite.texture as Texture2D;
        bool isCrunch = texture2D != null && GraphicsFormatUtility.IsCrunchFormat(texture2D.format);
        bool notReadable = texture2D != null && !texture2D.isReadable;

        if (isCrunch || notReadable)
        {
            _hasWarnedFallback = true;
            Debug.LogWarning("[ButtonClickAlpha] 当前 Sprite 纹理为 Crunch 压缩或不可读，无法按 alphaThreshold 采样像素；将退化为 Sprite Mesh 区域命中（不支持半透明阈值）。如需精确阈值，请在纹理 Import Settings 关闭 Crunch 并勾选 Read/Write Enabled。", this);
        }
    }

    //等同于 Update 方法，不过当主框架进入暂停状态时，此方法也会停止调用（Main.Current.Pause = true）
    public void OnUpdateFrame()
    {
        
    }
}