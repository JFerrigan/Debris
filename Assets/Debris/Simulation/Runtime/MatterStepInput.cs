using System;
using Debris.Sites;
using UnityEngine;

namespace Debris.Simulation
{
    // Shared gameplay submission shape.  Positions are deliberately absent: physics owns pose.
    public readonly struct MatterStepInput
    {
        public readonly SiteCommand? Tool;
        public readonly float SuctionForce;
        public readonly Vector2 SuctionPosition;
        public readonly bool DoorRequestedOpen, MountedCutter, MountedSuction;
        public readonly Vector3 LocalForce;

        public MatterStepInput(SiteCommand? tool, float suctionForce, Vector2 suctionPosition,
            bool doorRequestedOpen, bool mountedCutter, bool mountedSuction, Vector3 localForce)
        {
            if (!float.IsFinite(suctionForce) || suctionForce < 0 || !float.IsFinite(suctionPosition.x) || !float.IsFinite(suctionPosition.y) ||
                !float.IsFinite(localForce.x) || !float.IsFinite(localForce.y) || !float.IsFinite(localForce.z))
                throw new ArgumentException("Matter step input must be finite and bounded.");
            Tool=tool; SuctionForce=suctionForce; SuctionPosition=suctionPosition;
            DoorRequestedOpen=doorRequestedOpen; MountedCutter=mountedCutter; MountedSuction=mountedSuction; LocalForce=localForce;
        }
    }
}
