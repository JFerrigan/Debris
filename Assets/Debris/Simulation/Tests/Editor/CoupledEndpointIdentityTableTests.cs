using System;
using System.Runtime.InteropServices;
using Debris.Simulation.CoupledContacts;
using NUnit.Framework;

namespace Debris.Simulation.Tests
{
    public sealed class CoupledEndpointIdentityTableTests
    {
        [Test] public void ReusedIdentityChangesGenerationEvenWhenItMovesToAnotherSlot()
        {
            Assert.That(Marshal.SizeOf<CoupledPersistentIdentity>(),Is.EqualTo(20));
            Assert.That(Marshal.SizeOf<CoupledEndpointBinding>(),Is.EqualTo(36));
            var table=new CoupledEndpointIdentityTable(3);
            var grain=CoupledPersistentIdentity.Grain(7);
            var rigid=new CoupledPersistentIdentity{Kind=2,Low=7};
            var original=table.Bind(0,grain,1);
            table.Bind(1,rigid,4);
            uint revision=table.TableRevision;
            Assert.Throws<InvalidOperationException>(()=>table.Bind(2,grain,1));
            Assert.That(table.TableRevision,Is.EqualTo(revision));
            Assert.That(table.Resolve(0).Generation,Is.EqualTo(original.Generation));
            table.Release(0);
            Assert.That(table.Snapshot()[0].SlotGeneration,
                Is.EqualTo(original.SlotGeneration+1));
            var moved=table.Bind(2,grain,1);
            Assert.That(moved.Generation,Is.EqualTo(original.Generation+1));
            Assert.That(moved.Identity.Equals(original.Identity),Is.True);
            Assert.That(table.Resolve(1).Identity.Equals(rigid),Is.True);
            var changed=table.BumpPhysicalRevision(2);
            Assert.That(changed.Generation,Is.EqualTo(moved.Generation));
            Assert.That(changed.Revision,Is.EqualTo(2));
            Assert.That(changed.SlotGeneration,Is.EqualTo(moved.SlotGeneration));
            Assert.Throws<InvalidOperationException>(()=>table.Bind(2,grain,1));
            Assert.That(table.Snapshot()[2].Revision,Is.EqualTo(2));
        }

        [Test] public void DirectSlotReplacementInvalidatesPriorIdentityLifetime()
        {
            var table=new CoupledEndpointIdentityTable(2);
            var first=CoupledPersistentIdentity.Grain(11);
            var second=CoupledPersistentIdentity.Grain(12);
            uint firstGeneration=table.Bind(0,first,1).Generation;
            uint firstSlotGeneration=table.Resolve(0).SlotGeneration;
            table.Bind(0,second,1);
            Assert.That(table.Resolve(0).SlotGeneration,
                Is.EqualTo(firstSlotGeneration+1));
            var rebound=table.Bind(1,first,1);
            Assert.That(rebound.Generation,Is.EqualTo(firstGeneration+1));
            Assert.That(table.Resolve(0).Identity.Equals(second),Is.True);
        }
    }
}
