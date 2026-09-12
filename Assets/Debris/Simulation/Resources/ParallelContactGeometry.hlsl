// Pure oriented-box contact geometry shared by parallel contact kernels.
//
// The helper returns the signed SAT separation (positive gap, negative
// penetration), a normal from A to B, and a world-space patch anchor. Equal
// depth, non-coplanar SAT axes are retained as a two-contact corner manifold;
// coplanar axes from the two boxes are deduplicated.

struct ParallelObb
{
    float2 center;
    float2 halfSize;
    float angle;
};

struct ParallelObbContact
{
    float2 normal;
    float separation;
    float2 contactPoint;
};

float2 pcg_axis(ParallelObb box,uint index)
{
    float s,c;sincos(box.angle,s,c);
    float2 x=float2(c,s);
    return index==0?x:index==1?float2(-x.y,x.x):(index==2?x:float2(-x.y,x.x));
}

float pcg_radius(ParallelObb box,float2 axis)
{
    float2 x=pcg_axis(box,0),y=pcg_axis(box,1);
    return box.halfSize.x*abs(dot(x,axis))+box.halfSize.y*abs(dot(y,axis));
}

float pcg_axis_separation(ParallelObb a,ParallelObb b,float2 axis)
{
    return abs(dot(b.center-a.center,axis))-pcg_radius(a,axis)-pcg_radius(b,axis);
}

bool pcg_finite_box(ParallelObb box)
{
    return all(isfinite(float4(box.center,box.halfSize)))&&isfinite(box.angle)&&all(box.halfSize>0);
}

void pcg_contact_on_axis(ParallelObb a,ParallelObb b,uint axisIndex,float2 axis,float separation,out ParallelObbContact result)
{
    float2 delta=b.center-a.center;
    float2 n=dot(delta,axis)>=0?axis:-axis;
    bool referenceA=axisIndex<2;
    float2 rc=referenceA?a.center:b.center;
    float2 ic=referenceA?b.center:a.center;
    ParallelObb referenceBox=a,incidentBox=b;if(!referenceA){referenceBox=b;incidentBox=a;}
    float2 rx=pcg_axis(referenceBox,0),ry=pcg_axis(referenceBox,1);
    float2 ix=pcg_axis(incidentBox,0),iy=pcg_axis(incidentBox,1);
    float2 rh=referenceA?a.halfSize:b.halfSize;
    float2 ih=referenceA?b.halfSize:a.halfSize;
    float2 rn=referenceA?n:-n;
    bool faceX=abs(dot(rn,rx))>=abs(dot(rn,ry));
    float2 tangent=faceX?ry:rx;
    float side=faceX?rh.y:rh.x;
    float2 refCenter=rc+rn*(faceX?rh.x:rh.y);

    bool incidentX=abs(dot(rn,ix))>=abs(dot(rn,iy));
    float2 incNormal=incidentX?ix:iy;
    incNormal*=dot(incNormal,rn)>0?-1:1;
    float2 incTangent=incidentX?iy:ix;
    float incHalf=incidentX?ih.y:ih.x;
    float2 edgeCenter=ic+incNormal*(incidentX?ih.x:ih.y);
    float2 p=edgeCenter-incTangent*incHalf;
    float2 q=edgeCenter+incTangent*incHalf;
    float u=dot(p-refCenter,tangent),v=dot(q-refCenter,tangent);
    float lo=0,hi=1;
    if(abs(v-u)>1e-8)
    {
        float t0=(-side-u)/(v-u),t1=(side-u)/(v-u);
        lo=max(0,min(t0,t1));hi=min(1,max(t0,t1));
    }

    float2 contactPoint;
    if(hi>=lo)
    {
        float2 p0=lerp(p,q,clamp(lo,0,1));
        float2 p1=lerp(p,q,clamp(hi,0,1));
        contactPoint=(p0+p1)*.5;
    }
    else
    {
        // Speculative corner gaps have no edge interval. Pick the support
        // endpoint nearest the reference side span before midpointing faces.
        float cu=clamp(u,-side,side),cv=clamp(v,-side,side);
        contactPoint=abs(cu-u)<=abs(cv-v)?p:q;
    }
    contactPoint-=rn*dot(contactPoint-refCenter,rn)*.5;
    result.normal=n;result.separation=separation;result.contactPoint=contactPoint;
}

// Returns false only for non-finite or degenerate boxes. A valid pair always
// returns one or two contacts, including separated speculative pairs.
bool ParallelObbContacts(ParallelObb a,ParallelObb b,out uint count,out ParallelObbContact contact0,out ParallelObbContact contact1)
{
    count=0;contact0=(ParallelObbContact)0;contact1=(ParallelObbContact)0;
    if(!pcg_finite_box(a)||!pcg_finite_box(b))return false;

    float2 axes[4]={pcg_axis(a,0),pcg_axis(a,1),pcg_axis(b,0),pcg_axis(b,1)};
    float separations[4];float best=-1e30;float scale=1;uint bestAxis=0;
    for(uint i=0;i<4;i++)
    {
        separations[i]=pcg_axis_separation(a,b,axes[i]);
        if(separations[i]>best){best=separations[i];bestAxis=i;}
        scale=max(scale,abs(separations[i])+pcg_radius(a,axes[i])+pcg_radius(b,axes[i]));
    }
    float tie=2e-5*scale;
    uint selected[2]={bestAxis,0};uint selectedCount=1;
    for(uint candidateAxis=0;candidateAxis<4;candidateAxis++)
        if(selectedCount<2 && abs(separations[candidateAxis]-best)<=tie && abs(dot(axes[candidateAxis],axes[bestAxis]))<=.9999)
        {selected[1]=candidateAxis;selectedCount=2;}
    if(selectedCount==0)return false;
    pcg_contact_on_axis(a,b,selected[0],axes[selected[0]],separations[selected[0]],contact0);
    count=1;
    if(selectedCount>1)
    {
        pcg_contact_on_axis(a,b,selected[1],axes[selected[1]],separations[selected[1]],contact1);
        count=2;
    }
    return true;
}
