using System;
using System.Collections.Generic;
namespace Debris.Ships
{
    [Serializable] public sealed class TankCell
    {
        public string Grade;
        public double Energy;
        public TankCell Copy()=>new TankCell{Grade=Grade,Energy=Energy};
    }
    [Serializable] public sealed class TankInventory
    {
        public int Capacity=300;
        public List<TankCell> Contents=new List<TankCell>();
        // Schema-1 fields are consumed once by migration; new saves leave them zero.
        public int Low,Standard,Dense;
        public double BurnRemainder;
        public static double GradeEnergy(string grade)=>grade=="low"?1:grade=="standard"?2:grade=="dense"?4:0;
        public int Count {get{MigrateLegacy();return Contents.Count;}}
        public double Energy {get{MigrateLegacy();double energy=0;foreach(var c in Contents)energy+=c.Energy;return energy;}}
        public void MigrateLegacy()
        {
            if(Contents==null)Contents=new List<TankCell>();
            if(Low==0&&Standard==0&&Dense==0&&BurnRemainder==0)return;
            if(Low<0||Standard<0||Dense<0||(long)Low+Standard+Dense>Capacity||Contents.Count!=0||!double.IsFinite(BurnRemainder)||BurnRemainder<0||BurnRemainder>Low+Standard*2.0+Dense*4.0)throw new InvalidOperationException("Invalid legacy tank inventory.");
            for(int i=0;i<Low;i++)Contents.Add(new TankCell{Grade="low",Energy=1});
            for(int i=0;i<Standard;i++)Contents.Add(new TankCell{Grade="standard",Energy=2});
            for(int i=0;i<Dense;i++)Contents.Add(new TankCell{Grade="dense",Energy=4});
            double burned=BurnRemainder;Low=Standard=Dense=0;BurnRemainder=0;Consume(burned);
        }
        public void Validate()
        {
            MigrateLegacy();if(Capacity<0||Contents.Count>Capacity)throw new InvalidOperationException("Invalid tank capacity.");
            foreach(var c in Contents)if(c==null||GradeEnergy(c.Grade)==0||!double.IsFinite(c.Energy)||c.Energy<=0||c.Energy>GradeEnergy(c.Grade))throw new InvalidOperationException("Invalid fuel cell energy.");
        }
        public bool Add(string grade,int count)
        {
            if(GradeEnergy(grade)==0||count<0||count>Capacity-Count)return false;
            for(int i=0;i<count;i++)Contents.Add(new TankCell{Grade=grade,Energy=GradeEnergy(grade)});return true;
        }
        public bool AddCell(string grade,double energy)
        {
            if(GradeEnergy(grade)==0||!double.IsFinite(energy)||energy<=0||energy>GradeEnergy(grade)||Count>=Capacity)return false;
            Contents.Add(new TankCell{Grade=grade,Energy=energy});return true;
        }
        public bool Consume(double energy)
        {
            if(!double.IsFinite(energy)||energy<0||energy>Energy)return false;
            while(energy>0&&Contents.Count>0)
            {
                var cell=Contents[0];double consumed=Math.Min(energy,cell.Energy);cell.Energy-=consumed;energy-=consumed;
                if(cell.Energy<=0)Contents.RemoveAt(0);
            }
            return true;
        }
        public TankInventory Copy()
        {
            Validate();var copy=new TankInventory{Capacity=Capacity};foreach(var c in Contents)copy.Contents.Add(c.Copy());return copy;
        }
    }
}
