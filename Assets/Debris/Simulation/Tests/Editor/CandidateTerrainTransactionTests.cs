using System.Collections;
using Debris.Materials;
using Debris.Ships;
using Debris.Sites;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Debris.Simulation.Tests
{
    public sealed class CandidateTerrainTransactionTests
    {
        [UnityTest,Timeout(120000)] public IEnumerator DamageOnlyDrillPublishesWithoutPlacementReadback()
        {
            var catalog=Resources.Load<MaterialCatalog>("Materials");var ship=new ShipRuntime(Resources.Load<ShipBlueprint>("StarterShip"));
            using(var mirror=new MatterSession(catalog,Resources.Load<AsteroidProfile>("Asteroid"),4,128,8192))
            {
                mirror.ConfigureShip(ship.CollisionMask(),ship.Position);mirror.ConfigureShipBody(ship.MassProperties(catalog));
                var snapshot=mirror.SnapshotAsync();while(!snapshot.IsCompleted)yield return null;
                SetDrillTarget(snapshot.Result);mirror.Restore(snapshot.Result);snapshot=mirror.SnapshotAsync();while(!snapshot.IsCompleted)yield return null;
                using(var candidate=ParallelGameplaySession.Import(snapshot.Result,ship,catalog))
                {
                    candidate.AttachTerrainMirror(mirror);Assert.That(candidate.RequestMountedDrill(1,6),Is.True);
                    Assert.That(candidate.TryAdvanceTerrainEdit(out var result),Is.True);Assert.That(result.Status,Is.EqualTo(CandidateEditStatus.DamageApplied));Assert.That(candidate.TopologyBusy,Is.False);Assert.That(candidate.Solver.SnapshotRequests,Is.Zero);
                }
            }
        }
        [UnityTest,Timeout(120000)] public IEnumerator ReleasedCellPublishesTerrainGrainAndNextShapeRevisionTogether()
        {
            var catalog=Resources.Load<MaterialCatalog>("Materials");var ship=new ShipRuntime(Resources.Load<ShipBlueprint>("StarterShip"));
            using(var mirror=new MatterSession(catalog,Resources.Load<AsteroidProfile>("Asteroid"),4,128,8192))
            {
                mirror.ConfigureShip(ship.CollisionMask(),ship.Position);mirror.ConfigureShipBody(ship.MassProperties(catalog));
                var snapshot=mirror.SnapshotAsync();while(!snapshot.IsCompleted)yield return null;
                SetDrillTarget(snapshot.Result);mirror.Restore(snapshot.Result);snapshot=mirror.SnapshotAsync();while(!snapshot.IsCompleted)yield return null;
                using(var candidate=ParallelGameplaySession.Import(snapshot.Result,ship,catalog))
                {
                    candidate.AttachTerrainMirror(mirror);Assert.That(candidate.RequestMountedDrill(1000000,6),Is.True);CandidateEditResult result=default;while(!candidate.TryAdvanceTerrainEdit(out result))yield return null;
                    Assert.That(result.Status,Is.EqualTo(CandidateEditStatus.Released));Assert.That(candidate.Terrain.MaterialAt(result.Cell),Is.Zero);Assert.That(candidate.TerrainRevision,Is.EqualTo(2));Assert.That(candidate.Solver.GrainCount,Is.EqualTo(1));Assert.That(candidate.Solver.BodyDefinitions[candidate.Solver.BodyCount-1].ShapeRevision,Is.EqualTo(2));
                    var state=candidate.Solver.SnapshotAsync();while(!state.IsCompleted)yield return null;Assert.That(state.Result.Grains[0].Material,Is.EqualTo(1));Assert.That(state.Result.Grains[0].Identity,Is.EqualTo(result.Identity));
                }
            }
        }
        static void SetDrillTarget(MatterSnapshot state)
        {
            foreach(var field in state.Fields)System.Array.Clear(field,0,field.Length);foreach(var damage in state.Damage)System.Array.Clear(damage,0,damage.Length);System.Array.Clear(state.Dirty,0,state.Dirty.Length);state.Counters=new uint[]{0,1,0,0};int x=Mathf.FloorToInt(state.ShipPose[0].x+63)-state.OriginX,y=Mathf.FloorToInt(state.ShipPose[0].y)-state.OriginY,slice=(y/state.ChunkSize)*state.Side+x/state.ChunkSize,index=(y%state.ChunkSize)*state.ChunkSize+x%state.ChunkSize;state.Fields[slice][index]=1;
        }
    }
}
