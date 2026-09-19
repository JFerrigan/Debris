using System;
using Debris.Materials;
using UnityEngine;

namespace Debris.Simulation
{
    // Candidate terrain is deliberately separate from MatterSession: the legacy
    // textures are a render mirror, not the authority for topology edits.
    public enum CandidateEditStatus
    {
        None, DamageApplied, Released, Busy, NoTarget, GrainCapacity,
        BoundaryCapacity, PlacementBlocked, IdentityExhausted,
        RevisionExhausted, StaleEdit, Unavailable, Faulted
    }

    public sealed class CandidateTerrainEdit
    {
        internal Vector2Int Cell;
        internal int Slice, Index;
        internal uint OldMaterial, ExpectedRevision, Identity;
        internal float OldDamage, NewDamage;
        internal CandidateEditStatus Status;
        internal bool Release;
    }

    public readonly struct CandidateEditResult
    {
        public readonly CandidateEditStatus Status;
        public readonly uint RequestId, Material, Identity, Revision;
        public readonly Vector2Int Cell;
        public readonly int ActiveGrains;
        internal CandidateEditResult(CandidateEditStatus status,uint requestId,Vector2Int cell,uint material,uint identity,uint revision,int grains)
        { Status=status;RequestId=requestId;Cell=cell;Material=material;Identity=identity;Revision=revision;ActiveGrains=grains; }
    }

    public sealed class CandidateTerrainState
    {
        readonly int side, chunkSize, width;
        readonly Vector2Int origin;
        readonly uint[][] fields;
        readonly float[][] damage;
        uint revision, nextIdentity;
        public int Side => side;
        public int ChunkSize => chunkSize;
        public int Width => width;
        public Vector2Int Origin => origin;
        public uint Revision => revision;
        public uint NextIdentity => nextIdentity;

        public CandidateTerrainState(MatterSnapshot imported, int terrainEndpoint)
        {
            if(imported==null) throw new ArgumentNullException(nameof(imported));
            if(imported.Side<1||imported.ChunkSize<1||imported.Fields==null||imported.Damage==null||imported.Fields.Length!=imported.Side*imported.Side||imported.Damage.Length!=imported.Fields.Length)
                throw new InvalidOperationException("Candidate terrain dimensions are invalid.");
            side=imported.Side;chunkSize=imported.ChunkSize;width=checked(side*chunkSize);origin=new Vector2Int(imported.OriginX,imported.OriginY);
            fields=new uint[imported.Fields.Length][];damage=new float[fields.Length][];
            uint maximum=0;
            if(imported.Cells!=null) foreach(var cell in imported.Cells) maximum=Math.Max(maximum,cell.Identity);
            for(int s=0;s<fields.Length;s++)
            {
                if(imported.Fields[s]==null||imported.Damage[s]==null||imported.Fields[s].Length!=chunkSize*chunkSize||imported.Damage[s].Length!=chunkSize*chunkSize) throw new InvalidOperationException("Candidate terrain chunk dimensions are invalid.");
                fields[s]=(uint[])imported.Fields[s].Clone();damage[s]=(float[])imported.Damage[s].Clone();
                for(int i=0;i<damage[s].Length;i++) if(!float.IsFinite(damage[s][i])||damage[s][i]<0) throw new InvalidOperationException("Candidate terrain damage is invalid.");
            }
            nextIdentity=imported.NextIdentity==0 ? checked(maximum+1) : imported.NextIdentity;
            if(nextIdentity==0||nextIdentity<=maximum) throw new InvalidOperationException("Candidate terrain identity sequence is invalid.");
            revision=1;
        }
        public bool TryAddress(Vector2Int worldCell,out int slice,out int index)
        {
            int x=worldCell.x-origin.x,y=worldCell.y-origin.y;
            if(x<0||y<0||x>=width||y>=width){slice=index=-1;return false;}
            slice=(y/chunkSize)*side+x/chunkSize;index=(y%chunkSize)*chunkSize+x%chunkSize;return true;
        }
        public uint MaterialAt(Vector2Int cell)=>TryAddress(cell,out var s,out var i)?fields[s][i]:0;
        public float DamageAt(Vector2Int cell)=>TryAddress(cell,out var s,out var i)?damage[s][i]:0;
        public uint[] BuildMask()
        {
            var result=new uint[width*width];
            for(int s=0;s<fields.Length;s++) for(int i=0;i<fields[s].Length;i++) { int x=(s%side)*chunkSize+i%chunkSize,y=(s/side)*chunkSize+i/chunkSize;result[y*width+x]=fields[s][i]; }
            return result;
        }
        public bool TrySelectDrillCell(Vector2 center,float radius,out Vector2Int selected)
        {
            selected=default;if(!float.IsFinite(radius)||radius<0)return false;float best=float.PositiveInfinity;bool found=false;
            for(int y=0;y<width;y++) for(int x=0;x<width;x++)
            {
                var cell=new Vector2Int(origin.x+x,origin.y+y);uint material=MaterialAt(cell);if(material==0||!Exposed(x,y))continue;
                float d=(new Vector2(cell.x+.5f,cell.y+.5f)-center).sqrMagnitude;if(d>radius*radius)continue;
                if(!found||d<best||(Mathf.Approximately(d,best)&&(cell.y<selected.y||(cell.y==selected.y&&cell.x<selected.x)))){found=true;best=d;selected=cell;}
            }
            return found;
        }
        bool Exposed(int x,int y)
        { return x==0||y==0||x==width-1||y==width-1||BuildMaterial(x-1,y)==0||BuildMaterial(x+1,y)==0||BuildMaterial(x,y-1)==0||BuildMaterial(x,y+1)==0; }
        uint BuildMaterial(int x,int y){if(x<0||y<0||x>=width||y>=width)return 0;int s=(y/chunkSize)*side+x/chunkSize,i=(y%chunkSize)*chunkSize+x%chunkSize;return fields[s][i];}
        public CandidateEditStatus TryPrepareCell(Vector2Int cell,float power,float dt,MaterialCatalog catalog,out CandidateTerrainEdit edit)
        {
            edit=null;if(!TryAddress(cell,out int s,out int i)||fields[s][i]==0)return CandidateEditStatus.NoTarget;
            if(!float.IsFinite(power)||!float.IsFinite(dt)||power<0||dt<0||catalog==null)return CandidateEditStatus.Unavailable;
            var definition=catalog.DefinitionAt((ushort)fields[s][i]);if(definition==null||!float.IsFinite(definition.Durability)||definition.Durability<=0)return CandidateEditStatus.Unavailable;
            float next=damage[s][i]+power*dt;if(!float.IsFinite(next))return CandidateEditStatus.Unavailable;
            bool release=next>=definition.Durability;if(release&&nextIdentity==uint.MaxValue)return CandidateEditStatus.IdentityExhausted;
            edit=new CandidateTerrainEdit{Cell=cell,Slice=s,Index=i,OldMaterial=fields[s][i],OldDamage=damage[s][i],NewDamage=release?0:next,ExpectedRevision=revision,Identity=nextIdentity,Release=release,Status=release?CandidateEditStatus.Released:CandidateEditStatus.DamageApplied};
            return edit.Status;
        }
        public bool IsCurrent(CandidateTerrainEdit edit)=>edit!=null&&edit.ExpectedRevision==revision&&edit.Slice>=0&&fields[edit.Slice][edit.Index]==edit.OldMaterial&&Mathf.Approximately(damage[edit.Slice][edit.Index],edit.OldDamage);
        public void Publish(CandidateTerrainEdit edit)
        {
            if(!IsCurrent(edit))throw new InvalidOperationException("Stale candidate terrain edit.");
            if(edit.Release){fields[edit.Slice][edit.Index]=0;damage[edit.Slice][edit.Index]=0;nextIdentity=checked(nextIdentity+1);revision=checked(revision+1);}else damage[edit.Slice][edit.Index]=edit.NewDamage;
        }
    }
}
