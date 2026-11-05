using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Debris.Simulation.Playground
{
    [Serializable] public sealed class PlaygroundDebugEvent
    {
        public int Version=1;
        public long Sequence;
        public string Utc,Session,Event,Detail,Context;
    }

    // Two bounded JSONL segments and a small in-memory tail. Logging must never
    // throw into an edit, a GPU callback, or a run/pause transition.
    public sealed class PlaygroundDebugLog : IDisposable
    {
        readonly string session=Guid.NewGuid().ToString("N");
        readonly Queue<PlaygroundDebugEvent> recent=new Queue<PlaygroundDebugEvent>();
        readonly long segmentBytes;
        StreamWriter writer;
        long sequence,bytes;
        bool disposed;
        public string Path { get; }
        public string Error { get; private set; }
        public PlaygroundDebugEvent[] Recent=>recent.ToArray();
        // Keep failure exports below their 2 MiB reader limit, even with escaped stacks.
        public PlaygroundDebugEvent[] ExportTail
        {
            get{var all=recent.ToArray();int count=Math.Min(16,all.Length);var tail=new PlaygroundDebugEvent[count];Array.Copy(all,all.Length-count,tail,0,count);return tail;}
        }
        public PlaygroundDebugLog(string path,long segmentBytes=4*1024*1024)
        {
            Path=path;this.segmentBytes=Math.Max(1024,segmentBytes);
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                // Preserve the previous run before opening the current log.
                Rotate();
            }
            catch(Exception e){Disable(e);}
        }
        public void Write(string name,string detail=null,string context=null)
        {
            if(disposed)return;
            try
            {
                var entry=new PlaygroundDebugEvent{Sequence=++sequence,Utc=DateTime.UtcNow.ToString("O"),
                    Session=session,Event=name,Detail=Clip(detail,4096),Context=Clip(context,2048)};
                if(recent.Count==128)recent.Dequeue();recent.Enqueue(entry);
                if(writer==null)return;
                string line=JsonUtility.ToJson(entry);long size=Encoding.UTF8.GetByteCount(line)+1;
                if(bytes+size>segmentBytes)Rotate();
                writer.WriteLine(line);writer.Flush();bytes+=size;
            }
            catch(Exception e){Disable(e);}
        }
        static string Clip(string value,int limit)=>value!=null&&value.Length>limit?value.Substring(0,limit)+" [truncated]":value;
        void Rotate()
        {
            writer?.Dispose();writer=null;
            if(File.Exists(Path)){File.Delete(Path+".previous");File.Move(Path,Path+".previous");}
            writer=new StreamWriter(Path,false,new UTF8Encoding(false));bytes=0;
        }
        void Disable(Exception e){Error=e.ToString();try{writer?.Dispose();}catch{}writer=null;}
        public void Dispose(){if(disposed)return;Write("log.closed");disposed=true;try{writer?.Dispose();}catch(Exception e){Error=e.ToString();}writer=null;}
    }
}
