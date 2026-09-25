using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Debris.Simulation.ParallelProof;
using ProofGrain = Debris.Simulation.ParallelProof.LooseCell;
using UnityEngine;

namespace Debris.Simulation.Tests
{
    // V2-0A input format. Every scalar is little-endian; floats use their IEEE-754
    // bits. Sections: 1 grains (48 B), 2 endpoints (32 B), 3 body parameters
    // (32 B), 4 boundaries (32 B), 5 original masses (4 B), 6 diagnostics (4 B).
    // The JSON manifest carries provenance only; input.bin is authoritative.
    internal sealed class CoupledReplayInput
    {
        public ProofGrain[] Grains;
        public BodyState[] Endpoints;
        public BodyParameters[] Parameters;
        public Boundary[] Boundaries;
        public float[] Masses;
        public uint[] Diagnostics;
        public int GrainCapacity, BoundaryCapacity, VelocityIterations, PositionIterations;
        public int CandidateSlots, RigidContactCapacity;
        public float Friction, Dt, Torque, SuctionForce;
        public Vector2 Force;
        public bool MountedSuction, DefaultMasses;
        public int SourceAttempt, SourceCommitted;
    }

    [Serializable]
    internal sealed class CoupledReplayManifest
    {
        public string magic = "B3R_PRESTEP";
        public int version = 1;
        public string sha256, fixture, profile, capturePhase = "CommittedBeforeStep";
        public string cacheKind = "none", bodyIdentityKind = "fixture-ordinal";
        public string sourceHead, solverSha256, shaderSha256, fixtureSha256, baseFixtureSha256, recordsSha256, editorVersion, backend;
        public string runUtc, inputRelativePath = "input.bin";
        public int sourceAttempt, sourceCommitted, localReplayAttempt = 1;
        public int fixtureRevision, workloadRevision;
        public int pageOriginX, pageOriginY;
        public int bodyEndpointStart;
        public int[] bodyOrdinals;
    }

    internal static class CoupledReplayArchive
    {
        const int MaxBytes = 64 * 1024 * 1024;
        const string Magic = "B3R_PRESTEP";

        public static string HashFile(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        static void U(Stream s, uint value)
        {
            for (int i = 0; i < 4; i++) s.WriteByte((byte)(value >> (8 * i)));
        }
        static uint U(Stream s)
        {
            uint value = 0;
            for (int i = 0; i < 4; i++)
            {
                int b = s.ReadByte();
                if (b < 0) throw new InvalidDataException("Truncated replay input");
                value |= (uint)b << (8 * i);
            }
            return value;
        }
        static void F(Stream s, float value) => U(s, unchecked((uint)BitConverter.SingleToInt32Bits(value)));
        static float F(Stream s) => BitConverter.Int32BitsToSingle(unchecked((int)U(s)));
        static void V(Stream s, Vector2 value) { F(s, value.x); F(s, value.y); }
        static Vector2 V(Stream s) => new Vector2(F(s), F(s));
        static void Grain(Stream s, ProofGrain g) { V(s,g.Center); V(s,g.Velocity); F(s,g.Angle); F(s,g.AngularVelocity); U(s,g.Material); U(s,g.Identity); U(s,g.Flags); U(s,g.Reserved0); U(s,g.Reserved1); U(s,g.Reserved2); }
        static ProofGrain Grain(Stream s) => new ProofGrain { Center=V(s), Velocity=V(s), Angle=F(s), AngularVelocity=F(s), Material=U(s), Identity=U(s), Flags=U(s), Reserved0=U(s), Reserved1=U(s), Reserved2=U(s) };
        static void Body(Stream s, BodyState b) { V(s,b.Center); V(s,b.Velocity); F(s,b.Angle); F(s,b.AngularVelocity); U(s,b.Reserved0); U(s,b.Reserved1); }
        static BodyState Body(Stream s) => new BodyState { Center=V(s), Velocity=V(s), Angle=F(s), AngularVelocity=F(s), Reserved0=U(s), Reserved1=U(s) };
        static void Parameter(Stream s, BodyParameters p) { F(s,p.InverseMass); F(s,p.InverseInertia); V(s,p.LocalCOM); U(s,p.BoundaryStart); U(s,p.BoundaryCount); U(s,p.Mobility); U(s,p.ShapeRevision); }
        static BodyParameters Parameter(Stream s) => new BodyParameters { InverseMass=F(s), InverseInertia=F(s), LocalCOM=V(s), BoundaryStart=U(s), BoundaryCount=U(s), Mobility=U(s), ShapeRevision=U(s) };
        static void Patch(Stream s, Boundary b) { V(s,b.Center); V(s,b.HalfSize); U(s,b.Body); U(s,b.Feature); U(s,b.Reserved0); U(s,b.Reserved1); }
        static Boundary Patch(Stream s) => new Boundary { Center=V(s), HalfSize=V(s), Body=U(s), Feature=U(s), Reserved0=U(s), Reserved1=U(s) };
        static void Section(Stream s, int id, int count, int stride, Action write)
        {
            U(s,(uint)id); U(s,(uint)count); U(s,checked((uint)(count*stride)));
            write();
        }
        static int Count(Stream s, int id, int max, int stride)
        {
            if (U(s) != id) throw new InvalidDataException("Missing, duplicate or reordered section " + id);
            uint count = U(s), length = U(s);
            if (count > max || length != count * (uint)stride) throw new InvalidDataException("Invalid section length/count " + id);
            return (int)count;
        }
        static void Check(bool valid, string message) { if (!valid) throw new InvalidDataException(message); }
        public static byte[] Encode(CoupledReplayInput a)
        {
            Check(a != null && a.Grains != null && a.Endpoints != null && a.Parameters != null && a.Boundaries != null && a.Diagnostics != null, "Missing input section");
            Check(a.Grains.Length <= 8192 && a.GrainCapacity >= a.Grains.Length && a.GrainCapacity <= 8192, "Invalid grain capacity");
            Check(a.Endpoints.Length == a.GrainCapacity + a.Parameters.Length && a.Parameters.Length <= 18, "Invalid endpoint indexing");
            Check(a.Boundaries.Length <= 4096 && a.BoundaryCapacity >= a.Boundaries.Length && a.BoundaryCapacity <= 4096, "Invalid boundary capacity");
            Check(a.DefaultMasses ? a.Masses == null : a.Masses != null && a.Masses.Length == a.Grains.Length, "Invalid mass path");
            Check(a.Diagnostics.Length == 16, "Invalid diagnostics");
            using (var s = new MemoryStream())
            {
                var magic = Encoding.ASCII.GetBytes(Magic); s.Write(magic,0,magic.Length);
                U(s,1); U(s,6); U(s,(uint)a.GrainCapacity); U(s,(uint)a.BoundaryCapacity);
                U(s,(uint)a.VelocityIterations); U(s,(uint)a.PositionIterations); U(s,(uint)a.CandidateSlots); U(s,(uint)a.RigidContactCapacity);
                F(s,a.Friction); F(s,a.Dt); V(s,a.Force); F(s,a.Torque); F(s,a.SuctionForce);
                U(s,a.MountedSuction ? 1u : 0u); U(s,a.DefaultMasses ? 1u : 0u);
                U(s,(uint)a.SourceAttempt); U(s,(uint)a.SourceCommitted);
                Section(s,1,a.Grains.Length,48,()=>{foreach(var x in a.Grains) Grain(s,x);});
                Section(s,2,a.Endpoints.Length,32,()=>{foreach(var x in a.Endpoints) Body(s,x);});
                Section(s,3,a.Parameters.Length,32,()=>{foreach(var x in a.Parameters) Parameter(s,x);});
                Section(s,4,a.Boundaries.Length,32,()=>{foreach(var x in a.Boundaries) Patch(s,x);});
                Section(s,5,a.Masses == null ? 0 : a.Masses.Length,4,()=>{if(a.Masses!=null)foreach(var x in a.Masses) F(s,x);});
                Section(s,6,a.Diagnostics.Length,4,()=>{foreach(var x in a.Diagnostics) U(s,x);});
                Check(s.Length <= MaxBytes, "Oversized replay input"); return s.ToArray();
            }
        }
        public static CoupledReplayInput Decode(byte[] bytes)
        {
            Check(bytes != null && bytes.Length <= MaxBytes, "Oversized replay input");
            using (var s = new MemoryStream(bytes,false))
            {
                var magic = new byte[Magic.Length];
                Check(s.Read(magic,0,magic.Length) == magic.Length && Encoding.ASCII.GetString(magic) == Magic, "Invalid replay magic");
                Check(U(s) == 1, "Unknown replay version"); Check(U(s) == 6, "Invalid section count");
                var a = new CoupledReplayInput { GrainCapacity=(int)U(s), BoundaryCapacity=(int)U(s), VelocityIterations=(int)U(s), PositionIterations=(int)U(s), CandidateSlots=(int)U(s), RigidContactCapacity=(int)U(s), Friction=F(s), Dt=F(s), Force=V(s), Torque=F(s), SuctionForce=F(s) };
                uint mounted=U(s), defaultMass=U(s); Check(mounted<=1 && defaultMass<=1,"Invalid command flags");
                a.MountedSuction=mounted==1; a.DefaultMasses=defaultMass==1;
                a.SourceAttempt=(int)U(s); a.SourceCommitted=(int)U(s);
                int n=Count(s,1,8192,48); a.Grains=new ProofGrain[n]; for(int i=0;i<n;i++) a.Grains[i]=Grain(s);
                Check(a.GrainCapacity>=n && a.GrainCapacity<=8192,"Invalid grain capacity");
                n=Count(s,2,8210,32); a.Endpoints=new BodyState[n]; for(int i=0;i<n;i++) a.Endpoints[i]=Body(s);
                n=Count(s,3,18,32); a.Parameters=new BodyParameters[n]; for(int i=0;i<n;i++) a.Parameters[i]=Parameter(s);
                Check(a.Endpoints.Length==a.GrainCapacity+n,"Invalid endpoint indexing");
                n=Count(s,4,4096,32); a.Boundaries=new Boundary[n]; for(int i=0;i<n;i++) a.Boundaries[i]=Patch(s);
                Check(a.BoundaryCapacity>=n && a.BoundaryCapacity<=4096,"Invalid boundary capacity");
                n=Count(s,5,8192,4); if(n>0){a.Masses=new float[n];for(int i=0;i<n;i++)a.Masses[i]=F(s);}
                Check(a.DefaultMasses ? n==0 : n==a.Grains.Length,"Invalid original masses");
                n=Count(s,6,16,4); Check(n==16,"Invalid diagnostics length"); a.Diagnostics=new uint[n]; for(int i=0;i<n;i++)a.Diagnostics[i]=U(s);
                Check(s.Position==s.Length,"Trailing replay bytes"); return a;
            }
        }
        public static void ValidateForReplay(CoupledReplayInput a)
        {
            Check(a.Dt == 1f/60f && a.SourceAttempt>0 && a.SourceCommitted>=0, "Invalid step identity or dt");
            Check((a.VelocityIterations==4 && a.PositionIterations==2) || (a.VelocityIterations==8 && a.PositionIterations==4) || (a.VelocityIterations==12 && a.PositionIterations==6), "Invalid profile");
            Check(a.CandidateSlots>=1 && a.CandidateSlots<=64 && a.RigidContactCapacity>=1 && a.RigidContactCapacity<=4096, "Invalid solver options");
            Check(Finite(a.Friction) && a.Friction>=0 && Finite(a.Force.x) && Finite(a.Force.y) && Finite(a.Torque) && Finite(a.SuctionForce) && a.SuctionForce>=0, "Invalid command");
            if(a.Masses!=null)foreach(float m in a.Masses)Check(Finite(m) && m>0,"Invalid grain mass");
            foreach(var g in a.Grains)Check(Finite(g.Center.x)&&Finite(g.Center.y)&&Finite(g.Velocity.x)&&Finite(g.Velocity.y)&&Finite(g.Angle)&&Finite(g.AngularVelocity),"Invalid grain motion");
            foreach(var b in a.Endpoints)Check(Finite(b.Center.x)&&Finite(b.Center.y)&&Finite(b.Velocity.x)&&Finite(b.Velocity.y)&&Finite(b.Angle)&&Finite(b.AngularVelocity),"Invalid endpoint motion");
            foreach(var p in a.Parameters)Check(Finite(p.InverseMass)&&Finite(p.InverseInertia)&&p.InverseMass>=0&&p.InverseInertia>=0,"Invalid body mass");
        }
        static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);

        public static void Write(string caseDirectory, CoupledReplayInput input, CoupledReplayManifest manifest)
        {
            string reservation=caseDirectory+".reserve";
            using(var lockFile=new FileStream(reservation,FileMode.CreateNew,FileAccess.Write,FileShare.None)){}
            string binary=Path.Combine(caseDirectory,"input.bin"), json=Path.Combine(caseDirectory,"manifest.json");
            bool created=false;
            try
            {
                if(Directory.Exists(caseDirectory) || File.Exists(caseDirectory)) throw new IOException("Archive already exists: " + caseDirectory);
                Directory.CreateDirectory(caseDirectory); created=true;
                ValidateForReplay(input);
                byte[] bytes=Encode(input);
                using(var f=new FileStream(binary,FileMode.CreateNew,FileAccess.Write,FileShare.None)) f.Write(bytes,0,bytes.Length);
                manifest.sha256=HashFile(binary); manifest.sourceAttempt=input.SourceAttempt; manifest.sourceCommitted=input.SourceCommitted;
                using(var f=new FileStream(json,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                using(var w=new StreamWriter(f,new UTF8Encoding(false))) w.Write(JsonUtility.ToJson(manifest,true));
            }
            catch { if(created){if(File.Exists(json))File.Delete(json); if(File.Exists(binary))File.Delete(binary); Directory.Delete(caseDirectory);} throw; }
            finally { File.Delete(reservation); }
        }
        public static CoupledReplayInput Read(string caseDirectory, out CoupledReplayManifest manifest)
        {
            string json=Path.Combine(caseDirectory,"manifest.json"), binary=Path.Combine(caseDirectory,"input.bin");
            Check(File.Exists(json) && File.Exists(binary),"Incomplete replay archive");
            manifest=JsonUtility.FromJson<CoupledReplayManifest>(File.ReadAllText(json));
            Check(manifest!=null && manifest.magic==Magic && manifest.version==1 && manifest.capturePhase=="CommittedBeforeStep" && manifest.cacheKind=="none" && manifest.inputRelativePath=="input.bin", "Invalid replay manifest");
            Check(new FileInfo(binary).Length<=MaxBytes && HashFile(binary)==manifest.sha256,"Replay hash mismatch");
            var input=Decode(File.ReadAllBytes(binary)); ValidateForReplay(input);
            Check(manifest.sourceAttempt==input.SourceAttempt && manifest.sourceCommitted==input.SourceCommitted,"Replay source tick mismatch");
            return input;
        }
    }
}
