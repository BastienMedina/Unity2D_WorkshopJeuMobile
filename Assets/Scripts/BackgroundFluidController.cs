using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RawImage))]
[ExecuteAlways]
public class BackgroundFluidController : MonoBehaviour
{
    [Header("Color Settings")]
    public Color colorA = new Color(0.1f, 0.4f, 0.6f, 1f);
    public Color colorB = new Color(0.4f, 0.1f, 0.6f, 1f);

    [Header("Animation Settings")]
    [Range(0.1f, 10f)] public float speed = 1.0f;
    [Range(1f, 50f)] public float scale = 5.0f;
    [Range(0f, 1f)] public float intensity = 0.5f;

    [Header("Art Style")]
    [Range(32, 1024)] public float pixelSize = 128f;
    [Range(2, 16)] public float posterizeSteps = 4f;

    private RawImage _rawImage;
    private Material _material;

    // Material property IDs for performance
    private static readonly int ColorAId = Shader.PropertyToID("_ColorA");
    private static readonly int ColorBId = Shader.PropertyToID("_ColorB");
    private static readonly int SpeedId = Shader.PropertyToID("_Speed");
    private static readonly int ScaleId = Shader.PropertyToID("_Scale");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int PixelSizeId = Shader.PropertyToID("_PixelSize");
    private static readonly int PosterizeStepsId = Shader.PropertyToID("_PosterizeSteps");

    private void Awake()
    {
        _rawImage = GetComponent<RawImage>();
        UpdateMaterial();
    }

    private void OnEnable()
    {
        UpdateMaterial();
    }

    private void Update()
    {
        SyncProperties();
    }

    private void OnValidate()
    {
        SyncProperties();
    }

    private void UpdateMaterial()
    {
        if (_rawImage == null) _rawImage = GetComponent<RawImage>();
        
        if (_rawImage.material != null && _rawImage.material.shader.name == "Custom/BackgroundFluid")
        {
            _material = _rawImage.material;
        }
    }

    private void SyncProperties()
    {
        if (_material == null)
        {
            UpdateMaterial();
            if (_material == null) return;
        }

        _material.SetColor(ColorAId, colorA);
        _material.SetColor(ColorBId, colorB);
        _material.SetFloat(SpeedId, speed);
        _material.SetFloat(ScaleId, scale);
        _material.SetFloat(IntensityId, intensity);
        _material.SetFloat(PixelSizeId, pixelSize);
        _material.SetFloat(PosterizeStepsId, posterizeSteps);
    }
}
