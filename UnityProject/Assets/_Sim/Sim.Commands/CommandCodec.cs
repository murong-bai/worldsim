using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Sim.Core;

namespace Sim.Commands
{
    /// <summary>
    /// 命令编解码器：命令对象 ↔ 文本行。
    /// 行格式：tick|type|arg0|arg1|...
    /// 用途：回放/存档（GDD §3.5）；联机命令传输复用同一格式（GDD §3.6）。
    /// 新命令类型必须在此注册 —— 注册遗漏会在回放测试中立刻暴露。
    /// </summary>
    public static class CommandCodec
    {
        private static readonly Dictionary<string, Func<string[], ISimCommand>> Decoders =
            new Dictionary<string, Func<string[], ISimCommand>>();

        private static readonly Dictionary<Type, string> TypeNames =
            new Dictionary<Type, string>();

        private static readonly Dictionary<Type, Action<ISimCommand, List<string>>> Encoders =
            new Dictionary<Type, Action<ISimCommand, List<string>>>();

        static CommandCodec()
        {
            Register<PlaceTileCommand>("place_tile",
                a => new PlaceTileCommand(ParsePos(a[0], a[1]), ParseUshort(a[2])) { ByEntityId = ParseInt(a[3]) },
                (c, args) => { AddPos(args, c.Pos); AddUshort(args, c.TileId); AddInt(args, c.ByEntityId); });

            Register<DigTileCommand>("dig_tile",
                a => new DigTileCommand(ParsePos(a[0], a[1])) { ByEntityId = ParseInt(a[2]) },
                (c, args) => { AddPos(args, c.Pos); AddInt(args, c.ByEntityId); });

            Register<SetPlayerInputCommand>("set_player_input",
                // M2.2 加入第 4 参数 climb；旧存档（3 参数）缺省 0，向后兼容
                a => new SetPlayerInputCommand(ParseInt(a[0]), ParseByte(a[1]), ParseByte(a[2]),
                    a.Length > 3 ? ParseByte(a[3]) : (byte)0),
                (c, args) =>
                {
                    AddInt(args, c.EntityId);
                    AddByte(args, c.MoveInput);
                    AddByte(args, c.JumpInput);
                    AddByte(args, c.ClimbInput);
                });

            Register<SpawnEntityCommand>("spawn_entity",
                a => new SpawnEntityCommand(ParseUshort(a[0]), ParsePos(a[1], a[2]), ParseUshort(a[3]), ParseUshort(a[4]), ParseByte(a[5])),
                (c, args) =>
                {
                    AddUshort(args, c.Kind);
                    AddPos(args, c.Pos);
                    AddUshort(args, c.Aux);
                    AddUshort(args, c.Aux2);
                    AddByte(args, c.Data);
                });

            Register<MoveEntityCommand>("move_entity",
                a => new MoveEntityCommand(ParseInt(a[0]), ParsePos(a[1], a[2])),
                (c, args) =>
                {
                    AddInt(args, c.EntityId);
                    AddPos(args, c.Target);
                });

            Register<CraftCommand>("craft",
                a => new CraftCommand(ParsePos(a[0], a[1]), a[2]) { ByEntityId = ParseInt(a[3]) },
                (c, args) =>
                {
                    AddPos(args, c.Pos);
                    args.Add(c.RecipeId);
                    AddInt(args, c.ByEntityId);
                });

            Register<DepositCommand>("deposit",
                a => new DepositCommand(ParsePos(a[0], a[1]), ParseInt(a[2])),
                (c, args) =>
                {
                    AddPos(args, c.Pos);
                    AddInt(args, c.ByEntityId);
                });

            Register<MarkDigCommand>("mark_dig",
                a => new MarkDigCommand(ParsePos(a[0], a[1])),
                (c, args) => { AddPos(args, c.Pos); });
        }

        private static void Register<TCommand>(string typeName, Func<string[], TCommand> decode, Action<TCommand, List<string>> encode)
            where TCommand : class, ISimCommand
        {
            Decoders[typeName] = decode;
            TypeNames[typeof(TCommand)] = typeName;
            Encoders[typeof(TCommand)] = (cmd, args) => encode((TCommand)cmd, args);
        }

        public static ISimCommand Decode(string typeName, string[] args)
        {
            Func<string[], ISimCommand> decoder;
            if (!Decoders.TryGetValue(typeName, out decoder))
                throw new FormatException("未注册的命令类型: " + typeName);
            return decoder(args);
        }

        public static string EncodeRecord(CommandRecord record)
        {
            var commandType = record.Command.GetType();
            string typeName;
            if (!TypeNames.TryGetValue(commandType, out typeName))
                throw new FormatException("未注册的命令类型: " + commandType.Name);

            var args = new List<string>();
            Action<ISimCommand, List<string>> encoder;
            if (!Encoders.TryGetValue(commandType, out encoder))
                throw new FormatException("未注册的命令类型: " + commandType.Name);
            encoder(record.Command, args);

            var sb = new StringBuilder();
            sb.Append(record.Tick.ToString(CultureInfo.InvariantCulture));
            sb.Append('|').Append(typeName);
            for (int i = 0; i < args.Count; i++)
            {
                sb.Append('|').Append(args[i]);
            }
            return sb.ToString();
        }

        public static CommandRecord DecodeRecord(string line)
        {
            string[] parts = line.Split('|');
            if (parts.Length < 2)
                throw new FormatException("损坏的命令行: " + line);
            long tick = long.Parse(parts[0], CultureInfo.InvariantCulture);
            var args = new string[parts.Length - 2];
            if (args.Length > 0)
            {
                Array.Copy(parts, 2, args, 0, args.Length);
            }
            var command = Decode(parts[1], args);
            return new CommandRecord { Tick = tick, Command = command };
        }

        private static GridPos ParsePos(string x, string y) => new GridPos(ParseInt(x), ParseInt(y));
        private static int ParseInt(string s) => int.Parse(s, CultureInfo.InvariantCulture);
        private static ushort ParseUshort(string s) => ushort.Parse(s, CultureInfo.InvariantCulture);
        private static byte ParseByte(string s) => byte.Parse(s, CultureInfo.InvariantCulture);

        private static void AddPos(List<string> args, GridPos pos)
        {
            AddInt(args, pos.X);
            AddInt(args, pos.Y);
        }
        private static void AddInt(List<string> args, int v) => args.Add(v.ToString(CultureInfo.InvariantCulture));
        private static void AddUshort(List<string> args, ushort v) => args.Add(v.ToString(CultureInfo.InvariantCulture));
        private static void AddByte(List<string> args, byte v) => args.Add(v.ToString(CultureInfo.InvariantCulture));
    }
}
