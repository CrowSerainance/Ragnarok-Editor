// ============================================================================
// RsmMeshBuilder.cs - Convert RsmFile to WPF Model3DGroup (BrowEdit3-style)
// ============================================================================
// Builds hierarchical mesh transforms, applies RSW instance position/rotation/scale,
// and renders textured triangles. No animations (static pose only).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using ROMapOverlayEditor.Vfs;
using ROMapOverlayEditor.ThreeD;
using ROMapOverlayEditor.Rsw;

namespace ROMapOverlayEditor.Rsm
{
    /// <summary>
    /// Converts parsed RSM data into WPF 3D geometry (Model3DGroup) for display.
    /// Applies mesh hierarchy (offset matrix + position) and RSW instance transform.
    /// </summary>
    public static class RsmMeshBuilder
    {
        /// <summary>
        /// Build a WPF Model3DGroup from an RSM file with the given instance transform.
        /// </summary>
        /// <param name="rsm">Parsed RSM (from RsmParser.TryParse)</param>
        /// <param name="vfs">VFS for loading textures</param>
        /// <param name="position">Instance position (RO world units)</param>
        /// <param name="rotationDeg">Instance rotation in degrees (X=pitch, Y=yaw, Z=roll)</param>
        /// <param name="scale">Instance scale</param>
        /// <returns>Model3DGroup ready to add to Viewport3D, or null if rsm is null/empty</returns>
        public static Model3DGroup? Build(
            RsmFile? rsm,
            IVfs? vfs,
            (float X, float Y, float Z) position,
            (float X, float Y, float Z) rotationDeg,
            (float X, float Y, float Z) scale)
        {
            if (rsm == null || rsm.Meshes == null || rsm.Meshes.Count == 0)
                return null;

            var instanceTransform = BuildInstanceMatrix(position, rotationDeg, scale);
            var group = new Model3DGroup();

            foreach (var mesh in rsm.Meshes)
            {
                // Only process root meshes; children are reached via hierarchy when we compute world matrix
                if (mesh.Parent != null)
                    continue;

                var meshModel = BuildMeshRecursive(rsm, mesh, Matrix4x4.Identity, vfs);
                if (meshModel != null)
                    group.Children.Add(meshModel);
            }

            if (group.Children.Count == 0)
                return null;

            group.Transform = new MatrixTransform3D(instanceTransform);
            return group;
        }

        /// <summary>
        /// Build from RswModel (convenience).
        /// </summary>
        public static Model3DGroup? BuildFromRswModel(RsmFile? rsm, IVfs? vfs, RswModel? model, double worldScale = 1.0)
        {
            if (model == null) return null;
            float s = (float)worldScale;
            return Build(rsm, vfs,
                (model.Position.X * s, model.Position.Y * s, model.Position.Z * s),
                (model.Rotation.X, model.Rotation.Y, model.Rotation.Z),
                (model.Scale.X, model.Scale.Y, model.Scale.Z));
        }

        private static Matrix3D BuildInstanceMatrix(
            (float X, float Y, float Z) position,
            (float X, float Y, float Z) rotationDeg,
            (float X, float Y, float Z) scale)
        {
            var rot = BrowEditCoordinates.RswRotationToQuaternion(new Vector3(rotationDeg.X, rotationDeg.Y, rotationDeg.Z));
            var q = new System.Windows.Media.Media3D.Quaternion((double)rot.X, (double)rot.Y, (double)rot.Z, (double)rot.W);
            var scaleTransform = new ScaleTransform3D(scale.X, scale.Y, scale.Z);
            var rotTransform = new RotateTransform3D(new System.Windows.Media.Media3D.QuaternionRotation3D(q));
            var transTransform = new TranslateTransform3D(position.X, position.Y, position.Z);
            var combined = new Transform3DGroup();
            combined.Children.Add(scaleTransform);
            combined.Children.Add(rotTransform);
            combined.Children.Add(transTransform);
            return combined.Value;
        }

        private static Model3DGroup? BuildMeshRecursive(RsmFile rsm, RsmMesh mesh, Matrix4x4 parentWorld, IVfs? vfs)
        {
            // Local transform: T(Position+Position2) * OffsetMatrix (3x3 in 4x4)
            var pos = mesh.Position + mesh.Position2;
            var t = Matrix4x4.CreateTranslation(pos.X, pos.Y, pos.Z);
            var m = t * mesh.OffsetMatrix;
            var world = m * parentWorld;

            var group = new Model3DGroup();

            // This mesh's geometry (vertices in local space, transformed by world)
            var geom = BuildMeshGeometry(mesh, world);
            if (geom != null)
            {
                int texIndex = mesh.TextureIndices.Count > 0 ? mesh.TextureIndices[0] : 0;
                string? texName = texIndex >= 0 && texIndex < rsm.Textures.Count ? rsm.Textures[texIndex] : null;
                var material = GetMaterial(vfs, texName);
                var model = new GeometryModel3D(geom, material);
                group.Children.Add(model);
            }

            foreach (var child in mesh.Children)
            {
                var childModel = BuildMeshRecursive(rsm, child, world, vfs);
                if (childModel != null)
                    group.Children.Add(childModel);
            }

            return group.Children.Count > 0 ? group : null;
        }

        private static MeshGeometry3D? BuildMeshGeometry(RsmMesh mesh, Matrix4x4 world)
        {
            var positions = new Point3DCollection();
            var normals = new Vector3DCollection();
            var texCoords = new PointCollection();
            var indices = new Int32Collection();

            bool hasNormals = false;
            foreach (var face in mesh.Faces)
            {
                if (face.VertexIndex0 < 0 || face.VertexIndex0 >= mesh.Vertices.Count ||
                    face.VertexIndex1 < 0 || face.VertexIndex1 >= mesh.Vertices.Count ||
                    face.VertexIndex2 < 0 || face.VertexIndex2 >= mesh.Vertices.Count)
                    continue;

                var v0 = mesh.Vertices[face.VertexIndex0];
                var v1 = mesh.Vertices[face.VertexIndex1];
                var v2 = mesh.Vertices[face.VertexIndex2];

                var p0 = Vector3.Transform(v0, world);
                var p1 = Vector3.Transform(v1, world);
                var p2 = Vector3.Transform(v2, world);

                var n0 = face.VertexNormal0; var n1 = face.VertexNormal1; var n2 = face.VertexNormal2;
                n0 = Vector3.Normalize(Vector3.TransformNormal(n0, world));
                n1 = Vector3.Normalize(Vector3.TransformNormal(n1, world));
                n2 = Vector3.Normalize(Vector3.TransformNormal(n2, world));

                int ti0 = Math.Clamp(face.TexCoordIndex0, 0, mesh.TexCoords.Count - 1);
                int ti1 = Math.Clamp(face.TexCoordIndex1, 0, mesh.TexCoords.Count - 1);
                int ti2 = Math.Clamp(face.TexCoordIndex2, 0, mesh.TexCoords.Count - 1);
                var uv0 = ti0 < mesh.TexCoords.Count ? mesh.TexCoords[ti0] : Vector2.Zero;
                var uv1 = ti1 < mesh.TexCoords.Count ? mesh.TexCoords[ti1] : Vector2.Zero;
                var uv2 = ti2 < mesh.TexCoords.Count ? mesh.TexCoords[ti2] : Vector2.Zero;

                int baseIdx = positions.Count;
                positions.Add(ToPoint3D(p0)); positions.Add(ToPoint3D(p1)); positions.Add(ToPoint3D(p2));
                normals.Add(ToVector3D(n0)); normals.Add(ToVector3D(n1)); normals.Add(ToVector3D(n2));
                texCoords.Add(new System.Windows.Point(uv0.X, uv0.Y));
                texCoords.Add(new System.Windows.Point(uv1.X, uv1.Y));
                texCoords.Add(new System.Windows.Point(uv2.X, uv2.Y));
                indices.Add(baseIdx + 0); indices.Add(baseIdx + 1); indices.Add(baseIdx + 2);
                hasNormals = true;
            }

            if (positions.Count == 0) return null;

            var geometry = new MeshGeometry3D
            {
                Positions = positions,
                TriangleIndices = indices,
                TextureCoordinates = texCoords
            };
            if (hasNormals && normals.Count == positions.Count)
                geometry.Normals = normals;
            return geometry;
        }

        private static Point3D ToPoint3D(Vector3 v) => new Point3D(v.X, v.Y, v.Z);
        private static Vector3D ToVector3D(Vector3 v) => new Vector3D(v.X, v.Y, v.Z);

        private static Material GetMaterial(IVfs? vfs, string? textureName)
        {
            if (vfs != null && !string.IsNullOrWhiteSpace(textureName))
            {
                var bmp = RsmTextureResolver.TryLoadTexture(vfs, textureName);
                if (bmp != null)
                    return new DiffuseMaterial(new ImageBrush(bmp));
            }
            return new DiffuseMaterial(new SolidColorBrush(Colors.Magenta));
        }

    }
}
