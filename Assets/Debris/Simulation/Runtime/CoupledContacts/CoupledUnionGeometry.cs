using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Debris.Simulation.CoupledContacts
{
    [StructLayout(LayoutKind.Sequential, Pack=4, Size=8)]
    public struct CoupledCoverageRange
    {
        public uint Start,Count;
    }

    // Immutable topology generation supplied to the indexed diagnostic path.
    // For indexed queries, Group.FirstQuery/QueryCount select its face span;
    // Group.Reserved stores the rigid endpoint.
    public sealed class CoupledUnionGeometry
    {
        public readonly CoupledUnionPatch[] Patches;
        public readonly CoupledExteriorFace[] Faces;
        public readonly CoupledUnionQueryGroup[] Groups;
        public readonly Vector4[] WorldBounds;
        public readonly CoupledUnionPatch[] CoveragePatches;
        public readonly CoupledCoverageRange[] CoverageRanges;

        public CoupledUnionGeometry(CoupledUnionPatch[] patches,
            CoupledExteriorFace[] faces,CoupledUnionQueryGroup[] groups)
        {
            if(patches==null||faces==null||groups==null||
               patches.Length>4096||faces.Length>4096||groups.Length>64)
                throw new ArgumentException("Invalid V2 union geometry capacity");
            Patches=(CoupledUnionPatch[])patches.Clone();
            Faces=(CoupledExteriorFace[])faces.Clone();
            Groups=(CoupledUnionQueryGroup[])groups.Clone();
            WorldBounds=new Vector4[Groups.Length];
            CoverageRanges=new CoupledCoverageRange[Groups.Length];
            var coverage=new List<CoupledUnionPatch>();
            for(int groupIndex=0;groupIndex<Groups.Length;groupIndex++)
            {
                var group=Groups[groupIndex];
                if((ulong)group.PatchStart+group.PatchCount>(ulong)Patches.Length||
                   (ulong)group.FirstQuery+group.QueryCount>(ulong)Faces.Length||
                   !Finite(group.UnionCenter.x)||!Finite(group.UnionCenter.y)||
                   !Finite(group.UnionAngle))
                    throw new ArgumentException("Invalid V2 union group");
                float c=Mathf.Cos(group.UnionAngle),s=Mathf.Sin(group.UnionAngle);
                float minX=float.PositiveInfinity,minY=float.PositiveInfinity;
                float maxX=float.NegativeInfinity,maxY=float.NegativeInfinity;
                for(uint i=group.PatchStart;i<group.PatchStart+group.PatchCount;i++)
                {
                    var patch=Patches[(int)i];
                    if(!Finite(patch.Center.x)||!Finite(patch.Center.y)||
                       !Finite(patch.Half.x)||!Finite(patch.Half.y)||
                       patch.Half.x<=0||patch.Half.y<=0)
                        throw new ArgumentException("Invalid V2 union patch");
                    float x=group.UnionCenter.x+c*patch.Center.x-s*patch.Center.y;
                    float y=group.UnionCenter.y+s*patch.Center.x+c*patch.Center.y;
                    float hx=Mathf.Abs(c)*patch.Half.x+Mathf.Abs(s)*patch.Half.y;
                    float hy=Mathf.Abs(s)*patch.Half.x+Mathf.Abs(c)*patch.Half.y;
                    minX=Mathf.Min(minX,x-hx);minY=Mathf.Min(minY,y-hy);
                    maxX=Mathf.Max(maxX,x+hx);maxY=Mathf.Max(maxY,y+hy);
                }
                WorldBounds[groupIndex]=group.PatchCount==0?
                    new Vector4(1,1,0,0):new Vector4(minX,minY,maxX,maxY);
                CoverageRanges[groupIndex]=new CoupledCoverageRange{Start=(uint)coverage.Count};
                AppendDisjointCoverage(Patches,group,coverage);
                var range=CoverageRanges[groupIndex];
                range.Count=(uint)coverage.Count-range.Start;
                CoverageRanges[groupIndex]=range;
            }
            CoveragePatches=coverage.ToArray();
            foreach(var face in Faces)
                if(!Finite(face.A.x)||!Finite(face.A.y)||!Finite(face.B.x)||!Finite(face.B.y)||
                   !Finite(face.Normal.x)||!Finite(face.Normal.y)||face.Side>3||
                   (face.B-face.A).sqrMagnitude<=1e-12f)
                    throw new ArgumentException("Invalid exterior face");
        }

        public CoupledContactPrimitive FacePrimitive(int faceIndex,int groupIndex,
            bool anchored,Vector2 sweep)
        {
            if(faceIndex<0||faceIndex>=Faces.Length||groupIndex<0||groupIndex>=Groups.Length||
               !Finite(sweep.x)||!Finite(sweep.y))throw new ArgumentException("Invalid exterior proxy");
            var face=Faces[faceIndex];var group=Groups[groupIndex];
            if(faceIndex<group.FirstQuery||
               (ulong)faceIndex>=(ulong)group.FirstQuery+group.QueryCount)
                throw new ArgumentException("Face does not belong to union group");
            float c=Mathf.Cos(group.UnionAngle),s=Mathf.Sin(group.UnionAngle);
            Vector2 Rotate(Vector2 v)=>new Vector2(c*v.x-s*v.y,s*v.x+c*v.y);
            Vector2 a=group.UnionCenter+Rotate(face.A);
            Vector2 b=group.UnionCenter+Rotate(face.B);
            Vector2 delta=b-a;
            return new CoupledContactPrimitive{Center=(a+b)*.5f,
                Half=new Vector2(delta.magnitude*.5f,.001f),
                Angle=Mathf.Atan2(delta.y,delta.x),Endpoint=group.Reserved,
                Sweep=sweep,Anchored=anchored?1u:0u,Kind=1,
                FaceIndex=(uint)faceIndex,UnionIndex=(uint)groupIndex,
                ContactClass=2,FeatureId=checked((uint)faceIndex+1)};
        }

        static bool Finite(float x)=>!float.IsNaN(x)&&!float.IsInfinity(x);

        static void AppendDisjointCoverage(CoupledUnionPatch[] patches,
            CoupledUnionQueryGroup group,List<CoupledUnionPatch> output)
        {
            const double tolerance=1e-8;
            var cuts=new List<double>((int)group.PatchCount*2);
            for(uint i=group.PatchStart;i<group.PatchStart+group.PatchCount;i++)
            {
                var p=patches[(int)i];
                cuts.Add((double)p.Center.x-p.Half.x);
                cuts.Add((double)p.Center.x+p.Half.x);
            }
            cuts.Sort();
            for(int x=0;x+1<cuts.Count;x++)
            {
                double lo=cuts[x],hi=cuts[x+1];
                if(hi-lo<=tolerance)continue;
                double mid=(lo+hi)*.5;
                var intervals=new List<Vector2>();
                for(uint i=group.PatchStart;i<group.PatchStart+group.PatchCount;i++)
                {
                    var p=patches[(int)i];
                    if(mid<p.Center.x-p.Half.x||mid>p.Center.x+p.Half.x)continue;
                    intervals.Add(new Vector2(p.Center.y-p.Half.y,p.Center.y+p.Half.y));
                }
                intervals.Sort((a,b)=>a.x.CompareTo(b.x));
                for(int i=0;i<intervals.Count;)
                {
                    double bottom=intervals[i].x,top=intervals[i].y;
                    i++;
                    while(i<intervals.Count&&intervals[i].x<=top+tolerance)
                    {top=Math.Max(top,intervals[i].y);i++;}
                    output.Add(new CoupledUnionPatch{
                        Center=new Vector2((float)((lo+hi)*.5),(float)((bottom+top)*.5)),
                        Half=new Vector2((float)((hi-lo)*.5),(float)((top-bottom)*.5))});
                    if(output.Count>65536)
                        throw new InvalidOperationException("V2 union coverage capacity exceeded");
                }
            }
        }
    }
}
