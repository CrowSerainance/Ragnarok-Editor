// ============================================================================
// RsmParser.cs - Ragnarok Online 3D Model Parser (Based on BrowEdit3)
// ============================================================================
// TARGET: F:\2026 PROJECT\ROMapOverlayEditor\ROMapOverlayEditor\Rsm\RsmParser.cs
// ACTION: CREATE NEW FILE
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;

namespace ROMapOverlayEditor.Rsm
{
    /// <summary>
    /// Parser for RSM (Ragnarok Static Model) files.
    /// Based on BrowEdit3's Rsm.cpp implementation.
    /// </summary>
    public static class RsmParser
    {
        private static Encoding? _encoding;
        
        static RsmParser()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            try { _encoding = Encoding.GetEncoding(949); }  // Korean
            catch { _encoding = Encoding.GetEncoding(1252); }  // Western
        }
        
        /// <summary>
        /// Parse an RSM file from bytes.
        /// </summary>
        public static (bool Ok, string Message, RsmFile? Model) TryParse(byte[] data)
        {
            if (data == null || data.Length < 16)
                return (false, "RSM: File too small", null);
            
            try
            {
                using var ms = new MemoryStream(data);
                using var br = new BinaryReader(ms, Encoding.ASCII, leaveOpen: true);
                
                var rsm = new RsmFile();
                
                // ============================================================
                // HEADER
                // ============================================================
                
                // Magic "GRSM" (4 bytes)
                string sig = Encoding.ASCII.GetString(br.ReadBytes(4));
                if (sig != "GRSM")
                    return (false, $"RSM: Invalid magic '{sig}' (expected GRSM)", null);
                
                rsm.Signature = sig;
                
                // Version (2 bytes, big-endian in BrowEdit)
                byte vLo = br.ReadByte();
                byte vHi = br.ReadByte();
                rsm.Version = (ushort)((vHi << 8) | vLo);
                
                // Animation length, shade type
                rsm.AnimationLength = br.ReadInt32();
                rsm.ShadeType = br.ReadInt32();
                
                // Alpha (version >= 0x0104)
                if (rsm.Version >= 0x0104)
                    rsm.Alpha = br.ReadByte();
                else
                    rsm.Alpha = 255;
                
                // ============================================================
                // TEXTURES AND MESH INFO
                // ============================================================
                
                float fps = 1.0f;
                int meshCount;
                
                if (rsm.Version >= 0x0203)
                {
                    // Version 2.3+
                    fps = br.ReadSingle();
                    rsm.AnimationLength = (int)Math.Ceiling(rsm.AnimationLength * fps);
                    
                    int rootMeshCount = br.ReadInt32();
                    for (int i = 0; i < rootMeshCount; i++)
                    {
                        rsm.MainMeshName = ReadDynamicString(br);
                    }
                    
                    meshCount = br.ReadInt32();
                }
                else if (rsm.Version >= 0x0202)
                {
                    // Version 2.2
                    fps = br.ReadSingle();
                    rsm.AnimationLength = (int)Math.Ceiling(rsm.AnimationLength * fps);
                    
                    int textureCount = br.ReadInt32();
                    if (textureCount < 0 || textureCount > 1000)
                        return (false, $"RSM: Invalid texture count {textureCount}", null);
                    
                    for (int i = 0; i < textureCount; i++)
                        rsm.Textures.Add(ReadDynamicString(br));
                    
                    int rootMeshCount = br.ReadInt32();
                    for (int i = 0; i < rootMeshCount; i++)
                    {
                        rsm.MainMeshName = ReadDynamicString(br);
                    }
                    
                    meshCount = br.ReadInt32();
                }
                else
                {
                    // Version < 2.2 (most common)
                    // Skip 16 bytes of unknown/reserved data
                    br.ReadBytes(16);
                    
                    int textureCount = br.ReadInt32();
                    if (textureCount < 0 || textureCount > 1000)
                        return (false, $"RSM: Invalid texture count {textureCount}", null);
                    
                    for (int i = 0; i < textureCount; i++)
                    {
                        string texName = ReadFixedString(br, 40);
                        rsm.Textures.Add(texName);
                    }
                    
                    rsm.MainMeshName = ReadFixedString(br, 40);
                    meshCount = br.ReadInt32();
                }
                
                if (meshCount < 0 || meshCount > 10000)
                    return (false, $"RSM: Invalid mesh count {meshCount}", null);
                
                // ============================================================
                // MESHES
                // ============================================================
                
                var meshDict = new Dictionary<string, RsmMesh>();
                
                for (int i = 0; i < meshCount; i++)
                {
                    var mesh = ParseMesh(br, rsm);
                    if (mesh == null)
                        return (false, $"RSM: Failed to parse mesh {i}", null);
                    
                    mesh.Index = i;
                    
                    // Handle duplicate names
                    string uniqueName = mesh.Name;
                    while (meshDict.ContainsKey(uniqueName))
                        uniqueName += "(dup)";
                    
                    if (string.IsNullOrEmpty(uniqueName))
                        uniqueName = $"mesh_{i}";
                    
                    mesh.Name = uniqueName;
                    meshDict[uniqueName] = mesh;
                    rsm.Meshes.Add(mesh);
                }
                
                // Build mesh hierarchy
                BuildMeshHierarchy(rsm, meshDict);
                
                // Calculate bounding box
                CalculateBoundingBox(rsm);
                
                // Skip volume boxes (we don't need them for rendering)
                
                return (true, $"RSM v{rsm.Version >> 8}.{rsm.Version & 0xFF}: {meshCount} meshes, {rsm.Textures.Count} textures", rsm);
            }
            catch (Exception ex)
            {
                return (false, $"RSM: Parse error - {ex.Message}", null);
            }
        }
        
        /// <summary>
        /// Parse a single mesh from the stream.
        /// </summary>
        private static RsmMesh? ParseMesh(BinaryReader br, RsmFile rsm)
        {
            var mesh = new RsmMesh();
            
            // Name and parent
            if (rsm.Version >= 0x0202)
            {
                mesh.Name = ReadDynamicString(br);
                mesh.ParentName = ReadDynamicString(br);
            }
            else
            {
                mesh.Name = ReadFixedString(br, 40);
                mesh.ParentName = ReadFixedString(br, 40);
            }
            
            // Texture indices
            if (rsm.Version >= 0x0203)
            {
                int texCount = br.ReadInt32();
                for (int i = 0; i < texCount; i++)
                {
                    string texFile = ReadDynamicString(br);
                    int idx = rsm.Textures.IndexOf(texFile);
                    if (idx < 0)
                    {
                        idx = rsm.Textures.Count;
                        rsm.Textures.Add(texFile);
                    }
                    mesh.TextureIndices.Add(idx);
                }
            }
            else
            {
                int texCount = br.ReadInt32();
                for (int i = 0; i < texCount; i++)
                {
                    int texId = br.ReadInt32();
                    mesh.TextureIndices.Add(texId);
                }
            }
            
            // Offset matrix (3x3 rotation/scale stored as 9 floats)
            float m00 = br.ReadSingle(); float m01 = br.ReadSingle(); float m02 = br.ReadSingle();
            float m10 = br.ReadSingle(); float m11 = br.ReadSingle(); float m12 = br.ReadSingle();
            float m20 = br.ReadSingle(); float m21 = br.ReadSingle(); float m22 = br.ReadSingle();
            
            mesh.OffsetMatrix = new Matrix4x4(
                m00, m01, m02, 0,
                m10, m11, m12, 0,
                m20, m21, m22, 0,
                0, 0, 0, 1
            );
            
            // Position
            mesh.Position = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            
            // Additional transform data (version < 0x0202)
            if (rsm.Version < 0x0202)
            {
                mesh.Position2 = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                mesh.RotationAngle = br.ReadSingle();
                mesh.RotationAxis = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                mesh.Scale = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            }
            else
            {
                mesh.Position2 = Vector3.Zero;
                mesh.RotationAngle = 0;
                mesh.RotationAxis = Vector3.Zero;
                mesh.Scale = Vector3.One;
            }
            
            // ============================================================
            // VERTICES
            // ============================================================
            
            int vertexCount = br.ReadInt32();
            if (vertexCount < 0 || vertexCount > 100000)
                return null;
            
            for (int i = 0; i < vertexCount; i++)
            {
                mesh.Vertices.Add(new Vector3(
                    br.ReadSingle(),
                    br.ReadSingle(),
                    br.ReadSingle()
                ));
            }
            
            // ============================================================
            // TEXTURE COORDINATES
            // ============================================================
            
            int texCoordCount = br.ReadInt32();
            if (texCoordCount < 0 || texCoordCount > 100000)
                return null;
            
            for (int i = 0; i < texCoordCount; i++)
            {
                // Version >= 0x0102 has an extra float (color?)
                if (rsm.Version >= 0x0102)
                    br.ReadSingle();
                
                mesh.TexCoords.Add(new Vector2(
                    br.ReadSingle(),
                    br.ReadSingle()
                ));
            }
            
            // ============================================================
            // FACES
            // ============================================================
            
            int faceCount = br.ReadInt32();
            if (faceCount < 0 || faceCount > 100000)
                return null;
            
            for (int i = 0; i < faceCount; i++)
            {
                var face = new RsmFace();
                
                // Version >= 0x0202 has face length prefix
                int faceLen = -1;
                if (rsm.Version >= 0x0202)
                    faceLen = br.ReadInt32();
                
                // Vertex indices
                face.VertexIndex0 = br.ReadInt16();
                face.VertexIndex1 = br.ReadInt16();
                face.VertexIndex2 = br.ReadInt16();
                
                // Texcoord indices
                face.TexCoordIndex0 = br.ReadInt16();
                face.TexCoordIndex1 = br.ReadInt16();
                face.TexCoordIndex2 = br.ReadInt16();
                
                // Texture index and padding
                face.TextureIndex = br.ReadInt16();
                face.Padding = br.ReadInt16();
                face.TwoSided = br.ReadInt32();
                
                // Smooth groups (version >= 0x0102)
                if (rsm.Version >= 0x0102)
                {
                    face.SmoothGroup0 = br.ReadInt32();
                    
                    if (faceLen > 24)
                        face.SmoothGroup1 = br.ReadInt32();
                    if (faceLen > 28)
                        face.SmoothGroup2 = br.ReadInt32();
                    
                    // Skip extra data
                    for (int j = 32; j < faceLen; j += 4)
                        br.ReadInt32();
                }
                
                // Calculate face normal
                if (face.VertexIndex0 >= 0 && face.VertexIndex0 < mesh.Vertices.Count &&
                    face.VertexIndex1 >= 0 && face.VertexIndex1 < mesh.Vertices.Count &&
                    face.VertexIndex2 >= 0 && face.VertexIndex2 < mesh.Vertices.Count)
                {
                    var v0 = mesh.Vertices[face.VertexIndex0];
                    var v1 = mesh.Vertices[face.VertexIndex1];
                    var v2 = mesh.Vertices[face.VertexIndex2];
                    
                    var edge1 = v1 - v0;
                    var edge2 = v2 - v0;
                    face.Normal = Vector3.Normalize(Vector3.Cross(edge1, edge2));
                    
                    // For flat shading, vertex normals = face normal
                    face.VertexNormal0 = face.Normal;
                    face.VertexNormal1 = face.Normal;
                    face.VertexNormal2 = face.Normal;
                }
                
                mesh.Faces.Add(face);
            }
            
            // ============================================================
            // ANIMATION FRAMES
            // ============================================================
            
            // Scale frames (version >= 0x0106)
            if (rsm.Version >= 0x0106)
            {
                int scaleFrameCount = br.ReadInt32();
                for (int i = 0; i < scaleFrameCount; i++)
                {
                    mesh.ScaleFrames.Add(new RsmScaleFrame
                    {
                        Time = br.ReadInt32(),
                        Scale = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()),
                        Data = br.ReadSingle()
                    });
                }
            }
            
            // Rotation frames
            int rotFrameCount = br.ReadInt32();
            for (int i = 0; i < rotFrameCount; i++)
            {
                int time = br.ReadInt32();
                float x = br.ReadSingle();
                float y = br.ReadSingle();
                float z = br.ReadSingle();
                float w = br.ReadSingle();
                
                mesh.RotationFrames.Add(new RsmRotationFrame
                {
                    Time = time,
                    Rotation = new Quaternion(x, y, z, w)
                });
            }
            
            // Position frames (version >= 0x0202)
            if (rsm.Version >= 0x0202)
            {
                int posFrameCount = br.ReadInt32();
                for (int i = 0; i < posFrameCount; i++)
                {
                    mesh.PositionFrames.Add(new RsmPositionFrame
                    {
                        Time = br.ReadInt32(),
                        Position = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()),
                        Data = br.ReadSingle()
                    });
                }
                
                // Texture animation frames (version >= 0x0203) - skip for now
                if (rsm.Version >= 0x0203)
                {
                    int texAnimCount = br.ReadInt32();
                    for (int i = 0; i < texAnimCount; i++)
                    {
                        int texId = br.ReadInt32();
                        int typeCount = br.ReadInt32();
                        for (int j = 0; j < typeCount; j++)
                        {
                            int type = br.ReadInt32();
                            int frameCount = br.ReadInt32();
                            for (int k = 0; k < frameCount; k++)
                            {
                                br.ReadInt32(); // time
                                br.ReadSingle(); // data
                            }
                        }
                    }
                }
            }
            
            return mesh;
        }
        
        /// <summary>
        /// Build parent-child relationships between meshes.
        /// </summary>
        private static void BuildMeshHierarchy(RsmFile rsm, Dictionary<string, RsmMesh> meshDict)
        {
            foreach (var mesh in rsm.Meshes)
            {
                if (!string.IsNullOrEmpty(mesh.ParentName) && meshDict.TryGetValue(mesh.ParentName, out var parent))
                {
                    mesh.Parent = parent;
                    parent.Children.Add(mesh);
                }
            }
        }
        
        /// <summary>
        /// Calculate bounding box for the entire model.
        /// </summary>
        private static void CalculateBoundingBox(RsmFile rsm)
        {
            Vector3 min = new Vector3(float.MaxValue);
            Vector3 max = new Vector3(float.MinValue);
            
            foreach (var mesh in rsm.Meshes)
            {
                foreach (var vertex in mesh.Vertices)
                {
                    // Transform vertex by offset matrix
                    var transformed = Vector3.Transform(vertex, mesh.OffsetMatrix);
                    transformed += mesh.Position + mesh.Position2;
                    
                    min = Vector3.Min(min, transformed);
                    max = Vector3.Max(max, transformed);
                }
            }
            
            rsm.BoundingBoxMin = min;
            rsm.BoundingBoxMax = max;
            rsm.BoundingBoxCenter = (min + max) * 0.5f;
        }
        
        /// <summary>
        /// Read a null-terminated string with fixed buffer size.
        /// </summary>
        private static string ReadFixedString(BinaryReader br, int size)
        {
            byte[] bytes = br.ReadBytes(size);
            int nullPos = Array.IndexOf(bytes, (byte)0);
            if (nullPos < 0) nullPos = size;
            
            try
            {
                return _encoding!.GetString(bytes, 0, nullPos).Trim();
            }
            catch
            {
                return Encoding.ASCII.GetString(bytes, 0, nullPos).Trim();
            }
        }
        
        /// <summary>
        /// Read a length-prefixed string (used in newer RSM versions).
        /// </summary>
        private static string ReadDynamicString(BinaryReader br)
        {
            int len = br.ReadInt32();
            if (len <= 0 || len > 1024)
                return "";
            
            byte[] bytes = br.ReadBytes(len);
            
            try
            {
                return _encoding!.GetString(bytes).TrimEnd('\0');
            }
            catch
            {
                return Encoding.ASCII.GetString(bytes).TrimEnd('\0');
            }
        }
    }
}
