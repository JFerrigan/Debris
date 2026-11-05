using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Debris.Simulation.Playground;
using NUnit.Framework;
using UnityEngine;

namespace Debris.Simulation.Tests
{
    public sealed class PlaygroundWorldTests
    {
        [TestCase(PlaygroundPreset.Empty),TestCase(PlaygroundPreset.HeavyBlock),TestCase(PlaygroundPreset.OffCenter)]
        [TestCase(PlaygroundPreset.Stack),TestCase(PlaygroundPreset.PackedPocket),TestCase(PlaygroundPreset.FreePair)]
        public void PresetsHaveValidNonoverlappingGeometryAndUniqueIdentities(PlaygroundPreset preset)
        {
            var world=PlaygroundWorldDefinition.Preset(preset);world.Validate();PlaygroundBudget.Admit(world);
            Assert.That(world.Objects.Select(o=>o.Id).Distinct().Count(),Is.EqualTo(world.Objects.Count));
            Assert.That(world.Objects.Where(o=>o.Wall).All(o=>o.Anchored),Is.True);
            if(preset==PlaygroundPreset.FreePair)Assert.That(world.Objects.Any(o=>o.Anchored),Is.False);
        }
        [Test] public void RotatedPlacementRejectsCornerOverlapAndContainedShape()
        {
            var world=new PlaygroundWorldDefinition();
            var box=new PlaygroundObject{Shape=PlaygroundShape.Rectangle,Half=new Vector2(4,1),Angle=Mathf.PI/4};
            Assert.That(world.TryAdd(box,out _),Is.True);
            Assert.That(world.CanPlace(new PlaygroundObject{Center=new Vector2(2.5f,2.5f)}),Is.False);
            Assert.That(world.CanPlace(new PlaygroundObject{Center=Vector2.zero}),Is.False);
            Assert.That(world.CanPlace(new PlaygroundObject{Center=new Vector2(-8,0)}),Is.True);
        }
        [Test] public void GrainCapacityDoesNotEvictOrConsumeIdentityOnRefusal()
        {
            var world=new PlaygroundWorldDefinition();
            for(int i=0;i<256;i++)Assert.That(world.TryAdd(new PlaygroundObject{Center=new Vector2((i%16)*2,(i/16)*2)},out _),Is.True);
            uint next=world.NextGrainIdentity;string[] ids=world.Objects.Select(o=>o.Id).ToArray();
            Assert.That(world.TryAdd(new PlaygroundObject{Center=new Vector2(-10,0)},out var reason),Is.False);
            Assert.That(reason,Does.Contain("capacity"));Assert.That(world.NextGrainIdentity,Is.EqualTo(next));
            Assert.That(world.Objects.Select(o=>o.Id),Is.EqualTo(ids));
        }
        [Test] public void RectangleMassAndInertiaUseAreaAndUnitGrainVolume()
        {
            var body=new PlaygroundObject{Shape=PlaygroundShape.Rectangle,Half=new Vector2(2,3),Density=5};
            Assert.That(body.Mass,Is.EqualTo(120));Assert.That(body.Inertia,Is.EqualTo(520).Within(1e-5));
            var grain=new PlaygroundObject{Density=6};Assert.That(grain.Mass,Is.EqualTo(6));Assert.That(grain.Inertia,Is.EqualTo(1));
        }
        [Test] public void SeedPacksGrainsFirstAndPreservesBodyIdentityAfterRemoval()
        {
            var world=PlaygroundWorldDefinition.Preset(PlaygroundPreset.HeavyBlock);
            var before=PlaygroundSeed.Build(world);int grains=before.Initial.Grains.Length;
            Assert.That(before.Ordered.Take(grains).All(o=>o.Shape==PlaygroundShape.Grain),Is.True);
            var body=before.Ordered.First(o=>!o.Anchored&&o.Shape==PlaygroundShape.Rectangle);
            var key=before.Initial.Bindings[Array.FindIndex(before.Ordered,o=>o.Id==body.Id)].Identity;
            world.Objects.RemoveAll(o=>o.Shape==PlaygroundShape.Grain);
            var after=PlaygroundSeed.Build(world);
            Assert.That(after.Initial.Bindings[Array.FindIndex(after.Ordered,o=>o.Id==body.Id)].Identity,Is.EqualTo(key));
            Assert.That(after.Initial.Grains,Is.Empty);
            Assert.That(Marshal.SizeOf<PlaygroundShapeRecord>(),Is.EqualTo(32));
            Assert.That(Marshal.OffsetOf<PlaygroundShapeRecord>(nameof(PlaygroundShapeRecord.Color)).ToInt32(),Is.EqualTo(8));
        }
        [Test] public void TrulyEmptyWorldHasNoSolverSeedOrExplicitBufferReservation()
        {
            var world=new PlaygroundWorldDefinition();var seed=PlaygroundSeed.Build(world);
            Assert.That(seed.Ordered,Is.Empty);Assert.That(seed.Initial,Is.Null);
            Assert.That(PlaygroundBudget.RequiredBytes(0,0),Is.Zero);
        }
        [Test] public void InvalidDensityMobilityIdentityAndWholeShapeBoundsAreRejected()
        {
            var world=PlaygroundWorldDefinition.Preset(PlaygroundPreset.FreePair);
            var b=world.Objects[0];b.Density=float.NaN;Assert.Throws<ArgumentException>(()=>world.Validate());
            b.Density=1;b.Anchored=true;b.Spin=1;Assert.Throws<ArgumentException>(()=>world.Validate());
            b.Spin=0;b.Id=world.Objects[1].Id;Assert.Throws<ArgumentException>(()=>world.Validate());
            b.Id=Guid.NewGuid().ToString("N");b.Center=new Vector2(511,0);Assert.Throws<ArgumentException>(()=>world.Validate());
        }
        [Test] public void SceneRoundtripPreservesStateAndRejectsFutureOrIncompleteFiles()
        {
            var world=PlaygroundWorldDefinition.Preset(PlaygroundPreset.OffCenter);
            var body=world.Objects.First(o=>!o.Anchored);body.Angle=.1234567f;body.Spin=.007f;
            string json=JsonUtility.ToJson(world);
            var restored=PlaygroundSceneFile.Decode(json);
            Assert.That(JsonUtility.ToJson(restored),Is.EqualTo(json));
            Assert.Throws<InvalidDataException>(()=>PlaygroundSceneFile.Decode("{\"Version\":2,\"Objects\":[]}"));
            Assert.Throws<InvalidDataException>(()=>PlaygroundSceneFile.Decode("{\"Version\":1}"));
            var duplicate=world.Copy();duplicate.Objects[1].Id=duplicate.Objects[0].Id;
            Assert.Throws<ArgumentException>(()=>PlaygroundSceneFile.Decode(JsonUtility.ToJson(duplicate)));
        }
        [Test] public void InvalidSavePreservesExistingSceneFile()
        {
            string directory=Path.Combine(Path.GetTempPath(),"debris-playground-"+Guid.NewGuid().ToString("N"));
            string path=Path.Combine(directory,"world.json");
            try
            {
                var world=PlaygroundWorldDefinition.Preset(PlaygroundPreset.FreePair);PlaygroundSceneFile.Save(path,world);
                byte[] before=File.ReadAllBytes(path);world.Objects[0].Density=-1;
                Assert.Throws<ArgumentException>(()=>PlaygroundSceneFile.Save(path,world));
                Assert.That(File.ReadAllBytes(path),Is.EqualTo(before));
                Assert.That(Directory.GetFiles(directory),Has.Length.EqualTo(1));
            }
            finally { if(Directory.Exists(directory))Directory.Delete(directory,true); }
        }
    }
}
