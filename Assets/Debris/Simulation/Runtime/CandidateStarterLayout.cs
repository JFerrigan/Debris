using System;
using System.Collections.Generic;
using Debris.Materials;
using Debris.Ships;
using UnityEngine;

namespace Debris.Simulation
{
    // Candidate-only loose playground.  This is intentionally applied after a
    // snapshot is taken: legacy startup and saved sites remain byte-for-byte
    // unchanged until R3 owns migration.
    public static class CandidateStarterLayout
    {
        public const int GrainCount = 96;

        public static MatterSnapshot Add(MatterSnapshot snapshot, ShipRuntime ship, MaterialCatalog catalog)
        {
            if (snapshot == null || ship == null || catalog == null) throw new ArgumentNullException();
            if (snapshot.Cells.Length != 0) return snapshot; // Never alter an imported/save population.
            if (snapshot.Capacity < GrainCount) throw new InvalidOperationException("Candidate starter layout exceeds particle capacity.");

            var cells = new List<LooseCell>(GrainCount);
            // The asteroid is generated near the page centre and the starter
            // begins left of it.  Still score both sides, so changed terrain or
            // a changed starter pose cannot put a pile inside an obstacle.
            var left = Build(snapshot, ship, catalog, -1);
            var right = Build(snapshot, ship, catalog, 1);
            var selected = left.Count == GrainCount ? left : right.Count == GrainCount ? right : null;
            if (selected == null) throw new InvalidOperationException("Candidate starter layout has no clear 96-grain placement.");
            cells.AddRange(selected);
            snapshot.Cells = cells.ToArray();
            snapshot.Counters = (uint[])snapshot.Counters.Clone();
            snapshot.Counters[0] = GrainCount;
            snapshot.Counters[1] += GrainCount;
            uint next = Math.Max(snapshot.NextIdentity, 1u);
            for (int i = 0; i < snapshot.Cells.Length; i++) snapshot.Cells[i].Identity = next + (uint)i;
            snapshot.NextIdentity = next + GrainCount;
            return snapshot;
        }

        static List<LooseCell> Build(MatterSnapshot snapshot, ShipRuntime ship, MaterialCatalog catalog, int side)
        {
            var result = new List<LooseCell>(GrainCount);
            ushort material = FirstMaterial(catalog);
            // Four visibly separated 4x4 loose piles (64 grains). Search
            // whole blocks, never a partial pile, when a generated asteroid
            // occupies a proposed location.
            for (int pile = 0; pile < 4; pile++)
                if (!AddBlock(snapshot, ship, result, material, side, pile)) return result;
            // Isolated grains occupy a separate, easily visible line.  A two
            // cell pitch prevents starting contacts while retaining a pile-like
            // field the player can disperse.
            for (int i = 0; i < 128 && result.Count < GrainCount; i++)
            {
                int row = i / 16, column = i % 16;
                var point = ship.Position + new Vector2(side * (104 + column * 2), -76 + row * 22);
                TryAdd(snapshot, ship, result, point, material);
            }
            return result;
        }

        static bool AddBlock(MatterSnapshot snapshot, ShipRuntime ship, List<LooseCell> cells, ushort material, int side, int pile)
        {
            for (int scan = 0; scan < 72; scan++)
            {
                int column = scan % 12, row = scan / 12;
                var origin = ship.Position + new Vector2(side * (84 + column * 8), -72 + row * 28 + pile * 3);
                var trial = new List<LooseCell>(cells);
                for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++) TryAdd(snapshot, ship, trial, origin + new Vector2(side * x, y), material);
                if (trial.Count == cells.Count + 16) { cells.AddRange(trial.GetRange(cells.Count, 16)); return true; }
            }
            return false;
        }

        static ushort FirstMaterial(MaterialCatalog catalog)
        {
            for (ushort i = 1; i <= catalog.Count; i++) if (catalog.DefinitionAt(i).Density > 0) return i;
            throw new InvalidOperationException("Candidate starter layout needs a positive-density material.");
        }

        static void TryAdd(MatterSnapshot snapshot, ShipRuntime ship, List<LooseCell> cells, Vector2 lowerLeft, ushort material)
        {
            var centre = lowerLeft + Vector2.one * .5f;
            if (centre.x < -511 || centre.x >= 511 || centre.y < -511 || centre.y >= 511 || OccupiedTerrain(snapshot, lowerLeft) || OccupiedHull(ship, lowerLeft)) return;
            foreach (var cell in cells) if ((cell.Position + Vector2.one * .5f - centre).sqrMagnitude < .999f) return;
            cells.Add(new LooseCell { Position = lowerLeft, Velocity = Vector2.zero, Material = material, Flags = 1 });
        }

        static bool OccupiedTerrain(MatterSnapshot snapshot, Vector2 lowerLeft)
        {
            int x = Mathf.FloorToInt(lowerLeft.x) - snapshot.OriginX, y = Mathf.FloorToInt(lowerLeft.y) - snapshot.OriginY;
            int width = snapshot.Side * snapshot.ChunkSize;
            if (x < 0 || y < 0 || x >= width || y >= width) return true;
            int cx = x / snapshot.ChunkSize, cy = y / snapshot.ChunkSize;
            return snapshot.Fields[cy * snapshot.Side + cx][(y % snapshot.ChunkSize) * snapshot.ChunkSize + x % snapshot.ChunkSize] != 0;
        }

        static bool OccupiedHull(ShipRuntime ship, Vector2 lowerLeft)
        {
            var local = ship.ToLocal(lowerLeft + Vector2.one * .5f);
            // A cell-centred candidate can only overlap the matching hull cell
            // at startup because all generated grains start angle zero.
            int x = Mathf.FloorToInt(local.x), y = Mathf.FloorToInt(local.y);
            if (x < -64 || x >= 64 || y < -64 || y >= 64) return false;
            return ship.CollisionMask()[(y + 64) * 128 + x + 64] != 0;
        }
    }
}
