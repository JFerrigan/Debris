using Debris.Materials;
using NUnit.Framework;
using UnityEngine;

namespace Debris.Simulation.Tests
{
    public sealed class CandidateTerrainTests
    {
        [Test]
        public void TranslatedChunkAddressesRoundTrip()
        {
            var terrain=new CandidateTerrainState(Snapshot(),0);
            Assert.That(terrain.TryAddress(new Vector2Int(-7,13),out var first,out var index),Is.True);
            Assert.That(first,Is.EqualTo(0));Assert.That(index,Is.EqualTo(0));
            Assert.That(terrain.TryAddress(new Vector2Int(-3,17),out first,out index),Is.True);
            Assert.That(first,Is.EqualTo(3));Assert.That(index,Is.EqualTo(0));
            Assert.That(terrain.TryAddress(new Vector2Int(-8,13),out _,out _),Is.False);
        }
        [Test]
        public void ImportOwnsTerrainArraysAndPreparedEditsArePrivate()
        {
            var source=Snapshot();var terrain=new CandidateTerrainState(source,0);source.Fields[0][0]=0;source.Damage[0][0]=99;
            Assert.That(terrain.MaterialAt(new Vector2Int(-7,13)),Is.EqualTo(1));Assert.That(terrain.DamageAt(new Vector2Int(-7,13)),Is.Zero);
        }
        [Test]
        public void DrillSelectionIsExposedAndDeterministic()
        {
            var source=Snapshot();source.Fields[0][4]=1;source.Fields[0][5]=1;source.Fields[0][6]=1;source.Fields[0][9]=1;
            var terrain=new CandidateTerrainState(source,0);
            Assert.That(terrain.TrySelectDrillCell(new Vector2(-5.5f,14.5f),3,out var cell),Is.True);
            Assert.That(cell,Is.EqualTo(new Vector2Int(-6,14)));
        }
        [Test]
        public void PreparingAndStaleEditsDoNotOverwriteLiveTerrain()
        {
            var terrain=new CandidateTerrainState(Snapshot(),0);var catalog=Resources.Load<MaterialCatalog>("Materials");
            var cell=new Vector2Int(-7,13);var material=terrain.MaterialAt(cell);var damage=terrain.DamageAt(cell);var revision=terrain.Revision;
            var status=terrain.TryPrepareCell(cell,120,1f/60,catalog,out var first);
            Assert.That(status,Is.EqualTo(CandidateEditStatus.DamageApplied).Or.EqualTo(CandidateEditStatus.Released));
            Assert.That(terrain.MaterialAt(cell),Is.EqualTo(material));Assert.That(terrain.DamageAt(cell),Is.EqualTo(damage));Assert.That(terrain.Revision,Is.EqualTo(revision));
            terrain.Publish(first);terrain.TryPrepareCell(cell,120,1f/60,catalog,out _);
            Assert.That(terrain.IsCurrent(first),Is.False);
        }
        static MatterSnapshot Snapshot()
        {
            const int side=2,chunk=4;var fields=new uint[4][];var damage=new float[4][];
            for(int i=0;i<4;i++){fields[i]=new uint[16];damage[i]=new float[16];}
            fields[0][0]=1;
            return new MatterSnapshot{Side=side,ChunkSize=chunk,OriginX=-7,OriginY=13,Fields=fields,Damage=damage,Cells=new[] {new LooseCell{Identity=3}},NextIdentity=4};
        }
    }
}
