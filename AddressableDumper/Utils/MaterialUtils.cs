using UnityEngine;

namespace AddressableDumper.Utils
{
    public static class MaterialUtils
    {
        static Material _defaultMaterial;

        public static Material DefaultMaterial
        {
            get
            {
                if (!_defaultMaterial)
                {
                    GameObject tempCube = GameObject.CreatePrimitive(PrimitiveType.Cube);

                    _defaultMaterial = tempCube.GetComponent<Renderer>().sharedMaterial;

                    GameObject.Destroy(tempCube);
                }

                return _defaultMaterial;
            }
        }

        public static Shader DefaultShader
        {
            get
            {
                Material material = DefaultMaterial;
                return material ? material.shader : null;
            }
        }
    }
}
