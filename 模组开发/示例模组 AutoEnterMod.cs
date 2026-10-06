// 示例模组：自动演示助手
// 作用：演示模组接口能力——
//   1) 载入时立刻向序列追加一个演示步骤（立即在网格/时间轴可见）；
//   2) 订阅“播放结束”，每次播放结束后再追加一个步骤；
//   3) 通过状态栏回传信息。
// 编译方式见同目录 编译模组.ps1
using System;
using VkSim;

namespace VkSim.ModDemo
{
    public class AutoDemoMod : IVkMod
    {
        public string Name { get { return "自动演示模组"; } }
        public string Description { get { return "载入即加 F1、播放结束加 A，演示模组可增改步骤"; } }
        private IVkContext _ctx;

        public void Initialize(IVkContext ctx)
        {
            _ctx = ctx;
            // 1) 载入时立即加一个演示步骤（应用分配 Id 并刷新显示）
            _ctx.AddStep("f1", 0, 150);
            // 2) 订阅播放结束事件
            ctx.PlayEnded += OnEnded;
            _ctx.SetStatus("模组已载入: " + Name + " —— 已自动加入演示步骤 F1[0-150ms]");
        }

        private void OnEnded()
        {
            _ctx.AddStep("a", 0, 120);
            _ctx.SetStatus("模组：播放结束，已追加演示步骤 A[0-120ms]");
        }

        public void Dispose()
        {
            if (_ctx != null) _ctx.PlayEnded -= OnEnded;
        }
    }
}
