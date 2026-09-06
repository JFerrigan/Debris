// Shared world/ship collision. Ship matter is a local mask; cells keep universal volume.
StructuredBuffer<uint> _Hull;
RWStructuredBuffer<int> _CargoOccupancy;
RWStructuredBuffer<float4> _ShipPose; // [0] x,y,angle,enabled; [1] vx,vy,omega,collision; [2] cargo count,mass,door,reserved
float4 _ShipMotion;
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
// Unit square AABBs vs ship-oriented unit squares. Strict SAT on both sets of axes.
bool SquaresOverlap(float2 worldCenter,float2 localCenter,float4 pose)
{
    float2 delta=World(localCenter,pose)-worldCenter;
    float extent=.5*(1+abs(cos(pose.z))+abs(sin(pose.z)))-.00001;
    return all(abs(delta)<extent)&&all(abs(Rotate(delta,-pose.z))<extent);
}
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
    for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++)
    {
        int2 q=p+int2(x,y);if(!LocalInside(q))continue;
        if(HullAt(q)&&all(abs(target-float2(q))<.99999))return false;
        int other=_CargoOccupancy[CargoIndex(q)];
        if(other>0&&other!=(int)self+1&&all(abs(target-_Cells[other-1].position)<.99999))return false;
    }
    float4 pose=_ShipPose[0];float2 center=World(target+.5,pose);int2 wp=(int2)floor(center);
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
void MoveShip(uint3 id:SV_DispatchThreadID)
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
    float4 old=_ShipPose[0], next=old;next.xy+=_ShipMotion.xy;next.z+=_ShipMotion.z;
    bool blocked=false;
    // Test candidate hull against nearby authoritative fixed and outside loose cells.
    for(int y=-64;y<64&&!blocked;y++)for(int x=-64;x<64&&!blocked;x++)
    {
        int2 p=int2(x,y);if(!HullAt(p))continue;
        float2 center=World(float2(p)+.5,next);int2 at=(int2)floor(center);
        if(!Inside(at)){blocked=true;break;}
        for(int dy=-2;dy<=2&&!blocked;dy++)for(int dx=-2;dx<=2&&!blocked;dx++)
        {
            int2 q=at+int2(dx,dy);if(!Inside(q))continue;
            if(_Field[Address(q)]>0&&SquaresOverlap(float2(q)+.5,float2(p)+.5,next))blocked=true;
            int other=_Occupancy[Index(q)];
            if(other>0&&SquaresOverlap(_Cells[other-1].position+.5,float2(p)+.5,next))blocked=true;
        }
    }
    // Cargo moves with its coordinate frame; ensure it cannot rotate through site matter.
    for(uint i=0;i<_Counters[0]&&!blocked;i++)
    {
        Cell c=_Cells[i];if((c.flags&4)==0)continue;
        float2 center=World(c.position+.5,next);int2 at=(int2)floor(center);
        for(int dy=-2;dy<=2&&!blocked;dy++)for(int dx=-2;dx<=2&&!blocked;dx++)
        {
            int2 q=at+int2(dx,dy);if(!Inside(q)){blocked=true;continue;}
            if(_Field[Address(q)]>0&&SquaresOverlap(float2(q)+.5,c.position+.5,next))blocked=true;
            int other=_Occupancy[Index(q)];if(other>0&&SquaresOverlap(_Cells[other-1].position+.5,c.position+.5,next))blocked=true;
        }
    }
    if(!blocked)_ShipPose[0]=next;
    _ShipPose[1]=blocked?float4(0,0,0,1):float4(_ShipMotion.xyz*60,0);
    
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
        if(cargo&&_ShipPose[2].z>0&&c.position.x<-30)
        {
            float2 world=World(c.position+.5,pose)-.5;
            if(Free(world,i))
            {
                _CargoOccupancy[CargoIndex((int2)floor(c.position))]=0;
                c.position=world;c.flags=0;_Occupancy[Index((int2)floor(world))]=(int)i+1;cargo=false;
            }
        }
        if(cargo){count++;mass+=_Properties[c.material].y;}
        _Cells[i]=c;
    }
    _ShipPose[2]=float4(count,mass,_ShipPose[2].z,0);
}
