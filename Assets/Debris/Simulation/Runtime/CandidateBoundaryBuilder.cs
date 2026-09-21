using System;
using System.Collections.Generic;
using Debris.Simulation.ParallelProof;
using UnityEngine;

namespace Debris.Simulation
{
    internal static class CandidateBoundaryBuilder
    {
        // Terrain faces are cached by their affected scanline. A single cell
        // release can only change its own row/column and their neighbours.
        internal sealed class TerrainBoundaryCache
        {
            readonly int width,endpoint,originX,originY,cellOriginX,cellOriginY;
            readonly uint[] mask;
            readonly List<Boundary>[] horizontal,vertical;
            internal TerrainBoundaryCache(CandidateTerrainState terrain)
            {
                // The anchored body pose supplies the terrain world origin.
                // Cache patches remain terrain-local, as they did at import.
                if(terrain==null)throw new ArgumentNullException(nameof(terrain));width=terrain.Width;endpoint=terrain.TerrainEndpoint;originX=0;originY=0;cellOriginX=terrain.Origin.x;cellOriginY=terrain.Origin.y;mask=terrain.BuildMask();
                horizontal=new List<Boundary>[width*2];vertical=new List<Boundary>[width*2];for(int y=0;y<width;y++){RebuildHorizontal(y,0);RebuildHorizontal(y,1);}for(int x=0;x<width;x++){RebuildVertical(x,0);RebuildVertical(x,1);}
            }
            TerrainBoundaryCache(TerrainBoundaryCache source)
            {width=source.width;endpoint=source.endpoint;originX=source.originX;originY=source.originY;cellOriginX=source.cellOriginX;cellOriginY=source.cellOriginY;mask=(uint[])source.mask.Clone();horizontal=(List<Boundary>[])source.horizontal.Clone();vertical=(List<Boundary>[])source.vertical.Clone();}
            bool Solid(int x,int y)=>x>=0&&y>=0&&x<width&&y<width&&mask[y*width+x]!=0;
            void RebuildHorizontal(int y,int edge)
            {
                if(y<0||y>=width)return;var line=new List<Boundary>();int x=0;while(x<width){bool open=Solid(x,y)&&(edge==0?!Solid(x,y-1):!Solid(x,y+1));if(!open){x++;continue;}int start=x;while(x<width&&Solid(x,y)&&(edge==0?!Solid(x,y-1):!Solid(x,y+1)))x++;float count=x-start;line.Add(new Boundary{Body=(uint)endpoint,Center=new Vector2(originX+start+count*.5f,originY+y+(edge==0?0:1)),HalfSize=new Vector2(count*.5f,.001f)});}horizontal[y*2+edge]=line;
            }
            void RebuildVertical(int x,int edge)
            {
                if(x<0||x>=width)return;var line=new List<Boundary>();int y=0;while(y<width){bool open=Solid(x,y)&&(edge==0?!Solid(x-1,y):!Solid(x+1,y));if(!open){y++;continue;}int start=y;while(y<width&&Solid(x,y)&&(edge==0?!Solid(x-1,y):!Solid(x+1,y)))y++;float count=y-start;line.Add(new Boundary{Body=(uint)endpoint,Center=new Vector2(originX+x+(edge==0?0:1),originY+start+count*.5f),HalfSize=new Vector2(.001f,count*.5f)});}vertical[x*2+edge]=line;
            }
            internal TerrainBoundaryCache ApplyRelease(CandidateTerrainState terrain,CandidateTerrainEdit edit)
            {
                if(terrain==null||edit==null||!edit.Release||!terrain.IsCurrent(edit))throw new InvalidOperationException("Candidate terrain cache received a stale release.");var copy=new TerrainBoundaryCache(this);int x=edit.Cell.x-cellOriginX,y=edit.Cell.y-cellOriginY;if(x<0||y<0||x>=width||y>=width||copy.mask[y*width+x]==0)throw new InvalidOperationException("Candidate terrain cache address is invalid.");copy.mask[y*width+x]=0;
                for(int row=y-1;row<=y+1;row++){copy.RebuildHorizontal(row,0);copy.RebuildHorizontal(row,1);}for(int column=x-1;column<=x+1;column++){copy.RebuildVertical(column,0);copy.RebuildVertical(column,1);}return copy;
            }
            internal Boundary[] Flatten()
            {
                var result=new List<Boundary>();for(int y=0;y<width;y++)for(int edge=0;edge<2;edge++)result.AddRange(horizontal[y*2+edge]);for(int x=0;x<width;x++)for(int edge=0;edge<2;edge++)result.AddRange(vertical[x*2+edge]);
                float minX=originX,minY=originY,maxX=originX+width,maxY=originY+width;result.Add(new Boundary{Body=(uint)endpoint,Center=new Vector2(minX-.001f,(minY+maxY)*.5f),HalfSize=new Vector2(.001f,width*.5f+.001f)});result.Add(new Boundary{Body=(uint)endpoint,Center=new Vector2(maxX+.001f,(minY+maxY)*.5f),HalfSize=new Vector2(.001f,width*.5f+.001f)});result.Add(new Boundary{Body=(uint)endpoint,Center=new Vector2((minX+maxX)*.5f,minY-.001f),HalfSize=new Vector2(width*.5f+.001f,.001f)});result.Add(new Boundary{Body=(uint)endpoint,Center=new Vector2((minX+maxX)*.5f,maxY+.001f),HalfSize=new Vector2(width*.5f+.001f,.001f)});return result.ToArray();
            }
        }
        internal sealed class CandidateBoundaryReplacement
        {
            internal readonly Boundary[] Patches;
            internal readonly BodyParameters[] Definitions;
            internal readonly uint ExpectedRevision;
            internal CandidateBoundaryReplacement(Boundary[] patches,BodyParameters[] definitions,uint revision)
            {Patches=patches;Definitions=definitions;ExpectedRevision=revision;}
        }
        internal static Boundary[] BuildTerrainCache(CandidateTerrainState terrain,int endpoint)
        {
            if(terrain==null||terrain.TerrainEndpoint!=endpoint)throw new ArgumentException(nameof(terrain));return new TerrainBoundaryCache(terrain).Flatten();
        }
        internal static bool TryPrepareTerrainReplacement(CandidateTerrainState terrain,CandidateTerrainEdit edit,TerrainBoundaryCache cache,BodyParameters[] current,Boundary[] dynamicPrefix,int capacity,out CandidateBoundaryReplacement replacement,out TerrainBoundaryCache nextCache)
        {
            replacement=null;nextCache=null;if(terrain==null||cache==null||current==null||dynamicPrefix==null||capacity<0)return false;
            if(edit==null||!terrain.IsCurrent(edit))return false;
            if(!edit.Release)return false;nextCache=cache.ApplyRelease(terrain,edit);var terrainPatches=nextCache.Flatten();
            if(dynamicPrefix.Length+terrainPatches.Length>capacity)return false;
            var patches=new Boundary[dynamicPrefix.Length+terrainPatches.Length];Array.Copy(dynamicPrefix,patches,dynamicPrefix.Length);Array.Copy(terrainPatches,0,patches,dynamicPrefix.Length,terrainPatches.Length);
            for(int i=0;i<patches.Length;i++)patches[i].Feature=(uint)i;
            var definitions=(BodyParameters[])current.Clone();
            // Import always puts the anchored terrain at the final endpoint.
            // Refusing any other layout is safer than mutating a fragment.
            int terrainDefinition=definitions.Length-1;
            if(terrainDefinition<0||definitions[terrainDefinition].Mobility!=0)return false;
            definitions[terrainDefinition].BoundaryStart=(uint)dynamicPrefix.Length;definitions[terrainDefinition].BoundaryCount=(uint)terrainPatches.Length;definitions[terrainDefinition].ShapeRevision=checked(terrain.Revision+1);
            replacement=new CandidateBoundaryReplacement(patches,definitions,terrain.Revision);return true;
        }
        internal static void AppendMaskBoundaries(List<Boundary> output,uint[] mask,int endpoint,int originX,int originY,bool enclosePage)
        {
            if(output==null||mask==null)throw new ArgumentNullException();int width=(int)Mathf.Sqrt(mask.Length);if(width*width!=mask.Length)throw new ArgumentException("Mask must be square.");
            void Add(Vector2 c,Vector2 h)=>output.Add(new Boundary{Body=(uint)endpoint,Feature=(uint)output.Count,Center=c,HalfSize=h});
            for(int y=0;y<width;y++)for(int edge=0;edge<2;edge++){int x=0;while(x<width){bool open=mask[y*width+x]!=0&&(edge==0?(y==0||mask[(y-1)*width+x]==0):(y==width-1||mask[(y+1)*width+x]==0));if(!open){x++;continue;}int start=x;while(x<width&&mask[y*width+x]!=0&&(edge==0?(y==0||mask[(y-1)*width+x]==0):(y==width-1||mask[(y+1)*width+x]==0)))x++;float n=x-start;Add(new Vector2(originX+start+n*.5f,originY+y+(edge==0?0:1)),new Vector2(n*.5f,.001f));}}
            for(int x=0;x<width;x++)for(int edge=0;edge<2;edge++){int y=0;while(y<width){bool open=mask[y*width+x]!=0&&(edge==0?(x==0||mask[y*width+x-1]==0):(x==width-1||mask[y*width+x+1]==0));if(!open){y++;continue;}int start=y;while(y<width&&mask[y*width+x]!=0&&(edge==0?(x==0||mask[y*width+x-1]==0):(x==width-1||mask[y*width+x+1]==0)))y++;float n=y-start;Add(new Vector2(originX+x+(edge==0?0:1),originY+start+n*.5f),new Vector2(.001f,n*.5f));}}
            if(enclosePage){float minX=originX,minY=originY,maxX=originX+width,maxY=originY+width;Add(new Vector2(minX-.001f,(minY+maxY)*.5f),new Vector2(.001f,width*.5f+.001f));Add(new Vector2(maxX+.001f,(minY+maxY)*.5f),new Vector2(.001f,width*.5f+.001f));Add(new Vector2((minX+maxX)*.5f,minY-.001f),new Vector2(width*.5f+.001f,.001f));Add(new Vector2((minX+maxX)*.5f,maxY+.001f),new Vector2(width*.5f+.001f,.001f));}
        }
    }
}
