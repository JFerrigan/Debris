using System;
using Debris.Core;
using Debris.World;
using Debris.Materials;
using Debris.Sites;
using NUnit.Framework;
using UnityEngine;
namespace Debris.Core.Tests
{
    public class ContentTests
    {
        [Test] public void RandomRangeNeverIncludesUpperBound()
        {
            var random=new DeterministicRandom(42);
            for(int i=0;i<100000;i++) { Assert.That(random.NextFloat(),Is.LessThan(1f)); Assert.That(random.NextInt(7),Is.InRange(0,6)); }
            Assert.Throws<ArgumentOutOfRangeException>(()=>random.NextInt(0));
        }
        [Test] public void IdAndCoordinateValidation()
        {
            Assert.That(new StableId("ABCDEF00000000000000000000000000").Value,Is.EqualTo("abcdef00000000000000000000000000"));
            Assert.Throws<ArgumentException>(()=>new StableId("not-an-id"));
            Assert.Throws<ArgumentOutOfRangeException>(()=>ChunkCoord.FromCell(0,0,0));
        }
        [Test] public void GeneratedChunkMatchesSubchunksAndSiteIdentityChangesGeometry()
        {
            var catalog=Resources.Load<MaterialCatalog>("Materials"); var profile=Resources.Load<AsteroidProfile>("Asteroid");
            Assert.That(catalog,Is.Not.Null);catalog.Validate();profile.Validate(catalog);
            var id=new StableId("00000000000000000000000000000001");
            var large=AsteroidGenerator.GenerateChunk(42,id,-1,-1,128,profile,catalog);
            for(int cy=0;cy<2;cy++) for(int cx=0;cx<2;cx++)
            {
                var small=AsteroidGenerator.GenerateChunk(42,id,-2+cx,-2+cy,64,profile,catalog);
                for(int y=0;y<64;y++) for(int x=0;x<64;x++) Assert.That(small[y*64+x],Is.EqualTo(large[(cy*64+y)*128+cx*64+x]));
            }
            CollectionAssert.AreEqual(large,AsteroidGenerator.GenerateChunk(42,id,-1,-1,128,profile,catalog));
            CollectionAssert.AreNotEqual(large,AsteroidGenerator.GenerateChunk(42,new StableId("00000000000000000000000000000002"),-1,-1,128,profile,catalog));
        }
        [Test] public void InvalidCatalogAndWeightsFailBeforeGeneration()
        {
            var material=ScriptableObject.CreateInstance<MaterialDefinition>();var catalog=ScriptableObject.CreateInstance<MaterialCatalog>();var profile=ScriptableObject.CreateInstance<AsteroidProfile>();
            try
            {
                material.Configure("test",Color.white,1,1,1,Color.black);
                Assert.Throws<InvalidOperationException>(()=>catalog.Configure(new[]{material,material}));
                catalog.Configure(new[]{material});
                profile.Configure(10,20,new[]{new MaterialBand{MaterialKey="test",Weight=-1}});
                Assert.Throws<InvalidOperationException>(()=>profile.Validate(catalog));
                profile.Configure(10,20,new[]{new MaterialBand{MaterialKey="missing",Weight=1}});
                Assert.Throws<System.Collections.Generic.KeyNotFoundException>(()=>profile.Validate(catalog));
            }
            finally {UnityEngine.Object.DestroyImmediate(profile);UnityEngine.Object.DestroyImmediate(catalog);UnityEngine.Object.DestroyImmediate(material);}
        }
    }
}
