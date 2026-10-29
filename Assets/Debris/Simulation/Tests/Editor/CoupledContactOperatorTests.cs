using System;
using System.Runtime.InteropServices;
using Debris.Simulation.CoupledContacts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Debris.Simulation.Tests
{
    public sealed class CoupledContactOperatorTests
    {
        [Test] public void EmptyFrozenGraphKeepsFreeMotionAcrossRepeatedProducts()
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            var bodies=new[]{new CoupledContactBody{Center=Vector2.zero,InverseMass=1,InverseInertia=1}};
            using(var op=new CoupledContactOperator(bodies,Array.Empty<CoupledContactRow>()))
            {
                var free=new[]{new Vector4(2,-3,.25f,0)};
                for(int i=0;i<2;i++)
                {
                    var result=op.Apply(free,Array.Empty<float>());
                    Assert.That(op.SegmentCount,Is.Zero);
                    Assert.That(result.RowVelocity,Is.Empty);
                    Assert.That(result.EndpointMotion[0],Is.EqualTo(free[0]));
                }
            }
        }
        [Test] public void FrozenGpuProductMatchesOffCenterTwoBodyMassAndTorque()
        {
            Assert.That(Marshal.SizeOf<CoupledContactBody>(),Is.EqualTo(16));
            Assert.That(Marshal.SizeOf<CoupledContactRow>(),Is.EqualTo(32));
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            var bodies=new[]
            {
                new CoupledContactBody{Center=Vector2.zero,InverseMass=1,InverseInertia=1},
                new CoupledContactBody{Center=Vector2.right,InverseMass=1,InverseInertia=1}
            };
            float root=Mathf.Sqrt(2.125f);
            var row=new CoupledContactRow{A=0,B=1,Point=new Vector2(.5f,.25f),
                Direction=Vector2.right,InverseRoot=1/root};
            using(var op=new CoupledContactOperator(bodies,new[]{row}))
            {
                var result=op.Apply(new Vector4[2],new[]{root});
                Assert.That(result.EndpointMotion[0].x,Is.EqualTo(-1).Within(1e-5));
                Assert.That(result.EndpointMotion[1].x,Is.EqualTo(1).Within(1e-5));
                Assert.That(result.EndpointMotion[0].z,Is.EqualTo(.25).Within(1e-5));
                Assert.That(result.EndpointMotion[1].z,Is.EqualTo(-.25).Within(1e-5));
                Assert.That(result.RowVelocity[0],Is.EqualTo(root).Within(1e-5));
                var repeated=op.Apply(new Vector4[2],new[]{root});
                Assert.That(repeated.RowVelocity[0],Is.EqualTo(result.RowVelocity[0]).Within(1e-6));
            }
        }

        [Test] public void HighDegreeReactionUsesTheSameRowOrderAfterEveryScatter()
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            const int contacts=200;
            var bodies=new CoupledContactBody[contacts+1];
            var rows=new CoupledContactRow[contacts];
            var impulses=new float[contacts];
            for(int i=0;i<bodies.Length;i++)
                bodies[i]=new CoupledContactBody{InverseMass=1,InverseInertia=1};
            float root=Mathf.Sqrt(2);
            for(int i=0;i<contacts;i++)
            {
                rows[i]=new CoupledContactRow{A=0,B=(uint)(i+1),
                    Point=Vector2.zero,Direction=Vector2.right,InverseRoot=1/root};
                impulses[i]=root*(i%2==0?1f:-1f)/(i+1);
            }
            using(var op=new CoupledContactOperator(bodies,rows))
            {
                int first=0;
                for(int repeat=0;repeat<4;repeat++)
                {
                    var result=op.Apply(new Vector4[bodies.Length],impulses);
                    int bits=BitConverter.SingleToInt32Bits(result.EndpointMotion[0].x);
                    if(repeat==0)first=bits;
                    else Assert.That(bits,Is.EqualTo(first));
                }
            }
        }

        [Test] public void FrozenGpuProductMatchesIndependentDoubleMatrix()
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            var referenceBodies=new[]
            {
                new DBody{Center=new DVec(0,0),InverseMass=.125,InverseInertia=.04},
                new DBody{Center=new DVec(1,.2),InverseMass=2,InverseInertia=3},
                new DBody{Center=new DVec(-.3,1),InverseMass=0,InverseInertia=0}
            };
            var referenceRows=new[]
            {
                new DRow{A=0,B=1,Point=new DVec(.5,-.4),Direction=new DVec(1,0)},
                new DRow{A=0,B=1,Point=new DVec(.5,-.4),Direction=new DVec(0,1)},
                new DRow{A=1,B=2,Point=new DVec(.2,.7),Direction=new DVec(0,1)},
                new DRow{A=0,B=2,Point=new DVec(-.4,.3),Direction=new DVec(-1,0)}
            };
            var system=new DSystem(referenceBodies,referenceRows);
            var bodies=new CoupledContactBody[referenceBodies.Length];
            for(int i=0;i<bodies.Length;i++)bodies[i]=new CoupledContactBody
            {
                Center=new Vector2((float)referenceBodies[i].Center.X,
                    (float)referenceBodies[i].Center.Y),
                InverseMass=(float)referenceBodies[i].InverseMass,
                InverseInertia=(float)referenceBodies[i].InverseInertia
            };
            var rows=new CoupledContactRow[referenceRows.Length];
            var x=new[]{.7f,-.2f,1.1f,.35f};
            var physical=new double[x.Length];
            for(int i=0;i<rows.Length;i++)
            {
                double root=Math.Sqrt(system.A[i,i]);
                physical[i]=x[i]/root;
                var r=referenceRows[i];
                rows[i]=new CoupledContactRow{A=(uint)r.A,B=(uint)r.B,
                    Point=new Vector2((float)r.Point.X,(float)r.Point.Y),
                    Direction=new Vector2((float)r.Direction.X,(float)r.Direction.Y),
                    InverseRoot=(float)(1/root)};
            }
            var expectedMotion=system.Apply(physical);
            var expectedRows=system.GatherApply(physical);
            using(var op=new CoupledContactOperator(bodies,rows))
            {
                var actual=op.Apply(new Vector4[bodies.Length],x);
                for(int i=0;i<bodies.Length;i++)
                {
                    Assert.That(actual.EndpointMotion[i].x,
                        Is.EqualTo(expectedMotion[3*i]).Within(2e-5));
                    Assert.That(actual.EndpointMotion[i].y,
                        Is.EqualTo(expectedMotion[3*i+1]).Within(2e-5));
                    Assert.That(actual.EndpointMotion[i].z,
                        Is.EqualTo(expectedMotion[3*i+2]).Within(2e-5));
                }
                for(int i=0;i<rows.Length;i++)
                    Assert.That(actual.RowVelocity[i],
                        Is.EqualTo(expectedRows[i]/Math.Sqrt(system.A[i,i])).Within(2e-5));
            }
        }

        [Test] public void GpuConvexManifoldFeedsScaledRowsAndOperator()
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            Assert.That(Marshal.SizeOf<CoupledContactPair>(),Is.EqualTo(48));
            var bodies=new[]
            {
                new CoupledContactBody{Center=Vector2.zero,InverseMass=1,InverseInertia=1},
                new CoupledContactBody{Center=Vector2.right,InverseMass=1,InverseInertia=1}
            };
            var pair=new CoupledContactPair{ACenter=Vector2.zero,AHalf=Vector2.one*.5f,
                AEndpoint=0,BCenter=Vector2.right,BHalf=Vector2.one*.5f,BEndpoint=1};
            float root=Mathf.Sqrt(2.5f);
            var explicitRows=new CoupledContactRow[4];
            for(int point=0;point<2;point++)for(int axis=0;axis<2;axis++)
                explicitRows[2*point+axis]=new CoupledContactRow{A=0,B=1,
                    Point=new Vector2(.5f,point==0?-.5f:.5f),
                    Direction=axis==0?Vector2.right:Vector2.up,InverseRoot=1/root};
            var impulses=new[]{root,0,root,0};
            using(var generated=new CoupledContactOperator(bodies,new[]{pair}))
            using(var expected=new CoupledContactOperator(bodies,explicitRows))
            {
                var actual=generated.Apply(new Vector4[2],impulses);
                var direct=expected.Apply(new Vector4[2],impulses);
                Assert.That(actual.ActiveRows,Is.EqualTo(4));
                for(int i=0;i<4;i++)
                {
                    Assert.That(actual.Rows[i].Point.x,Is.EqualTo(explicitRows[i].Point.x).Within(1e-5));
                    Assert.That(actual.Rows[i].Point.y,Is.EqualTo(explicitRows[i].Point.y).Within(1e-5));
                    Assert.That(actual.Rows[i].InverseRoot,Is.EqualTo(explicitRows[i].InverseRoot).Within(1e-5));
                    Assert.That(actual.RowVelocity[i],Is.EqualTo(direct.RowVelocity[i]).Within(1e-5));
                }
                for(int i=0;i<2;i++)
                {
                    Assert.That(actual.EndpointMotion[i].x,Is.EqualTo(direct.EndpointMotion[i].x).Within(1e-5));
                    Assert.That(actual.EndpointMotion[i].z,Is.EqualTo(direct.EndpointMotion[i].z).Within(1e-5));
                }
            }
        }

        [Test] public void SeparatedGpuPairProducesNoActiveRowsOrReaction()
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            var bodies=new[]
            {
                new CoupledContactBody{Center=Vector2.zero,InverseMass=1,InverseInertia=1},
                new CoupledContactBody{Center=new Vector2(3,0),InverseMass=1,InverseInertia=1}
            };
            var pair=new CoupledContactPair{ACenter=Vector2.zero,AHalf=Vector2.one*.5f,
                AEndpoint=0,BCenter=new Vector2(3,0),BHalf=Vector2.one*.5f,BEndpoint=1};
            using(var op=new CoupledContactOperator(bodies,new[]{pair}))
            {
                var result=op.Apply(new Vector4[2],new[]{1f,1f,1f,1f});
                Assert.That(result.ActiveRows,Is.Zero);
                Assert.That(result.EndpointMotion[0],Is.EqualTo(Vector4.zero));
                Assert.That(result.EndpointMotion[1],Is.EqualTo(Vector4.zero));
            }
        }

        [Test] public void IndexedGpuPrimitivesFeedCompactRowsAndFrozenProduct()
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            var bodies=new[]
            {
                new CoupledContactBody{Center=Vector2.zero,InverseMass=1,InverseInertia=1},
                new CoupledContactBody{Center=Vector2.right,InverseMass=1,InverseInertia=1}
            };
            var primitives=new[]
            {
                new CoupledContactPrimitive{Center=Vector2.zero,Half=Vector2.one*.5f,Endpoint=0},
                new CoupledContactPrimitive{Center=Vector2.right,Half=Vector2.one*.5f,Endpoint=1}
            };
            float root=Mathf.Sqrt(2.5f);
            var impulses=new float[CoupledContactRowBatch.MaxRows];
            impulses[0]=root;impulses[2]=root;
            var explicitRows=new CoupledContactRow[4];
            for(int point=0;point<2;point++)for(int axis=0;axis<2;axis++)
                explicitRows[2*point+axis]=new CoupledContactRow{A=0,B=1,
                    Point=new Vector2(.5f,point==0?-.5f:.5f),
                    Direction=axis==0?Vector2.right:Vector2.up,InverseRoot=1/root};
            using(var indexed=new CoupledContactOperator(bodies,primitives,new Vector2(-8,-8),1,16,16))
            using(var direct=new CoupledContactOperator(bodies,explicitRows))
            {
                var actual=indexed.Apply(new Vector4[2],impulses);
                var expected=direct.Apply(new Vector4[2],new[]{root,0,root,0});
                Assert.That(actual.Success,Is.True);
                Assert.That(indexed.ActualRowCount,Is.EqualTo(4));
                Assert.That(actual.ActiveRows,Is.EqualTo(4));
                for(int i=0;i<4;i++)
                    Assert.That(actual.RowVelocity[i],Is.EqualTo(expected.RowVelocity[i]).Within(1e-5));
                for(int i=0;i<2;i++)
                    Assert.That(actual.EndpointMotion[i].x,
                        Is.EqualTo(expected.EndpointMotion[i].x).Within(1e-5));
            }
        }

        [Test] public void GpuContactKeysIncludeSlotGenerationRevisionAndFeatures()
        {
            Assert.That(Marshal.SizeOf<CoupledContactKeyWords>(),Is.EqualTo(32));
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            var bodies=new[]{
                new CoupledContactBody{InverseMass=1,InverseInertia=1},
                new CoupledContactBody{Center=Vector2.right,InverseMass=1,InverseInertia=1}
            };
            var primitives=new[]{
                new CoupledContactPrimitive{Center=Vector2.zero,Half=Vector2.one*.5f,
                    Endpoint=0,FeatureId=11},
                new CoupledContactPrimitive{Center=Vector2.right,Half=Vector2.one*.5f,
                    Endpoint=1,FeatureId=22}
            };
            var identities=new CoupledEndpointIdentityTable(2);
            identities.Bind(0,CoupledPersistentIdentity.Grain(101),3);
            identities.Bind(1,CoupledPersistentIdentity.Grain(102),4);
            using(var op=new CoupledContactOperator(bodies,primitives,
                new Vector2(-8,-8),1,16,16,endpointIdentities:identities))
            {
                var result=op.Apply(new Vector4[2],
                    new float[CoupledContactRowBatch.MaxRows]);
                Assert.That(result.Success,Is.True);
                Assert.That(op.ActualRowCount,Is.EqualTo(4));
                var keys=new GraphicsBuffer(GraphicsBuffer.Target.Structured,4,32);
                var commands=new CommandBuffer();
                try
                {
                    op.RecordContactKeys(commands,keys);
                    Graphics.ExecuteCommandBuffer(commands);
                    var found=new CoupledContactKeyWords[4];keys.GetData(found);
                    Assert.That(found[0].Endpoints,Is.EqualTo(1u<<14));
                    Assert.That(found[0].SlotGenerationA,Is.EqualTo(1));
                    Assert.That(found[0].SlotGenerationB,Is.EqualTo(1));
                    Assert.That(found[0].RevisionA,Is.EqualTo(3));
                    Assert.That(found[0].RevisionB,Is.EqualTo(4));
                    Assert.That(found[0].FeatureA,Is.EqualTo(11));
                    Assert.That(found[0].FeatureB,Is.EqualTo(22));
                    Assert.That(found[0].Point&4095u,Is.EqualTo(1));
                    Assert.That((found[0].Point>>12)&4095u,Is.EqualTo(3));
                    Assert.That((found[0].Point>>24)&3u,Is.EqualTo(1));
                    Assert.That((found[1].Point>>26)&3u,Is.EqualTo(1));
                    identities.BumpPhysicalRevision(0);
                    Assert.Throws<InvalidOperationException>(()=>
                        op.RecordContactKeys(commands,keys));
                }
                finally{commands.Release();keys.Release();}
            }
        }

        [Test] public void IndexedThinUnionChoosesOneExitBeforeOperator()
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            var patches=new[]{new CoupledUnionPatch{Center=Vector2.zero,
                Half=new Vector2(.1f,2),Feature=1}};
            var faces=CoupledExteriorFaces.Build(patches);
            var group=new CoupledUnionQueryGroup{PatchCount=1,
                QueryCount=(uint)faces.Length,Reserved=1};
            var union=new CoupledUnionGeometry(patches,faces,new[]{group});
            int left=Array.FindIndex(faces,f=>f.Normal==Vector2.left);
            int right=Array.FindIndex(faces,f=>f.Normal==Vector2.right);
            Assert.That(left,Is.GreaterThanOrEqualTo(0));
            Assert.That(right,Is.GreaterThanOrEqualTo(0));
            var primitives=new[]
            {
                new CoupledContactPrimitive{Center=Vector2.zero,
                    Half=Vector2.one*.5f,Endpoint=0},
                union.FacePrimitive(left,0,true,Vector2.zero),
                union.FacePrimitive(right,0,true,Vector2.zero)
            };
            var bodies=new[]{new CoupledContactBody{Center=Vector2.zero,
                InverseMass=1,InverseInertia=1},
                new CoupledContactBody{Center=Vector2.zero}};
            using(var op=new CoupledContactOperator(bodies,primitives,
                new Vector2(-8,-8),1,16,16,union))
            {
                var result=op.Apply(new Vector4[2],
                    new float[CoupledContactRowBatch.MaxRows]);
                Assert.That(result.Success,Is.True);
                Assert.That(op.ActualRowCount,Is.EqualTo(4));
                Assert.That(result.Rows[0].Direction.x,Is.EqualTo(1).Within(1e-5));
                Assert.That(result.Rows[2].Direction.x,Is.EqualTo(1).Within(1e-5));
            }
        }

        [Test] public void IndexedEmptyCavityRetainsBothWalls()
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            var patches=new[]
            {
                new CoupledUnionPatch{Center=new Vector2(0,-1),Half=new Vector2(1.5f,.5f),Feature=1},
                new CoupledUnionPatch{Center=new Vector2(0,1),Half=new Vector2(1.5f,.5f),Feature=2},
                new CoupledUnionPatch{Center=new Vector2(-1,0),Half=Vector2.one*.5f,Feature=3},
                new CoupledUnionPatch{Center=new Vector2(1,0),Half=Vector2.one*.5f,Feature=4}
            };
            var faces=CoupledExteriorFaces.Build(patches);
            int left=Array.FindIndex(faces,f=>f.Normal==Vector2.right&&
                Mathf.Abs(f.A.x+.5f)<1e-5f);
            int right=Array.FindIndex(faces,f=>f.Normal==Vector2.left&&
                Mathf.Abs(f.A.x-.5f)<1e-5f);
            Assert.That(left,Is.GreaterThanOrEqualTo(0));
            Assert.That(right,Is.GreaterThanOrEqualTo(0));
            var union=new CoupledUnionGeometry(patches,faces,new[]{
                new CoupledUnionQueryGroup{PatchCount=(uint)patches.Length,
                    QueryCount=(uint)faces.Length,Reserved=1}});
            var primitives=new[]
            {
                new CoupledContactPrimitive{Center=Vector2.zero,
                    Half=Vector2.one*.5f,Endpoint=0},
                union.FacePrimitive(left,0,true,Vector2.zero),
                union.FacePrimitive(right,0,true,Vector2.zero)
            };
            var bodies=new[]{new CoupledContactBody{Center=Vector2.zero,
                InverseMass=1,InverseInertia=1},
                new CoupledContactBody{Center=Vector2.zero}};
            using(var op=new CoupledContactOperator(bodies,primitives,
                new Vector2(-8,-8),1,16,16,union))
            {
                var result=op.Apply(new Vector4[2],
                    new float[CoupledContactRowBatch.MaxRows]);
                Assert.That(result.Success,Is.True);
                Assert.That(op.ActualRowCount,Is.EqualTo(8));
                bool positive=false,negative=false;
                for(int i=0;i<result.Rows.Length;i+=2)
                {
                    positive|=result.Rows[i].Direction.x>.9f;
                    negative|=result.Rows[i].Direction.x<-.9f;
                }
                Assert.That(positive&&negative,Is.True);
            }
        }

        [Test] public void IndexedFiniteRigidFacesRetainTwoPointTorqueSupport()
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            var firstPatch=new CoupledUnionPatch{Center=Vector2.zero,
                Half=Vector2.one*.5f,Feature=1};
            var secondPatch=new CoupledUnionPatch{Center=Vector2.zero,
                Half=Vector2.one*.5f,Feature=2};
            var firstFaces=CoupledExteriorFaces.Build(new[]{firstPatch});
            var secondFaces=CoupledExteriorFaces.Build(new[]{secondPatch});
            var faces=new CoupledExteriorFace[firstFaces.Length+secondFaces.Length];
            Array.Copy(firstFaces,faces,firstFaces.Length);
            Array.Copy(secondFaces,0,faces,firstFaces.Length,secondFaces.Length);
            int right=Array.FindIndex(firstFaces,f=>f.Normal==Vector2.right);
            int left=Array.FindIndex(secondFaces,f=>f.Normal==Vector2.left);
            Assert.That(right,Is.GreaterThanOrEqualTo(0));
            Assert.That(left,Is.GreaterThanOrEqualTo(0));
            var groups=new[]
            {
                new CoupledUnionQueryGroup{PatchStart=0,PatchCount=1,
                    FirstQuery=0,QueryCount=(uint)firstFaces.Length,Reserved=0},
                new CoupledUnionQueryGroup{UnionCenter=Vector2.right,
                    PatchStart=1,PatchCount=1,FirstQuery=(uint)firstFaces.Length,
                    QueryCount=(uint)secondFaces.Length,Reserved=1}
            };
            var union=new CoupledUnionGeometry(new[]{firstPatch,secondPatch},faces,groups);
            // All exterior proxies must coexist without a false corner fault.
            var primitives=new CoupledContactPrimitive[faces.Length];
            for(int i=0;i<firstFaces.Length;i++)
                primitives[i]=union.FacePrimitive(i,0,false,Vector2.zero);
            for(int i=0;i<secondFaces.Length;i++)
                primitives[firstFaces.Length+i]=union.FacePrimitive(
                    firstFaces.Length+i,1,false,Vector2.zero);
            var bodies=new[]{
                new CoupledContactBody{Center=Vector2.zero,InverseMass=1,InverseInertia=1},
                new CoupledContactBody{Center=Vector2.right,InverseMass=1,InverseInertia=1}
            };
            using(var op=new CoupledContactOperator(bodies,primitives,
                new Vector2(-8,-8),1,16,16,union))
            {
                var result=op.Apply(new Vector4[2],
                    new float[CoupledContactRowBatch.MaxRows]);
                Assert.That(result.Success,Is.True);
                Assert.That(op.ActualRowCount,Is.EqualTo(4));
                Assert.That(result.Rows[0].Direction.x,Is.EqualTo(1).Within(1e-5));
                Assert.That(result.Rows[2].Direction.x,Is.EqualTo(1).Within(1e-5));
                uint expectedA=(uint)right;
                uint expectedB=(uint)(firstFaces.Length+left);
                Assert.That(result.Features[0].APrimitive,Is.EqualTo(expectedA));
                Assert.That(result.Features[0].BPrimitive,Is.EqualTo(expectedB));
            }

            // At a diagonal point the two tied axes describe one contact.
            groups[1].UnionCenter=Vector2.one;
            bodies[1].Center=Vector2.one;
            var cornerUnion=new CoupledUnionGeometry(
                new[]{firstPatch,secondPatch},faces,groups);
            var cornerPrimitives=new CoupledContactPrimitive[faces.Length];
            for(int i=0;i<firstFaces.Length;i++)
                cornerPrimitives[i]=cornerUnion.FacePrimitive(i,0,false,Vector2.zero);
            for(int i=0;i<secondFaces.Length;i++)
                cornerPrimitives[firstFaces.Length+i]=cornerUnion.FacePrimitive(
                    firstFaces.Length+i,1,false,Vector2.zero);
            using(var op=new CoupledContactOperator(bodies,cornerPrimitives,
                new Vector2(-8,-8),1,16,16,cornerUnion))
            {
                var result=op.Apply(new Vector4[2],
                    new float[CoupledContactRowBatch.MaxRows]);
                Assert.That(result.Success,Is.True);
                Assert.That(op.ActualRowCount,Is.EqualTo(2));
                Assert.That(result.Rows[0].Direction.sqrMagnitude,
                    Is.EqualTo(1).Within(1e-5));
            }
        }

        [Test] public void IndexedNonopposingRigidCornerHasOneSelectedAxisPoint()
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            var first=new CoupledUnionPatch{Center=Vector2.zero,
                Half=Vector2.one*.5f,Feature=1};
            var second=new CoupledUnionPatch{Center=Vector2.zero,
                Half=Vector2.one*.5f,Feature=2};
            var firstFaces=CoupledExteriorFaces.Build(new[]{first});
            var secondFaces=CoupledExteriorFaces.Build(new[]{second});
            // Select the two perpendicular exterior intervals in isolation to
            // exercise the corner fallback. The full-union opposing-face case
            // is covered by IndexedFiniteRigidFacesRetainTwoPointTorqueSupport.
            var top=Array.Find(firstFaces,f=>f.Normal==Vector2.up);
            var left=Array.Find(secondFaces,f=>f.Normal==Vector2.left);
            var union=new CoupledUnionGeometry(new[]{first,second},
                new[]{top,left},new[]{
                    new CoupledUnionQueryGroup{PatchStart=0,PatchCount=1,
                        FirstQuery=0,QueryCount=1,Reserved=0},
                    new CoupledUnionQueryGroup{UnionCenter=Vector2.one,
                        PatchStart=1,PatchCount=1,FirstQuery=1,QueryCount=1,Reserved=1}
                });
            var primitives=new[]{
                union.FacePrimitive(0,0,false,Vector2.zero),
                union.FacePrimitive(1,1,false,Vector2.zero)
            };
            var bodies=new[]{
                new CoupledContactBody{Center=Vector2.zero,InverseMass=1,InverseInertia=1},
                new CoupledContactBody{Center=Vector2.one,InverseMass=1,InverseInertia=1}
            };
            using(var op=new CoupledContactOperator(bodies,primitives,
                new Vector2(-8,-8),1,16,16,union))
            {
                var result=op.Apply(new Vector4[2],
                    new float[CoupledContactRowBatch.MaxRows]);
                Assert.That(result.Success,Is.True);
                Assert.That(op.ActualRowCount,Is.EqualTo(2));
                Assert.That(result.Rows[0].Direction.x,Is.EqualTo(1).Within(1e-5));
                Assert.That(result.Rows[0].Point.x,Is.EqualTo(.5f).Within(1e-5));
                Assert.That(result.Rows[0].Point.y,Is.EqualTo(.5f).Within(1e-5));
            }
        }

        [TestCase(200),TestCase(4096),Explicit("V2-1 high-degree GPU operator workload")]
        public void SegmentedFiniteHullReactionIncludesEveryIncidentPoint(int count)
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            var bodies=new CoupledContactBody[count+1];
            var rows=new CoupledContactRow[count];var impulses=new float[count];
            bodies[0]=new CoupledContactBody{Center=Vector2.zero,InverseMass=.01f,InverseInertia=.01f};
            float root=Mathf.Sqrt(1.01f);
            for(int i=0;i<count;i++)
            {
                bodies[i+1]=new CoupledContactBody{Center=Vector2.right,InverseMass=1,InverseInertia=1};
                rows[i]=new CoupledContactRow{A=0,B=(uint)(i+1),Point=new Vector2(.5f,0),
                    Direction=Vector2.right,InverseRoot=1/root};
                impulses[i]=root;
            }
            using(var op=new CoupledContactOperator(bodies,rows))
            {
                var result=op.Apply(new Vector4[count+1],impulses);
                TestContext.WriteLine("points={0} segments={1} hullSegments={2} bytes={3}",
                    count,op.SegmentCount,op.MaxSegmentsPerBody,op.BufferBytes);
                Assert.That(op.MaxSegmentsPerBody,Is.EqualTo((count+63)/64));
                Assert.That(result.EndpointMotion[0].x,Is.EqualTo(-count*.01f).Within(2e-3));
                for(int i=0;i<count;i++)
                {
                    Assert.That(result.EndpointMotion[i+1].x,Is.EqualTo(1).Within(1e-5));
                    Assert.That(result.RowVelocity[i],Is.EqualTo((1+count*.01f)/root).Within(2e-3));
                }
            }
        }
    }
}
