// Bounded body-only manifolds. Grain work remains in the parallel solver.
#define RIGID_MAX_BODIES 17u
#define RIGID_MAX_POINTS_PER_PAIR 64u
#define RIGID_MAX_POINTS 4096u
#define RIGID_MAX_MANIFOLD_POINTS 2u
#define RIGID_FAULT_POINT_CAPACITY 256u
#define RIGID_FAULT_PAIR_CAPACITY 512u
#define RIGID_FAULT_NONFINITE 1024u
#define RIGID_FAULT_SOLID_PENETRATION 2048u
#define RIGID_FAULT_TOPOLOGY 4096u

// 8 uints (32 bytes) + 40 float bytes = 72-byte StructuredBuffer element.
// pointIndex avoids the reserved HLSL identifier `point`.
struct RigidContact
{
    uint bodyA, bodyB, featureA, featureB;
    uint pointIndex, reserved0, reserved1, reserved2;
    float2 normal;
    float separation, normalLambda;
    float2 armA, armB;
    float tangentLambda, positionLambda;
};

RWStructuredBuffer<RigidContact> _RigidContacts;
RWStructuredBuffer<uint> _RigidContactCount;
RWStructuredBuffer<uint> _RigidPairCounts;

float _RigidGatherMargin;
float _RigidSolidTarget;
uint _RigidCapacity;

float RigidCross(float2 a, float2 b)
{
    return a.x * b.y - a.y * b.x;
}

float2 RigidPerp(float2 a)
{
    return float2(-a.y, a.x);
}

float2 RigidRotate(float2 v, float angle)
{
    float s, c;
    sincos(angle, s, c);
    return float2(c * v.x - s * v.y, s * v.x + c * v.y);
}

float RigidSubstepDt()
{
    return _Dt / max(1u, _D[2]);
}

// SAT plus an incident-edge clip. The normal points from body A toward body B.
// A face overlap emits two edge endpoints; a corner emits one point.
void RigidBoxPatchManifold(uint boundaryA, uint boundaryB,
    out float2 normal, out float separation, out float2 p0, out float2 p1,
    out uint pointCount)
{
    Boundary patchA = _Boundaries[boundaryA];
    Boundary patchB = _Boundaries[boundaryB];
    State a = _State[patchA.body];
    State b = _State[patchB.body];
    Parameters pa = _Parameters[patchA.body];
    Parameters pb = _Parameters[patchB.body];
    float2 ax = RigidRotate(float2(1, 0), a.angle);
    float2 ay = RigidPerp(ax);
    float2 bx = RigidRotate(float2(1, 0), b.angle);
    float2 by = RigidPerp(bx);
    float2 ca = a.center + RigidRotate(patchA.center - pa.com, a.angle);
    float2 cb = b.center + RigidRotate(patchB.center - pb.com, b.angle);
    float2 ha = patchA.halfSize;
    float2 hb = patchB.halfSize;
    float2 delta = cb - ca;
    float2 axes[4] = { ax, ay, bx, by };
    separation = -1e20;uint winningAxis=0;
    normal = ax;
    [unroll] for (uint i = 0; i < 4; ++i)
    {
        float2 axis = axes[i];
        float radiusA = ha.x * abs(dot(ax, axis)) + ha.y * abs(dot(ay, axis));
        float radiusB = hb.x * abs(dot(bx, axis)) + hb.y * abs(dot(by, axis));
        float candidate = abs(dot(delta, axis)) - radiusA - radiusB;
        if (candidate > separation)
        {
            separation = candidate;winningAxis=i;
            normal = dot(delta, axis) >= 0 ? axis : -axis;
        }
    }

    bool referenceA = winningAxis<2;
    float2 referenceCenter = referenceA ? ca : cb;
    float2 referenceX = referenceA ? ax : bx;
    float2 referenceY = referenceA ? ay : by;
    float2 referenceHalf = referenceA ? ha : hb;
    float2 incidentCenter = referenceA ? cb : ca;
    float2 incidentX = referenceA ? bx : ax;
    float2 incidentY = referenceA ? by : ay;
    float2 incidentHalf = referenceA ? hb : ha;
    float2 referenceNormal = referenceA ? normal : -normal;
    bool faceX = abs(dot(referenceNormal, referenceX)) > abs(dot(referenceNormal, referenceY));
    float2 tangent = faceX ? referenceY : referenceX;
    float side = faceX ? referenceHalf.y : referenceHalf.x;
    float faceExtent = faceX ? referenceHalf.x : referenceHalf.y;
    float2 faceCenter = referenceCenter + referenceNormal * faceExtent;
    bool incidentFaceX = abs(dot(referenceNormal, incidentX)) > abs(dot(referenceNormal, incidentY));
    float2 incidentNormal = incidentFaceX ? incidentX : incidentY;
    incidentNormal *= dot(incidentNormal, referenceNormal) > 0 ? -1 : 1;
    float2 incidentTangent = incidentFaceX ? incidentY : incidentX;
    float incidentExtent = incidentFaceX ? incidentHalf.y : incidentHalf.x;
    float2 edgeCenter = incidentCenter + incidentNormal * (incidentFaceX ? incidentHalf.x : incidentHalf.y);
    float2 edgeA = edgeCenter - incidentTangent * incidentExtent;
    float2 edgeB = edgeCenter + incidentTangent * incidentExtent;
    float u = dot(edgeA - faceCenter, tangent);
    float v = dot(edgeB - faceCenter, tangent);
    float lo = 0, hi = 1;
    if (abs(v - u) > 1e-8)
    {
        float c0 = (-side - u) / (v - u);
        float c1 = (side - u) / (v - u);
        lo = max(0, min(c0, c1));
        hi = min(1, max(c0, c1));
    }
    float t0 = saturate(lo);
    float t1 = saturate(max(lo, hi));
    p0 = lerp(edgeA, edgeB, t0);
    p1 = lerp(edgeA, edgeB, t1);
    if (length(p1 - p0) <= 1e-5)
    {
        p0 = (p0 + p1) * .5;
        p1 = p0;
        pointCount = 1;
    }
    else
    {
        pointCount = RIGID_MAX_MANIFOLD_POINTS;
    }
}

void RigidStoreContact(uint endpointA, uint endpointB, uint ordinalA, uint ordinalB,
    uint featureA, uint featureB,
    uint pointIndex, float2 n, float separation, float2 worldPoint, uint contactIndex)
{
    RigidContact c = (RigidContact)0;
    c.bodyA = endpointA;
    c.bodyB = endpointB;
    c.featureA = featureA;
    c.featureB = featureB;
    c.pointIndex = pointIndex;
    c.normal = n;
    c.separation = separation;
    c.armA = worldPoint - _State[endpointA].center;
    c.armB = worldPoint - _State[endpointB].center;
    _RigidContacts[contactIndex] = c;
}

// Once per active substep; only the small fixed body-pair table is serial.
[numthreads(1,1,1)] void RigidClearContacts(uint id:SV_DispatchThreadID)
{
    if(!active())return;
    for(uint i=0;i<RIGID_MAX_BODIES*RIGID_MAX_BODIES;i++)_RigidPairCounts[i]=0;
    _RigidContactCount[0]=0;_D[13]=0;_D[14]=0;
}

// Dispatch over _BoundaryCount squared. Each thread handles one unordered
// cached patch pair, so no serial grain work exists in this rigid-only path.
[numthreads(64, 1, 1)]
void RigidGatherContacts(uint id : SV_DispatchThreadID)
{
    if (!active() || _BoundaryCount == 0) return;
    uint pairSpan = _BoundaryCount * _BoundaryCount;
    if (id >= pairSpan) return;
    uint featureA = id / _BoundaryCount;
    uint featureB = id - featureA * _BoundaryCount;
    if (featureB <= featureA) return;
    uint endpointA=_Boundaries[featureA].body,endpointB=_Boundaries[featureB].body;
    if(endpointA<_N||endpointB<_N||endpointA>=_Endpoints||endpointB>=_Endpoints){fault(RIGID_FAULT_TOPOLOGY);return;}
    uint bodyA=endpointA-_N,bodyB=endpointB-_N;
    if (bodyA == bodyB) return;
    if (bodyB < bodyA)
    {
        uint swapBody = bodyA; bodyA = bodyB; bodyB = swapBody;
        uint swapEndpoint = endpointA; endpointA = endpointB; endpointB = swapEndpoint;
        uint swapFeature = featureA; featureA = featureB; featureB = swapFeature;
    }
    float2 n, p0, p1;
    float separation;
    uint pointCount;
    RigidBoxPatchManifold(featureA, featureB, n, separation, p0, p1, pointCount);
    if (!isfinite(separation) || !all(isfinite(float4(n, p0))))
    {
        InterlockedOr(_D[0], RIGID_FAULT_NONFINITE);
        return;
    }
    if (separation > _RigidGatherMargin) return;
    for (uint pointIndex = 0; pointIndex < pointCount; ++pointIndex)
    {
        uint pairSlot=0;InterlockedAdd(_RigidPairCounts[bodyA*RIGID_MAX_BODIES+bodyB],1,pairSlot);InterlockedMax(_D[14],pairSlot+1);
        if(pairSlot>=RIGID_MAX_POINTS_PER_PAIR){fault(RIGID_FAULT_PAIR_CAPACITY);continue;}
        uint contactIndex=0;InterlockedAdd(_RigidContactCount[0],1,contactIndex);
        if(contactIndex>=min(_RigidCapacity,RIGID_MAX_POINTS)){fault(RIGID_FAULT_POINT_CAPACITY);continue;}
        InterlockedMax(_D[13],contactIndex+1);
        RigidStoreContact(endpointA, endpointB, bodyA, bodyB, featureA, featureB, pointIndex, n, separation,
            pointIndex == 0 ? p0 : p1, contactIndex);
    }
}

// Check the reservation total in a separate dispatch before any impulse solve.
// This also catches a reservation whose store is suppressed by buffer bounds.
[numthreads(1,1,1)] void RigidCheckCapacity(uint id:SV_DispatchThreadID)
{
    if(!active())return;
    if(_RigidContactCount[0]>min(_RigidCapacity,RIGID_MAX_POINTS))fault(RIGID_FAULT_POINT_CAPACITY);
    if(_D[14]>RIGID_MAX_POINTS_PER_PAIR)fault(RIGID_FAULT_PAIR_CAPACITY);
}

float RigidEffective(Parameters p, float2 arm, float2 n)
{
    float lever = RigidCross(arm, n);
    return p.im + p.ii * lever * lever;
}

// One sequential sweep over the bounded rigid-only manifold set. The parent
// invokes two sweeps after each grain iteration; no grain is solved here.
[numthreads(1,1,1)] void RigidSolveVelocity(uint id:SV_DispatchThreadID)
{
    if(!active())return;
    uint count=min(_RigidContactCount[0],min(_RigidCapacity,RIGID_MAX_POINTS));
    for(uint i=0;i<count;i++)
    {
        RigidContact c=_RigidContacts[i];State a=_State[c.bodyA],b=_State[c.bodyB];
        Parameters pa=_Parameters[c.bodyA],pb=_Parameters[c.bodyB];
        float2 relative=b.velocity+b.spin*RigidPerp(c.armB)-a.velocity-a.spin*RigidPerp(c.armA);
        float k=RigidEffective(pa,c.armA,c.normal)+RigidEffective(pb,c.armB,c.normal);if(k<=0)continue;
        float target=-max(0,c.separation)/RigidSubstepDt();
        float next=max(0,c.normalLambda+(target-dot(relative,c.normal))/k);
        float2 impulse=(next-c.normalLambda)*c.normal;c.normalLambda=next;
        a.velocity-=pa.im*impulse;a.spin-=pa.ii*RigidCross(c.armA,impulse);
        b.velocity+=pb.im*impulse;b.spin+=pb.ii*RigidCross(c.armB,impulse);
        float2 tangent=RigidPerp(c.normal);
        relative=b.velocity+b.spin*RigidPerp(c.armB)-a.velocity-a.spin*RigidPerp(c.armA);
        float kt=RigidEffective(pa,c.armA,tangent)+RigidEffective(pb,c.armB,tangent);
        float nt=c.separation<=.0001?clamp(c.tangentLambda-dot(relative,tangent)/max(kt,1e-20),-_Friction*next,_Friction*next):0;
        impulse=(nt-c.tangentLambda)*tangent;c.tangentLambda=nt;
        a.velocity-=pa.im*impulse;a.spin-=pa.ii*RigidCross(c.armA,impulse);
        b.velocity+=pb.im*impulse;b.spin+=pb.ii*RigidCross(c.armB,impulse);
        _State[c.bodyA]=a;_State[c.bodyB]=b;_RigidContacts[i]=c;
    }
}

// Nonlinear positional sweep: physical velocities are never changed.
[numthreads(1,1,1)] void RigidSolvePosition(uint id:SV_DispatchThreadID)
{
    if(!active())return;
    uint count=min(_RigidContactCount[0],min(_RigidCapacity,RIGID_MAX_POINTS));
    for(uint i=0;i<count;i++)
    {
        RigidContact c=_RigidContacts[i];float2 n,p0,p1;float separation;uint points;
        RigidBoxPatchManifold(c.featureA,c.featureB,n,separation,p0,p1,points);
        State a=_State[c.bodyA],b=_State[c.bodyB];Parameters pa=_Parameters[c.bodyA],pb=_Parameters[c.bodyB];
        float2 anchor=c.pointIndex==0||points<2?p0:p1;float2 ra=anchor-a.center,rb=anchor-b.center;
        float k=RigidEffective(pa,ra,n)+RigidEffective(pb,rb,n);if(k<=0)continue;
        float next=max(0,c.positionLambda+(-separation-_RigidSolidTarget)/k);
        float2 correction=(next-c.positionLambda)*n;c.positionLambda=next;
        a.center-=pa.im*correction;a.angle-=pa.ii*RigidCross(ra,correction);
        b.center+=pb.im*correction;b.angle+=pb.ii*RigidCross(rb,correction);
        _State[c.bodyA]=a;_State[c.bodyB]=b;_RigidContacts[i]=c;
    }
}

[numthreads(1,1,1)] void RigidValidate(uint id:SV_DispatchThreadID)
{
    if(_D[12]==0)return;
    uint count=min(_RigidContactCount[0],min(_RigidCapacity,RIGID_MAX_POINTS));
    for(uint i=0;i<count;i++)
    {
        RigidContact c=_RigidContacts[i];float2 n,p0,p1;float separation;uint points;
        RigidBoxPatchManifold(c.featureA,c.featureB,n,separation,p0,p1,points);
        float penetration=max(0,-separation);InterlockedMax(_D[15],asuint(penetration));
        if(!isfinite(penetration))fault(RIGID_FAULT_NONFINITE);
        else if(penetration>.001)fault(RIGID_FAULT_SOLID_PENETRATION);
    }
}
