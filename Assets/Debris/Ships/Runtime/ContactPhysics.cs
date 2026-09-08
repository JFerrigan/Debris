using System;
using UnityEngine;
namespace Debris.Ships
{
    public enum BodyMobility { Dynamic, Anchored }
    // Cell-space units: one square cell has unit volume (unit effective thickness).
    public struct BodyMass
    {
        public float Mass, Inertia;
        public Vector2 Center;
        public BodyMobility Mobility;
        public float InverseMass => Mobility == BodyMobility.Anchored ? 0 : 1 / Mass;
        public float InverseInertia => Mobility == BodyMobility.Anchored ? 0 : 1 / Inertia;
        public void Validate()
        {
            if (!float.IsFinite(Mass) || Mass <= 0 || !float.IsFinite(Inertia) || Inertia <= 0 ||
                !float.IsFinite(Center.x) || !float.IsFinite(Center.y) || !Enum.IsDefined(typeof(BodyMobility), Mobility))
                throw new ArgumentException("Invalid body mass properties.");
        }
    }
    // Analytical oracle. Production high-volume contacts are solved on the GPU.
    public static class ContactPhysics
    {
        public static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        public static Vector2 Surface(Vector2 velocity, float spin, Vector2 offset) => velocity + new Vector2(-offset.y, offset.x) * spin;
        public static float Impulse(BodyMass a, BodyMass b, Vector2 va, float wa, Vector2 vb, float wb,
            Vector2 ra, Vector2 rb, Vector2 normal, float restitution = 0)
        {
            a.Validate(); b.Validate();
            float closing = Vector2.Dot(Surface(vb, wb, rb) - Surface(va, wa, ra), normal);
            float ca = Cross(ra, normal), cb = Cross(rb, normal);
            float k = a.InverseMass + b.InverseMass + ca * ca * a.InverseInertia + cb * cb * b.InverseInertia;
            return closing < 0 && k > 0 ? -(1 + restitution) * closing / k : 0;
        }
        public static Vector3 Accelerate(BodyMass body, Vector3 velocity, Vector3 force, float dt)
        {
            body.Validate();
            return velocity + new Vector3(force.x * body.InverseMass, force.y * body.InverseMass, force.z * body.InverseInertia) * dt;
        }
    }
}
