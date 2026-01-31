using System.Collections.Generic;
using System.Numerics;

namespace ROMapOverlayEditor.Rsm
{
    public class RsmFile
    {
        public string Signature { get; set; } = "";
        public ushort Version { get; set; }
        public int AnimationLength { get; set; }
        public int ShadeType { get; set; }
        public byte Alpha { get; set; }
        public string MainMeshName { get; set; } = "";
        public List<string> Textures { get; set; } = new List<string>();
        public List<RsmMesh> Meshes { get; set; } = new List<RsmMesh>();
        public Vector3 BoundingBoxMin { get; set; }
        public Vector3 BoundingBoxMax { get; set; }
        public Vector3 BoundingBoxCenter { get; set; }
    }

    public class RsmMesh
    {
        public int Index { get; set; }
        public string Name { get; set; } = "";
        public string ParentName { get; set; } = "";
        public RsmMesh? Parent { get; set; }
        public List<RsmMesh> Children { get; set; } = new List<RsmMesh>();

        public List<int> TextureIndices { get; set; } = new List<int>();
        
        public Matrix4x4 OffsetMatrix { get; set; }
        public Vector3 Position { get; set; }
        public Vector3 Position2 { get; set; }
        public float RotationAngle { get; set; }
        public Vector3 RotationAxis { get; set; }
        public Vector3 Scale { get; set; } = Vector3.One;

        public List<Vector3> Vertices { get; set; } = new List<Vector3>();
        public List<Vector2> TexCoords { get; set; } = new List<Vector2>();
        public List<RsmFace> Faces { get; set; } = new List<RsmFace>();

        public List<RsmScaleFrame> ScaleFrames { get; set; } = new List<RsmScaleFrame>();
        public List<RsmRotationFrame> RotationFrames { get; set; } = new List<RsmRotationFrame>();
        public List<RsmPositionFrame> PositionFrames { get; set; } = new List<RsmPositionFrame>();
    }

    public class RsmFace
    {
        public short VertexIndex0;
        public short VertexIndex1;
        public short VertexIndex2;
        public short TexCoordIndex0;
        public short TexCoordIndex1;
        public short TexCoordIndex2;
        public short TextureIndex;
        public short Padding;
        public int TwoSided;
        public int SmoothGroup0;
        public int SmoothGroup1;
        public int SmoothGroup2;

        public Vector3 Normal;
        public Vector3 VertexNormal0;
        public Vector3 VertexNormal1;
        public Vector3 VertexNormal2;
    }

    public class RsmScaleFrame { public int Time; public Vector3 Scale; public float Data; }
    public class RsmRotationFrame { public int Time; public Quaternion Rotation; }
    public class RsmPositionFrame { public int Time; public Vector3 Position; public float Data; }
}
