namespace Sim.Core
{
    /// <summary>
    /// 命令接口：一切改变世界的玩家/网络操作都是命令（GDD §3.2）。
    /// 单人：本地入队零开销；联机：同一队列换成网络来源，核心不变。
    /// 命令 Apply 必须幂等地应对非法输入（静默忽略），不得抛异常破坏模拟。
    /// </summary>
    public interface ISimCommand
    {
        string CommandType { get; }
        void Apply(Simulation sim);
    }
}
