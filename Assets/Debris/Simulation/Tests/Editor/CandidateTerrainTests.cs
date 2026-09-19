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
        static MatterSnapshot Snapshot()
        {
            const int side=2,chunk=4;var fields=new uint[4][];var damage=new float[4][];
            for(int i=0;i<4;i++){fields[i]=new uint[16];damage[i]=new float[16];}
            fields[0][0]=1;
            return new MatterSnapshot{Side=side,ChunkSize=chunk,OriginX=-7,OriginY=13,Fields=fields,Damage=damage,Cells=new[] {new LooseCell{Identity=3}},NextIdentity=4};
        }
    }
}
