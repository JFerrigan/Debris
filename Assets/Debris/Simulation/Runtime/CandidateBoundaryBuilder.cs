using System;
using System.Collections.Generic;
using Debris.Simulation.ParallelProof;
using UnityEngine;

namespace Debris.Simulation
{
    internal static class CandidateBoundaryBuilder
    {
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
