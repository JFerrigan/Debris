// Shared world/ship collision. Ship matter is a local mask; cells keep universal volume.
StructuredBuffer<uint> _Hull;
RWStructuredBuffer<uint> _ShipImpact;
RWStructuredBuffer<uint> _ShipSweep; // blocked flag, lowest local hull contact
RWStructuredBuffer<int> _CargoOccupancy;
RWStructuredBuffer<float4> _ShipPose; // [0] x,y,angle,enabled; [1] vx,vy,omega,collision; [2] cargo count,mass,door,reserved
float4 _ShipMotion, _ShipForce, _ShipMass; // inverse mass, inverse inertia, local COM x/y
int _PhysicalShip;
RWStructuredBuffer<uint> _ContactStats; // impulses, rejected poses, substeps, reserved
int _DoorOpen, _ShipEnabled, _Domain, _MountedCut, _MountedSuction;
float2 Rotate(float2 p,float angle){float c=cos(angle),s=sin(angle);return float2(p.x*c-p.y*s,p.x*s+p.y*c);}
float2 Local(float2 world,float4 pose){return Rotate(world-pose.xy,-pose.z);}
float2 World(float2 local,float4 pose){return Rotate(local,pose.z)+pose.xy;}
bool LocalInside(int2 p){return all(p>=-64)&&all(p<64);}
int CargoIndex(int2 p){return (p.y+64)*128+p.x+64;}
bool HullAt(int2 p)
{
    if(!LocalInside(p))return false;
    uint value=_Hull[CargoIndex(p)];
    if(value==0xffffffff && _ShipPose[2].z>0)return false;
    return value!=0;
}
float Cross2(float2 a,float2 b){return a.x*b.y-a.y*b.x;}
float2 SurfaceVelocity(float4 motion,float2 r){return motion.xy+float2(-r.y,r.x)*motion.z;}
float3 ShipDisplacement()
{
    if(_PhysicalShip==0)return _ShipMotion.xyz;
    float4 pose=_ShipPose[0],motion=_ShipPose[1];
    float angle=motion.z*_Delta;
    // Velocity is measured at COM; keep the mask origin consistent while rotating.
    return float3(motion.xy*_Delta+Rotate(_ShipMass.zw,pose.z)-Rotate(_ShipMass.zw,pose.z+angle),angle);
}
[numthreads(1,1,1)]
void ApplyShipForce(uint3 id:SV_DispatchThreadID)
{
    float4 v=_ShipPose[1];v.xyz+=float3(Rotate(_ShipForce.xy,_ShipPose[0].z)*_ShipMass.x,_ShipForce.z*_ShipMass.y)/60;v.w=0;_ShipPose[1]=v;
}
// Maximum separating distance along the four SAT axes; normal points from hull to cell.
float HullSeparation(float2 center,float2 hullCenter,float angle,out float2 normal)
{
    float2 delta=center-hullCenter;float best=-1e20;normal=0;
    for(int axis=0;axis<4;axis++)
    {
        float2 n=axis==0?float2(1,0):axis==1?float2(0,1):Rotate(axis==2?float2(1,0):float2(0,1),angle);
        float extent=.5*(abs(n.x)+abs(n.y)+abs(dot(n,Rotate(float2(1,0),angle)))+abs(dot(n,Rotate(float2(0,1),angle))));
        float projected=dot(delta,n),gap=abs(projected)-extent;
        if(gap>best){best=gap;normal=projected>=0?n:-n;}
    }
    return best;
}
[numthreads(1,1,1)]
void SolveShipCells(uint3 id:SV_DispatchThreadID)
{
    // Exclusive body writer and stable cell ordering: no stale parallel impulse sums.
    float4 pose=_ShipPose[0],v=_ShipPose[1];float2 com=World(_ShipMass.zw,pose);
    _ContactStats[2]++;
    for(uint i=0;i<_Counters[0];i++)
    {
        Cell cell=_Cells[i];if((cell.flags&4)!=0)continue;
        float2 center=cell.position+.5;int2 at=(int2)floor(Local(center,pose));
        float earliest=1e20,separation=0;float2 chosen=0,lever=0;
        for(int y=-2;y<=2;y++)for(int x=-2;x<=2;x++)
        {
            int2 p=at+int2(x,y);if(!HullAt(p))continue;
            float2 hc=World(float2(p)+.5,pose),normal;
            float gap=HullSeparation(center,hc,pose.z,normal);
            float2 r=center-normal*.5-com;
            float closing=-dot(cell.velocity-SurfaceVelocity(v,r),normal);
            if(closing<=.000001||gap>closing*_Delta+.00002)continue;
            // Test the other axes at the relative swept endpoint too, excluding diagonal misses.
            float2 ignored;
            if(HullSeparation(center+(cell.velocity-SurfaceVelocity(v,r))*_Delta,hc,pose.z,ignored)>.00002)continue;
            float toi=max(0,gap)/closing;
            if(toi<earliest){earliest=toi;chosen=normal;lever=r;separation=gap;}
        }
        if(earliest==1e20)continue;
        float closing=-dot(cell.velocity-SurfaceVelocity(v,lever),chosen);
        float cellInv=1/_Properties[cell.material].y,arm=Cross2(lever,chosen);
        float j=max(0,closing)/(_ShipMass.x+cellInv+arm*arm*_ShipMass.y);
        v.xy-=chosen*(j*_ShipMass.x);v.z-=arm*j*_ShipMass.y;
        cell.velocity+=chosen*(j*cellInv);cell.flags=0;
        // A geometric skin above cell-space float ULP prevents exact touching from alternating
        // between overlap and separation away from origin. This changes no velocity/energy.
        if(separation<.0001 && separation>-.001)
        {
            float2 corrected=cell.position+chosen*(.0001-separation);
            if(Free(corrected,i))
            {
                _Occupancy[Index((int2)floor(cell.position))]=0;cell.position=corrected;
                _Occupancy[Index((int2)floor(cell.position))]=(int)i+1;_ContactStats[3]++;
            }
        }
        _Cells[i]=cell;_ContactStats[0]++;
    }
    _ShipPose[1]=v;
}
// Unit square AABBs vs ship-oriented unit squares. Strict SAT on both sets of axes.
bool SquaresOverlap(float2 worldCenter,float2 localCenter,float4 pose)
{
    float2 delta=World(localCenter,pose)-worldCenter;
    float extent=.5*(1+abs(cos(pose.z))+abs(sin(pose.z)))-.00001;
    return all(abs(delta)<extent)&&all(abs(Rotate(delta,-pose.z))<extent);
}
#include "FragmentMatter.hlsl"
bool ShipBlocked(float2 worldPosition,uint self)
{
    if(_ShipEnabled==0)return false;
    float4 pose=_ShipPose[0];float2 center=worldPosition+.5;int2 p=(int2)floor(Local(center,pose));
    for(int y=-2;y<=2;y++)for(int x=-2;x<=2;x++)
    {
        int2 q=p+int2(x,y);if(!LocalInside(q))continue;
        if(HullAt(q)&&SquaresOverlap(center,float2(q)+.5,pose))return true;
        int other=_CargoOccupancy[CargoIndex(q)];
        if(other>0&&other!=(int)self+1&&SquaresOverlap(center,_Cells[other-1].position+.5,pose))return true;
    }
    return false;
}
bool CargoFree(float2 target,uint self)
{
    int2 p=(int2)floor(target);if(!LocalInside(p)||!LocalInside((int2)ceil(target)))return false;
    int occupant=_CargoOccupancy[CargoIndex(p)];if(occupant>0&&occupant!=(int)self+1)return false;
    for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++)
    {
        int2 q=p+int2(x,y);if(!LocalInside(q))continue;
        if(HullAt(q)&&all(abs(target-float2(q))<.99999))return false;
        int other=_CargoOccupancy[CargoIndex(q)];
        if(other>0&&other!=(int)self+1&&all(abs(target-_Cells[other-1].position)<.99999))return false;
    }
    float4 pose=_ShipPose[0];float2 center=World(target+.5,pose);if(FragmentBlocked(center,pose.z,-1))return false;int2 wp=(int2)floor(center);
    for(int y=-2;y<=2;y++)for(int x=-2;x<=2;x++)
    {
        int2 q=wp+int2(x,y);if(!Inside(q))continue;
        if(_Field[Address(q)]>0&&SquaresOverlap(float2(q)+.5,target+.5,pose))return false;
        int other=_Occupancy[Index(q)];
        if(other>0&&other!=(int)self+1&&SquaresOverlap(_Cells[other-1].position+.5,target+.5,pose))return false;
    }
    return true;
}
[numthreads(1,1,1)]
void PrepareShip(uint3 id:SV_DispatchThreadID)
{
    if(_ShipEnabled==0)return;
    float4 doorState=_ShipPose[2];doorState.z=_DoorOpen;_ShipPose[2]=doorState;
    if(_DoorOpen==0)
    {
        // A blocked door stays open; closing must never create matter overlap.
        bool obstructed=false;
        for(uint i=0;i<_Counters[0]&&!obstructed;i++)
        {
            Cell cell=_Cells[i];bool cargo=(cell.flags&4)!=0;
            float2 center=cargo?World(cell.position+.5,_ShipPose[0]):cell.position+.5;
            int2 at=(int2)floor(Local(center,_ShipPose[0]));
            for(int y=-2;y<=2&&!obstructed;y++)for(int x=-2;x<=2&&!obstructed;x++)
            {
                int2 p=at+int2(x,y);if(!LocalInside(p)||_Hull[CargoIndex(p)]!=0xffffffff)continue;
                if(cargo?all(abs(cell.position-float2(p))<.99999):SquaresOverlap(center,float2(p)+.5,_ShipPose[0]))obstructed=true;
            }
        }
        if(obstructed){doorState.z=1;_ShipPose[2]=doorState;}
    }
    _ShipSweep[0]=0;_ShipSweep[1]=0xffffffff;
}
[numthreads(64,1,1)]
void CheckShipHull(uint3 id:SV_DispatchThreadID)
{
    if(id.x>=16384)return;
    int2 p=int2(id.x%128,id.x/128)-64;if(!HullAt(p))return;
    float4 next=_ShipPose[0];next.xyz+=ShipDisplacement();
    float2 center=World(float2(p)+.5,next);int2 at=(int2)floor(center);
    if(!Inside(at)){InterlockedOr(_ShipSweep[0],1);return;}
    bool blocked=FragmentBlocked(center,next.z,-1);
    for(int dy=-2;dy<=2&&!blocked;dy++)for(int dx=-2;dx<=2&&!blocked;dx++)
    {
        int2 q=at+int2(dx,dy);if(!Inside(q))continue;
        if(_Field[Address(q)]>0&&SquaresOverlap(float2(q)+.5,float2(p)+.5,next))blocked=true;
        int other=_Occupancy[Index(q)];
        if(other>0&&SquaresOverlap(_Cells[other-1].position+.5,float2(p)+.5,next))blocked=true;
    }
    if(blocked){InterlockedOr(_ShipSweep[0],1);InterlockedMin(_ShipSweep[1],id.x);}
}
[numthreads(64,1,1)]
void CheckShipCargo(uint3 id:SV_DispatchThreadID)
{
    if(id.x>=_Counters[0])return;
    Cell c=_Cells[id.x];if((c.flags&4)==0)return;
    float4 next=_ShipPose[0];next.xyz+=ShipDisplacement();
    float2 center=World(c.position+.5,next);int2 at=(int2)floor(center);
    bool blocked=FragmentBlocked(center,next.z,-1);
    for(int dy=-2;dy<=2&&!blocked;dy++)for(int dx=-2;dx<=2&&!blocked;dx++)
    {
        int2 q=at+int2(dx,dy);if(!Inside(q)){blocked=true;continue;}
        if(_Field[Address(q)]>0&&SquaresOverlap(float2(q)+.5,c.position+.5,next))blocked=true;
        int other=_Occupancy[Index(q)];if(other>0&&SquaresOverlap(_Cells[other-1].position+.5,c.position+.5,next))blocked=true;
    }
    if(blocked)InterlockedOr(_ShipSweep[0],1);
}
[numthreads(1,1,1)]
void MoveShip(uint3 id:SV_DispatchThreadID)
{
    float4 old=_ShipPose[0],next=old;next.xyz+=ShipDisplacement();
    bool blocked=_ShipSweep[0]!=0;
    if(blocked&&_ShipSweep[1]!=0xffffffff&&_ShipImpact[3]==0)
    {
        int2 contact=int2(_ShipSweep[1]%128,_ShipSweep[1]/128)-64;
        _ShipImpact[0]=contact.x+64;_ShipImpact[1]=contact.y+64;_ShipImpact[2]=asuint(length(_ShipMotion.xy+Rotate(float2(-contact.y,contact.x)*_ShipMotion.z,old.z))*60);_ShipImpact[3]=1;
    }
    if(!blocked)_ShipPose[0]=next;
    if(_PhysicalShip!=0)
    {
        float4 velocity=_ShipPose[1];velocity.w=blocked?1:0;_ShipPose[1]=velocity;
        if(blocked)_ContactStats[1]++;
    }
    else _ShipPose[1]=blocked?float4(0,0,0,1):float4(_ShipMotion.xyz*60,0);
}
[numthreads(1,1,1)]
void TransferCargo(uint3 id:SV_DispatchThreadID)
{
    if(_ShipEnabled==0)return;
    float4 pose=_ShipPose[0];float count=0,mass=0;
    for(uint i=0;i<_Counters[0];i++)
    {
        Cell c=_Cells[i];bool cargo=(c.flags&4)!=0;
        if(!cargo&&_ShipPose[2].z>0)
        {
            float2 local=Local(c.position+.5,pose)-.5;
            if(local.x>=-25&&local.x<=-20&&abs(local.y)<23&&CargoFree(local,i))
            {
                _Occupancy[Index((int2)floor(c.position))]=0;
                c.position=local;c.flags=4;_CargoOccupancy[CargoIndex((int2)floor(local))]=(int)i+1;cargo=true;
            }
        }
        if(cargo&&(c.position.x<-30||c.position.x>30||c.position.y<-30||c.position.y>30))
        {
            float2 world=World(c.position+.5,pose)-.5;
            if(Free(world,i))
            {
                _CargoOccupancy[CargoIndex((int2)floor(c.position))]=0;
                c.position=world;c.flags=0;_Occupancy[Index((int2)floor(world))]=(int)i+1;cargo=false;
            }
        }
        if(cargo&&all(c.position>=-25.0001)&&all(c.position<=24.0001)){count++;mass+=_Properties[c.material].y;}
        _Cells[i]=c;
    }
    _ShipPose[2]=float4(count,mass,_ShipPose[2].z,0);
}
