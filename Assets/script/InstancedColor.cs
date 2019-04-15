using UnityEngine;

public class InstancedColor : MonoBehaviour {
    private static MaterialPropertyBlock _propertyBlock;
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    [SerializeField] private Color color = Color.white;

    private void Awake () {
        OnValidate();
    }

    private void OnValidate () {
        if (_propertyBlock == null) {
            _propertyBlock = new MaterialPropertyBlock();
        }
        _propertyBlock.SetColor(ColorId, color);
        GetComponent<MeshRenderer>().SetPropertyBlock(_propertyBlock);
    }
}