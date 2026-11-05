using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Debris.Simulation.Playground
{
    public static class PlaygroundSceneFile
    {
        const int MaximumBytes=1024*1024;
        public static PlaygroundWorldDefinition Load(string path)
        {
            var info=new FileInfo(path);
            if(info.Length>MaximumBytes)throw new InvalidDataException("Playground scene exceeds 1 MiB");
            return Decode(File.ReadAllText(path));
        }
        public static PlaygroundWorldDefinition Decode(string json)
        {
            if(string.IsNullOrWhiteSpace(json)||Encoding.UTF8.GetByteCount(json)>MaximumBytes)
                throw new InvalidDataException("Empty or oversized playground scene");
            // Require an explicit file version: JsonUtility may run field initializers.
            var envelope=JsonUtility.FromJson<VersionEnvelope>(json);
            if(envelope==null||envelope.Version!=1||envelope.Objects==null)throw new InvalidDataException("Unsupported or incomplete playground scene");
            var world=JsonUtility.FromJson<PlaygroundWorldDefinition>(json);
            if(world==null)throw new InvalidDataException("Invalid playground scene");
            world.Validate();return world;
        }
        [Serializable] sealed class VersionEnvelope { public int Version=0; public PlaygroundObject[] Objects=null; }
        public static void Save(string path,PlaygroundWorldDefinition world)
        {
            world.Validate();WriteAtomic(path,JsonUtility.ToJson(world,true));
        }
        public static void WriteAtomic(string path,string contents)
        {
            string target=Path.GetFullPath(path),directory=Path.GetDirectoryName(target);
            Directory.CreateDirectory(directory);
            string temp=Path.Combine(directory,"."+Path.GetFileName(target)+"."+Guid.NewGuid().ToString("N")+".tmp");
            try
            {
                using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                {
                    byte[] bytes=Encoding.UTF8.GetBytes(contents);stream.Write(bytes,0,bytes.Length);stream.Flush(true);
                }
                if(File.Exists(target))File.Replace(temp,target,null);else File.Move(temp,target);
            }
            finally { if(File.Exists(temp))File.Delete(temp); }
        }
    }
}
