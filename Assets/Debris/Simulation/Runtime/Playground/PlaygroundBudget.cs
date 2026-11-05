using System;
using Debris.Simulation.CoupledContacts;

namespace Debris.Simulation.Playground
{
    // Construction arithmetic for the concrete convex-only playground path.
    // The session compares this with the actual arena ledger after allocation;
    // an upstream allocation change must update this admission contract.
    public static class PlaygroundBudget
    {
        public static long RequiredBytes(int endpoints,int grains)
        {
            if(endpoints<0||grains<0||grains>endpoints||grains>PlaygroundWorldDefinition.GrainLimit||
               endpoints-grains>PlaygroundWorldDefinition.BodyLimit)throw new ArgumentOutOfRangeException();
            if(endpoints==0)return 0;
            long n=endpoints,g=Math.Max(1,grains),c=Math.Max(16,endpoints*8);
            const long rows=262144,manifolds=65536,pairs=524288,bins=65536;
            long rowBlocks=rows/256,bodyBlocks=(n+255)/256,cacheBlocks=(c+255)/256,segments=n+(2*rows+63)/64;
            long state=64+2*(100*n+48*g+64*c+12);
            long transaction=256+24+CoupledTickTransaction.CompletionCapacity*64;
            long broad=88*n+3*bins*4+2*(bins/256)*4+1048576*4+64*4+4+2*pairs*8+12+36*n+4+4;
            long manifold=52+4+4+2*pairs*4+2*(pairs/256)*4+manifolds*(48+8+52)+4+4+32+4*16+32;
            long rowBatch=16*n+rows*(4+4+32+4+24)+2*rowBlocks*4+8+4*16;
            long product=rows*8+2*rows*8+2*rows*16+segments*32+(n+1)*4+64*n+4*bodyBlocks*4+5*16;
            long residual=9*rows*4+2*rowBlocks*4+16*n+196;
            long preconditioner=rows*20+12;
            long arnoldi=20*rows*4+18*rowBlocks*4+17*4+17*16*4+5*16*4+17*4+4+4+24+4+4;
            long search=240*n+3*32+48+8+manifolds*4+2*manifolds*16+(manifolds/256)*8+32;
            long position=80+32+search+32;
            long bridge=244*n+64+32+16+16+4+8+8+48+32+64+8+80*c+4+262144*4+8*cacheBlocks;
            long stepper=residual+preconditioner+arnoldi+32+position+32+bridge;
            long playground=48*n+32;
            return checked(state+transaction+broad+manifold+rowBatch+product+stepper+playground);
        }
        public static void Admit(PlaygroundWorldDefinition world)
        {
            world.Validate();int grains=0;foreach(var o in world.Objects)if(o.Shape==PlaygroundShape.Grain)grains++;
            long required=RequiredBytes(world.Objects.Count,grains);
            // Reserve 64 KiB for a frozen edit-time render image of this capped world.
            if(required+65536>CoupledContactArena.DefaultLimitBytes)
                throw new InvalidOperationException($"Playground explicit buffers would exceed 128 MiB ({required} + edit display reserve)");
        }
    }
}
