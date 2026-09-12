using UnityEngine;
namespace Debris.Simulation.ParallelProof
{
    public sealed class ProofFixture
    {
        public LooseCell[] Grains;
        public BodyState[] Bodies;
        public BodyParameters[] Parameters;
        public Boundary[] Boundaries;
        public Vector2 Force;
        public float Torque;
        public ParallelGrainSolver Create(int velocity,int position,float friction=.3f)=>new ParallelGrainSolver(Grains,Bodies,Parameters,Boundaries,velocity,position,friction);
    }
    public static class ProofFixtures
    {
        public static ProofFixture Packed(bool sharedMotion)
        {
            const int count=2500;const float mass=10000,inertia=5000000,spin=.06f;
            var grains=new LooseCell[count];
            for(int y=0;y<50;y++)for(int x=0;x<50;x++){
                int i=y*50+x;var center=new Vector2(x-24.5f,y-24.5f);
                grains[i]=new LooseCell{Center=center,Velocity=sharedMotion?new Vector2(3-spin*center.y,spin*center.x):Vector2.zero,
                    AngularVelocity=sharedMotion?spin:0,Material=1,Identity=(uint)i+1};
            }
            return new ProofFixture{
                Grains=grains,Bodies=new[]{new BodyState{Velocity=sharedMotion?new Vector2(3,0):Vector2.zero,AngularVelocity=sharedMotion?spin:0}},
                Parameters=new[]{new BodyParameters{InverseMass=1/mass,InverseInertia=1/inertia,BoundaryCount=4,Mobility=1,ShapeRevision=1}},
                Boundaries=new[]{
                    new Boundary{Center=new Vector2(-25.5f,0),HalfSize=new Vector2(.5f,25),Body=count,Feature=0},
                    new Boundary{Center=new Vector2(25.5f,0),HalfSize=new Vector2(.5f,25),Body=count,Feature=1},
                    new Boundary{Center=new Vector2(0,-25.5f),HalfSize=new Vector2(26,.5f),Body=count,Feature=2},
                    new Boundary{Center=new Vector2(0,25.5f),HalfSize=new Vector2(26,.5f),Body=count,Feature=3}},
                Force=sharedMotion?Vector2.zero:new Vector2(mass*6,0),Torque=sharedMotion?0:inertia*.06f};
        }
        public static ProofFixture RigidImpact()
        {
            return new ProofFixture{
                Grains=new[]{new LooseCell{Center=new Vector2(100,100),Material=1,Identity=1}},
                Bodies=new[]{new BodyState{Center=new Vector2(-1,1.5f),Velocity=Vector2.right},new BodyState()},
                Parameters=new[]{new BodyParameters{InverseMass=1,InverseInertia=6,BoundaryCount=1,Mobility=1},new BodyParameters{InverseMass=.25f,InverseInertia=3f/17,BoundaryStart=1,BoundaryCount=1,Mobility=1}},
                Boundaries=new[]{new Boundary{Body=1,HalfSize=Vector2.one*.5f},new Boundary{Body=2,HalfSize=new Vector2(.5f,2)}}};
        }
        public static ProofFixture IsolatedHeavy()
        {
            return new ProofFixture{
                Grains=new[]{new LooseCell{Center=new Vector2(.5f,0),Material=1,Identity=1}},
                Bodies=new[]{new BodyState{Center=new Vector2(-.5f,0),Velocity=new Vector2(10,0)}},
                Parameters=new[]{new BodyParameters{InverseMass=.0001f,InverseInertia=.0001f,BoundaryCount=1,Mobility=1,ShapeRevision=1}},
                Boundaries=new[]{new Boundary{HalfSize=Vector2.one*.5f,Body=1}}};
        }
    }
}
