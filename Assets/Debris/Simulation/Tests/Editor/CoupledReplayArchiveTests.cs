using System;
using System.IO;
using Debris.Simulation.ParallelProof;
using ProofGrain = Debris.Simulation.ParallelProof.LooseCell;
using NUnit.Framework;
using UnityEngine;

namespace Debris.Simulation.Tests
{
    public sealed class CoupledReplayArchiveTests
    {
        static CoupledReplayInput Sample()
        {
            return new CoupledReplayInput {
                Grains=new[]{new ProofGrain { Center=new Vector2(-0f,2), Velocity=new Vector2(3,-4), Angle=-0f,
                    Material=1,Identity=19,Flags=7,Reserved0=11,Reserved1=12,Reserved2=13 }},
                Endpoints=new[]{new BodyState { Center=new Vector2(-0f,2),Reserved0=23 },default(BodyState),
                    new BodyState { Center=new Vector2(5,6),Reserved1=29 }},
                Parameters=new[]{new BodyParameters { InverseMass=.25f,InverseInertia=1.5f,BoundaryCount=1,Mobility=1,ShapeRevision=3 }},
                Boundaries=new[]{new Boundary { HalfSize=Vector2.one,Body=2,Feature=42,Reserved0=44,Reserved1=45 }},
                Masses=new[]{4f},Diagnostics=new uint[16],GrainCapacity=2,BoundaryCapacity=3,
                VelocityIterations=4,PositionIterations=2,CandidateSlots=64,RigidContactCapacity=4096,
                Friction=.3f,Dt=1f/60f,Force=new Vector2(-0f,1),SourceAttempt=2,SourceCommitted=1
            };
        }
        static string Temp() => Path.Combine(Path.GetTempPath(),"b3r-v2-0a-codec-"+Guid.NewGuid().ToString("N"));
        [Test] public void ExactRoundtripPreservesBitsAndAllocatedEndpointIndex()
        {
            var sample=Sample(); var bytes=CoupledReplayArchive.Encode(sample);
            var result=CoupledReplayArchive.Decode(bytes);
            Assert.That(CoupledReplayArchive.Encode(result),Is.EqualTo(bytes));
            Assert.That(result.Endpoints.Length,Is.EqualTo(3));
            Assert.That(result.Boundaries[0].Body,Is.EqualTo(2u));
            Assert.That(BitConverter.SingleToInt32Bits(result.Grains[0].Center.x),Is.EqualTo(unchecked((int)0x80000000)));
            Assert.That(result.Grains[0].Reserved2,Is.EqualTo(13u));
            Assert.That(result.Masses[0],Is.EqualTo(4f));
        }
        [Test] public void RejectsMalformedVersionSectionsLengthAndTrailingBytes()
        {
            var bytes=CoupledReplayArchive.Encode(Sample());
            var shortBytes=new byte[bytes.Length-1]; Array.Copy(bytes,shortBytes,shortBytes.Length);
            Assert.Throws<InvalidDataException>(()=>CoupledReplayArchive.Decode(shortBytes));
            var version=(byte[])bytes.Clone(); version[11]=2;
            Assert.Throws<InvalidDataException>(()=>CoupledReplayArchive.Decode(version));
            var section=(byte[])bytes.Clone(); section[87]=2;
            Assert.Throws<InvalidDataException>(()=>CoupledReplayArchive.Decode(section));
            var duplicate=(byte[])bytes.Clone(); duplicate[83]=2;
            Assert.Throws<InvalidDataException>(()=>CoupledReplayArchive.Decode(duplicate));
            var length=(byte[])bytes.Clone(); length[91]=47;
            Assert.Throws<InvalidDataException>(()=>CoupledReplayArchive.Decode(length));
            var negative=(byte[])bytes.Clone(); negative[87]=255; negative[88]=255; negative[89]=255; negative[90]=255;
            Assert.Throws<InvalidDataException>(()=>CoupledReplayArchive.Decode(negative));
            var trailing=new byte[bytes.Length+1]; Array.Copy(bytes,trailing,bytes.Length);
            Assert.Throws<InvalidDataException>(()=>CoupledReplayArchive.Decode(trailing));
        }
        [Test] public void MissingManifestHashMismatchAndDuplicateWritePreserveArchive()
        {
            string path=Temp();
            try
            {
                var sample=Sample(); var manifest=new CoupledReplayManifest();
                CoupledReplayArchive.Write(path,sample,manifest);
                string binary=Path.Combine(path,"input.bin"), json=Path.Combine(path,"manifest.json");
                string first=CoupledReplayArchive.HashFile(binary);
                Assert.Throws<IOException>(()=>CoupledReplayArchive.Write(path,sample,new CoupledReplayManifest()));
                Assert.That(CoupledReplayArchive.HashFile(binary),Is.EqualTo(first));
                File.Delete(json);
                Assert.Throws<InvalidDataException>(()=>CoupledReplayArchive.Read(path,out _));
                File.WriteAllText(json,JsonUtility.ToJson(manifest));
                var data=File.ReadAllBytes(binary); data[data.Length-1]^=1; File.WriteAllBytes(binary,data);
                Assert.Throws<InvalidDataException>(()=>CoupledReplayArchive.Read(path,out _));
            }
            finally { if(Directory.Exists(path))Directory.Delete(path,true); }
        }
        [Test] public void ValidatorRejectsInvalidMassAndMotion()
        {
            var sample=Sample(); sample.Masses[0]=float.NaN;
            var decoded=CoupledReplayArchive.Decode(CoupledReplayArchive.Encode(sample));
            Assert.Throws<InvalidDataException>(()=>CoupledReplayArchive.ValidateForReplay(decoded));
            sample.Masses[0]=4; sample.Grains[0].Velocity.x=float.PositiveInfinity;
            Assert.Throws<InvalidDataException>(()=>CoupledReplayArchive.ValidateForReplay(sample));
        }
    }
}
