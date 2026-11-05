namespace Sim.Core
{
    /// <summary>
    /// 模拟系统接口。系统的执行顺序 = 注册顺序（Simulation.RegisterSystem），
    /// 顺序即确定性行为的一部分，任何调整都要在版本历史中记录（GDD §3.3）。
    /// </summary>
    public interface ISimSystem
    {
        string Name { get; }
        void Tick(Simulation sim);
    }
}
