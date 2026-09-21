using Debris.Materials;
using Debris.Simulation.ParallelProof;
using NUnit.Framework;
using System.Collections.Generic;
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
        [Test]
        public void IncrementalReleasedCellCacheMatchesFullRebuildAtTranslatedConcaveSeam()
        {
            var source=Snapshot();source.Fields[0][1]=source.Fields[0][2]=source.Fields[0][5]=source.Fields[0][9]=1;source.Fields[1][0]=source.Fields[1][4]=1;
            var terrain=new CandidateTerrainState(source,9);var catalog=Resources.Load<MaterialCatalog>("Materials");var cell=new Vector2Int(-6,13);
            Assert.That(terrain.TryPrepareCell(cell,1000000,1,catalog,out var edit),Is.EqualTo(CandidateEditStatus.Released));
            var incremental=new CandidateBoundaryBuilder.TerrainBoundaryCache(terrain).ApplyRelease(terrain,edit).Flatten();var expected=new List<Boundary>();CandidateBoundaryBuilder.AppendMaskBoundaries(expected,terrain.BuildMask(edit),9,terrain.Origin.x,terrain.Origin.y,true);
            Assert.That(incremental.Length,Is.EqualTo(expected.Count));for(int i=0;i<expected.Count;i++){Assert.That(incremental[i].Body,Is.EqualTo(expected[i].Body));Assert.That(incremental[i].Center,Is.EqualTo(expected[i].Center));Assert.That(incremental[i].HalfSize,Is.EqualTo(expected[i].HalfSize));}
        }
        [Test]
        public void SupportedPageRequiresTheCompleteProposedSquare()
        {
            Assert.That(CandidateTerrainState.IsSupportedGrainSquare(new Vector2(-511.5f,511.5f)),Is.True);
            Assert.That(CandidateTerrainState.IsSupportedGrainSquare(new Vector2(-511.5001f,0)),Is.False);
            Assert.That(CandidateTerrainState.IsSupportedGrainSquare(new Vector2(511.5001f,0)),Is.False);
        }
        [Test]
        public void ReleasePreparesTheNextTerrainShapeRevision()
        {
            var terrain=new CandidateTerrainState(Snapshot(),9);var catalog=Resources.Load<MaterialCatalog>("Materials");Assert.That(terrain.TryPrepareCell(new Vector2Int(-7,13),1000000,1,catalog,out var edit),Is.EqualTo(CandidateEditStatus.Released));
            var current=new[]{new BodyParameters{Mobility=0,ShapeRevision=terrain.Revision}};var cache=new CandidateBoundaryBuilder.TerrainBoundaryCache(terrain);
            Assert.That(CandidateBoundaryBuilder.TryPrepareTerrainReplacement(terrain,edit,cache,current,System.Array.Empty<Boundary>(),4096,out var replacement,out _),Is.True);
            Assert.That(replacement.Definitions[0].ShapeRevision,Is.EqualTo(terrain.Revision+1));
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
