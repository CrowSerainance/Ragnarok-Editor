//
// WpfQuaternionUtil.cs — helpers for WPF Quaternion (no 1-arg or Matrix3D ctor)
//
using System;
using System.Windows.Media.Media3D;

namespace ROMapOverlayEditor.ThreeD
{
    public static class WpfQuaternionUtil
    {
        public static Quaternion FromAxisAngleDeg(Vector3D axis, double angleDeg)
        {
            if (axis.LengthSquared < 1e-12) axis = new Vector3D(0, 1, 0);
            axis.Normalize();
            return new Quaternion(axis, angleDeg);
        }

        // Yaw (Y), Pitch (X), Roll (Z) in degrees
        public static Quaternion FromYawPitchRollDeg(double yawDeg, double pitchDeg, double rollDeg)
        {
            var qYaw   = FromAxisAngleDeg(new Vector3D(0, 1, 0), yawDeg);
            var qPitch = FromAxisAngleDeg(new Vector3D(1, 0, 0), pitchDeg);
            var qRoll  = FromAxisAngleDeg(new Vector3D(0, 0, 1), rollDeg);

            var q = qYaw;
            q *= qPitch;
            q *= qRoll;
            q.Normalize();
            return q;
        }

        public static Quaternion FromAxisAngleRad(Vector3D axis, double angleRad)
            => FromAxisAngleDeg(axis, angleRad * (180.0 / Math.PI));

        /// <summary>Extract rotation quaternion from a rotation (or rotation+scale) matrix.
        /// WPF Quaternion does not have a Matrix3D constructor.</summary>
        public static Quaternion FromRotationMatrix(Matrix3D m)
        {
            double m11 = m.M11, m12 = m.M12, m13 = m.M13;
            double m21 = m.M21, m22 = m.M22, m23 = m.M23;
            double m31 = m.M31, m32 = m.M32, m33 = m.M33;

            double trace = m11 + m22 + m33;
            double x, y, z, w;

            if (trace > 1e-6)
            {
                double s = 0.5 / Math.Sqrt(trace + 1.0);
                w = 0.25 / s;
                x = (m23 - m32) * s;
                y = (m31 - m13) * s;
                z = (m12 - m21) * s;
            }
            else if (m11 > m22 && m11 > m33)
            {
                double s = 2.0 * Math.Sqrt(1.0 + m11 - m22 - m33);
                w = (m23 - m32) / s;
                x = 0.25 * s;
                y = (m12 + m21) / s;
                z = (m31 + m13) / s;
            }
            else if (m22 > m33)
            {
                double s = 2.0 * Math.Sqrt(1.0 + m22 - m11 - m33);
                w = (m31 - m13) / s;
                x = (m12 + m21) / s;
                y = 0.25 * s;
                z = (m23 + m32) / s;
            }
            else
            {
                double s = 2.0 * Math.Sqrt(1.0 + m33 - m11 - m22);
                w = (m12 - m21) / s;
                x = (m31 + m13) / s;
                y = (m23 + m32) / s;
                z = 0.25 * s;
            }

            return new Quaternion(x, y, z, w);
        }
    }
}
