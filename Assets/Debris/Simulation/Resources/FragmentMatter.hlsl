StructuredBuffer<uint> _FragmentHull;
StructuredBuffer<float4> _FragmentPose;
RWStructuredBuffer<float4> _FragmentNextPose;
int _FragmentCount, _MovingFragment;
float _FragmentDelta;
bool SquarePair(float2 a,float angleA,float2 b,float angleB)
{
    float angle=angleA-angleB,extent=.5*(1+abs(cos(angle))+abs(sin(angle)))-.00001;
    float2 delta=b-a;
    return all(abs(Rotate(delta,-angleA))<extent)&&all(abs(Rotate(delta,-angleB))<extent);
}
bool FragmentBlocked(float2 center,float angle,int self)
{
    for(int f=0;f<_FragmentCount;f++)
    {
        if(f==self)continue;float4 pose=_FragmentPose[f*2];int2 p=(int2)floor(Local(center,pose));
        for(int dy=-2;dy<=2;dy++)for(int dx=-2;dx<=2;dx++)
        {
            int2 q=p+int2(dx,dy);if(!LocalInside(q)||_FragmentHull[f*16384+CargoIndex(q)]==0)continue;
            if(SquarePair(center,angle,World(float2(q)+.5,pose),pose.z))return true;
        }
    }
    return false;
}
[numthreads(1,1,1)]
void MoveFragment(uint3 id:SV_DispatchThreadID)
{
    int f=_MovingFragment;float4 old=_FragmentPose[f*2],motion=_FragmentPose[f*2+1],next=old;
    // Small substeps preserve collision admission at the bounded active-region edge.
    next.xy+=motion.xy*_FragmentDelta;next.z+=motion.z*_FragmentDelta;
    bool blocked=false;
    for(int y=-64;y<64&&!blocked;y++)for(int x=-64;x<64&&!blocked;x++)
    {
        int2 p=int2(x,y);if(_FragmentHull[f*16384+CargoIndex(p)]==0)continue;
        float2 center=World(float2(p)+.5,next);int2 world=(int2)floor(center);
        if(!Inside(world)||FragmentBlocked(center,next.z,f)){blocked=true;break;}
        for(int dy=-2;dy<=2&&!blocked;dy++)for(int dx=-2;dx<=2&&!blocked;dx++)
        {
            int2 q=world+int2(dx,dy);if(!Inside(q))continue;
            if(_Field[Address(q)]>0&&SquarePair(center,next.z,float2(q)+.5,0))blocked=true;
            int other=_Occupancy[Index(q)];if(other>0&&SquarePair(center,next.z,_Cells[other-1].position+.5,0))blocked=true;
        }
        if(_ShipEnabled!=0)
        {
            float4 ship=_ShipPose[0];int2 local=(int2)floor(Local(center,ship));
            for(int dy=-2;dy<=2&&!blocked;dy++)for(int dx=-2;dx<=2&&!blocked;dx++)
            {
                int2 q=local+int2(dx,dy);if(!LocalInside(q))continue;
                if(HullAt(q)&&SquarePair(center,next.z,World(float2(q)+.5,ship),ship.z))blocked=true;
                int other=_CargoOccupancy[CargoIndex(q)];if(other>0&&SquarePair(center,next.z,World(_Cells[other-1].position+.5,ship),ship.z))blocked=true;
            }
        }
    }
    // Separate read/write buffers avoid UAV/SRV aliasing; the owner copies this result in command order.
    for(int i=0;i<32;i++)_FragmentNextPose[i]=_FragmentPose[i];
    _FragmentNextPose[f*2]=blocked?old:next;
    _FragmentNextPose[f*2+1]=blocked?float4(0,0,0,1):motion;
}
