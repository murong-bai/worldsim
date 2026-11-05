using Sim.Core;

namespace Sim.Commands
{
    /// <summary>生成实体（玩家/工人/掉落物等）。</summary>
    public sealed class SpawnEntityCommand : ISimCommand
    {
        public ushort Kind;
        public GridPos Pos;
        public ushort Aux;
        public ushort Aux2;
        public byte Data;

        public SpawnEntityCommand() { }

        public SpawnEntityCommand(ushort kind, GridPos pos, ushort aux, ushort aux2, byte data)
        {
            Kind = kind;
            Pos = pos;
            Aux = aux;
            Aux2 = aux2;
            Data = data;
        }

        public string CommandType => "spawn_entity";

        public void Apply(Simulation sim)
        {
            sim.Entities.Spawn(Kind, Pos, Aux, Aux2, Data);
        }
    }
}
