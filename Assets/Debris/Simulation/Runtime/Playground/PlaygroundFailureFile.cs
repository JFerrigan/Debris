using System;
using System.IO;
using UnityEngine;

namespace Debris.Simulation.Playground
{
    [Serializable]
    public sealed class PlaygroundFailureFile
    {
        public int Version=1;
        public int CommandVersion=1;
        public string Utc,UnityVersion,ApplicationVersion,AssemblyId,BuildIdentity,Reason;
        public uint WorldRevision,WorldGeneration;
        public ulong AttemptedTick,AcceptedTick;
        public PlaygroundWorldDefinition World;
        public PlaygroundInput Command;
        public uint[] Diagnostics;
        public string DebugLogPath;
        public PlaygroundDebugEvent[] RecentEvents;
        public static PlaygroundFailureFile Load(string path)
        {
            if(new FileInfo(path).Length>2*1024*1024)throw new InvalidDataException("Failure export exceeds 2 MiB");
            string text=File.ReadAllText(path);var v=JsonUtility.FromJson<VersionHeader>(text);
            if(v==null||v.Version!=1||v.CommandVersion!=1)throw new InvalidDataException("Unsupported or incomplete failure export");
            var result=JsonUtility.FromJson<PlaygroundFailureFile>(text);
            if(result==null||result.World==null)throw new InvalidDataException("Failure export has no world");
            result.World.Validate();return result;
        }
        [Serializable] sealed class VersionHeader { public int Version=0,CommandVersion=0; }
    }
}
